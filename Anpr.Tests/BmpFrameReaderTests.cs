using Anpr.Sdk.Frames;

namespace Anpr.Tests;

/// <summary>
/// ffmpeg BMP borusu: kareler başlıktaki uzunlukla ayrılır; bozuk baytlarda 'BM' bulununcaya kadar kayılır.
/// Bu okuyucu 16 MB'lık kareleri doğrudan hedef tampona okur — kopya yok; testler çerçevelemeyi doğrular.
/// </summary>
public class BmpFrameReaderTests
{
    private static async Task<List<byte[]>> ReadAll(byte[] stream, List<int>? resyncs = null)
    {
        using var ms = new MemoryStream(stream);
        var header = new byte[14];
        var frames = new List<byte[]>();
        while (true)
        {
            var f = await BmpFrameReader.ReadNextAsync(ms, header, CancellationToken.None, n => resyncs?.Add(n));
            if (f == null) break;
            frames.Add(f);
        }
        return frames;
    }

    [Fact]
    public async Task Reads_consecutive_frames_exactly_by_declared_length()
    {
        var a = VideoFrameTests.MakeBmp(4, 3, (x, y) => (1, 2, 3));
        var b = VideoFrameTests.MakeBmp(2, 5, (x, y) => (9, 8, 7));
        var frames = await ReadAll(a.Concat(b).ToArray());

        Assert.Equal(2, frames.Count);
        Assert.Equal(a, frames[0]);
        Assert.Equal(b, frames[1]);
        Assert.True(VideoFrame.TryFromBmp(frames[1], DateTimeOffset.UnixEpoch, 0, out var vf, out _));
        Assert.Equal(2, vf!.Width); Assert.Equal(5, vf.Height);
    }

    [Fact]
    public async Task Resyncs_on_garbage_and_reports_skipped_bytes()
    {
        var a = VideoFrameTests.MakeBmp(3, 3, (x, y) => (1, 1, 1));
        var garbage = new byte[] { 0x00, 0x42, 0x4D, 0xFF, 0x11 };   // içinde sahte 'BM' da var ama uzunluk mantıksız
        var resyncs = new List<int>();
        var frames = await ReadAll(garbage.Concat(a).ToArray(), resyncs);

        Assert.Single(frames);
        Assert.Equal(a, frames[0]);
        Assert.Equal(new[] { garbage.Length }, resyncs);
    }

    [Fact]
    public async Task Truncated_stream_returns_null_without_throwing()
    {
        var a = VideoFrameTests.MakeBmp(3, 3, (x, y) => (1, 1, 1));
        var frames = await ReadAll(a.AsSpan(0, a.Length - 5).ToArray());   // son 5 bayt gelmedi (ffmpeg öldü)
        Assert.Empty(frames);
    }

    [Fact]
    public void LooksLikeHeader_requires_magic_and_plausible_size()
    {
        var ok = VideoFrameTests.MakeBmp(1, 1, (x, y) => (0, 0, 0));
        Assert.True(BmpFrameReader.LooksLikeHeader(ok));
        var tooSmall = (byte[])ok.Clone(); tooSmall[2] = 10; tooSmall[3] = 0; tooSmall[4] = 0; tooSmall[5] = 0;
        Assert.False(BmpFrameReader.LooksLikeHeader(tooSmall));
        var huge = (byte[])ok.Clone(); huge[5] = 0x7F;   // ~2 GB
        Assert.False(BmpFrameReader.LooksLikeHeader(huge));
    }

    [Fact]
    public async Task Reuse_buffer_is_used_when_size_matches_and_ignored_otherwise()
    {
        var a = VideoFrameTests.MakeBmp(4, 3, (x, y) => (1, 2, 3));
        var header = new byte[14];
        var spare = new byte[a.Length];
        using (var ms = new MemoryStream(a))
        {
            var got = await BmpFrameReader.ReadNextAsync(ms, header, CancellationToken.None, reuse: spare);
            Assert.Same(spare, got);                 // aynı tampon dolduruldu
            Assert.Equal(a, got);
        }
        var wrongSize = new byte[a.Length + 8];
        using (var ms = new MemoryStream(a))
        {
            var got = await BmpFrameReader.ReadNextAsync(ms, header, CancellationToken.None, reuse: wrongSize);
            Assert.NotSame(wrongSize, got);          // boyut tutmadı → yeni tahsis
            Assert.Equal(a, got);
        }
    }
}
