# Anpr.Sdk

CPU-only **Türk plaka tanıma (ANPR)** çalışma zamanı — otopark yazılımına (PMS) doğrudan gömülmek
için. RTSP kameradan kareyi çeker, plakayı tespit + OCR eder, çok-kareli oylama + TR gramer
kısıtlaması + varyant bastırma uygular ve **tek bir C# olayı** olarak plaka yayınlar. GPU gerektirmez;
otopark ofis PC'lerinde çalışacak şekilde tasarlanmıştır.

Modeller (dedektör + Erzurum saha fine-tune OCR + rektifikasyon + SR) pakete gömülüdür ve
tüketicinin çıktı dizinine `models/` olarak kopyalanır — ekstra dosya taşımak gerekmez.

## Gereksinimler
- .NET 8.0+ (x64 / arm64)
- `ffmpeg` (PATH'te ya da `options.Ffmpeg.Path`) — RTSP + HEVC çözer

## Hızlı başlangıç

```csharp
using Anpr.Sdk;
using Anpr.Sdk.Configuration;

var runtime = new AnprRuntime(new AnprServiceOptions {
    Cameras = {
        new CameraOptions { Id = "giris", Name = "Giriş",
                            RtspUrl = "rtsp://kullanici:sifre@ip:554/Streaming/Channels/101" },
        new CameraOptions { Id = "cikis", Name = "Çıkış", RtspUrl = "rtsp://..." },
    }
}, loggerFactory);   // ILoggerFactory opsiyonel

runtime.PlateDetected += e => {
    if (e.Scope != "roi") return;               // ROI dışı "gözlem" okumaları karar üretmez
    if (e.LowConfidence) OperatorOnayinaDusur(e); // güven < eşik → manuel onay
    else PlakaKaydet(e.PlateCompact, e.Timestamp, e.CameraId);
    // e.EvidencePath  -> plaka kırpıntısı JPEG (evidence/…)
    // e.FramePath     -> aracın da göründüğü TAM KARE JPEG (son okunan aracı görselleme)
};

await runtime.StartAsync();
// … uygulama ömrü boyunca çalışır …
await runtime.StopAsync();
```

## `PlateDetected` kontratı (önemli)
İşleyici, kamera worker **döngü thread'inde SENKRON** ve birden çok kameradan **EŞZAMANLI** çağrılır:
- İşleyiciniz **thread-safe/reentrant** olmalı (paylaşılan durumu kilitleyin).
- İşleyici **HIZLI dönmeli** — uzun iş (bariyer I/O, DB, ağ) işleyicide DEĞİL, kendi kuyruğunuzda asenkron.
  Bloklayan işleyici boru hattını durdurur ve **kare düşürür** (araç kaçırılır).
- İşleyicideki istisnalar yutulur+loglanır (boru hattı düşmez).

## Olay sözleşmesi (`PlateEvent`)
`EventId` (idempotency), `Plate` / `PlateCompact`, `Confidence` / `OcrConfidence`,
`LowConfidence` (operatör onayı bayrağı), `Candidates` (top-K aday), `Scope` (`roi`/`gozlem`),
`EvidencePath` (plaka kırpıntısı), `FramePath` (tam kare — araç görünür).

## Doğruluk artırma: KnownPlates (sitenin bilinen araçları)
Otoparkta düzenli araçlar (abone/personel/protokol) baskındır. Bu plakaları `KnownPlates`'e verirseniz,
sistematik yanlış-okumalar (ör. bir kare `25 ADM 032` yerine `25 AUM 032` okunursa) bilinen plakaya
otomatik düzeltilir (lev≤1). **Ölçüldü: +9.3pp** (site verisinde), `%0.16` yanlış-snap.

```csharp
var options = new AnprServiceOptions {
    Cameras = { /* … */ },
    KnownPlates = { "25 ADM 032", "25 TGG 001", /* sitenizin düzenli araçları */ },
    SnapMaxLev = 1,               // lev≤1 (lev≤2 ölçüldü: bariyer için fazla yanlış-snap)
    SnapMaxOcrConfidence = 0.99f, // varsayılan; ≥0.99 güvenli okumalar korunur, altındakiler düzeltilir
};
```
Listeyi elle bakmak yerine **veriden üretebilirsiniz**: kameranın kendi ANPR kayıtları + kendi
olaylarınızın ÇAPRAZ-agreement'ı (bkz. `scripts/harvest_knownplates.py`) — filo değiştikçe yeniden çalıştırın.

## Kalıcılık / zero-loss (önemli)
SDK **in-process** olay verir; kalıcılık **PMS'in sorumluluğudur** (SDK sizin DB'nizi bilmez, disk kuyruğu tutmaz):
- **`PlateDetected`** = kolaylık yolu (bounded-dispatcher; çok yavaş işleyicide olay DÜŞEBİLİR — `DroppedEvents`).
- **Zero-loss için `extraSinks` (IEventSink)** geçin: her olay için awaited çağrılır (DÜŞMEZ). DB/dosyaya
  yazan bir `IEventSink` implement edin (hızlı/async yazın; retry/idempotency `EventId` ile sizde). Örnek:
```csharp
class DbSink : IEventSink {
    public async Task PublishAsync(PlateEvent e, CancellationToken ct) => await _db.UpsertAsync(e.EventId, e, ct);
}
var runtime = new AnprRuntime(options, loggerFactory, extraSinks: new[] { new DbSink() });
```

## Notlar
- Web/panel bu pakette YOKTUR (yalnız motor + orkestrasyon). Canlı izleme paneli ayrı serviste kalır.
- Kameralara yalnız salt-okuma RTSP erişimi gerekir; kamera ayarları değiştirilmez.
- KVKK: kanıt kırpıntıları/tam kareler `evidence/` altına yazılır; saklama süresi ve adet sınırı ayarlanabilir.

Sürüm notları ve ayrıntılı entegrasyon: proje deposundaki `docs/entegrasyon-kilavuzu.md`.
