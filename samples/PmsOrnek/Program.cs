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

// Doğruluk: sitenin bilinen araçlarını verin → sistematik yanlış-okumalar bilinen plakaya lev≤1
// düzeltilir (ör. bir karede 25 ADM 032 -> 25 AUM 032 okunursa geri düzelir). Ölçüldü: +9.3pp,
// %0.16 yanlış-snap. Listeyi veriden üretin: scripts/harvest_knownplates.py (kamera+kendi okumalar).
// options.KnownPlates.AddRange(new[] { "25 ADM 032", "25 TGG 001" });
// options.SnapMaxLev = 1;                 // lev≤1 (lev≤2 bariyer için fazla yanlış-snap ölçüldü)
// options.SnapMaxOcrConfidence = 0.99f;   // varsayılan; model ≥0.99 okumaları korunur

if (giris != null) options.Cameras.Add(new CameraOptions { Id = "giris", Name = "Giriş", RtspUrl = giris });
if (cikis != null) options.Cameras.Add(new CameraOptions { Id = "cikis", Name = "Çıkış", RtspUrl = cikis });

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true)
                                                     .SetMinimumLevel(LogLevel.Information));

await using var runtime = new AnprRuntime(options, loggerFactory);

int eventCount = 0;
// KONTRAT: işleyici worker döngü thread'inde SENKRON + birden çok kameradan EŞZAMANLI çağrılır →
// thread-safe olmalı VE HIZLI dönmeli (uzun iş = kendi kuyruğunuza atın; bloklarsanız boru hattı durur,
// araç kaçırılır). Burada Interlocked + Console.WriteLine kullanıldı (ikisi de güvenli/hızlı).
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
