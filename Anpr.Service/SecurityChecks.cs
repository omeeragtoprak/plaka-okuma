using System.Net;

namespace Anpr.Service;

/// <summary>Açılış güvenlik kontrolleri (H4). Saf/test-edilebilir tutulur.</summary>
public static class SecurityChecks
{
    /// <summary>Panel korumasız maruz kalıyor mu: bind loopback DIŞINDA (LAN/tüm arayüzler) VE
    /// PanelToken boş ise, canlı görüntü/plaka/ROI kimlik-doğrulamasız erişilebilir (KVKK/güvenlik).</summary>
    public static bool IsInsecureExposure(IPAddress bind, string? panelToken)
        => !IPAddress.IsLoopback(bind) && string.IsNullOrWhiteSpace(panelToken);
}
