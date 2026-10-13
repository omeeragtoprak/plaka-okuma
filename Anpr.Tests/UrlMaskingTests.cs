using System.Reflection;

namespace Anpr.Tests;

/// <summary>
/// Kimlik bilgisi maskeleme (2026-07-28 HIGH bulgu): ffmpeg hata satırında TAM RTSP URL'sini basıyor,
/// bu satır maskelenmeden dosya loguna yazılıyordu — canlı ölçüm: tek günün logunda 20 satırda açık
/// kamera admin şifresi. "Kamera bağlanmıyor" vakasında ilk paylaşılan dosya logtur, yani sızıntı tam da
/// şifrenin en çok dolaştığı anda oluşuyordu.
/// </summary>
public class UrlMaskingTests
{
    private static readonly Type T =
        typeof(Anpr.Sdk.AnprRuntime).Assembly.GetType("Anpr.Sdk.Internal.UrlMasking")!;

    private static string Mask(string s) =>
        (string)T.GetMethod("MaskCredentials", BindingFlags.NonPublic | BindingFlags.Static)!
                 .Invoke(null, new object?[] { s })!;

    private static string MaskKnown(string s, string? secret) =>
        (string)T.GetMethod("MaskKnownSecret", BindingFlags.NonPublic | BindingFlags.Static)!
                 .Invoke(null, new object?[] { s, secret })!;

    [Fact]
    public void Masks_password_in_real_ffmpeg_error_line()
    {
        // Canlı logdan alınmış gerçek satır biçimi.
        const string line = "Error opening input file rtsp://admin:s3cr3tPw@192.168.5.15:554/Streaming/Channels/101.";
        var masked = Mask(line);
        Assert.DoesNotContain("s3cr3tPw", masked);
        Assert.Contains("****", masked);
        Assert.Contains("192.168.5.15", masked);   // teşhis için host korunmalı
    }

    [Fact]
    public void Leaves_lines_without_credentials_untouched()
    {
        const string line = "Connection to tcp://192.168.5.14:554 failed: Connection refused";
        Assert.Equal(line, Mask(line));
    }

    [Fact]
    public void Known_secret_is_removed_even_without_url_shape()
    {
        // İkinci savunma: ffmpeg sürümü URL'yi farklı basarsa regex kaçabilir.
        Assert.DoesNotContain("s3cr3tPw", MaskKnown("auth failed for user admin pass s3cr3tPw", "s3cr3tPw"));
    }

    [Fact]
    public void Short_or_empty_secret_does_not_corrupt_text()
    {
        // Çok kısa şifre yanlış eşleşme yapardı (ör. "abc" her yerde) → devre dışı olmalı.
        const string line = "abc problem in stream";
        Assert.Equal(line, MaskKnown(line, "abc"));
        Assert.Equal(line, MaskKnown(line, null));
    }
}
