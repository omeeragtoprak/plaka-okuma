using System.Text.RegularExpressions;
using Anpr.Core;
using Anpr.Service.Configuration;
using Anpr.Service.Events;
using Anpr.Service.Frames;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anpr.Service.Workers;

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
    private readonly CancellationTokenSource _cts = new();

    /// <summary>/health tarafından okunur. Başlangıç/kapanışta liste değişirken anlık görüntü döner
    /// (canlı mutable listeyi enumerate edip InvalidOperationException almamak için).</summary>
    public IReadOnlyList<CameraWorker> Workers
    {
        get { lock (_workersLock) return _workers.ToArray(); }
    }
    public DateTimeOffset StartedAtUtc { get; private set; }

    public AnprCameraSupervisor(
        IOptions<AnprServiceOptions> options,
        IEnumerable<IEventSink> sinks,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _sinks = sinks.ToList();
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AnprCameraSupervisor>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartedAtUtc = DateTimeOffset.UtcNow;

        string detector = ResolveModelPath(_options.DetectorModelPath);
        string ocr = ResolveModelPath(_options.OcrModelPath);
        string ocrCfg = ResolveModelPath(_options.OcrConfigPath);

        _logger.LogInformation("ANPR başlıyor | dedektör={Det} | ocr={Ocr} | threads={Threads} | kameralar={Count}",
            Path.GetFileName(detector), Path.GetFileName(ocr), _options.OnnxIntraOpThreads, _options.Cameras.Count);

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
                };

                var engine = AnprEngine.Create(anprOptions);
                var sourceLogger = _loggerFactory.CreateLogger($"FrameSource:{cam.Id}");
                var frameSource = new FfmpegFrameSource(cam.Id, rtspUrl, _options.Ffmpeg, _options.Fps, sourceLogger);
                var workerLogger = _loggerFactory.CreateLogger($"Camera:{cam.Id}");

                var worker = new CameraWorker(cam, _options, frameSource, engine, _sinks, workerLogger);
                worker.Start(_cts.Token);
                lock (_workersLock) _workers.Add(worker);

                _logger.LogInformation("[{Cam}] '{Name}' kuruldu: {Url}", cam.Id, cam.Name, MaskUrl(rtspUrl));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Cam}] başlatılamadı", cam.Id);
            }
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ANPR durduruluyor...");
        _cts.Cancel();

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
        _cts.Dispose();
    }

    private static string BuildRtspUrl(CameraOptions cam)
    {
        if (!string.IsNullOrWhiteSpace(cam.RtspUrl)) return cam.RtspUrl;
        // Hikvision varsayılan ana akış
        return $"rtsp://{Uri.EscapeDataString(cam.Username)}:{Uri.EscapeDataString(cam.Password)}@{cam.Ip}:554/Streaming/Channels/101";
    }

    // Göreli yolları uygulama çıktı dizinine (models/ buraya kopyalanır) ya da çalışma dizinine göre çöz.
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
