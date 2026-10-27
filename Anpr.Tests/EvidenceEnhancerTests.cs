using Anpr.Service.Panel;
using SkiaSharp;

namespace Anpr.Tests;

/// <summary>
/// İnsan gözü için kanıt iyileştirme. Ölçülmüş gerekçe: 22 gözle doğrulamada 4 kayıt (%18) insan
/// gözüyle de okunamadı; tam kareden geniş kadraj + germe + keskinleştirme 5 belirsizin 2'sini
/// kurtardı. Bu kod OCR hattını ETKİLEMEZ — yalnız operatörün ekranını iyileştirir.
/// </summary>
public class EvidenceEnhancerTests
{
    /// <summary>Verilen aralıkta gri değerler taşıyan sentetik görüntü.</summary>
    private static SKBitmap Gradient(int w, int h, byte from, byte to)
    {
        var bmp = new SKBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)(from + (to - from) * x / Math.Max(1, w - 1));
                bmp.SetPixel(x, y, new SKColor(v, v, v));
            }
        return bmp;
    }

    private static (byte min, byte max) Range(SKBitmap b)
    {
        byte lo = 255, hi = 0;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
            {
                byte v = b.GetPixel(x, y).Red;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
        return (lo, hi);
    }

    [Fact]
    public void Dark_night_image_gets_its_contrast_stretched()
    {
        // Gece pozlaması: tüm bilgi 40-90 bandında sıkışmış. Germe sonrası bant açılmalı.
        using var src = Gradient(64, 32, 40, 90);
        using var outp = EvidenceEnhancer.Enhance(src, sharpenAmount: 0f);
        var (lo, hi) = Range(outp);
        Assert.True(hi - lo > 200, $"kontrast açılmalıydı, bulundu {hi - lo}");
    }

    [Fact]
    public void Overexposed_image_is_handled_by_the_same_code_path()
    {
        // Aşırı pozlanmış (far parlaması): bilgi 200-250'de. Mutlak germe bunu çözemez, persentil çözer.
        using var src = Gradient(64, 32, 200, 250);
        using var outp = EvidenceEnhancer.Enhance(src, sharpenAmount: 0f);
        var (lo, hi) = Range(outp);
        Assert.True(hi - lo > 200, $"aşırı pozlanmış kare de gerilmeliydi, bulundu {hi - lo}");
    }

    [Fact]
    public void Flat_single_colour_image_does_not_throw_or_produce_garbage()
    {
        // Dejenere histogram: hi==lo. Sıfıra bölme ya da taşma OLMAMALI.
        using var src = Gradient(16, 16, 128, 128);
        using var outp = EvidenceEnhancer.Enhance(src);
        Assert.Equal(16, outp.Width);
        var (lo, hi) = Range(outp);
        Assert.True(hi >= lo);
    }

    [Fact]
    public void Output_dimensions_match_input()
    {
        using var src = Gradient(37, 19, 10, 240);
        using var outp = EvidenceEnhancer.Enhance(src);
        Assert.Equal(37, outp.Width);
        Assert.Equal(19, outp.Height);
    }

    [Fact]
    public void Tiny_image_skips_sharpening_instead_of_crashing()
    {
        // 3x3'ten küçükte komşuluk yok — kod keskinleştirmeyi atlamalı, patlamamalı.
        using var src = Gradient(2, 2, 0, 255);
        using var outp = EvidenceEnhancer.Enhance(src, sharpenAmount: 1.0f);
        Assert.Equal(2, outp.Width);
    }

    // --- Geniş kadraj kırpma ---
    [Fact]
    public void CropWide_expands_around_the_box()
    {
        using var frame = Gradient(400, 200, 0, 255);
        using var crop = EvidenceEnhancer.CropWide(frame, x: 100, y: 50, boxW: 100, boxH: 40, padRatio: 0.5f);
        // pad = 50 yatay, 20 dikey → 100+2*50 = 200 genişlik, 40+2*20 = 80 yükseklik
        Assert.Equal(200, crop.Width);
        Assert.Equal(80, crop.Height);
    }

    [Fact]
    public void CropWide_clamps_at_frame_edges_instead_of_throwing()
    {
        using var frame = Gradient(200, 100, 0, 255);
        using var crop = EvidenceEnhancer.CropWide(frame, x: 0, y: 0, boxW: 200, boxH: 100, padRatio: 1.0f);
        Assert.Equal(200, crop.Width);    // taşma kırpıldı
        Assert.Equal(100, crop.Height);
    }

    [Fact]
    public void CropWide_with_degenerate_box_returns_something_usable()
    {
        using var frame = Gradient(100, 50, 0, 255);
        using var crop = EvidenceEnhancer.CropWide(frame, x: 500, y: 500, boxW: 0, boxH: 0, padRatio: 0.3f);
        Assert.True(crop.Width > 0 && crop.Height > 0);
    }

    // --- Büyütme ---
    [Fact]
    public void UpscaleTo_enlarges_small_crops_for_human_eyes()
    {
        using var src = Gradient(100, 40, 0, 255);
        using var big = EvidenceEnhancer.UpscaleTo(src, 800);
        Assert.True(big.Width >= 700, $"büyütülmeliydi, bulundu {big.Width}");
    }

    [Fact]
    public void UpscaleTo_leaves_already_large_images_alone()
    {
        using var src = Gradient(900, 300, 0, 255);
        using var same = EvidenceEnhancer.UpscaleTo(src, 800);
        Assert.Equal(900, same.Width);
    }
}
