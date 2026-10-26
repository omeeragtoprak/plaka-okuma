using System.Buffers.Binary;
using Anpr.Sdk.Frames;
using SkiaSharp;

namespace Anpr.Tests;

/// <summary>
/// Faz 2 kare türü: ham BGRA (BMP) kare, motorun beklediği RGBA düzenine KAYIPSIZ ve kanal sırası DOĞRU
/// çevrilmeli. Kanal sırası kayarsa (BGR↔RGB) model girdisi sessizce bozulur — testler pikselleri tek tek doğrular.
/// </summary>
public class VideoFrameTests
{
    /// <summary>Elle BMP üretir: 32 bpp, BI_RGB, piksel = (b,g,r,a). topDown=true → negatif yükseklik (ffmpeg gibi).</summary>
    internal static byte[] MakeBmp(int w, int h, Func<int, int, (byte b, byte g, byte r)> px, bool topDown = true)
    {
        int stride = w * 4;
        int size = VideoFrame.BmpHeaderMin + stride * h;
        var d = new byte[size];
        d[0] = (byte)'B'; d[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(2), (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(10), VideoFrame.BmpHeaderMin);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(14), 40);          // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(18), w);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(22), topDown ? -h : h);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(26), 1);           // planes
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(28), 32);          // bpp
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(30), 0);           // BI_RGB
        for (int y = 0; y < h; y++)
        {
            int fileRow = topDown ? y : h - 1 - y;   // dosyadaki satır sırası
            for (int x = 0; x < w; x++)
            {
                var (b, g, r) = px(x, y);
                int o = VideoFrame.BmpHeaderMin + fileRow * stride + x * 4;
                d[o] = b; d[o + 1] = g; d[o + 2] = r; d[o + 3] = 255;
            }
        }
        return d;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bgra_bmp_converts_to_rgba_with_correct_channels_and_row_order(bool topDown)
    {
        // Her pikselin rengi konumundan türetilir: r=x*40, g=y*60, b=200-x*30. Satır/kanal karışması hemen görünür.
        var bmp = MakeBmp(3, 2, (x, y) => ((byte)(200 - x * 30), (byte)(y * 60), (byte)(x * 40)), topDown);
        var frame = VideoFrame.FromBmp(bmp, DateTimeOffset.UnixEpoch, 7);

        Assert.Equal(VideoPixelFormat.Bgra32, frame.Format);
        Assert.Equal(3, frame.Width); Assert.Equal(2, frame.Height);
        Assert.Equal(topDown, frame.TopDown);
        Assert.Equal(12, frame.Stride);
        Assert.Equal(7, frame.Sequence);

        using var rgba = frame.ToRgbaBitmap();
        Assert.Equal(SKColorType.Rgba8888, rgba.ColorType);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
            {
                var c = rgba.GetPixel(x, y);
                Assert.Equal((byte)(x * 40), c.Red);
                Assert.Equal((byte)(y * 60), c.Green);
                Assert.Equal((byte)(200 - x * 30), c.Blue);
                Assert.Equal(255, c.Alpha);
            }
    }

    [Fact]
    public void RowOffset_points_to_the_visual_row_regardless_of_storage_order()
    {
        var top = VideoFrame.FromBmp(MakeBmp(2, 3, (x, y) => ((byte)y, 0, 0), topDown: true), DateTimeOffset.UnixEpoch);
        var bottom = VideoFrame.FromBmp(MakeBmp(2, 3, (x, y) => ((byte)y, 0, 0), topDown: false), DateTimeOffset.UnixEpoch);
        for (int y = 0; y < 3; y++)
        {
            Assert.Equal((byte)y, top.Data[top.RowOffset(y)]);       // B kanalı = görsel satır no
            Assert.Equal((byte)y, bottom.Data[bottom.RowOffset(y)]);
        }
    }

    [Fact]
    public void TryFromBmp_rejects_wrong_bpp_short_data_and_bad_magic()
    {
        var ok = MakeBmp(2, 2, (x, y) => (1, 2, 3));
        Assert.True(VideoFrame.TryFromBmp(ok, DateTimeOffset.UnixEpoch, 0, out _, out _));

        var bpp24 = (byte[])ok.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(bpp24.AsSpan(28), 24);
        Assert.False(VideoFrame.TryFromBmp(bpp24, DateTimeOffset.UnixEpoch, 0, out _, out var e1));
        Assert.Contains("32 bpp", e1);

        var shortData = ok.AsSpan(0, ok.Length - 1).ToArray();
        Assert.False(VideoFrame.TryFromBmp(shortData, DateTimeOffset.UnixEpoch, 0, out _, out var e2));
        Assert.Contains("eksik", e2);

        var badMagic = (byte[])ok.Clone(); badMagic[0] = (byte)'X';
        Assert.False(VideoFrame.TryFromBmp(badMagic, DateTimeOffset.UnixEpoch, 0, out _, out var e3));
        Assert.Contains("imza", e3);

        var compressed = (byte[])ok.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(30), 1); // BI_RLE8
        Assert.False(VideoFrame.TryFromBmp(compressed, DateTimeOffset.UnixEpoch, 0, out _, out _));
    }

    [Fact]
    public void Jpeg_frame_ToJpeg_returns_same_array_without_reencoding()
    {
        // Eski davranışın korunması: kanıt tam karesi "yeniden kodlanmadan" yazılır → aynı referans dönmeli.
        using var bmp = new SKBitmap(8, 8);
        bmp.Erase(SKColors.Orange);
        using var img = SKImage.FromBitmap(bmp);
        var jpeg = img.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();

        var frame = VideoFrame.FromJpeg(jpeg, DateTimeOffset.UnixEpoch);
        Assert.Same(jpeg, frame.ToJpeg(50));
        using var decoded = frame.ToRgbaBitmap();
        Assert.Equal(8, decoded.Width);
        Assert.Equal(SKColorType.Rgba8888, decoded.ColorType);
    }

    [Fact]
    public void Bgra_frame_ToJpeg_encodes_and_respects_maxWidth()
    {
        var frame = VideoFrame.FromBmp(MakeBmp(64, 32, (x, y) => (10, 20, 30)), DateTimeOffset.UnixEpoch);
        var full = frame.ToJpeg(85);
        var small = frame.ToJpeg(85, maxWidth: 16);
        Assert.True(full.Length > 2 && full[0] == 0xFF && full[1] == 0xD8);   // JPEG SOI
        using var s = SKBitmap.Decode(small);
        Assert.Equal(16, s.Width);
        Assert.Equal(8, s.Height);   // en-boy oranı korunur
    }

    // ─────────────── tampon geri dönüşüm protokolü (Faz 2) ───────────────

    [Fact]
    public void Taken_frame_can_never_be_retired()
    {
        var f = VideoFrame.FromBmp(MakeBmp(2, 2, (x, y) => (1, 2, 3)), DateTimeOffset.UnixEpoch);
        Assert.True(f.TryTake());        // tüketici aldı
        Assert.False(f.TryRetire());     // kaynak tamponu GERİ ALAMAZ
        Assert.False(f.IsRetired);       // ve bayrak geri alınmış olmalı (kare geçerli kalır)
        Assert.True(f.TryTake());        // tekrar alınabilir (aynı tüketici yeniden yoklar)
    }

    [Fact]
    public void Retired_frame_is_refused_to_consumers()
    {
        var f = VideoFrame.FromBmp(MakeBmp(2, 2, (x, y) => (1, 2, 3)), DateTimeOffset.UnixEpoch);
        Assert.True(f.TryRetire());      // kimse almamıştı → tampon geri alındı
        Assert.False(f.TryTake());       // geç gelen tüketici kareyi ALAMAZ (tampon üzerine yazılacak)
        Assert.True(f.WasTaken);         // deneme kaydedildi ama...
        Assert.True(f.IsRetired);        // ...kare geri alınmış kalır
    }

    [Fact]
    public void ChooseSpare_reuses_only_untaken_same_size_bgra_frames()
    {
        var a = VideoFrame.FromBmp(MakeBmp(4, 4, (x, y) => (1, 2, 3)), DateTimeOffset.UnixEpoch);
        Assert.Same(a.Data, FfmpegFrameSource.ChooseSpare(a, a.Data.Length));          // alınmamış → tamponu ver

        var b = VideoFrame.FromBmp(MakeBmp(4, 4, (x, y) => (1, 2, 3)), DateTimeOffset.UnixEpoch);
        b.TryTake();
        Assert.Null(FfmpegFrameSource.ChooseSpare(b, b.Data.Length));                  // alınmış → ASLA

        var c = VideoFrame.FromBmp(MakeBmp(4, 4, (x, y) => (1, 2, 3)), DateTimeOffset.UnixEpoch);
        Assert.Null(FfmpegFrameSource.ChooseSpare(c, c.Data.Length + 4));              // boyut farklı → yeni tahsis

        Assert.Null(FfmpegFrameSource.ChooseSpare(null, 100));
        var j = VideoFrame.FromJpeg(new byte[] { 0xFF, 0xD8 }, DateTimeOffset.UnixEpoch);
        Assert.Null(FfmpegFrameSource.ChooseSpare(j, 2));                              // JPEG yolu geri dönüştürülmez
    }

    [Fact]
    public async Task Take_and_retire_race_never_hands_out_a_recycled_buffer()
    {
        // Yarış: çok sayıda kare, her birinde bir tüketici ve bir üretici aynı anda karar verir.
        // Değişmez: (tüketici aldı) ∧ (üretici geri aldı) AYNI ANDA olamaz.
        int violations = 0;
        for (int round = 0; round < 2000; round++)
        {
            var f = VideoFrame.FromBmp(MakeBmp(2, 2, (x, y) => (1, 2, 3)), DateTimeOffset.UnixEpoch);
            bool took = false, retired = false;
            using var gate = new ManualResetEventSlim(false);
            var t1 = Task.Run(() => { gate.Wait(); took = f.TryTake(); });
            var t2 = Task.Run(() => { gate.Wait(); retired = f.TryRetire(); });
            gate.Set();
            await Task.WhenAll(t1, t2);
            if (took && retired) violations++;
        }
        Assert.Equal(0, violations);
    }
}
