using Anpr.Service;

namespace Anpr.Tests;

/// <summary>M14: /health üst-durumu kör bariyer kamerasını maskelememeli.</summary>
public class HealthStatusTests
{
    [Theory]
    [InlineData(0, 0, "no-cameras")]
    [InlineData(2, 2, "ok")]        // hepsi bağlı
    [InlineData(2, 1, "degraded")]  // giriş KÖR ama önceden "ok" derdi — asıl düzeltme
    [InlineData(2, 0, "down")]      // hiçbiri
    [InlineData(1, 1, "ok")]
    [InlineData(1, 0, "down")]
    public void Compute_reflects_all_some_none(int total, int connected, string expected)
        => Assert.Equal(expected, HealthStatus.Compute(total, connected));
}
