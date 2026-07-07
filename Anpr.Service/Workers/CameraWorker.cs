using Anpr.Core;
using Anpr.Core.Confirmation;
using Anpr.Service.Configuration;
using Anpr.Service.Events;
using Anpr.Service.Frames;
using Microsoft.Extensions.Logging;

namespace Anpr.Service.Workers;

/// <summary>
/// Tek bir kamera için okuma döngüsü: en güncel kareyi al -> tespit+oku -> TR doğrula
/// -> çok kareli onay (PlateConfirmer) -> geçerli, onaylı ve YENİ ise olay yayınla.
/// Her worker kendi AnprEngine, frame source ve confirmer'ına sahiptir.
/// </summary>
public sealed class CameraWorker : IAsyncDisposable
{
    private readonly CameraOptions _camera;
    private readonly AnprServiceOptions _options;
    private readonly IFrameSource _frameSource;
    private readonly AnprEngine _engine;
    private readonly IReadOnlyList<IEventSink> _sinks;
    private readonly ILogger _logger;
    private readonly PlateConfirmer _confirmer;
    private readonly List<Task> _pendingPublishes = new();

    private CancellationToken _ct;
    private Task? _loopTask;

    public string CameraId => _camera.Id;
    public string CameraName => _camera.Name;
    public bool IsConnected => _frameSource.IsConnected;
    public DateTimeOffset? LastFrameUtc => _frameSource.LastFrameUtc;
    public long FramesReceived => _frameSource.FramesReceived;
    public int RestartCount => _frameSource.RestartCount;
    public long FramesProcessed { get; private set; }
    public long EventsEmitted { get; private set; }

    // Panel: son işlenen kare (ffmpeg'den gelen JPEG bytes — kopyasız) + son tespit kutuları.
    private volatile byte[]? _lastFrameJpeg;
    private volatile IReadOnlyList<PlateBox>? _lastBoxes;
    public byte[]? LastFrameJpeg => _lastFrameJpeg;
    /// <summary>Kaynağın EN SON karesi (ffmpeg varış hızında; işleme hızından bağımsız) — canlı MJPEG için.</summary>
    public byte[]? PeekSourceFrame() => _frameSource.TryGetLatestFrame();
    public IReadOnlyList<PlateBox> LastBoxes => _lastBoxes ?? Array.Empty<PlateBox>();
    public string CurrentRoi => _engine.RoiSpec;
    public void ApplyRoi(string? spec) => _engine.SetRoi(spec);
    public DateTimeOffset? LastEventUtc { get; private set; }
    public string? LastPlate { get; private set; }

    public CameraWorker(
        CameraOptions camera,
        AnprServiceOptions options,
        IFrameSource frameSource,
        AnprEngine engine,
        IReadOnlyList<IEventSink> sinks,
        ILogger logger)
    {
        _camera = camera;
        _options = options;
        _frameSource = frameSource;
        _engine = engine;
        _sinks = sinks;
        _logger = logger;
        _confirmer = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(Math.Max(1, options.DedupWindowSeconds)),
            SettleWindow = TimeSpan.FromSeconds(Math.Max(0, options.SettleWindowSeconds)),
            MinConfirmations = Math.Max(1, options.MinConfirmations),
            CharacterVoting = options.CharacterVoting,
            ConstrainedDecoding = options.ConstrainedDecoding,
            MergeAcrossProvince = options.MergeAcrossProvince,
            FuzzyMergeDistance = options.FuzzyMergeDistance,
            EarlyEmitMinOcr = options.EarlyEmitMinOcr,
        });
    }

    public void Start(CancellationToken cancellationToken)
    {
        _ct = cancellationToken;
        _frameSource.Start(cancellationToken);
        _loopTask = Task.Run(() => LoopAsync(cancellationToken), CancellationToken.None);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        double activeFps = _options.Fps > 0 ? _options.Fps : 1;
        double idleFps = _options.IdleFps > 0 ? _options.IdleFps : activeFps;
        var activeHold = TimeSpan.FromSeconds(Math.Max(0, _options.ActiveHoldSeconds));
        DateTimeOffset activeUntil = DateTimeOffset.MinValue;

        _logger.LogInformation("[{Cam}] okuma döngüsü başladı (aktif {Fps} / boşta {Idle} fps, onay={Min}, oylama={Vote})",
            _camera.Id, activeFps, idleFps, _options.MinConfirmations, _options.CharacterVoting);

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            var frame = _frameSource.TryGetLatestFrame();
            if (frame != null)
            {
                try
                {
                    if (ObserveFrame(frame, now)) activeUntil = now + activeHold; // tespit -> aktif moda geç
                }
                catch (Exception ex) { _logger.LogWarning("[{Cam}] kare işleme hatası: {Msg}", _camera.Id, ex.Message); }
            }

            foreach (var best in _confirmer.DrainReady(now))
                Emit(best, now);

            ReapPublishes();

            // Uyarlanabilir hız: araç varken aktif, yoksa boşta fps.
            double fps = now < activeUntil ? activeFps : idleFps;
            try { await Task.Delay(TimeSpan.FromSeconds(1.0 / fps), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // Tespit olup olmadığını döner (aktif moda geçiş için).
    private bool ObserveFrame(byte[] frame, DateTimeOffset now)
    {
        FramesProcessed++;
        var readings = _engine.Recognize(frame);
        _lastFrameJpeg = frame;
        _lastBoxes = readings.Select(r => r.Box).ToArray();

        foreach (var r in readings)
        {
            if (!r.IsValidTr) continue;                          // çöp/geçersiz okumaları gönderme
            if (r.Confidence < _options.EmitMinConfidence) continue;
            if (r.OcrConfidence < _options.EmitMinOcrConfidence) continue; // belirsiz/kötü okuma -> ele
            _confirmer.Observe(r, now);
        }
        return readings.Count > 0; // herhangi bir plaka tespiti = araç var
    }

    private void Emit(PlateReading best, DateTimeOffset now)
    {
        string compact = Compact(best.Text);
        if (compact.Length == 0) return;

        // Emit-seviyesi güven tabanı: birleşik/uzlaşı okumanın güveni de tabanı geçmeli
        // (taban kare-başında uygulanıyor ama uzlaşının güveni altına düşebilir -> sızıntıyı keser).
        if (best.OcrConfidence < _options.EmitMinOcrConfidence) return;
        if (best.Confidence < _options.EmitMinConfidence) return;

        var evt = new PlateEvent
        {
            Timestamp = now,
            CameraId = _camera.Id,
            CameraName = _camera.Name,
            Plate = best.Text,
            PlateCompact = compact,
            RawText = best.RawText,
            Confidence = best.Confidence,
            DetectorConfidence = best.DetectorConfidence,
            OcrConfidence = best.OcrConfidence,
            Box = best.Box,
        };

        EventsEmitted++;
        LastEventUtc = now;
        LastPlate = best.Text;

        _pendingPublishes.Add(PublishAsync(evt, _ct));
    }

    private async Task PublishAsync(PlateEvent evt, CancellationToken ct)
    {
        foreach (var sink in _sinks)
        {
            try { await sink.PublishAsync(evt, ct); }
            catch (Exception ex) { _logger.LogWarning("[{Cam}] sink hatası: {Msg}", _camera.Id, ex.Message); }
        }
    }

    private void ReapPublishes()
    {
        if (_pendingPublishes.Count == 0) return;
        _pendingPublishes.RemoveAll(t => t.IsCompleted);
    }

    private static string Compact(string text)
    {
        Span<char> buf = stackalloc char[text.Length];
        int n = 0;
        foreach (char c in text)
            if (char.IsLetterOrDigit(c)) buf[n++] = c;
        return new string(buf[..n]);
    }

    public async ValueTask DisposeAsync()
    {
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* yoksay */ }
        }

        // Bekleyen yayınları en fazla 5 sn boşalt (kapanışta son olayları kaybetme).
        try { await Task.WhenAll(_pendingPublishes).WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* yoksay */ }

        await _frameSource.DisposeAsync();
        _engine.Dispose();
    }
}
