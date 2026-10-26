using Anpr.Service.Events;
using Anpr.Sdk.Events;

namespace Anpr.Service.Panel;

/// <summary>Bizim plaka olaylarımızı panele (SSE) iter — mevcut IEventSink desenine 4. üye.</summary>
public sealed class PanelEventSink : IEventSink
{
    private readonly PanelHub _hub;

    public PanelEventSink(PanelHub hub) => _hub = hub;

    public Task PublishAsync(PlateEvent e, CancellationToken cancellationToken)
    {
        _hub.Publish(new
        {
            source = "ours",
            scope = e.Scope,
            cameraId = e.CameraId,
            plate = e.Plate,
            compact = e.PlateCompact,
            timeIso = e.Timestamp.ToLocalTime().ToString("o"),
            conf = Math.Round(e.OcrConfidence, 2),
            ev = e.EvidencePath,
            lowConf = e.LowConfidence,
            cands = e.Candidates == null ? null : string.Join(" ", e.Candidates.Select(c => c.Compact)),
            box = new { x = e.Box.X, y = e.Box.Y, w = e.Box.Width, h = e.Box.Height },
        });
        return Task.CompletedTask;
    }
}
