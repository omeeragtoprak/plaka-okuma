using Anpr.Sdk.Configuration;
using Anpr.Sdk.Frames;

namespace Anpr.Tests;

/// <summary>ffmpeg açılış-probe'u (H2): eksik ffmpeg SESSİZCE sonsuz-retry yerine NET hata vermeli.</summary>
public class FfmpegFrameSourceTests
{
    [Fact]
    public void EnsureAvailable_throws_clear_error_when_ffmpeg_missing()
    {
        // Var olmayan ikili -> Process.Start Win32Exception atar -> EnsureAvailable net InvalidOperationException'a çevirir.
        var opts = new FfmpegOptions { Path = "ffmpeg-definitely-not-installed-xyz-123" };
        var ex = Assert.Throws<InvalidOperationException>(() => FfmpegFrameSource.EnsureAvailable(opts));
        Assert.Contains("ffmpeg", ex.Message);
        Assert.Contains("bariyer", ex.Message);   // net remediation mesajı (sessiz başarısızlık değil)
    }
}
