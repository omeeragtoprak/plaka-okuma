using Anpr.Sdk;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>M1: PMS'e salt-okunur kamera durumu; somut CameraWorker lifecycle metotları sızmamalı.</summary>
public class ICameraStatusTests
{
    [Fact]
    public void CameraWorker_implements_ICameraStatus()
        => Assert.True(typeof(ICameraStatus).IsAssignableFrom(typeof(CameraWorker)));

    [Fact]
    public void ICameraStatus_is_readonly_and_leaks_no_lifecycle_methods()
    {
        var t = typeof(ICameraStatus);
        // Metot YOK (property getter'ları SpecialName) → Start/DisposeAsync/ApplyRoi sızmadı.
        Assert.Empty(t.GetMethods().Where(m => !m.IsSpecialName));
        // Tüm property'ler salt-okunur (PMS runtime durumunu mutasyona uğratamaz).
        Assert.All(t.GetProperties(), p => Assert.False(p.CanWrite));
        // Kritik durum alanları mevcut.
        Assert.NotNull(t.GetProperty("IsConnected"));
        Assert.NotNull(t.GetProperty("FrameMsP95"));
        Assert.NotNull(t.GetProperty("RestartCount"));
    }
}
