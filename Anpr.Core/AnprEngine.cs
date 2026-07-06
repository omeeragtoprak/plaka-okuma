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
    private volatile RoiPolygon? _roi;
    private volatile string _roiSpec;
    private bool _disposed;

    private AnprEngine(AnprOptions options, LicensePlateDetector detector, LicensePlateRecognizer recognizer)
    {
        _options = options;
        _detector = detector;
        _recognizer = recognizer;
        _roi = RoiPolygon.Parse(options.RoiPolygon);
        _roiSpec = options.RoiPolygon ?? "";
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

        return new AnprEngine(options, detector, recognizer);
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

        var detections = _detector.Detect(image);
        var readings = new List<PlateReading>(detections.Count);

        int minPlateW = (int)(image.Width * _options.MinPlateWidthRatio);
        foreach (var det in detections)
        {
            // OCR'ı yalnızca MinConfidence üstündeki tespitlere uygula (CPU tasarrufu).
            if (det.Confidence < _options.MinConfidence) continue;
            // Tetik bölgesi: çok küçük/uzak plakalara OCR yapma.
            if (det.Width < minPlateW) continue;
            // ROI: kapsam dışı tespitlere OCR yapma (kapsam hizalama + CPU tasarrufu).
            if (!InRoi(det, image.Width, image.Height)) continue;

            var rect = ExpandAndClamp(det, image.Width, image.Height, _options.CropPaddingRatio);
            if (rect.Width < 2 || rect.Height < 2) continue;

            using var crop = SkiaImaging.ExtractSubset(image, rect);
            SkiaSharp.SKBitmap? deskewed = null;
            if (_options.DeskewPlates)
            {
                deskewed = PlateRectifier.Deskew(crop);
                if (ReferenceEquals(deskewed, crop)) deskewed = null; // değişmediyse kopya yok
            }
            var ocr = _recognizer.Recognize(deskewed ?? crop);
            deskewed?.Dispose();

            var reading = BuildReading(ocr, new PlateBox(rect.Left, rect.Top, rect.Width, rect.Height), det.Confidence);
            if (reading != null) readings.Add(reading);
        }

        readings.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        return readings;
    }

    /// <summary>Recognize gibi ama her okumanın KIRPINTISINI da döner (çağıran sahiplenir, dispose eder).
    /// Çok-kareli füzyon için worker'ın kırpıntı saklaması gerektiğinde kullanılır.</summary>
    public IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> RecognizeWithCrops(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        using var image = SkiaImaging.DecodeRgba(imageBytes);
        var detections = _detector.Detect(image);
        var results = new List<(PlateReading, SKBitmap)>();
        int minPlateW = (int)(image.Width * _options.MinPlateWidthRatio);
        foreach (var det in detections)
        {
            if (det.Confidence < _options.MinConfidence) continue;
            if (det.Width < minPlateW) continue;
            if (!InRoi(det, image.Width, image.Height)) continue;
            var rect = ExpandAndClamp(det, image.Width, image.Height, _options.CropPaddingRatio);
            if (rect.Width < 2 || rect.Height < 2) continue;
            using var crop = SkiaImaging.ExtractSubset(image, rect);
            var ocr = _recognizer.Recognize(crop);
            var reading = BuildReading(ocr, new PlateBox(rect.Left, rect.Top, rect.Width, rect.Height), det.Confidence);
            if (reading != null) results.Add((reading, crop.Copy()));
        }
        return results;
    }

    /// <summary>Birkaç kırpıntıyı füzyonla birleştirip füzyon görüntüsünde OCR çalıştırır → tek okuma.</summary>
    public PlateReading? RecognizeFused(IReadOnlyList<SKBitmap> crops)
    {
        if (crops == null || crops.Count == 0) return null;
        using var fused = PlateFuser.Fuse(crops);
        var ocr = _recognizer.Recognize(fused);
        return BuildReading(ocr, default, 1.0f);
    }

    private PlateReading? BuildReading(Recognition.PlateOcrResult ocr, PlateBox box, float detConf)
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
        _disposed = true;
    }
}
