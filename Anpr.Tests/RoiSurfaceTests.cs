using Anpr.Sdk;
using Anpr.Sdk.Configuration;

namespace Anpr.Tests;

/// <summary>
/// Taşınabilirlik (2026-07-29): ROI ürünün EN kritik saha kalibrasyonu ama SDK'da çalışma-zamanı
/// yüzeyi YOKTU — tek yol <c>[Obsolete] Cameras</c> üzerinden somut CameraWorker'a inmekti.
/// (Çizim/kalıcılık Anpr.Service panelinde yaşıyor ve ürüne GİRMİYOR.)
/// <para>KAPSAM UYARISI: burada yalnız "kamera bulunamadı" sözleşmesi doğrulanır. "Bulundu" yolu
/// çalışan bir runtime ister (worker listesi yalnız StartAsync içinde dolar) ve model + ffmpeg
/// gerektirdiği için birim testinde KAPSANMAZ — saha kabulünde doğrulanmalıdır.</para>
/// </summary>
public class RoiSurfaceTests
{
    private static AnprServiceOptions Opts() => new()
    {
        Cameras = { new CameraOptions { Id = "giris", Name = "Giriş", RtspUrl = "rtsp://yok/akis" } },
        EvidenceCrops = false,
    };

    [Fact]
    public async Task GetRoi_returns_null_for_unknown_camera_instead_of_throwing()
    {
        await using var rt = new AnprRuntime(Opts());
        Assert.Null(rt.GetRoi("boyle-bir-kamera-yok"));
    }

    [Fact]
    public async Task SetRoi_returns_false_for_unknown_camera_instead_of_throwing()
    {
        // PMS yanlış kamera kimliği verirse SESSİZ başarı olmamalı — çağıran false görüp fark etmeli.
        await using var rt = new AnprRuntime(Opts());
        Assert.False(rt.SetRoi("boyle-bir-kamera-yok", "100,400;900,400;900,950;100,950"));
    }

    [Fact]
    public async Task Roi_api_rejects_null_camera_id()
    {
        await using var rt = new AnprRuntime(Opts());
        Assert.Throws<ArgumentNullException>(() => rt.GetRoi(null!));
        Assert.Throws<ArgumentNullException>(() => rt.SetRoi(null!, ""));
    }
}
