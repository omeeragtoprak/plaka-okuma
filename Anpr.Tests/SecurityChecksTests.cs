using System.Net;
using Anpr.Service;

namespace Anpr.Tests;

/// <summary>H4: panel korumasız-maruziyet tespiti (LAN-açık bind + boş PanelToken).</summary>
public class SecurityChecksTests
{
    [Theory]
    [InlineData("0.0.0.0", null)]     // tüm arayüzler + token yok
    [InlineData("0.0.0.0", "")]       // tüm arayüzler + boş token
    [InlineData("0.0.0.0", "   ")]    // tüm arayüzler + boşluk token
    [InlineData("192.168.1.5", null)] // LAN IP + token yok
    [InlineData("::", null)]          // IPv6 Any + token yok
    public void Insecure_when_non_loopback_and_no_token(string bind, string? token)
        => Assert.True(SecurityChecks.IsInsecureExposure(IPAddress.Parse(bind), token));

    [Theory]
    [InlineData("127.0.0.1", null)]   // loopback: token olmadan da güvenli
    [InlineData("127.0.0.1", "")]
    [InlineData("0.0.0.0", "gizli")]  // token varsa LAN-açık bile korunuyor
    [InlineData("192.168.1.5", "gizli")]
    [InlineData("::1", null)]         // IPv6 loopback: token olmadan da güvenli
    public void Secure_when_loopback_or_token_present(string bind, string? token)
        => Assert.False(SecurityChecks.IsInsecureExposure(IPAddress.Parse(bind), token));
}
