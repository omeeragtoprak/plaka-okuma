using System.Text;
using Anpr.Sdk.Frames;

namespace Anpr.Tests;

/// <summary>
/// ffmpeg Y4M borusu (2.0.0): tek başlık satırı + her kare için "FRAME\n" + sabit uzunlukta YUV. Okuyucu 6 MB'lık
/// kareleri doğrudan hedef tampona alır; testler başlık çözümünü, çerçevelemeyi ve hizasızlık davranışını tutar.
/// </summary>
public class Y4mFrameReaderTests
{
    private static byte[] Stream(string header, params byte[][] frames)
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(header + "\n"));
        foreach (var f in frames) { ms.Write("FRAME\n"u8); ms.Write(f); }
        return ms.ToArray();
    }

    [Fact]
    public void ParseHeader_reads_size_and_range_from_ffmpeg_header()
    {
        var h = Y4mFrameReader.ParseHeader("YUV4MPEG2 W2688 H1520 F5:1 Ip A0:0 C420mpeg2 XYSCSS=420MPEG2 XCOLORRANGE=LIMITED");
        Assert.Equal(2688, h.Width); Assert.Equal(1520, h.Height); Assert.False(h.FullRange);
        Assert.Equal(2688 * 1520 * 3 / 2, h.FrameBytes);

        Assert.True(Y4mFrameReader.ParseHeader("YUV4MPEG2 W640 H360 F25:1 Ip A1:1 C420jpeg XCOLORRANGE=FULL").FullRange);
        Assert.False(Y4mFrameReader.ParseHeader("YUV4MPEG2 W640 H360 F25:1").FullRange);   // etiket yok → 4:2:0, sınırlı
    }

    [Theory]
    [InlineData("MJPEG W640 H360")]                              // imza yok
    [InlineData("YUV4MPEG2 W640 H360 C420p10")]                  // 10-bit
    [InlineData("YUV4MPEG2 W640 H360 C422")]                     // 4:2:2
    [InlineData("YUV4MPEG2 W640 H360 C444")]
    [InlineData("YUV4MPEG2 W640 H360 Cmono")]
    [InlineData("YUV4MPEG2 H360 C420jpeg")]                      // genişlik yok
    [InlineData("YUV4MPEG2 W99999 H360 C420jpeg")]               // saçma boyut
    public void ParseHeader_rejects_unsupported_streams(string line)
        => Assert.Throws<InvalidDataException>(() => Y4mFrameReader.ParseHeader(line));

    [Fact]
    public async Task Reads_header_then_consecutive_frames_and_reuses_buffer()
    {
        var a = Yuv420FrameViewTests.MakeYuv(8, 4, (x, y) => (10, 20, 30));
        var b = Yuv420FrameViewTests.MakeYuv(8, 4, (x, y) => (200, 90, 60));
        using var ms = new MemoryStream(Stream("YUV4MPEG2 W8 H4 F5:1 Ip A1:1 C420mpeg2 XCOLORRANGE=LIMITED", a, b));

        var h = await Y4mFrameReader.ReadHeaderAsync(ms, CancellationToken.None);
        Assert.NotNull(h);
        Assert.Equal(8, h!.Value.Width); Assert.Equal(4, h.Value.Height);

        var marker = new byte[8];
        var f1 = await Y4mFrameReader.ReadFrameAsync(ms, h.Value, marker, CancellationToken.None);
        Assert.Equal(a, f1);
        var spare = new byte[h.Value.FrameBytes];
        var f2 = await Y4mFrameReader.ReadFrameAsync(ms, h.Value, marker, CancellationToken.None, reuse: spare);
        Assert.Same(spare, f2);                 // doğru boydaki tampon yeniden kullanıldı
        Assert.Equal(b, f2);
        Assert.Null(await Y4mFrameReader.ReadFrameAsync(ms, h.Value, marker, CancellationToken.None));   // akış bitti

        var f = VideoFrame.FromYuv420(f2!, 8, 4, h.Value.FullRange, DateTimeOffset.UnixEpoch);
        Assert.Equal(VideoPixelFormat.Yuv420p, f.Format);
    }

    [Fact]
    public async Task Wrong_size_reuse_buffer_is_ignored()
    {
        var a = Yuv420FrameViewTests.MakeYuv(8, 4, (x, y) => (10, 20, 30));
        using var ms = new MemoryStream(Stream("YUV4MPEG2 W8 H4 C420jpeg", a));
        var h = (await Y4mFrameReader.ReadHeaderAsync(ms, CancellationToken.None))!.Value;
        var wrong = new byte[h.FrameBytes + 3];
        var got = await Y4mFrameReader.ReadFrameAsync(ms, h, new byte[8], CancellationToken.None, reuse: wrong);
        Assert.NotSame(wrong, got);
        Assert.Equal(a, got);
    }

    [Fact]
    public async Task Truncated_frame_returns_null_and_empty_stream_has_no_header()
    {
        var a = Yuv420FrameViewTests.MakeYuv(8, 4, (x, y) => (10, 20, 30));
        var bytes = Stream("YUV4MPEG2 W8 H4 C420jpeg", a);
        using var ms = new MemoryStream(bytes.AsSpan(0, bytes.Length - 5).ToArray());   // son 5 bayt gelmedi (ffmpeg öldü)
        var h = (await Y4mFrameReader.ReadHeaderAsync(ms, CancellationToken.None))!.Value;
        Assert.Null(await Y4mFrameReader.ReadFrameAsync(ms, h, new byte[8], CancellationToken.None));

        using var empty = new MemoryStream(Array.Empty<byte>());
        Assert.Null(await Y4mFrameReader.ReadHeaderAsync(empty, CancellationToken.None));
    }

    [Fact]
    public async Task Misaligned_stream_throws_instead_of_producing_a_shifted_frame()
    {
        // Sabit uzunluklu akışta hizayı kaybetmek = karenin kaymış baytlardan kurulması. Sessizce devam etmek
        // YANLIŞ GÖRÜNTÜ üretir; doğru davranış hata verip ffmpeg'i yeniden başlatmaktır.
        var a = Yuv420FrameViewTests.MakeYuv(8, 4, (x, y) => (10, 20, 30));
        var bytes = Stream("YUV4MPEG2 W8 H4 C420jpeg", a).ToList();
        int frameStart = bytes.Count - a.Length - 6;
        bytes.InsertRange(frameStart, new byte[] { 1, 2, 3 });   // işaretten önce fazladan bayt
        using var ms = new MemoryStream(bytes.ToArray());
        var h = (await Y4mFrameReader.ReadHeaderAsync(ms, CancellationToken.None))!.Value;
        await Assert.ThrowsAsync<InvalidDataException>(() => Y4mFrameReader.ReadFrameAsync(ms, h, new byte[8], CancellationToken.None));
    }

    [Fact]
    public async Task Frame_marker_with_parameters_is_accepted()
    {
        var a = Yuv420FrameViewTests.MakeYuv(8, 4, (x, y) => (10, 20, 30));
        var ms = new MemoryStream();
        ms.Write("YUV4MPEG2 W8 H4 C420jpeg\n"u8);
        ms.Write("FRAME Ip\n"u8);
        ms.Write(a);
        ms.Position = 0;
        var h = (await Y4mFrameReader.ReadHeaderAsync(ms, CancellationToken.None))!.Value;
        Assert.Equal(a, await Y4mFrameReader.ReadFrameAsync(ms, h, new byte[8], CancellationToken.None));
    }
}
