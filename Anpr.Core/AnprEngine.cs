using Anpr.Core.Detection;
using Anpr.Core.Imaging;
using Anpr.Core.Recognition;
using Anpr.Core.TurkishPlates;
using SkiaSharp;

namespace Anpr.Core;

/// <summary>
/// Uçtan uca ANPR motoru: tespit -> kırp -> OCR -> TR doğrula/normalize.
/// Thread-safe DEĞİLDİR (ONNX session'ları sıralı kullanılmalı). Her kamera/iş parçacığı
/// kendi AnprEngine örneğini kullanmalı ya da çağrılar serileştirilmeli.
/// </summary>
public sealed class AnprEngine : IDisposable
{
    private readonly AnprOptions _options;
    private readonly LicensePlateDetector _detector;
    private readonly LicensePlateRecognizer _recognizer;
    private readonly PlateRectifier2? _rectifier;
    private readonly PlateSuperResolver? _superResolver;
    private volatile RoiPolygon? _roi;
    private volatile string _roiSpec;
    private bool _disposed;

    private AnprEngine(AnprOptions options, LicensePlateDetector detector, LicensePlateRecognizer recognizer,
        PlateRectifier2? rectifier, PlateSuperResolver? superResolver)
    {
        _options = options;
        _detector = detector;
        _recognizer = recognizer;
        _rectifier = rectifier;
        _superResolver = superResolver;
        _roi = RoiPolygon.Parse(options.RoiPolygon);
        _roiSpec = options.RoiPolygon ?? "";
    }

    /// <summary>Rektifikasyon aşaması kullanılabilir mi (model yüklendi)?</summary>
    public bool RectifierAvailable => _rectifier != null;

    /// <summary>SR aşaması kullanılabilir mi?</summary>
    public bool SuperResolverAvailable => _superResolver != null;

    /// <summary>Küçük kırpıntıyı ×2 büyütür; aşama kapalı/başarısızsa null.</summary>
    public SKBitmap? TrySuperResolve(SKBitmap crop) => _superResolver?.Upscale(crop);

    /// <summary>Kırpıntıyı WPOD ile rektifiye eder; dörtgen yok/aşama kapalıysa null.</summary>
    public PlateRectifier2.RectifyResult? TryRectify(SKBitmap crop) => _rectifier?.Rectify(crop);

    /// <summary>Hazır kırpıntıda yalnız OCR + TR doğrulama (zenginleştirilmiş gözlemler için).</summary>
    public PlateReading? ReadCrop(SKBitmap crop, float detConf = 1f, PlateBox box = default)
    {
        var ocr = _recognizer.Recognize(crop);
        return BuildReading(ocr, box, detConf);
    }

    /// <summary>Geçerli ROI tanımı ("" = kapalı).</summary>
    public string RoiSpec => _roiSpec;

    /// <summary>ROI'yi çalışırken değiştirir (panelden). Geçersiz/boş tanım = filtre kapalı. Thread-safe (volatile swap).</summary>
    public void SetRoi(string? spec)
    {
        _roi = RoiPolygon.Parse(spec);
        _roiSpec = _roi != null ? spec! : "";
    }

    // ROI tanımlıysa tespit merkezi çokgen içinde olmalı; tanımsızsa her tespit geçer.
    private bool InRoi(PlateDetection det, int imgW, int imgH)
        => _roi == null || _roi.ContainsPixel(det.X + det.Width / 2f, det.Y + det.Height / 2f, imgW, imgH);

    /// <summary>Modelin beklediği kare dedektör giriş boyutu (ör. 384).</summary>
    public int DetectorInputSize => _detector.InputSize;

    /// <summary>OCR yapılandırması (alfabe, giriş boyutu vb.).</summary>
    public PlateOcrConfig OcrConfig => _recognizer.Config;

    /// <summary>Verilen yapılandırma ile bir motor örneği oluşturur (modelleri yükler).</summary>
    public static AnprEngine Create(AnprOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Aynı model+ayar → tek ONNX oturumu (kameralar arası paylaşım; bkz. OnnxSessionPool). null = paylaşım yok.
        string? share = options.SessionShareKey;

        var detector = new LicensePlateDetector(
            options.DetectorModelPath,
            options.CreateSessionOptions(),
            options.DetectorScoreThreshold,
            share);

        var recognizer = new LicensePlateRecognizer(
            options.OcrModelPath,
            options.OcrConfigPath,
            options.CreateSessionOptions(),
            share);

        string modelsDir = Path.GetDirectoryName(Path.GetFullPath(options.OcrModelPath)) ?? ".";
        string Resolve(string p) => Path.IsPathRooted(p) ? p : Path.Combine(modelsDir, Path.GetFileName(p));

        PlateRectifier2? rectifier = null;
        if (options.EnhanceRectify)
        {
            try
            {
                string wpod = Resolve(options.WpodModelPath);
                if (File.Exists(wpod))
                    rectifier = new PlateRectifier2(wpod, options.CreateSessionOptions(), share);
            }
            catch { rectifier = null; } // zarif düşüş: aşama kapalı, servis çalışmaya devam eder
        }

        PlateSuperResolver? sr = null;
        if (options.EnhanceSr)
        {
            try
            {
                string srp = Resolve(options.SrModelPath);
                if (File.Exists(srp))
                    sr = new PlateSuperResolver(srp, options.CreateSessionOptions(), share);
            }
            catch { sr = null; }
        }

        return new AnprEngine(options, detector, recognizer, rectifier, sr);
    }

    /// <summary>JPEG/PNG byte dizisinden plaka okur.</summary>
    public IReadOnlyList<PlateReading> Recognize(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        using var image = SkiaImaging.DecodeRgba(imageBytes);
        return Recognize(image);
    }

    /// <summary>Bir görüntüden plaka okur.</summary>
    public IReadOnlyList<PlateReading> Recognize(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Recognize(new SkBitmapFrameView(image));
    }

    /// <summary>
    /// Bir kare GÖRÜNÜMÜNDEN plaka okur (bkz. <see cref="IFrameView"/>). YUV kare burada tam RGB'ye
    /// çevrilmeden işlenir: dedektör girdisi doğrudan örneklenir, yalnız plaka kırpıntıları RGBA'ya çevrilir.
    /// </summary>
    public IReadOnlyList<PlateReading> Recognize(IFrameView view)
    {
        var results = RecognizeCore(view, wantCrops: false);
        var readings = new List<PlateReading>(results.Count);
        foreach (var (r, _) in results) readings.Add(r);
        readings.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        return readings;
    }

    /// <summary>Recognize gibi ama her okumanın KIRPINTISINI da döner (çağıran sahiplenir, dispose eder).
    /// Çok-kareli füzyon için worker'ın kırpıntı saklaması gerektiğinde kullanılır.</summary>
    public IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> RecognizeWithCrops(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        using var image = SkiaImaging.DecodeRgba(imageBytes);
        return RecognizeCore(new SkBitmapFrameView(image), wantCrops: true);
    }

    /// <summary>Recognize gibi ama SKBitmap'ten, kırpıntılarla (canlı füzyon hattı için).</summary>
    public IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> RecognizeWithCrops(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return RecognizeCore(new SkBitmapFrameView(image), wantCrops: true);
    }

    /// <summary>Recognize gibi ama kare görünümünden, kırpıntılarla (çağıran kırpıntıları sahiplenir).</summary>
    public IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> RecognizeWithCrops(IFrameView view)
        => RecognizeCore(view, wantCrops: true);

    private int _frameTick; // ROI-kırpım modunda tam-kare gözlem taraması sayacı (kamera başına motor = tek thread)

    // Ortak boru hattı: tespit -> (bölge içi hepsi + bölge dışı en büyük N "gözlem") -> kırp -> OCR.
    // ROI-kırpım modu: ana tespit ROI sınırlayıcı dikdörtgeninde koşar (plakalar dedektör girdisinde
    // büyür); gözlem için tam kare FullFrameEveryN karede bir ayrıca taranır.
    private List<(PlateReading Reading, SKBitmap Crop)> RecognizeCore(IFrameView image, bool wantCrops)
    {
        ArgumentNullException.ThrowIfNull(image);

        var roi = _roi;
        int W = image.Width, H = image.Height;
        var fullRect = new SKRectI(0, 0, W, H);

        (int X, int Y, int Wd, int Ht)? cropRect = null;
        if (roi != null && _options.RoiCropDetector)
        {
            var (rx, ry, rw, rh) = roi.BoundingRectPixels(W, H);
            // ROI neredeyse tam kareyse kırpımın kazancı yok — tam-kare yoluna düş.
            if ((long)rw * rh < 0.90 * (long)W * H) cropRect = (rx, ry, rw, rh);
        }

        IReadOnlyList<PlateDetection> zoneDets;
        List<PlateDetection>? outOfZone = null;
        int minPlateW = (int)(W * _options.MinPlateWidthRatio);
        var (minPx, maxPx) = _options.ResolvePlateWidthLimits(W);
        List<SizeSkippedDetection>? skipped = null;

        // Boyut/mesafe sınırı: gözlem yuvaları dağıtılmadan ÖNCE (büyük ama sınır dışı bölge-dışı plakalar yuvaları
        // tüketmesin). Atlanan tespit atılmaz, LastSizeSkipped ile bildirilir (aktif izleme/yön/canlı kutu sinyali).
        bool Eligible(PlateDetection d)
        {
            if (d.Confidence < _options.MinConfidence || d.Width < minPlateW) return false;
            if (d.Width >= minPx && (maxPx == 0 || d.Width <= maxPx)) return true;
            Interlocked.Increment(ref _sizeDropped);
            var r = ExpandAndClamp(d, W, H, _options.CropPaddingRatio);
            (skipped ??= new()).Add(new SizeSkippedDetection(new PlateBox(r.Left, r.Top, r.Width, r.Height), d.Width, InRoi(d, W, H)));
            return false;
        }

        if (cropRect is { } cr)
        {
            // Dedektör yalnız ROI sınır kutusunu görür; kutular tam-kare koordinatlarında döner.
            var mapped = _detector.Detect(image, new SKRectI(cr.X, cr.Y, cr.X + cr.Wd, cr.Y + cr.Ht));

            bool fullFrameTurn = _options.ObservationMaxPerFrame > 0 &&
                                 (++_frameTick % Math.Max(1, _options.FullFrameEveryN) == 0);

            var zone = new List<PlateDetection>();
            foreach (var d in mapped)
            {
                if (!Eligible(d)) continue;
                if (InRoi(d, W, H)) zone.Add(d);
                else if (!fullFrameTurn && _options.ObservationMaxPerFrame > 0) (outOfZone ??= new()).Add(d);
            }
            zoneDets = zone;

            if (fullFrameTurn)
            {
                // Tam-kare gözlem taraması: yalnız ROI dışındakiler (bölge içi zaten kırpım geçişinde).
                foreach (var d in _detector.Detect(image, fullRect))
                {
                    // Önce bölge: bölge içindekiler kırpım geçişinde zaten değerlendirildi (iki kez SAYILMASINLAR).
                    if (InRoi(d, W, H)) continue;
                    if (Eligible(d)) (outOfZone ??= new()).Add(d);
                }
            }
        }
        else
        {
            var dets = _detector.Detect(image, fullRect);
            var zone = new List<PlateDetection>(dets.Count);
            foreach (var d in dets)
            {
                if (!Eligible(d)) continue;
                if (InRoi(d, W, H)) zone.Add(d);
                else if (_options.ObservationMaxPerFrame > 0) (outOfZone ??= new()).Add(d);
            }
            zoneDets = zone;
        }

        var results = new List<(PlateReading, SKBitmap)>(zoneDets.Count + 2);
        foreach (var det in zoneDets)
            RecognizeOne(image, det, inZone: true, wantCrops, results);

        if (outOfZone != null)
        {
            // Kare başına en fazla N gözlem OCR'ı; en büyük (en okunabilir) plakalar önce.
            outOfZone.Sort((a, b) => b.Width.CompareTo(a.Width));
            int n = Math.Min(_options.ObservationMaxPerFrame, outOfZone.Count);
            for (int i = 0; i < n; i++)
                RecognizeOne(image, outOfZone[i], inZone: false, wantCrops, results);
        }

        LastSizeSkipped = skipped != null ? skipped.ToArray() : Array.Empty<SizeSkippedDetection>();
        return results;
    }

    /// <summary>Kalite kapısının OCR öncesi elediği kırpıntı sayısı (sağlık/izleme).</summary>
    public long QualityDroppedCount => Interlocked.Read(ref _qualityDropped);
    private long _qualityDropped;

    /// <summary>Geometri kapısında (plaka olamayacak şekil) elenen tespit sayısı — saha teşhisi.</summary>
    public long AspectDroppedCount => Interlocked.Read(ref _aspectDropped);
    private long _aspectDropped;

    /// <summary>
    /// Boyut/mesafe sınırı (<see cref="AnprOptions.MinPlateWidthPx"/>, <see cref="AnprOptions.MaxPlateWidthPx"/>,
    /// kalibrasyonlu mesafe sınırları) nedeniyle OCR'ı atlanan tespit sayısı. Sınır kapalıyken daima 0.
    /// </summary>
    public long SizeDroppedCount => Interlocked.Read(ref _sizeDropped);
    private long _sizeDropped;

    /// <summary>
    /// Son kare-düzeyi okuma çağrısında (<see cref="Recognize(IFrameView)"/>, <see cref="RecognizeWithCrops(IFrameView)"/>
    /// ve aşırı yüklemeleri) boyut/mesafe sınırı yüzünden OCR'ı atlanan tespitler. Her çağrıda yenilenir; yoksa boş.
    /// <para>⚠️ Motor kamera başına ve tek iş parçacığından kullanılır (SDK işçisi böyle kullanır); aynı motorda eşzamanlı
    /// çağrılarda bu liste çağrıya bağlanamaz.</para>
    /// </summary>
    public IReadOnlyList<SizeSkippedDetection> LastSizeSkipped { get; private set; } = Array.Empty<SizeSkippedDetection>();

    /// <summary>
    /// TR gramerine UYMADIĞI için geçersiz sayılan okuma sayısı. Bu okumalar CameraWorker'da tamamen
    /// atılır (çöp okumaları elemek için doğru davranış) ama sayaç olmadan SESSİZ bir kayıptı: gerçek
    /// bir plaka (desteklenmeyen özel format, aşırı bozuk kare) reddedilse fark edilemezdi. Sayacın
    /// sürekli artması, ya sahada desteklenmeyen bir plaka tipi ya da OCR bozulması demektir.
    /// </summary>
    public long TrRejectedCount => Interlocked.Read(ref _trRejected);
    private long _trRejected;

    private void RecognizeOne(IFrameView image, PlateDetection det, bool inZone, bool wantCrops,
        List<(PlateReading, SKBitmap)> results)
    {
        var rect = ExpandAndClamp(det, image.Width, image.Height, _options.CropPaddingRatio);
        if (rect.Width < 2 || rect.Height < 2) return;

        // Geometri kapısı: plaka olamayacak şekilli kutuyu OCR'a hiç sokma (uydurma okuma + boşa CPU).
        // Kalite kapısından ÖNCE, çünkü bu tamamen aritmetik (kırpıntı piksellerine dokunmaz).
        if (!HasPlateLikeAspect(rect.Width, rect.Height, _options.MinPlateAspectRatio))
        {
            Interlocked.Increment(ref _aspectDropped);
            return;
        }

        // Kırpıntı: bitmap görünümünde kareyle piksel paylaşır (kopyasız), YUV görünümünde küçük bir kopyadır.
        var crop = image.ExtractRgba(rect);
        bool cropHandedOver = false;
        try
        {
        // Kalite kapısı: bulanık / aşırı karanlık / patlamış kırpıntı OCR'a girmez (oylamayı kirletmez).
        if (_options.CropQualityGate)
        {
            var q = CropQualityScorer.Score(crop);
            if ((_options.QualityBlurMin > 0 && q.BlurVar < _options.QualityBlurMin) ||
                q.DarkPct > _options.QualityDarkMaxPct ||
                q.BrightPct > _options.QualityBrightMaxPct)
            {
                Interlocked.Increment(ref _qualityDropped);
                return;
            }
        }
        SKBitmap? deskewed = null;
        if (_options.DeskewPlates)
        {
            deskewed = PlateRectifier.Deskew(crop);
            if (ReferenceEquals(deskewed, crop)) deskewed = null; // değişmediyse kopya yok
        }
        var ocr = _recognizer.Recognize(deskewed ?? crop);
        deskewed?.Dispose();

        // HEDEFLİ İKİNCİ OKUMA (#91): kuyruk "dolgu mu, bir karakter daha mı" belirsizse kırpıntıyı
        // GENİŞ al ve BİR KEZ daha oku. Küresel dolgu artışı ölçülüp reddedildi (bkz. AnprOptions
        // .AmbiguousTailRecropPad remarks): zararı kazancından büyük. Bedel yalnız burada ödenir.
        if (_options.AmbiguousTailRecropPad > _options.CropPaddingRatio &&
            HasAmbiguousTail(ocr, _options.AmbiguousTailMaxGap))
        {
            var wide = ExpandAndClamp(det, image.Width, image.Height, _options.AmbiguousTailRecropPad);
            if (wide.Width > rect.Width && wide.Height >= rect.Height)
            {
                Interlocked.Increment(ref _tailRecropAttempts);
                using var wideCrop = image.ExtractRgba(wide);
                var wideOcr = _recognizer.Recognize(wideCrop);
                // MUHAFAZAKÂR KABUL: yalnız "aynı önek + tam 1 karakter DAHA UZUN + en az o kadar
                // güvenli" ise kabul et. Böylece ikinci okuma bir karakter EKLEYEBİLİR ama var olan
                // karakterleri DEĞİŞTİREMEZ; yanlış-okumayı yanlış-okumayla takas etme riski yok.
                if (wideOcr.Text.Length == ocr.Text.Length + 1 &&
                    wideOcr.Text.StartsWith(ocr.Text, StringComparison.Ordinal) &&
                    wideOcr.MeanConfidence >= ocr.MeanConfidence)
                {
                    ocr = wideOcr;
                    Interlocked.Increment(ref _tailRecropAccepted);
                }
            }
        }

        var reading = BuildReading(ocr, new PlateBox(rect.Left, rect.Top, rect.Width, rect.Height), det.Confidence, inZone,
                                   det.Width, image.Width);
        if (reading != null)
        {
            SKBitmap? keep = null;
            if (wantCrops)
            {
                // Görünüm zaten bağımsız kopya verdiyse ikinci kez kopyalama: sahipliği çağırana devret.
                if (image.ExtractIsOwnedCopy) { keep = crop; cropHandedOver = true; }
                else keep = crop.Copy();
            }
            results.Add((reading, keep!));
        }
        }
        finally { if (!cropHandedOver) crop.Dispose(); }
    }

    /// <summary>Birkaç kırpıntıyı füzyonla birleştirip füzyon görüntüsünde OCR çalıştırır → tek okuma.</summary>
    public PlateReading? RecognizeFused(IReadOnlyList<SKBitmap> crops)
    {
        if (crops == null || crops.Count == 0) return null;
        using var fused = PlateFuser.Fuse(crops);
        var ocr = _recognizer.Recognize(fused);
        return BuildReading(ocr, default, 1.0f);
    }

    private PlateReading? BuildReading(Recognition.PlateOcrResult ocr, PlateBox box, float detConf, bool inZone = true,
                                       int plateWidthPx = 0, int frameWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(ocr.Text)) return null;

        bool validTr = false;
        string text = ocr.Text;
        if (_options.ApplyTurkishValidation)
        {
            var tr = TurkishPlateNormalizer.Normalize(ocr.Text);
            validTr = tr.IsValid;
            if (validTr) text = tr.Normalized!;
            else Interlocked.Increment(ref _trRejected);   // sessiz kaybı ölçülebilir kıl
        }

        return new PlateReading
        {
            Text = text,
            RawText = ocr.Text,
            Confidence = detConf * ocr.MeanConfidence,
            DetectorConfidence = detConf,
            OcrConfidence = ocr.MeanConfidence,
            Box = box,
            IsValidTr = validTr,
            SlotProbs = ocr.SlotProbs,
            OcrAlphabet = ocr.Alphabet,
            InZone = inZone,
            PlateWidthPx = plateWidthPx > 0 ? plateWidthPx : null,
            EstimatedDistanceMeters = plateWidthPx > 0 ? _options.DistanceCalibration?.EstimateMeters(plateWidthPx, frameWidth) : null,
        };
    }

    /// <summary>
    /// Kırpıntı şekli bir plakaya benziyor mu (en/boy oranı ≥ eşik). TR plakası daima yatıktır;
    /// dikey/parçalı kutu dedektör hatasıdır ve OCR ondan uydurma plaka üretir. minRatio ≤ 0 = kapalı.
    /// </summary>
    internal static bool HasPlateLikeAspect(int width, int height, float minRatio)
    {
        if (minRatio <= 0) return true;            // kapalı
        if (height <= 0) return false;             // dejenere kutu
        return (float)width / height >= minRatio;
    }

    /// <summary>
    /// Kaç kez "kuyruk belirsiz" görülüp geniş kırpıntıyla ikinci okuma DENENDİ.
    /// (<see cref="AnprOptions.AmbiguousTailRecropPad"/> kapalıyken her zaman 0.)
    /// </summary>
    public long TailRecropAttempts => Interlocked.Read(ref _tailRecropAttempts);
    private long _tailRecropAttempts;

    /// <summary>
    /// İkinci okumanın KABUL edildiği sayı (muhafazakâr ölçüt: aynı önek + 1 karakter uzun + en az o
    /// kadar güvenli). Denendi/kabul oranı, ayarın sahada işe yarayıp yaramadığının doğrudan ölçüsüdür;
    /// sayaç olmadan ayar "açık ama etkisiz" olabilir ve fark edilmezdi.
    /// </summary>
    public long TailRecropAccepted => Interlocked.Read(ref _tailRecropAccepted);
    private long _tailRecropAccepted;

    /// <summary>
    /// Son gerçek karakterden SONRAKİ slotta karar belirsiz mi: P(dolgu) − P(en iyi karakter) &lt; maxGap.
    /// Yani model "burada bir karakter daha var" ile "plaka bitti" arasında kararsızsa true.
    /// Dolgu slotu yoksa (plaka tüm slotları doldurmuş) belirsizlik TANIMSIZDIR → false.
    /// </summary>
    internal static bool HasAmbiguousTail(PlateOcrResult ocr, float maxGap)
    {
        if (maxGap <= 0f || ocr.SlotProbs is not { Length: > 0 } probs ||
            ocr.Alphabet is not { Length: > 0 } alphabet) return false;

        int vocab = alphabet.Length;
        int slots = probs.Length / vocab;
        int padIdx = alphabet.IndexOf('_');
        if (padIdx < 0 || slots < 1) return false;

        for (int s = 0; s < slots; s++)
        {
            int start = s * vocab;
            float bestP = float.NegativeInfinity, bestNonPad = float.NegativeInfinity;
            int bestIdx = 0;
            for (int c = 0; c < vocab; c++)
            {
                float v = probs[start + c];
                if (v > bestP) { bestP = v; bestIdx = c; }
                if (c != padIdx && v > bestNonPad) bestNonPad = v;
            }
            if (bestIdx != padIdx) continue;      // hâlâ gerçek karakter — kuyruk başlamadı
            return bestP - bestNonPad < maxGap;   // ilk dolgu slotu: karar ne kadar net?
        }
        return false;                             // dolgu slotu yok → belirsizlik tanımsız
    }

    private static SKRectI ExpandAndClamp(PlateDetection det, int imgW, int imgH, float padRatio)
    {
        int padX = (int)(det.Width * padRatio);
        int padY = (int)(det.Height * padRatio);

        int left = Math.Max(0, det.X - padX);
        int top = Math.Max(0, det.Y - padY);
        int right = Math.Min(imgW, det.X + det.Width + padX);
        int bottom = Math.Min(imgH, det.Y + det.Height + padY);

        return new SKRectI(left, top, Math.Max(left, right), Math.Max(top, bottom));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _detector.Dispose();
        _recognizer.Dispose();
        _rectifier?.Dispose();
        _superResolver?.Dispose();
        _disposed = true;
    }
}
