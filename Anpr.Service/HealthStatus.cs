namespace Anpr.Service;

/// <summary>/health üst-durumu (M14). Saf/test-edilebilir. Kör bir bariyer kamerasını maskelememeli:
/// bir kamera bağlı diye "ok" demek, giriş kamerası düşükken sistemi sağlıklı gösterir.</summary>
public static class HealthStatus
{
    /// <summary>ok = TÜM kameralar bağlı · degraded = bazıları bağlı (en az biri KÖR) · down = hiçbiri ·
    /// no-cameras = yapılandırmada kamera yok.</summary>
    public static string Compute(int cameraCount, int connectedCount)
        => cameraCount == 0 ? "no-cameras"
         : connectedCount == cameraCount ? "ok"
         : connectedCount > 0 ? "degraded"
         : "down";
}
