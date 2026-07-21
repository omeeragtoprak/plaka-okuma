using System.Text.RegularExpressions;
using Anpr.Core;
using Anpr.Sdk.Configuration;
using Anpr.Sdk.Events;
using Anpr.Sdk.Frames;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anpr.Sdk.Workers;

/// <summary>
/// Kameranın etkin ROI tanımını çözer (ör. servis paneli diske kaydedilmiş ROI'yi döndürür).
/// null kayıt = yapılandırmadaki değer kullanılır.
/// </summary>
public delegate string RoiResolver(string cameraId, string configuredRoi);

/// <summary>
/// Tüm kameraları yönetir: yapılandırmadan her kamera için frame source + AnprEngine + CameraWorker
/// kurar, başlatır ve durdurur. /health endpoint'i worker durumlarını buradan okur.
/// </summary>
public sealed partial class AnprCameraSupervisor : IHostedService
{
    private readonly AnprServiceOptions _options;
    private readonly IReadOnlyList<IEventSink> _sinks;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AnprCameraSupervisor> _logger;

    private readonly List<CameraWorker> _workers = new();
    private readonly object _workersLock = new();
    // Her başlatmada YENİDEN oluşturulur: StopAsync token'ı iptal edip dispose ettiği için, yeniden
    // başlatılabilir olmalı (PMS gömülümü gece bakımında Stop→Start yapabilir). Tek-kullanımlık CTS,
    // ikinci Start'ta worker.Start(_cts.Token)'da ObjectDisposedException'a ve sessiz ölü servise yol açardı.
    private CancellationTokenSource? _cts;

    /// <summary>/health tarafından okunur. Başlangıç/kapanışta liste değişirken anlık görüntü döner
    /// (canlı mutable listeyi enumerate edip InvalidOperationException almamak için).</summary>
    public IReadOnlyList<CameraWorker> Workers
    {
        get { lock (_workersLock) return _workers.ToArray(); }
    }
    public DateTimeOffset StartedAtUtc { get; private set; }

    /// <summary>CPUID/mimari özeti (arm64 ya da x64 + sse42/avx/avx2 bayrakları) — /health'te raporlanır.</summary>
    public string CpuFeatures { get; private set; } = "";

    private readonly Events.FrequentPlateRegistry _frequent;
    private readonly RoiResolver _roiResolver;

    public AnprCameraSupervisor(
        IOptions<AnprServiceOptions> options,
        IEnumerable<IEventSink> sinks,
        Events.FrequentPlateRegistry frequent,
        ILoggerFactory loggerFactory,
        RoiResolver? roiResolver = null)
    {
        _options = options.Value;
        _sinks = sinks.ToList();
        _frequent = frequent;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AnprCameraSupervisor>();
        _roiResolver = roiResolver ?? ((_, configured) => configured);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartedAtUtc = DateTimeOffset.UtcNow;
        _cts = new CancellationTokenSource(); // yeniden başlatılabilirlik: taze token kaynağı

        string detector = ResolveModelPath(_options.DetectorModelPath);
        string ocr = ResolveModelPath(_options.OcrModelPath);
        string ocrCfg = ResolveModelPath(_options.OcrConfigPath);

        _logger.LogInformation("ANPR başlıyor | dedektör={Det} | ocr={Ocr} | threads={Threads} | kameralar={Count}",
            Path.GetFileName(detector), Path.GetFileName(ocr), _options.OnnxIntraOpThreads, _options.Cameras.Count);

        // CPUID/AVX probe (P1c): AVX'siz eski Intel'de 'illegal instruction' bilinen çökme sınıfı —
        // durum açılışta loglanır ve /health'te raporlanır (hedef-PC pilotunun ilk verisi).
        CpuFeatures = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
        if (System.Runtime.Intrinsics.X86.X86Base.IsSupported)
        {
            CpuFeatures += $" sse42={System.Runtime.Intrinsics.X86.Sse42.IsSupported}" +
                           $" avx={System.Runtime.Intrinsics.X86.Avx.IsSupported}" +
                           $" avx2={System.Runtime.Intrinsics.X86.Avx2.IsSupported}";
            if (!System.Runtime.Intrinsics.X86.Avx.IsSupported)
                _logger.LogWarning("Bu CPU AVX DESTEKLEMİYOR — ONNX Runtime performansı düşük olabilir; " +
                                   "OpenVINO 2026+ KULLANILAMAZ (AVX2 şart). Ölçüm sonuçlarını buna göre yorumlayın.");
        }
        _logger.LogInformation("CPU özellikleri: {Cpu}", CpuFeatures);

        // Model bütünlük kontrolü (yapılandırılmışsa): bozuk/yanlış sürüm modelle sessizce çalışmayı önler.
        VerifyModelHash(detector, _options.DetectorModelSha256, "dedektör");
        VerifyModelHash(ocr, _options.OcrModelSha256, "ocr");

        // ffmpeg erişilebilirlik probe'u (H2): kamera varsa ffmpeg şart. Yoksa StartFfmpeg her denemede
        // Win32Exception atıp SESSİZCE sonsuz yeniden-bağlanır (kare yok, bariyer hiç açılmaz, StartAsync
        // başarı döner). Açılışta NET hata ver — model-hash guard'ıyla aynı "sessizce çalışma" ailesi.
        if (_options.Cameras.Count > 0)
            Frames.FfmpegFrameSource.EnsureAvailable(_options.Ffmpeg);

        if (_options.Cameras.Count == 0)
            _logger.LogWarning("Yapılandırmada kamera yok (Anpr:Cameras boş).");

        foreach (var cam in _options.Cameras)
        {
            try
            {
                string rtspUrl = BuildRtspUrl(cam);

                var anprOptions = new AnprOptions
                {
                    DetectorModelPath = detector,
                    OcrModelPath = ocr,
                    OcrConfigPath = ocrCfg,
                    OnnxIntraOpThreads = _options.OnnxIntraOpThreads,
                    OnnxAllowSpinning = _options.OnnxAllowSpinning,
                    MinConfidence = _options.MinConfidence,
                    DetectorScoreThreshold = _options.DetectorScoreThreshold,
                    CropPaddingRatio = _options.CropPaddingRatio,
                    ApplyTurkishValidation = _options.ApplyTurkishValidation,
                    MinPlateWidthRatio = _options.MinPlateWidthRatio,
                    DeskewPlates = _options.DeskewPlates,
                    ObservationMaxPerFrame = _options.ObservationMaxPerFrame,
                    RoiCropDetector = _options.RoiCropDetector,
                    FullFrameEveryN = _options.FullFrameEveryN,
                    CropQualityGate = _options.CropQualityGate,
                    QualityBlurMin = _options.QualityBlurMin,
                    QualityDarkMaxPct = _options.QualityDarkMaxPct,
                    QualityBrightMaxPct = _options.QualityBrightMaxPct,
                    EnhanceRectify = _options.EnhanceRectify,
                    EnhanceTriggerOcr = _options.EnhanceTriggerOcr,
                    EnhanceBudgetMs = _options.EnhanceBudgetMs,
                    WpodModelPath = _options.WpodModelPath,
                    EnhanceSr = _options.EnhanceSr,
                    SrMaxWidthPx = _options.SrMaxWidthPx,
                    SrModelPath = _options.SrModelPath,
                    RoiPolygon = _roiResolver(cam.Id, cam.Roi),
                };

                var engine = AnprEngine.Create(anprOptions);
                var sourceLogger = _loggerFactory.CreateLogger($"FrameSource:{cam.Id}");
                var frameSource = new FfmpegFrameSource(cam.Id, rtspUrl, _options.Ffmpeg, _options.Fps, sourceLogger);
                var workerLogger = _loggerFactory.CreateLogger($"Camera:{cam.Id}");

                var worker = new CameraWorker(cam, _options, frameSource, engine, _sinks, _frequent, workerLogger);
                worker.Start(_cts!.Token); // StartAsync başında oluşturuldu, kesin non-null
                lock (_workersLock) _workers.Add(worker);

                _logger.LogInformation("[{Cam}] '{Name}' kuruldu: {Url}", cam.Id, cam.Name, MaskUrl(rtspUrl));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Cam}] başlatılamadı", cam.Id);
            }
        }

        // Sessiz yanlış-çalışmaya izin verme: yapılandırmada kamera VARDI ama hiçbiri başlayamadıysa
        // (ör. model bulunamadı/bozuk, tüm RTSP kurulum hataları), fırlat. PMS "çalışıyor" sanıp hiç
        // olay almamaktansa açık hata almalı. Kısmi başarı (bazı kameralar) kabul edilir.
        int started;
        lock (_workersLock) started = _workers.Count;
        if (_options.Cameras.Count > 0 && started == 0)
            throw new InvalidOperationException(
                $"ANPR başlatılamadı: {_options.Cameras.Count} kameranın hiçbiri kurulamadı " +
                "(model bulunamadı/bozuk ya da tüm kamera bağlantıları başarısız — yukarıdaki loglara bakın).");

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ANPR durduruluyor...");
        _cts?.Cancel();

        CameraWorker[] snapshot;
        lock (_workersLock)
        {
            snapshot = _workers.ToArray();
            _workers.Clear();
        }
        foreach (var w in snapshot)
        {
            try { await w.DisposeAsync(); } catch { /* yoksay */ }
        }
        _cts?.Dispose();
        _cts = null; // sonraki StartAsync taze token kaynağı oluşturur
    }

    private static string BuildRtspUrl(CameraOptions cam)
    {
        if (!string.IsNullOrWhiteSpace(cam.RtspUrl)) return cam.RtspUrl;
        // Hikvision varsayılan ana akış
        return $"rtsp://{Uri.EscapeDataString(cam.Username)}:{Uri.EscapeDataString(cam.Password)}@{cam.Ip}:554/Streaming/Channels/101";
    }

    // Göreli yolları uygulama çıktı dizinine (models/ buraya kopyalanır) ya da çalışma dizinine göre çöz.
    // Yapılandırmada hash verilmişse dosyanın SHA-256'sını doğrular; uyuşmazlıkta servis BAŞLATILMAZ
    // (R-16: bozuk indirme / yanlış sürümle sessiz çalışma, yanlış okumalardan daha tehlikelidir).
    private void VerifyModelHash(string path, string? expectedSha256, string label)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return;
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        string actual = Convert.ToHexString(sha.ComputeHash(fs));
        if (!actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{label} modeli SHA-256 uyuşmuyor: beklenen={expectedSha256}, dosya={actual} ({path}). " +
                "Model dosyası bozuk veya yanlış sürüm — servis başlatılmadı.");
        _logger.LogInformation("{Label} modeli SHA-256 doğrulandı", label);
    }

    private static string ResolveModelPath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path)) return path;

        var candidates = new[]
        {
            path,
            Path.Combine(AppContext.BaseDirectory, path),
            Path.Combine(Directory.GetCurrentDirectory(), path),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return Path.GetFullPath(c);

        // Bulunamadıysa en olası yolu döndür (motor net bir hata fırlatacak).
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static string MaskUrl(string url) => PasswordRegex().Replace(url, "$1****$3");

    [GeneratedRegex(@"(rtsp://[^:]+:)([^@]*)(@)")]
    private static partial Regex PasswordRegex();
}
