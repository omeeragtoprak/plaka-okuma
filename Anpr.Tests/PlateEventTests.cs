using Anpr.Core;
using Anpr.Sdk.Events;

namespace Anpr.Tests;

/// <summary>M5: olay şema sürümü (webhook/jsonl tüketicileri sürümü kontrol edebilsin).</summary>
public class PlateEventTests
{
    [Fact]
    public void SchemaVersion_defaults_to_1()
    {
        var e = new PlateEvent
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            CameraId = "giris", CameraName = "Giriş",
            Plate = "25 ABC 123", PlateCompact = "25ABC123", RawText = "25ABC123",
            Confidence = 0.9f, DetectorConfidence = 0.9f, OcrConfidence = 0.9f,
            Box = new PlateBox(0, 0, 10, 10),
        };
        Assert.Equal(1, e.SchemaVersion);   // şema değişince bump edilir (bilinçli kırılma)
    }
}
