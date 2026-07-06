using Anpr.Core;
using Anpr.Core.Confirmation;

namespace Anpr.Tests;

public class PlateConfirmerTests
{
    private static PlateReading Reading(string text, float conf, bool valid = true) => new()
    {
        Text = text,
        RawText = text.Replace(" ", ""),
        Confidence = conf,
        DetectorConfidence = conf,
        OcrConfidence = conf,
        Box = new PlateBox(0, 0, 10, 10),
        IsValidTr = valid,
    };

    private static PlateConfirmer New(int minConf = 2, double settleSec = 3) => new(new PlateConfirmerOptions
    {
        DedupWindow = TimeSpan.FromSeconds(8),
        SettleWindow = TimeSpan.FromSeconds(settleSec),
        MinConfirmations = minConf,
    });

    [Fact]
    public void One_off_misread_is_suppressed_best_variant_wins()
    {
        // Gerçek senaryo: araç 32 karede "25 AFH 149" (yüksek güven), 1 karede yanlış "25 AFH 11".
        var c = New(minConf: 2);
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("25 AFH 149", 0.95f), t);
        c.Observe(Reading("25 AFH 11", 0.40f), t.AddSeconds(1)); // tek seferlik yanlış (düşük güven)

        var ready = c.DrainReady(t.AddSeconds(1));

        // İl+harf kimliği aynı -> tek olay, en yüksek güvenli okuma kazanır.
        Assert.Single(ready);
        Assert.Equal("25 AFH 149", ready[0].Text);
    }

    [Fact]
    public void Single_frame_car_still_emitted_after_settle()
    {
        var c = New(minConf: 2, settleSec: 3);
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("34 ABC 123", 0.9f), t);

        Assert.Empty(c.DrainReady(t.AddSeconds(1)));        // henüz onaylanmadı, settle dolmadı
        var ready = c.DrainReady(t.AddSeconds(4));          // settle doldu -> yine de yayınla
        Assert.Single(ready);
        Assert.Equal("34 ABC 123", ready[0].Text);
    }

    [Fact]
    public void Confirmed_car_emitted_once_then_deduped()
    {
        var c = New(minConf: 2);
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("06 BC 345", 0.8f), t);
        c.Observe(Reading("06 BC 345", 0.85f), t.AddSeconds(1));
        Assert.Single(c.DrainReady(t.AddSeconds(1)));       // onaylandı, yayınlandı

        // Aynı araç görülmeye devam etse de dedup penceresinde tekrar yayınlanmaz.
        c.Observe(Reading("06 BC 345", 0.9f), t.AddSeconds(2));
        Assert.Empty(c.DrainReady(t.AddSeconds(2)));
    }

    [Fact]
    public void Different_cars_emitted_separately()
    {
        var c = New(minConf: 1); // onay kapalı -> ilk görüşte yayınla
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("25 GC 147", 0.9f), t);
        c.Observe(Reading("25 AG 368", 0.9f), t);
        var ready = c.DrainReady(t);
        Assert.Equal(2, ready.Count);
    }

    [Fact]
    public void Invalid_readings_are_ignored()
    {
        var c = New(minConf: 1);
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("GARBAGE", 0.9f, valid: false), t);
        Assert.Empty(c.DrainReady(t));
    }
}
