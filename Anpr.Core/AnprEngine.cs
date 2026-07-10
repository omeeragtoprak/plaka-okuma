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

        var detector = new LicensePlateDetector(
            options.DetectorModelPath,
            options.CreateSessionOptions(),
            options.DetectorScoreThreshold);

        var recognizer = new LicensePlateRecognizer(
            options.OcrModelPath,
            options.OcrConfigPath,
            options.CreateSessionOptions());

        string modelsDir = Path.GetDirectoryName(Path.GetFullPath(options.OcrModelPath)) ?? ".";
        string Resolve(string p) => Path.IsPathRooted(p) ? p : Path.Combine(modelsDir, Path.GetFileName(p));

        PlateRectifier2? rectifier = null;
        if (options.EnhanceRectify)
        {
            try
            {
                string wpod = Resolve(options.WpodModelPath);
                if (File.Exists(wpod))
                    rectifier = new PlateRectifier2(wpod, options.CreateSessionOptions());
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
                    sr = new PlateSuperResolver(srp, options.CreateSessionOptions());
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
        var results = RecognizeCore(image, wantCrops: false);
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
        return RecognizeCore(image, wantCrops: true);
    }

    /// <summary>Recognize gibi ama SKBitmap'ten, kırpıntılarla (canlı füzyon hattı için).</summary>
    public IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> RecognizeWithCrops(SKBitmap image)
        => RecognizeCore(image, wantCrops: true);

    private int _frameTick; // ROI-kırpım modunda tam-kare gözlem taraması sayacı (kamera başına motor = tek thread)

    // Ortak boru hattı: tespit -> (bölge içi hepsi + bölge dışı en büyük N "gözlem") -> kırp -> OCR.
    // ROI-kırpım modu: ana tespit ROI sınırlayıcı dikdörtgeninde koşar (plakalar dedektör girdisinde
    // büyür); gözlem için tam kare FullFrameEveryN karede bir ayrıca taranır.
    private List<(PlateReading Reading, SKBitmap Crop)> RecognizeCore(SKBitmap image, bool wantCrops)
    {
        ArgumentNullException.ThrowIfNull(image);

        var roi = _roi;
        int W = image.Width, H = image.Height;

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

        bool Eligible(PlateDetection d) => d.Confidence >= _options.MinConfidence && d.Width >= minPlateW;

        if (cropRect is { } cr)
        {
            using var sub = SkiaImaging.ExtractSubset(image, new SKRectI(cr.X, cr.Y, cr.X + cr.Wd, cr.Y + cr.Ht));
            var dets = _detector.Detect(sub);
            var mapped = new List<PlateDetection>(dets.Count);
            foreach (var d in dets)
                mapped.Add(d with { X = d.X + cr.X, Y = d.Y + cr.Y }); // tam-kare koordinatlarına taşı

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
                foreach (var d in _detector.Detect(image))
                {
                    if (!Eligible(d)) continue;
                    if (!InRoi(d, W, H)) (outOfZone ??= new()).Add(d);
                }
            }
        }
        else
        {
            var dets = _detector.Detect(image);
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

        return results;
    }

    /// <summary>Kalite kapısının OCR öncesi elediği kırpıntı sayısı (sağlık/izleme).</summary>
    public long QualityDroppedCount => Interlocked.Read(ref _qualityDropped);
    private long _qualityDropped;

    private void RecognizeOne(SKBitmap image, PlateDetection det, bool inZone, bool wantCrops,
        List<(PlateReading, SKBitmap)> results)
    {
        var rect = ExpandAndClamp(det, image.Width, image.Height, _options.CropPaddingRatio);
        if (rect.Width < 2 || rect.Height < 2) return;

        using var crop = SkiaImaging.ExtractSubset(image, rect);

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

        var reading = BuildReading(ocr, new PlateBox(rect.Left, rect.Top, rect.Width, rect.Height), det.Confidence, inZone);
        if (reading != null) results.Add((reading, wantCrops ? crop.Copy() : null!));
    }

    /// <summary>Birkaç kırpıntıyı füzyonla birleştirip füzyon görüntüsünde OCR çalıştırır → tek okuma.</summary>
    public PlateReading? RecognizeFused(IReadOnlyList<SKBitmap> crops)
    {
        if (crops == null || crops.Count == 0) return null;
        using var fused = PlateFuser.Fuse(crops);
        var ocr = _recognizer.Recognize(fused);
        return BuildReading(ocr, default, 1.0f);
    }

    private PlateReading? BuildReading(Recognition.PlateOcrResult ocr, PlateBox box, float detConf, bool inZone = true)
    {
        if (string.IsNullOrWhiteSpace(ocr.Text)) return null;

        bool validTr = false;
        string text = ocr.Text;
        if (_options.ApplyTurkishValidation)
        {
            var tr = TurkishPlateNormalizer.Normalize(ocr.Text);
            validTr = tr.IsValid;
            if (validTr) text = tr.Normalized!;
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
        };
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
