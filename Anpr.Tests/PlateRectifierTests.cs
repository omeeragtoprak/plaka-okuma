using Anpr.Core.Imaging;
using SkiaSharp;

namespace Anpr.Tests;

public class PlateRectifierTests
{
    // Beyaz zemin üzerine yatay siyah "metin satırları" çiz (eğiklik = 0).
    private static SKBitmap MakeBars()
    {
        var bmp = new SKBitmap(new SKImageInfo(240, 80, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        using var p = new SKPaint { Color = SKColors.Black };
        c.DrawRect(20, 30, 200, 8, p);  // bir kalın yatay satır
        c.DrawRect(20, 46, 200, 8, p);  // ikinci satır
        return bmp;
    }

    private static SKBitmap RotateBy(SKBitmap src, double deg)
    {
        var dst = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var c = new SKCanvas(dst);
        c.Clear(SKColors.White);
        c.Translate(src.Width / 2f, src.Height / 2f);
        c.RotateDegrees((float)deg);
        c.Translate(-src.Width / 2f, -src.Height / 2f);
        c.DrawBitmap(src, 0, 0);
        return dst;
    }

    [Fact]
    public void Detects_skew_and_deskew_reduces_it()
    {
        using var bars = MakeBars();
        using var skewed = RotateBy(bars, 6.0);

        double estBefore = PlateRectifier.EstimateSkewAngle(skewed);
        Assert.True(Math.Abs(estBefore) >= 3.0, $"eğiklik algılanmadı: {estBefore}");

        using var fixedUp = PlateRectifier.Deskew(skewed);
        double estAfter = PlateRectifier.EstimateSkewAngle(fixedUp);
        Assert.True(Math.Abs(estAfter) < Math.Abs(estBefore), $"deskew eğikliği azaltmadı: {estBefore} -> {estAfter}");
        Assert.True(Math.Abs(estAfter) <= 2.0, $"deskew sonrası hâlâ eğik: {estAfter}");
    }

    [Fact]
    public void Straight_image_is_left_unchanged()
    {
        using var bars = MakeBars();
        double est = PlateRectifier.EstimateSkewAngle(bars);
        Assert.True(Math.Abs(est) <= 1.0, $"düz görüntüde sahte eğiklik: {est}");
        using var result = PlateRectifier.Deskew(bars);
        Assert.True(ReferenceEquals(result, bars)); // |açı|<1 -> aynı bitmap döner
    }
}

public class Rectifier2GeometryTests
{
    [Fact]
    public void Homography_maps_quad_corners_to_target_rect()
    {
        // Eğik dörtgen -> eksen-hizalı hedef; çözülen H her köşeyi ±0.5 px içinde taşımalı.
        float[] sx = { 20, 200, 210, 15 };
        float[] sy = { 30, 10, 90, 100 };
        float[] dx = { 0, 240, 240, 0 };
        float[] dy = { 0, 0, 80, 80 };

        var h = Anpr.Core.Imaging.PlateRectifier2.SolveHomography(sx, sy, dx, dy);
        Assert.NotNull(h);
        for (int i = 0; i < 4; i++)
        {
            float wz = h![6] * sx[i] + h[7] * sy[i] + h[8];
            float u = (h[0] * sx[i] + h[1] * sy[i] + h[2]) / wz;
            float v = (h[3] * sx[i] + h[4] * sy[i] + h[5]) / wz;
            Assert.True(MathF.Abs(u - dx[i]) < 0.5f && MathF.Abs(v - dy[i]) < 0.5f,
                $"köşe {i}: ({u:F2},{v:F2}) beklenen ({dx[i]},{dy[i]})");
        }
    }

    [Fact]
    public void Degenerate_quad_returns_null()
    {
        float[] sx = { 0, 0, 0, 0 };
        float[] sy = { 0, 0, 0, 0 };
        float[] dx = { 0, 240, 240, 0 };
        float[] dy = { 0, 0, 80, 80 };
        Assert.Null(Anpr.Core.Imaging.PlateRectifier2.SolveHomography(sx, sy, dx, dy));
    }
}

public class InjectObservationTests
{
    private static Anpr.Core.PlateReading Reading(string text, float conf) => new()
    {
        Text = text, RawText = text.Replace(" ", ""), Confidence = conf,
        DetectorConfidence = conf, OcrConfidence = conf,
        Box = new Anpr.Core.PlateBox(10, 10, 120, 50), IsValidTr = true,
    };

    [Fact]
    public void Injected_observation_updates_consensus_without_double_emit()
    {
        var c = new Anpr.Core.Confirmation.PlateConfirmer(new Anpr.Core.Confirmation.PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 2,
            FuzzyMergeDistance = 1,
            EarlyEmitMinOcr = 0.9f, // düşük güvenli iz bekletilir -> enhance penceresi
        });
        var t = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("25 ABC 123", 0.6f), t);
        c.Observe(Reading("25 ABC 123", 0.6f), t.AddSeconds(0.2));

        var pending = c.PendingLowConf(t.AddSeconds(0.4), 0.98f);
        Assert.Single(pending);

        // Zenginleştirilmiş yüksek güvenli gözlem enjekte edilir
        c.InjectObservation(pending[0].Key, Reading("25 ABC 123", 0.97f));
        c.MarkEnhanced(pending[0].Key);

        Assert.Empty(c.PendingLowConf(t.AddSeconds(0.5), 0.98f)); // tek deneme garantisi

        var ready = c.DrainReady(t.AddSeconds(4)); // settle
        Assert.Single(ready); // TEK emit
        Assert.Equal("25 ABC 123", ready[0].Text);
        Assert.Empty(c.DrainReady(t.AddSeconds(5))); // ikinci emit yok
    }
}
