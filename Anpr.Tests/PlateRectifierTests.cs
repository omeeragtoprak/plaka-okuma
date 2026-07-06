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
