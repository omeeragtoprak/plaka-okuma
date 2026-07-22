// Otopark yazılımına (PMS) DOĞRUDAN GÖMÜLÜM referans örneği: Anpr.Sdk + AnprRuntime.
// PMS projeniz `Anpr.Sdk` paketini referans alır, AnprRuntime'ı KENDİ süreci içinde çalıştırır ve
// PlateDetected olayını dinler. Kamera RTSP adresleri ortam değişkeninden (kimlik bilgisi repoya girmez):
//   ANPR_RTSP_GIRIS="rtsp://kullanici:sifre@ip:554/Streaming/Channels/101"
//   ANPR_RTSP_CIKIS="..." (isteğe bağlı ikinci kamera)
// Çalıştırma: dotnet run  (60 sn okur, gelen olayları yazar, kapanır).

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

var options = new AnprServiceOptions();   // üretimde doğrulanmış varsayılanlar (çoğu senaryoda yeterli)

// Siyah resmi/protokol plaka düzeltme (isteğe bağlı, offline +27pp doğrulandı): sitenizin bilinen
// resmi araç plakalarını girin; okuma bunlara lev≤SnapMaxLev yakınsa otomatik düzeltilir.
// options.KnownPlates.AddRange(new[] { "25 LT 424", "25 TGG 001" });
// options.SnapMaxLev = 1;   // 1 = güvenli (0 yanlış-snap ölçüldü)

if (giris != null) options.Cameras.Add(new CameraOptions { Id = "giris", Name = "Giriş", RtspUrl = giris });
if (cikis != null) options.Cameras.Add(new CameraOptions { Id = "cikis", Name = "Çıkış", RtspUrl = cikis });

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true)
                                                     .SetMinimumLevel(LogLevel.Information));

await using var runtime = new AnprRuntime(options, loggerFactory);

int eventCount = 0;
runtime.PlateDetected += e =>
{
    if (e.Scope != "roi") return;                       // "gozlem" (ROI dışı) okumalar bariyer kararına girmez
    Interlocked.Increment(ref eventCount);

    // PMS iş kuralı örneği:
    //  - e.EventId       : idempotency anahtarı (retry teslimi mükerrer olabilir; işlenmişse yoksay)
    //  - e.LowConfidence : true ise bariyeri otomatik açma, operatör onayına düşür (kanıt görseliyle)
    //  - e.EvidencePath  : plaka kırpıntısı JPEG ; e.FramePath : aracın da göründüğü tam kare
    //  - e.Candidates    : whitelist/abone eşleşmesi için top-K alternatif
    if (e.LowConfidence)
        OperatorOnayinaDusur(e);
    else
        BariyerVeKayit(e);
};

await runtime.StartAsync();   // modeller yoksa/bozuksa fırlatır (sessiz yanlış çalışma yok)
Console.WriteLine($"ANPR çalışıyor ({options.Cameras.Count} kamera, CPU: {runtime.CpuFeatures}). 60 sn dinleniyor...");

// Üretimde: uygulama ömrü boyunca çalışır; kapanışta StopAsync (aşağıdaki 'await using' halleder).
await Task.Delay(TimeSpan.FromSeconds(60));

foreach (var cam in runtime.CameraStatus)   // salt-okunur durum görünümü (M1)
    Console.WriteLine($"[{cam.CameraId}] bağlı={cam.IsConnected} kare={cam.FramesReceived} " +
                      $"işlenen={cam.FramesProcessed} p50={cam.FrameMsP50}ms p95={cam.FrameMsP95}ms olay={cam.EventsEmitted}");
Console.WriteLine($"Toplam roi olayı: {eventCount}");

await runtime.StopAsync();
return 0;

// --- PMS tarafı iş kuralı iskeleti (örnek) ---
static void BariyerVeKayit(Anpr.Sdk.Events.PlateEvent e) =>
    Console.WriteLine($">>> PLAKA: {e.Plate} (güven {e.OcrConfidence:P0}, {e.CameraId})");

static void OperatorOnayinaDusur(Anpr.Sdk.Events.PlateEvent e) =>
    Console.WriteLine($">>> OPERATÖR ONAYI: {e.Plate} (güven {e.OcrConfidence:P0}, {e.CameraId}, kanıt: {e.EvidencePath})");
