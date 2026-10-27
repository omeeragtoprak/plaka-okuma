using System.Text.RegularExpressions;
using Anpr.Service.Panel;

namespace Anpr.Tests;

/// <summary>
/// G7 arayüz denetimi (2026-10-27): <c>PanelToken</c> ayarlıyken her korumalı istek belirteci taşımalı. Analiz sayfası
/// <c>/api/analytics</c>'i ve kanıt görsellerini belirteçsiz istiyordu → token'lı kurulumda sayfa boş/kırık kalıyordu; sayfalar
/// arası bağlantılar da belirteci düşürüyordu. Bu metin denetimi gerilemeyi yakalar (tarayıcı denemesi ayrıca yapıldı).
/// </summary>
public class PanelPageTokenTests
{
    public static TheoryData<string, string> Pages => new()
    {
        { "panel", PanelPage.Html }, { "analiz", AnalysisPage.Html }, { "dogrulama", VerifyPage.Html }, { "kamera", CameraPage.Html },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void Protected_fetches_always_go_through_the_token_helper(string name, string html)
    {
        // fetch('/api…'), fetch("/events…"), fetch(`/evidence…`) — sarmalayıcısız korumalı istek OLMAMALI.
        var bare = Regex.Matches(html, @"fetch\(\s*['""`]/(api|events|evidence)[^'""`]*").Select(m => m.Value).ToArray();
        Assert.True(bare.Length == 0, $"{name}: belirteçsiz korumalı istek: {string.Join(" | ", bare)}");
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Internal_navigation_links_carry_the_token(string name, string html)
        => Assert.Contains("querySelectorAll('a[href^=\"/\"]')", html);

    [Fact]
    public void Analysis_evidence_images_carry_the_token()
    {
        Assert.Contains("src=\"${withTok('/'+r.evidence)}\"", AnalysisPage.Html);
        Assert.Contains("data-full=\"${withTok('/'+r.frame)}\"", AnalysisPage.Html);
    }

    [Fact]
    public void Camera_page_escapes_every_server_string()
    {
        // Öneri/sorun metinleri sunucudan gelir (ör. kameranın döndürdüğü kök öğe adı sorun metnine girer) → kaçışsız basılmamalı.
        foreach (var expr in new[] { "esc(a.title)", "esc(a.detail)", "esc(a.basis)", "esc(p)", "esc(c.cameraName || c.cameraId)" })
            Assert.Contains(expr, CameraPage.Html);
        Assert.DoesNotContain("${a.detail}", CameraPage.Html);
        Assert.DoesNotContain("${p}", CameraPage.Html);
    }

    [Fact]
    public void Camera_page_has_no_control_that_writes_to_the_camera()
    {
        // Sayfa salt-okunurdur: yalnız GET (fetch varsayılanı) — PUT/POST/DELETE çağrısı içermemeli.
        Assert.DoesNotMatch(@"method\s*:\s*['""](PUT|POST|DELETE|PATCH)", CameraPage.Html);
    }
}
