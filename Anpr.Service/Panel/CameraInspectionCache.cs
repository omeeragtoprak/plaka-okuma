using System.Collections.Concurrent;
using Anpr.Sdk.Cameras;

namespace Anpr.Service.Panel;

/// <summary>
/// Kamera incelemesi önbelleği (panel <c>/kamera</c>). İnceleme ağ isteği yapar (ffprobe + ISAPI GET); panel her açıldığında
/// kameralara gitmesin diye sonuç <see cref="Fresh"/> süresince yeniden kullanılır ve aynı kameraya eşzamanlı istekler tek
/// okumaya iner.
/// <para>🔒 <b>Kilitlenme koruması:</b> kamera kimlik bilgisini reddettiyse (401/403) <see cref="AuthBackoff"/> boyunca üretici
/// API'sine HİÇ gidilmez — "yeniden oku" düğmesine basılsa bile (Hikvision art arda hatalı girişte istemci IP'sini kilitleyebilir).
/// O sürede yalnız RTSP yoklaması yenilenir.</para>
/// </summary>
public sealed class CameraInspectionCache
{
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AuthBackoff = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, CameraInspection Result, DateTimeOffset? AuthRejectedAt)> _items =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="load">Gerçek inceleme; parametre = üretici API'si okunsun mu.</param>
    /// <summary>Bu kamera için üretici API'si şu an kimlik reddi yüzünden beklemede mi (panelin <c>authRejected</c> alanı).</summary>
    public bool IsInAuthBackoff(string cameraId, DateTimeOffset now) =>
        _items.TryGetValue(cameraId, out var c) && c.AuthRejectedAt is { } at && now - at < AuthBackoff;

    public async Task<CameraInspection> GetAsync(string cameraId, bool refresh, Func<bool, Task<CameraInspection>> load,
        DateTimeOffset now, CancellationToken ct = default)
    {
        var gate = _gates.GetOrAdd(cameraId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            _items.TryGetValue(cameraId, out var cached);
            if (cached.Result != null && !refresh && now - cached.At < Fresh) return cached.Result;

            bool backoff = cached.AuthRejectedAt is { } at && now - at < AuthBackoff;
            var result = await load(!backoff);
            DateTimeOffset? rejectedAt = result.Vendor?.AuthRejected == true ? now : (backoff ? cached.AuthRejectedAt : null);
            if (backoff)
                result = result with
                {
                    Problems = result.Problems.Append(
                        $"ISAPI okunmadı: kimlik bilgisi son {AuthBackoff.TotalMinutes:0} dk içinde reddedildi; kilitlenmeyi önlemek için " +
                        "beklemede. Kullanıcı adı/şifreyi düzeltip servisi yeniden başlatın.").ToArray(),
                };
            _items[cameraId] = (now, result, rejectedAt);
            return result;
        }
        finally { gate.Release(); }
    }
}
