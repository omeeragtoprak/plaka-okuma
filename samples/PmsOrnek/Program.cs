// Otopark yazılımına (PMS) DOĞRUDAN GÖMÜLÜM örneği: Anpr.Sdk referansı + AnprRuntime.
// RTSP adresleri ortam değişkeninden alınır (kimlik bilgisi repoya girmez):
//   ANPR_RTSP_GIRIS="rtsp://kullanici:sifre@ip:554/Streaming/Channels/101"
//   ANPR_RTSP_CIKIS="..." (isteğe bağlı ikinci kamera)
// Çalıştırma: dotnet run  (60 sn okur, gelen olayları yazar, kapanır)

using Anpr.Sdk;
using Anpr.Sdk.Configuration;
using Microsoft.Extensions.Logging;

string? giris = Environment.GetEnvironmentVariable("ANPR_RTSP_GIRIS");
string? cikis = Environment.GetEnvironmentVariable("ANPR_RTSP_CIKIS");
if (giris is null && cikis is null)
{
    Console.Error.WriteLine("ANPR_RTSP_GIRIS (ve isteğe bağlı ANPR_RTSP_CIKIS) ortam değişkenini ayarlayın.");
    return 1;
}

var options = new AnprServiceOptions();   // üretimde doğrulanmış varsayılanlar
// Ölçüm modu: ANPR_REQUIRE_APPROACHING=false ile filtre kapatılıp ham SizeTrend gözlemlenebilir.
if (string.Equals(Environment.GetEnvironmentVariable("ANPR_REQUIRE_APPROACHING"), "false", StringComparison.OrdinalIgnoreCase))
    options.RequireApproaching = false;
// Deney: ANPR_FPS ile işleme kare hızını dene (yön için daha çok örnek). Boşsa varsayılan (2).
if (double.TryParse(Environment.GetEnvironmentVariable("ANPR_FPS"), out var fps) && fps > 0)
    { options.Fps = fps; options.IdleFps = Math.Min(fps, options.IdleFps + 1); }
if (giris != null) options.Cameras.Add(new CameraOptions { Id = "giris", Name = "Giriş", RtspUrl = giris });
if (cikis != null) options.Cameras.Add(new CameraOptions { Id = "cikis", Name = "Çıkış", RtspUrl = cikis });

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true)
                                                     .SetMinimumLevel(LogLevel.Information));

await using var runtime = new AnprRuntime(options, loggerFactory);

int eventCount = 0;
runtime.PlateDetected += e =>
{
    if (e.Scope != "roi") return;                       // gözlem okumaları bariyer kararına girmez
    Interlocked.Increment(ref eventCount);

    // PMS iş kuralı örneği: yüksek güven -> bariyer/kayıt; düşük güven -> operatör onayı.
    string yon = e.SizeTrend < 0 ? "trend=? (kararsız)" : $"trend={e.SizeTrend:F2}";
    Console.WriteLine(e.LowConfidence
        ? $">>> OPERATÖR ONAYI: {e.Plate} (güven {e.OcrConfidence:P0}, {e.CameraId}, {yon})"
        : $">>> PLAKA: {e.Plate} (güven {e.OcrConfidence:P0}, {e.CameraId}, {yon})");
};

await runtime.StartAsync();
Console.WriteLine($"ANPR çalışıyor ({options.Cameras.Count} kamera, CPU: {runtime.CpuFeatures}). 60 sn dinleniyor...");
await Task.Delay(TimeSpan.FromSeconds(60));

foreach (var cam in runtime.Cameras)
    Console.WriteLine($"[{cam.CameraId}] bağlı={cam.IsConnected} kare={cam.FramesReceived} " +
                      $"işlenen={cam.FramesProcessed} p50={cam.FrameMsP50}ms p95={cam.FrameMsP95}ms olay={cam.EventsEmitted}");
Console.WriteLine($"Toplam roi olayı: {eventCount}");

await runtime.StopAsync();
return 0;
