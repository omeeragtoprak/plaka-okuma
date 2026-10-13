using Anpr.Core.Imaging;
using SkiaSharp;

namespace Anpr.Tests;

/// <summary>KVKK: fixture'lar SENTETİK üretilir — repoya plaka görüntüsü girmez.</summary>
public class CropQualityScorerTests
{
    private static SKBitmap Checker(int cell = 4, int w = 128, int h = 64)
    {
        var b = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool on = ((x / cell) + (y / cell)) % 2 == 0;
                b.SetPixel(x, y, on ? SKColors.White : SKColors.Black);
            }
        return b;
    }

    private static SKBitmap Uniform(byte v, int w = 128, int h = 64)
    {
        var b = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        b.Erase(new SKColor(v, v, v));
        return b;
    }

    private static SKBitmap Blurred(SKBitmap src, float sigma)
    {
        var info = new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var outB = new SKBitmap(info);
        using var canvas = new SKCanvas(outB);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) };
        canvas.DrawBitmap(src, 0, 0, paint);
        canvas.Flush();
        return outB;
    }

    [Fact]
    public void Sharp_scores_higher_blur_variance_than_blurred()
    {
        using var sharp = Checker();
        using var soft = Blurred(sharp, 3f);
        var qs = CropQualityScorer.Score(sharp);
        var qb = CropQualityScorer.Score(soft);
        Assert.True(qs.BlurVar > qb.BlurVar * 3,
            $"keskin ({qs.BlurVar:F0}) bulanıktan ({qb.BlurVar:F0}) belirgin yüksek olmalı");
    }

    [Fact]
    public void Uniform_image_has_near_zero_variance_and_extreme_histogram()
    {
        using var dark = Uniform(5);
        var q = CropQualityScorer.Score(dark);
        Assert.True(q.BlurVar < 1f);
        Assert.True(q.DarkPct > 0.99f);
        Assert.True(q.MeanGray < 10f);

        using var bright = Uniform(250);
        var q2 = CropQualityScorer.Score(bright);
        Assert.True(q2.BrightPct > 0.99f);
    }

    [Fact]
    public void Tiny_image_is_safe()
    {
        using var tiny = Uniform(128, w: 2, h: 2);
        var q = CropQualityScorer.Score(tiny);
        Assert.Equal(0f, q.BlurVar);
    }
}
