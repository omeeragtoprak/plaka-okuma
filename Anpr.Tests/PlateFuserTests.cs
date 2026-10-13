using Anpr.Core.Imaging;
using SkiaSharp;

namespace Anpr.Tests;

/// <summary>Çok-kareli füzyon (Faz B) korumaları: boyut, ortalama-doğruluğu, kenar-karartma yokluğu.</summary>
public class PlateFuserTests
{
    private static SKBitmap Uniform(byte v, int w = 60, int h = 30)
    {
        var b = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        b.Erase(new SKColor(v, v, v));
        return b;
    }

    private static SKBitmap Checker(byte on, byte off, int cell = 4, int w = 60, int h = 30)
    {
        var b = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool c = ((x / cell) + (y / cell)) % 2 == 0;
                byte v = c ? on : off;
                b.SetPixel(x, y, new SKColor(v, v, v));
            }
        return b;
    }

    [Fact]
    public void Single_crop_is_upscaled()
    {
        using var c = Uniform(128, 60, 30);
        using var f = PlateFuser.Fuse(new[] { c }, upscale: 2);
        Assert.Equal(120, f.Width);
        Assert.Equal(60, f.Height);
    }

    [Fact]
    public void Identical_uniform_crops_preserve_value_including_corners()
    {
        // Aynı düz gri kırpıntıların ortalaması AYNI griyi vermeli — köşeler dahil. Kenar-karartma
        // hatası (tam kare sayısına bölme) köşeleri koyulaştırırdı; tie-break + katkı-sayısı düzeltmesi
        // gereksiz kaymayı ve karartmayı önler.
        var crops = new[] { Uniform(160), Uniform(160), Uniform(160) };
        using var f = PlateFuser.Fuse(crops, upscale: 2);
        foreach (var c in crops) c.Dispose();

        (int x, int y)[] probes =
        {
            (0, 0), (f.Width - 1, 0), (0, f.Height - 1), (f.Width - 1, f.Height - 1),   // köşeler
            (f.Width / 2, f.Height / 2)                                                 // merkez
        };
        foreach (var (x, y) in probes)
        {
            var px = f.GetPixel(x, y);
            Assert.InRange(px.Red, 157, 163);   // ~160 (resize interpolasyonu için ufak tolerans)
            Assert.InRange(px.Green, 157, 163);
            Assert.InRange(px.Blue, 157, 163);
        }
    }

    [Fact]
    public void Multiple_distinct_crops_produce_valid_nonblack_output()
    {
        var crops = new[] { Checker(200, 40), Checker(200, 40), Checker(200, 40) };
        using var f = PlateFuser.Fuse(crops, upscale: 2);
        foreach (var c in crops) c.Dispose();

        Assert.Equal(120, f.Width);
        Assert.Equal(60, f.Height);
        long sum = 0;
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
                sum += f.GetPixel(x, y).Red;
        double mean = (double)sum / (f.Width * f.Height);
        Assert.InRange(mean, 60, 180);   // ne tamamen siyah ne tamamen beyaz (füzyon içerik korudu)
    }
}
