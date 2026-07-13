using Anpr.Service.Configuration;
using Anpr.Service.Events;
using Anpr.Service.Workers;
using Microsoft.Extensions.Options;

namespace Anpr.Service;

/// <summary>
/// Kalp-atışı İTMESİ (E5): WebhookUrl doluysa HeartbeatSeconds aralığıyla sağlık özeti POST edilir —
/// otopark yazılımı 'servis yaşıyor mu'yu çekmek zorunda kalmaz; kesilirse alarm üretebilir.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private readonly AnprServiceOptions _opts;
    private readonly AnprCameraSupervisor _sup;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(IOptions<AnprServiceOptions> opts, AnprCameraSupervisor sup,
        IHttpClientFactory http, ILogger<HeartbeatService> logger)
    {
        _opts = opts.Value; _sup = sup; _http = http; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.WebhookUrl) || _opts.HeartbeatSeconds <= 0) return;
        var period = TimeSpan.FromSeconds(Math.Max(5, _opts.HeartbeatSeconds));
        bool lastOk = true;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(period, ct); } catch (OperationCanceledException) { break; }
            try
            {
                var body = new
                {
                    type = "heartbeat",
                    timestamp = DateTimeOffset.Now,
                    cameras = _sup.Workers.Select(w => new
                    {
                        id = w.CameraId,
                        connected = w.IsConnected,
                        framesProcessed = w.FramesProcessed,
                        eventsEmitted = w.EventsEmitted,
                        qualityDropped = w.QualityDropped,
                        restartCount = w.RestartCount,
                    }),
                };
                using var resp = await _http.CreateClient().PostAsJsonAsync(_opts.WebhookUrl, body, ct);
                if (resp.IsSuccessStatusCode != lastOk)
                {
                    lastOk = resp.IsSuccessStatusCode;
                    _logger.LogWarning("heartbeat hedefi durumu değişti: {Ok}", lastOk ? "ulaşılıyor" : "ULAŞILAMIYOR");
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                if (lastOk) { lastOk = false; _logger.LogWarning("heartbeat hedefi ULAŞILAMIYOR"); }
            }
        }
    }
}
