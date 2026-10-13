using Anpr.Sdk.Configuration;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// Taşınabilirlik (2026-07-29): RTSP yolu eskiden SABİT kodluydu ("/Streaming/Channels/101" = Hikvision).
/// Müşteri yalnız Ip/Username/Password verdiğinde başka markada bağlantı SESSİZCE kurulamıyordu.
/// Artık şablonlanabilir; varsayılan eski davranışla BİREBİR aynı olmalı (kırıcı değişiklik yok).
/// </summary>
public class RtspUrlTemplateTests
{
    private static CameraOptions Cam() => new()
    {
        Id = "giris", Name = "Giriş", Ip = "192.168.1.64",
        Username = "operator", Password = "p@ss word",
    };

    [Fact]
    public void Explicit_RtspUrl_wins_over_template()
    {
        var c = Cam();
        c.RtspUrl = "rtsp://baska/akis";
        c.RtspUrlTemplate = "rtsp://{ip}/kullanilmamali";
        Assert.Equal("rtsp://baska/akis", AnprCameraSupervisor.BuildRtspUrl(c));
    }

    [Fact]
    public void Default_template_reproduces_previous_hardcoded_url_exactly()
    {
        // GERİYE UYUMLULUK KAPISI: mevcut Hikvision kurulumlarının URL'i DEĞİŞMEMELİ.
        // Eski kod: $"rtsp://{esc(user)}:{esc(pass)}@{ip}:554/Streaming/Channels/101"
        Assert.Equal("rtsp://operator:p%40ss%20word@192.168.1.64:554/Streaming/Channels/101",
                     AnprCameraSupervisor.BuildRtspUrl(Cam()));
    }

    [Fact]
    public void Credentials_are_url_escaped_so_special_chars_do_not_break_the_url()
    {
        var url = AnprCameraSupervisor.BuildRtspUrl(Cam());
        Assert.Contains("p%40ss%20word", url);   // '@' ve boşluk kaçışlı — ham hâli URL'i bozardı
        Assert.DoesNotContain("p@ss word", url);
    }

    [Fact]
    public void Custom_template_supports_other_brands()
    {
        var c = Cam();
        c.RtspUrlTemplate = "rtsp://{user}:{pass}@{ip}:{port}/cam/realmonitor?channel=1&subtype=0"; // Dahua
        Assert.Equal("rtsp://operator:p%40ss%20word@192.168.1.64:554/cam/realmonitor?channel=1&subtype=0",
                     AnprCameraSupervisor.BuildRtspUrl(c));
    }

    [Fact]
    public void Custom_port_is_substituted()
    {
        var c = Cam();
        c.RtspPort = 8554;
        Assert.Contains(":8554/", AnprCameraSupervisor.BuildRtspUrl(c));
    }

    [Fact]
    public void Blank_template_falls_back_to_default_instead_of_producing_garbage()
    {
        var c = Cam();
        c.RtspUrlTemplate = "   ";
        Assert.Equal("rtsp://operator:p%40ss%20word@192.168.1.64:554/Streaming/Channels/101",
                     AnprCameraSupervisor.BuildRtspUrl(c));
    }
}
