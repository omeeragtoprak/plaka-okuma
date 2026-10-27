using Anpr.Sdk.Cameras;
using Anpr.Service.Panel;

namespace Anpr.Tests;

/// <summary>Panel kamera incelemesi önbelleği: kameraya gereksiz istek atılmaması ve kimlik reddinde kilitlenme koruması.</summary>
public class CameraInspectionCacheTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static CameraInspection Result(bool authRejected = false) => new()
    {
        CameraId = "giris", CapturedUtc = T0,
        Vendor = new CameraSettingsSnapshot { CameraId = "giris", Source = "hikvision-isapi", CapturedUtc = T0, AuthRejected = authRejected },
    };

    [Fact]
    public async Task Fresh_result_is_reused_and_refresh_forces_a_read()
    {
        var cache = new CameraInspectionCache();
        int calls = 0;
        Task<CameraInspection> Load(bool _) { calls++; return Task.FromResult(Result()); }

        await cache.GetAsync("giris", false, Load, T0);
        await cache.GetAsync("giris", false, Load, T0.AddSeconds(30));
        Assert.Equal(1, calls);                                       // 60 sn içinde kameraya tekrar gidilmez
        await cache.GetAsync("giris", true, Load, T0.AddSeconds(31));
        Assert.Equal(2, calls);                                       // "yeniden oku"
        await cache.GetAsync("giris", false, Load, T0.AddSeconds(200));
        Assert.Equal(3, calls);                                       // süre doldu
    }

    [Fact]
    public async Task After_auth_rejection_vendor_api_is_not_retried_even_on_refresh()
    {
        var cache = new CameraInspectionCache();
        var readVendor = new List<bool>();
        bool reject = true;
        Task<CameraInspection> Load(bool vendor) { readVendor.Add(vendor); return Task.FromResult(Result(authRejected: vendor && reject)); }

        await cache.GetAsync("giris", false, Load, T0);
        var r = await cache.GetAsync("giris", true, Load, T0.AddMinutes(1));
        Assert.Equal(new[] { true, false }, readVendor);              // ikinci okumada ISAPI'ye GİDİLMEDİ
        Assert.Contains(r.Problems, p => p.Contains("kilitlenmeyi önlemek"));
        Assert.True(cache.IsInAuthBackoff("giris", T0.AddMinutes(1)));  // panel JSON'u "authRejected" doğru söylesin

        reject = false;
        await cache.GetAsync("giris", true, Load, T0.AddMinutes(9));
        Assert.False(readVendor[^1]);                                 // 10 dk dolmadan hâlâ beklemede
        await cache.GetAsync("giris", true, Load, T0.AddMinutes(11));
        Assert.True(readVendor[^1]);                                  // süre doldu → bir kez yeniden denenir
    }

    [Fact]
    public async Task Concurrent_requests_for_one_camera_share_a_single_read()
    {
        var cache = new CameraInspectionCache();
        int calls = 0;
        var gate = new TaskCompletionSource();
        async Task<CameraInspection> Load(bool _) { Interlocked.Increment(ref calls); await gate.Task; return Result(); }

        var a = cache.GetAsync("giris", false, Load, T0);
        var b = cache.GetAsync("giris", false, Load, T0);
        gate.SetResult();
        await Task.WhenAll(a, b);
        Assert.Equal(1, calls);
    }
}
