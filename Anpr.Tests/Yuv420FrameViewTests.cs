using Anpr.Core.Imaging;
using Anpr.Sdk.Frames;
using SkiaSharp;

namespace Anpr.Tests;

/// <summary>
/// YUV 4:2:0 kare görünümü (2.0.0): kamera karesi tam RGB'ye çevrilmeden okunur. Bu testler üç şeyi tutar:
/// (1) renk dönüşümü ffmpeg'in BGRA çıkışını üreten kuralla aynı (BT.601, en-yakın kroma),
/// (2) dedektör girdisinin DOĞRUDAN örneklenmesi, aynı piksellerin RGBA bitmap'inden Skia ile
///     üretilen girdiyle örtüşüyor (yarım piksel kayması = kutu kayması = sessiz doğruluk kaybı),
/// (3) bölge/kırpıntı koordinatları doğru.
/// </summary>
public class Yuv420FrameViewTests
{
    /// <summary>I420 tampon üretir. Kroma 2×2 bloğun SOL ÜST pikselinden alınır (dosya düzeni: Y, U, V).</summary>
    internal static byte[] MakeYuv(int w, int h, Func<int, int, (byte y, byte u, byte v)> px)
    {
        int cw = (w + 1) / 2, ch = (h + 1) / 2;
        var d = new byte[w * h + 2 * cw * ch];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (yy, u, v) = px(x, y);
                d[y * w + x] = yy;
                if ((x & 1) == 0 && (y & 1) == 0)
                {
                    d[w * h + (y / 2) * cw + x / 2] = u;
                    d[w * h + cw * ch + (y / 2) * cw + x / 2] = v;
                }
            }
        return d;
    }

    [Theory]
    // sınırlı aralık (yayın standardı): Y 16–235, kroma 16–240
    [InlineData(16, 128, 128, false, 0, 0, 0)]        // siyah
    [InlineData(235, 128, 128, false, 255, 255, 255)] // beyaz
    [InlineData(126, 128, 128, false, 128, 128, 128)] // orta gri
    [InlineData(81, 90, 240, false, 255, 0, 0)]       // kırmızı
    [InlineData(145, 54, 34, false, 0, 255, 0)]       // yeşil
    [InlineData(41, 240, 110, false, 0, 0, 255)]      // mavi
    // tam aralık ("yuvj420p")
    [InlineData(0, 128, 128, true, 0, 0, 0)]
    [InlineData(255, 128, 128, true, 255, 255, 255)]
    [InlineData(76, 85, 255, true, 254, 0, 0)]
    public void ExtractRgba_converts_with_bt601(byte y, byte u, byte v, bool full, int r, int g, int b)
    {
        var view = new Yuv420FrameView(MakeYuv(4, 4, (_, _) => (y, u, v)), 0, 4, 4, full);
        using var bmp = view.ExtractRgba(new SKRectI(0, 0, 4, 4));
        var c = bmp.GetPixel(1, 2);
        Assert.InRange(c.Red, r - 2, r + 2);
        Assert.InRange(c.Green, g - 2, g + 2);
        Assert.InRange(c.Blue, b - 2, b + 2);
        Assert.Equal(255, c.Alpha);
        Assert.Equal(SKColorType.Rgba8888, bmp.ColorType);
    }

    [Fact]
    public void Chroma_is_replicated_per_2x2_block_and_luma_is_per_pixel()
    {
        // Sol 2×2 blok kırmızımsı kroma, sağ 2×2 blok mavimsi kroma; luma piksel piksel değişir.
        // (Doygunluk ılımlı tutuldu: kanal 0/255'e kırpılırsa "kroma farkı sabit" önermesi bozulur.)
        var yuv = MakeYuv(4, 2, (x, yy) => ((byte)(60 + x * 20 + yy * 10), x < 2 ? (byte)110 : (byte)170, x < 2 ? (byte)170 : (byte)110));
        var view = new Yuv420FrameView(yuv, 0, 4, 2);
        using var bmp = view.ExtractRgba(new SKRectI(0, 0, 4, 2));
        // Aynı bloktaki iki piksel: kroma aynı → R-G farkı (kromadan gelir) aynı, parlaklık farklı.
        var a = bmp.GetPixel(0, 0); var b = bmp.GetPixel(1, 1);
        Assert.Equal(a.Red - a.Green, b.Red - b.Green, 2.0);
        Assert.True(b.Green > a.Green, "luma pikselden piksele değişmeli");
        // Komşu blok: kroma farklı → renk belirgin farklı.
        var c = bmp.GetPixel(2, 0);
        Assert.True(c.Blue - c.Red > 60, "sağ blok mavimsi olmalı");
        Assert.True(a.Red - a.Blue > 60, "sol blok kırmızımsı olmalı");
    }

    [Fact]
    public void ExtractRgba_region_maps_to_the_right_source_pixels_and_clamps()
    {
        // Luma = konumun işareti: Y = 16 + x + 10*y (U=V=128 → gri ton = (Y-16)*255/219)
        int w = 16, h = 12;
        var view = new Yuv420FrameView(MakeYuv(w, h, (x, y) => ((byte)(16 + x + 10 * y), 128, 128)), 0, w, h);
        using var crop = view.ExtractRgba(new SKRectI(5, 3, 9, 6));
        Assert.Equal(4, crop.Width); Assert.Equal(3, crop.Height);
        int Expect(int x, int y) => (int)Math.Round((x + 10 * y) * 255.0 / 219.0);
        Assert.InRange(crop.GetPixel(0, 0).Red, Expect(5, 3) - 1, Expect(5, 3) + 1);
        Assert.InRange(crop.GetPixel(3, 2).Red, Expect(8, 5) - 1, Expect(8, 5) + 1);

        using var clamped = view.ExtractRgba(new SKRectI(10, 8, 40, 40));   // kare dışına taşan istek → kıstırılır
        Assert.Equal(6, clamped.Width); Assert.Equal(4, clamped.Height);
        Assert.True(view.ExtractIsOwnedCopy);
    }

    [Fact]
    public void Constructor_rejects_short_buffers_and_bad_sizes()
    {
        Assert.Throws<ArgumentException>(() => new Yuv420FrameView(new byte[10], 0, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Yuv420FrameView(new byte[100], 0, 1, 4));
        Assert.Equal(4 * 4 + 2 * 2 * 2, Yuv420FrameView.FrameBytes(4, 4));
        Assert.Equal(5 * 3 + 2 * 3 * 2, Yuv420FrameView.FrameBytes(5, 3));   // tek boyutlar: kroma yukarı yuvarlanır
    }

    // ───────────── dedektör girdisi: doğrudan örnekleme ≈ aynı piksellerin Skia yeniden boyutlandırması ─────────────

    private static (float mean, float max) Compare(IFrameView a, IFrameView b, SKRectI region, int size)
    {
        float ratio = Math.Min((float)size / region.Width, (float)size / region.Height);
        int nw = Math.Clamp((int)Math.Round(region.Width * ratio), 1, size), nh = Math.Clamp((int)Math.Round(region.Height * ratio), 1, size);
        int px = (size - nw) / 2, py = (size - nh) / 2;
        var ta = new float[3 * size * size]; var tb = new float[3 * size * size];
        a.FillDetectorInput(region, ta, size, nw, nh, px, py);
        b.FillDetectorInput(region, tb, size, nw, nh, px, py);
        float sum = 0, max = 0; int n = 0;
        for (int p = 0; p < 3; p++)
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int i = p * size * size + (y + py) * size + x + px;
                    float d = Math.Abs(ta[i] - tb[i]) * 255f;
                    sum += d; if (d > max) max = d; n++;
                }
        return (sum / n, max);
    }

    [Theory]
    [InlineData(0, 0, 640, 360)]      // tam kare, 1,67× küçültme
    [InlineData(64, 40, 512, 220)]    // alt bölge (ROI kırpımı gibi), kenar kıstırması bölge sınırında
    [InlineData(300, 100, 200, 120)]  // bakış dikdörtgeni gibi küçük bölge → BÜYÜTME (1,92×)
    public void Direct_sampling_matches_skia_resize_of_the_same_pixels(int rx, int ry, int rw, int rh)
    {
        // Yumuşak eğimli, renkli bir sahne: x'e göre luma rampası + y'ye göre kroma kayması.
        // Yarım piksellik bir örnekleme kayması bu rampada birkaç seviyelik SİSTEMATİK fark üretir.
        int w = 640, h = 360;
        var yuv = MakeYuv(w, h, (x, y) => ((byte)(30 + x * 190 / w + (y % 7)), (byte)(100 + y * 60 / h), (byte)(160 - x * 50 / w)));
        var yv = new Yuv420FrameView(yuv, 0, w, h);
        using var rgba = yv.ExtractRgba(new SKRectI(0, 0, w, h));
        var sv = new SkBitmapFrameView(rgba);

        var (mean, max) = Compare(yv, sv, new SKRectI(rx, ry, rx + rw, ry + rh), 384);
        Assert.True(mean < 1.0, $"ortalama fark {mean:F2} seviye (beklenen < 1): örnekleme kuralı Skia ile uyuşmuyor");
        Assert.True(max < 6.0, $"azami fark {max:F1} seviye (beklenen < 6)");
    }

    [Fact]
    public void Direct_sampling_detects_a_half_pixel_shift()
    {
        // Testin GÜCÜNÜ kanıtlar: kaynağı 2 piksel kaydırılmış bir görünümle kıyaslayınca fark belirgin olmalı.
        // (Yukarıdaki eşik bunu yakalamıyorsa o test hiçbir şeyi korumuyordur.)
        int w = 640, h = 360;
        Func<int, int, (byte, byte, byte)> scene = (x, y) => ((byte)(30 + x * 190 / w), 128, 128);
        var a = new Yuv420FrameView(MakeYuv(w, h, scene), 0, w, h);
        var bYuv = new Yuv420FrameView(MakeYuv(w, h, (x, y) => scene(Math.Min(w - 1, x + 6), y)), 0, w, h);
        using var rgba = bYuv.ExtractRgba(new SKRectI(0, 0, w, h));
        var (mean, _) = Compare(a, new SkBitmapFrameView(rgba), new SKRectI(0, 0, w, h), 384);
        Assert.True(mean > 1.5, $"6 piksellik kayma yalnız {mean:F2} seviye fark üretti — eşik kaymayı göremiyor");
    }

    [Fact]
    public void ScaleToRgba_downscales_without_full_conversion()
    {
        int w = 64, h = 32;
        var view = new Yuv420FrameView(MakeYuv(w, h, (x, y) => ((byte)(16 + x * 3), 128, 128)), 0, w, h);
        using var small = view.ScaleToRgba(new SKRectI(0, 0, w, h), 16, 8);
        Assert.Equal(16, small.Width); Assert.Equal(8, small.Height);
        Assert.True(small.GetPixel(15, 4).Red > small.GetPixel(0, 4).Red + 100, "yatay rampa korunmalı");
        Assert.Equal(255, small.GetPixel(3, 3).Alpha);
    }

    // ───────────── VideoFrame bütünleşmesi ─────────────

    [Fact]
    public void VideoFrame_yuv_exposes_view_bitmap_and_jpeg()
    {
        int w = 64, h = 32;
        var data = MakeYuv(w, h, (x, y) => ((byte)(40 + x * 2), 110, 150));
        var f = VideoFrame.FromYuv420(data, w, h, fullRange: false, DateTimeOffset.UnixEpoch, 9);
        Assert.Equal(VideoPixelFormat.Yuv420p, f.Format);
        Assert.True(f.IsRaw);
        Assert.Equal(9, f.Sequence);
        Assert.NotNull(f.TryGetView());
        Assert.Equal(w, f.TryGetView()!.Width);

        using var rgba = f.ToRgbaBitmap();
        Assert.Equal(w, rgba.Width); Assert.Equal(SKColorType.Rgba8888, rgba.ColorType);

        var full = f.ToJpeg(85);
        var small = f.ToJpeg(85, maxWidth: 16);
        Assert.True(full.Length > 2 && full[0] == 0xFF && full[1] == 0xD8);
        using var s = SKBitmap.Decode(small);
        Assert.Equal(16, s.Width); Assert.Equal(8, s.Height);

        Assert.Throws<ArgumentException>(() => VideoFrame.FromYuv420(new byte[10], w, h, false, DateTimeOffset.UnixEpoch));
        // BGRA/JPEG kare görünüm vermez → çağıran bitmap yoluna düşer
        Assert.Null(VideoFrame.FromJpeg(new byte[] { 0xFF, 0xD8 }, DateTimeOffset.UnixEpoch).TryGetView());
        Assert.Null(VideoFrame.FromBmp(VideoFrameTests.MakeBmp(2, 2, (x, y) => (1, 2, 3)), DateTimeOffset.UnixEpoch).TryGetView());
    }

    [Fact]
    public void ChooseSpare_recycles_untaken_yuv_buffers_too()
    {
        var data = MakeYuv(8, 8, (x, y) => (50, 128, 128));
        var a = VideoFrame.FromYuv420(data, 8, 8, false, DateTimeOffset.UnixEpoch);
        Assert.Same(a.Data, FfmpegFrameSource.ChooseSpare(a, a.Data.Length));
        var b = VideoFrame.FromYuv420(MakeYuv(8, 8, (x, y) => (50, 128, 128)), 8, 8, false, DateTimeOffset.UnixEpoch);
        b.TryTake();
        Assert.Null(FfmpegFrameSource.ChooseSpare(b, b.Data.Length));   // tüketici almış → asla
    }
}
