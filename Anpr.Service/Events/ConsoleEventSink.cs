using Microsoft.Extensions.Logging;

namespace Anpr.Service.Events;

/// <summary>Olayları yapısal log olarak yazar.</summary>
public sealed class ConsoleEventSink : IEventSink
{
    private readonly ILogger<ConsoleEventSink> _logger;

    public ConsoleEventSink(ILogger<ConsoleEventSink> logger) => _logger = logger;

    public Task PublishAsync(PlateEvent e, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "PLAKA {Plate} | {CameraName} ({CameraId}) | güven={Confidence:P0} (det={Det:P0} ocr={Ocr:P0}) | ham='{Raw}' | {Timestamp:HH:mm:ss}",
            e.Plate, e.CameraName, e.CameraId, e.Confidence, e.DetectorConfidence, e.OcrConfidence, e.RawText, e.Timestamp);
        return Task.CompletedTask;
    }
}
