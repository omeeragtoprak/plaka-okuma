using Anpr.Service.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anpr.Service.Events;

/// <summary>Olayları yapısal log olarak yazar. KVKK: MaskPlatesInLogs açıksa plaka maskelenir.</summary>
public sealed class ConsoleEventSink : IEventSink
{
    private readonly ILogger<ConsoleEventSink> _logger;
    private readonly bool _mask;

    public ConsoleEventSink(ILogger<ConsoleEventSink> logger, IOptions<AnprServiceOptions> options)
    {
        _logger = logger;
        _mask = options.Value.MaskPlatesInLogs;
    }

    // "25ABC123" -> "25***123": il + son 3 kalır (KVKK log minimizasyonu; FR-42 deseni).
    internal static string Mask(string plate)
    {
        string c = new(plate.Where(char.IsLetterOrDigit).ToArray());
        return c.Length <= 5 ? c : $"{c[..2]}***{c[^3..]}";
    }

    public Task PublishAsync(PlateEvent e, CancellationToken cancellationToken)
    {
        string plate = _mask ? Mask(e.Plate) : e.Plate;
        _logger.LogInformation(
            "PLAKA{Obs} {Plate} | {CameraName} ({CameraId}) | güven={Confidence:P0} (det={Det:P0} ocr={Ocr:P0}) | ham='{Raw}' | {Timestamp:HH:mm:ss}",
            e.Scope == "roi" ? "" : "(gözlem)", plate, e.CameraName, e.CameraId, e.Confidence, e.DetectorConfidence, e.OcrConfidence,
            _mask ? Mask(e.RawText) : e.RawText, e.Timestamp);
        return Task.CompletedTask;
    }
}
