# OtoparkAnpr — Yerel CPU Plaka Tanıma (ANPR) Servisi

İki RTSP kameradan saniyede ~1 kare alıp Türkiye plakalarını **tespit eder + okur**, formatı
doğrular, aynı aracı tekrar saymaz (dedup) ve sonuçları hem log'a yazar hem de yerel bir HTTP API
ile sunar. **Yalnızca CPU** üzerinde çalışır (GPU/CUDA gerektirmez). Mevcut C# exe / .NET web
uygulamasına HTTP API veya `IEventSink` arayüzü ile entegre edilir.

> **Canlı kanıt (gerçek trafik):** Servis Erzurum Belediyesi giriş/çıkış kameralarında çalıştırıldı
> ve gerçek plakaları okudu, örn. `25 ADT 382`, `25 ABJ 617`, `26 ADR 879`. Sonuncusunda OCR ham
> okuması `26ADRB79` idi; rakam bölgesindeki `B` otomatik olarak `8`'e düzeltilip `26 ADR 879`
> üretildi. İki kamera da `1 fps`'te, 0 yeniden bağlanma ile stabil çalıştı.

---

## Hangi yol seçildi? (AlprNet mi, yedek OnnxRuntime planı mı?)

Görev, çekirdek için [AlprNet](https://github.com/Temppus/AlprNet)'i öneriyordu. AlprNet incelendi:
kullandığı **ONNX modelleri** (YOLOv9-t end2end dedektör + fast-plate-ocr CCT) bu projede **birebir**
kullanılıyor. Ancak AlprNet'in kendi `AlprNet.Lib` kodu baştan sona **OpenCvSharp `Mat`** tipine
bağlı. Geliştirme/çalışma makinesi **Apple Silicon (arm64 macOS)** ve OpenCvSharp'ın **arm64-macOS
için resmi native runtime'ı yok** (`OpenCvSharp4.runtime.win` sadece Windows). Bu nedenle AlprNet
bu makinede derlenip çalışamazdı.

Bu yüzden görevdeki **YEDEK PLAN** uygulandı: aynı ONNX modelleri doğrudan
**Microsoft.ML.OnnxRuntime (CPU)** ile yüklendi ve ön/son işleme (YOLOv9 letterbox + end2end çıktı
parse, CCT NHWC uint8 + greedy decode) **AlprNet'in kaynağına sadık kalınarak yeniden yazıldı**.
Görüntü işleme için OpenCvSharp yerine **SkiaSharp** kullanıldı.

**Neden SkiaSharp?** İlk tercih ImageSharp'tı; ancak ImageSharp **v4 derleme anında ticari lisans
anahtarı** dayatıyor (kamu/belediye kullanımı için ücretli) ve v3'te yüksek önemli CVE'ler var.
**SkiaSharp MIT lisanslıdır** (ticari/kamu kullanımı ücretsiz), **osx-arm64 ve win-x64** native
binary'lerini içerir; yani bu Mac'te de Windows hedefte de tek paketle çalışır. Hiçbir yerde
GPU/CUDA bağımlılığı yoktur.

**ONNX model giriş/çıkışları tahmin edilmedi**, modeller metadata + örnek çıkarımla doğrulandı:

| Model | Giriş | Çıkış |
|---|---|---|
| `yolo-v9-t-*-end2end` (dedektör) | `images` Float `[1,3,S,S]` NCHW, RGB, `/255`, letterbox(114) | `output0` Float `[N,7]` = `[img_id,x1,y1,x2,y2,class,score]` (NMS gömülü) |
| `cct_xs_v1_global` (OCR) | `input` **UInt8** `[N,64,128,3]` NHWC, RGB, **normalize yok** | `Identity:0` Float `[N,9,37]` — **zaten softmax olasılık** |

> Not: OCR çıktısı zaten softmax olasılık olduğundan tekrar softmax UYGULANMAZ (AlprNet burada
> tekrar softmax uygular; metin doğru çıkar ama güven skoru hatalı olur — bizde düzeltildi).

---

## Kullanılan modeller (CPU için en küçükler)

`models/` klasöründe (AlprNet deposundan alındı):

- **Dedektör (varsayılan):** `yolo-v9-t-384-license-plates-end2end.onnx`
  - Ampirik test: `256` gerçek (uzak/açılı) bir plakayı **kaçırdı**, `384`/`512` buldu. Bu yüzden
    varsayılan **384** (denge). Daha çok recall için `512`, en hızlı için `256` (config'ten değişir).
- **OCR (varsayılan):** `cct_xs_v2_global.onnx` (3.2 MB) + `cct_xs_v2_global_plate_config.yaml`
  - fast-plate-ocr **V2** modeli: 3× veriyle + boş-plaka negatifleriyle eğitilmiş, eğitim bölgelerinde
    **Türkiye dahil**. Çıkış: `plate` `[N,10,37]` + `region` `[N,66]` (ülke; region yok sayılır).
    Alfabe `0-9A-Z_`, 10 slot, 128×64 RGB uint8. `cct_s_v2` (daha doğru, biraz daha büyük) de mevcut.
  - Gerçek etiketli veride ölçüldü (aşağıya bkz): V1 → V2 plaka doğruluğu **%87.5 → %100**.

CPU yaklaşık süreleri (Apple M-serisi, 2 thread; zayıf bir ofis CPU'sunda birkaç kat daha yavaş olur):
tespit+OCR **256→~13ms, 384→~30ms, 512→~40ms / kare**. OCR yalnızca tespit olan karelerde çalışır.

---

## Ön gereksinimler

- **.NET 8 SDK** (proje `net8.0` hedefler). Bu makinede yalnızca **.NET 9** runtime kuruluydu;
  çalıştırılabilir projelere `<RollForward>LatestMajor</RollForward>` eklendiği için `net8.0`
  uygulaması .NET 9 runtime üzerinde de sorunsuz koşar. Hedef Windows'ta .NET 8 runtime varsa
  doğrudan koşar.
- **ffmpeg** (RTSP/HEVC çözümü için). `ffmpeg -version` ile doğrulayın. Kameralar **HEVC/H.265**
  yayınlıyor; ffmpeg bunu CPU'da çözer. (PATH'te değilse `Anpr:Ffmpeg:Path` ile tam yol verin.)
- **git** (modelleri içeren AlprNet deposunu klonlamak için — modeller zaten `models/`'a kopyalandı).

---

## Derleme ve çalıştırma

```bash
cd OtoparkAnpr

# 1) İlk kez: gerçek config'i şablondan oluştur ve kameralarını/şifreni doldur
cp Anpr.Service/appsettings.example.json Anpr.Service/appsettings.json
#   -> Anpr.Service/appsettings.json içindeki Cameras[].Username/Password/RtspUrl alanlarını düzelt
#   (appsettings.json .gitignore'dadır; ASLA commit edilmez)

# 2) Derle
dotnet build -c Release          # hatasız derlenmeli

# 3) Çekirdeği test et (örnek görsel + TR doğrulama birim testleri)
dotnet test  -c Release          # 28 test geçer

# 4) Servisi çalıştır
dotnet run -c Release --project Anpr.Service
#   veya yayınlanmış çıktıdan:  dotnet Anpr.Service/bin/Release/net8.0/Anpr.Service.dll
```

Çalışınca her kamera için bağlantı durumu loglanır ve her geçerli/yeni plaka şu biçimde düşer:

```
PLAKA 25 ADT 382 | Çıkış (cikis) | güven=81 % (det=81 % ocr=100 %) | ham='25ADT382' | 08:41:01
```

---

## Yapılandırma (`appsettings.json` → `Anpr` bölümü)

| Anahtar | Açıklama | Varsayılan |
|---|---|---|
| `Fps` | Aktif (araç varken) saniyede işlenen kare. Yüksek fps = araç başına çok örnek = daha iyi oylama | `4` |
| `IdleFps` | Boştayken (araç yokken) saniyede işlenen kare — CPU tasarrufu | `1` |
| `ActiveHoldSeconds` | Bir tespitten sonra bu kadar sn "aktif" hızda işlenir | `4` |
| `CharacterVoting` | Karakter-bazlı çok-kareli oylama (slot-bazlı uzlaşı). Doğruluğu artırır | `true` |
| `MinPlateWidthRatio` | Tetik bölgesi: OCR yalnızca plaka genişliği kare genişliğinin bu oranından büyükse. `0`=kapalı, `~0.03` uzakları eler | `0.0` |
| `DeskewPlates` | OCR öncesi plaka eğikliğini düzelt (deskew). Açılı veride ÖLÇÜLMELİ; varsayılan kapalı | `false` |
| `DedupWindowSeconds` | Aynı plaka aynı kamerada bu süre içinde tekrar görülürse tek olay | `8` |
| `MinConfidence` | OCR yalnızca bu dedektör skorunun üstündeki tespitlere uygulanır | `0.5` |
| `OnnxIntraOpThreads` | ONNX Runtime IntraOp thread (zayıf CPU için) | `2` |
| `DetectorModelPath` / `OcrModelPath` / `OcrConfigPath` | Model yolları (`models/` çıktıya kopyalanır) | 384 + cct_xs |
| `DetectorScoreThreshold` | Dedektör ham skor eşiği | `0.25` |
| `CropPaddingRatio` | Plaka kırpımında her kenara pay (OCR kenar kesmesini önler) | `0.06` |
| `EmitMinConfidence` | Olay yayınlamak için min birleşik güven (dedektör*OCR) | `0.0` |
| `EmitMinOcrConfidence` | Min OCR güveni. Metin doğruluğuyla ilintilidir; belirsiz/kötü okumayı eler. Katı için `0.85+`, her şeyi loglamak için `0` | `0.45` |
| `MinConfirmations` | Bir araç yayınlanmadan önce (il+harf kimliğiyle) en az kaç karede görülmeli. Tek seferlik yanlış okumaları eler. `1`=onay kapalı | `2` |
| `SettleWindowSeconds` | Onaya ulaşmayan araç, son görülmesinden bu kadar sn sonra yine de yayınlanır (tek karede geçen aracı kaçırmamak için) | `3` |
| `HttpPort` / `HttpBindAddress` | API portu / dinleme adresi (`0.0.0.0` = LAN'dan erişilebilir) | `8080` / `0.0.0.0` |
| `RecentEventsCapacity` | `/events/recent` için bellekteki olay sayısı | `200` |
| `WebhookUrl` | Doluysa her olay bu URL'e JSON POST edilir | `null` |
| `Ffmpeg.RtspTransport` | `tcp` (önerilir) / `udp` | `tcp` |
| `Ffmpeg.JpegQuality` | MJPEG kalitesi (2 en iyi .. 31 en kötü) | `4` |
| `Ffmpeg.ScaleWidth` | `0`=doğal; >0 ise kareyi bu genişliğe küçültür (CPU tasarrufu) | `0` |
| `Ffmpeg.NoFrameTimeoutSeconds` | Bu süre kare gelmezse yeniden bağlan | `12` |
| `Ffmpeg.ExtraInputArgs` | İleri CPU ayarı, ör. `"-discard nokey"` (yalnız anahtar kare çöz) | `null` |
| `Cameras[]` | `Id, Name, Ip, Username, Password, RtspUrl` | giris/cikis |

### Kimlik bilgisi notu
Kameralar **kullanıcı adı ≠ şifre** kullanıyor. Doğru bilgi **`admin` / `ertansa143623`** olarak
test edilip `appsettings.json`'a yazıldı (görevdeki "kullanıcı adı ile şifre aynı" varsayımı yanlıştı;
yalnızca config'teki `Username`/`Password`/`RtspUrl` düzeltildi, kod değişmedi).

### RTSP yolu notu
Kullanılan yol Hikvision **ana akış**: `/Streaming/Channels/101`. CPU'yu hafifletmek isterseniz **alt
akış** `/Streaming/Channels/102` deneyin (ama plaka için çözünürlük yetersiz kalabilir). Bağlantıyı
önce `ffprobe -rtsp_transport tcp "rtsp://admin:...@IP:554/Streaming/Channels/101"` ile doğrulayın.

---

## HTTP API

- `GET /health` — servis ve kamera durumu:
  ```json
  {
    "status": "ok",
    "cameras": [
      { "id":"giris","name":"Giriş","connected":true,"framesReceived":141,
        "restartCount":0,"eventsEmitted":4,"lastPlate":"25 ADT 382" }
    ]
  }
  ```
- `GET /events/recent?count=50` — son N plaka olayı:
  ```json
  {
    "total": 6, "returned": 5,
    "events": [
      { "timestamp":"...","cameraId":"giris","cameraName":"Giriş",
        "plate":"25 ADT 382","plateCompact":"25ADT382","rawText":"25ADT382",
        "confidence":0.62,"detectorConfidence":0.67,"ocrConfidence":0.91,
        "box":{"x":1987,"y":716,"width":174,"height":98} }
    ]
  }
  ```

---

## Mevcut sisteme entegrasyon

Üç yol var; ihtiyaca göre seçin:

1. **HTTP ile çekme (pull):** Web app `GET /events/recent`'i periyodik çağırır. En basit yol.
2. **Webhook (push):** `Anpr:WebhookUrl` ayarlayın; her olay anında o URL'e `PlateEvent` JSON'u
   olarak POST edilir. Web app bir endpoint açıp veritabanına yazar.
3. **`IEventSink` ile kod seviyesinde:** Yeni bir sink yazıp DI'a ekleyin. Örn. ileride bir
   **`DbSink`**:
   ```csharp
   public sealed class DbSink : IEventSink
   {
       public Task PublishAsync(PlateEvent e, CancellationToken ct) => /* DB'ye yaz */;
   }
   // Program.cs:  builder.Services.AddSingleton<IEventSink, DbSink>();
   ```
   Mevcut sink'ler: `ConsoleEventSink` (log), `InMemoryEventStore` (API için), `WebhookEventSink`.
   Tümü `IEventSink` uygular; worker olayı kayıtlı tüm sink'lere dağıtır.

`Anpr.Core` ayrıca bağımsız bir kütüphane olarak doğrudan kullanılabilir:
```csharp
using var engine = AnprEngine.Create(new AnprOptions { /* model yolları, threads ... */ });
IReadOnlyList<PlateReading> okumalar = engine.Recognize(jpegBytes);
// PlateReading: Text ("25 ABC 123"), RawText, Confidence, DetectorConfidence, OcrConfidence, Box, IsValidTr
```

---

## Türkiye plaka doğrulama / normalizasyon

Format: **il (01–81) + 1–3 harf + 2–5 rakam**, geçerli kombinasyonlar:
`1 harf→4-5 rakam`, `2 harf→3-4 rakam`, `3 harf→2-3 rakam`. **Q, W, X harfleri TR'de yoktur.**

OCR hataları **yalnızca formatın gerektirdiği pozisyonda** düzeltilir:
- rakam beklenen yerde harf→rakam: `O→0, I→1, B→8, S→5, G→6, Z→2, T→7, D→0, L→1, A→4, Q→0`
- harf beklenen yerde rakam→harf: `0→O, 1→I, 2→Z, 4→A, 5→S, 6→G, 7→T, 8→B`

En az düzeltme gerektiren geçerli kombinasyon seçilir; çok fazla düzeltme gerekiyorsa (çöp okuma)
**reddedilir** ve bariyere gönderilmez. Harf bloğunun tamamı rakamdan uydurulmuşsa (ör. tamamen
sayısal `3412345`) da reddedilir. (`Anpr.Core/TurkishPlates/TurkishPlateNormalizer.cs`)

---

## Doğruluk ve yanlış okumalar

Canlı kameralardan görsel doğrulama yapıldı (plaka kırpıntısı + OCR metni yan yana). Sonuç:

- **İyi karelerde okumalar %100 doğru** (ör. `25 AFH 149`, `25 GC 147`, `25 AEA 791`, `25 AG 368` —
  hepsi ocr %100).
- Yanlış/eksik okumalar yalnızca **kötü karelerde** (araç dönük/uzak, düşük dedektör güveni) ve
  **düşük güvenle** oluşur. İki savunma katmanı var:
  1. **TR doğrulama**: bozuk/eksik okumayı (ör. eksik `25GC14`) geçersiz sayıp bariyere göndermez.
  2. **Çok kareli onay (PlateConfirmer)**: bir araç onlarca karede görülür; aynı il+harf kimliğinin
     okumaları birleştirilir. `CharacterVoting=true` ile her **slot pozisyonu** için olasılıklar
     toplanıp pozisyon-başına en olası karakter seçilir (uzlaşı) — tek karede hiçbiri tam doğru olmasa
     bile doğru plaka çıkar. Tek karelik yanlış (ör. `25 AFH 11`, aslı `25 AFH 149`) elenir.
     `MinConfirmations`/`SettleWindowSeconds`/`CharacterVoting` ile ayarlanır.
  3. **OCR güven tabanı + tetik bölgesi**: `EmitMinOcrConfidence` belirsiz okumayı eler;
     `MinPlateWidthRatio` uzak/küçük plakalara OCR yapmaz.

Not: OCR ham çıktısı `8`/`B`, `0`/`O` gibi görsel olarak çok benzeyen karakterlerde tek kareden
kesin ayrım yapamaz; bu yüzden her olayda `ocrConfidence` ve `rawText` de döndürülür. En güvenilir
sonuç için aracın bariyere yakın olduğu (plakanın büyük göründüğü) kareler kullanılır.

### Ölçüm düzeneği (`Anpr.Bench`) — sayısal doğrulama

Doğruluk artık göz kararı değil, **etiketli gerçek veriyle ölçülüyor**:

```bash
# 1) Gerçek trafik yakala (ffmpeg) -> dataset/s1/{giris,cikis}/*.jpg
# 2) Araç görünümlerini çıkar + kırpıntı üret, sonra labels.json'a gerçek plakaları yaz:
dotnet run -c Release --project Anpr.Bench -- label dataset/s1 giris --ocr cct_xs_v2_global.onnx
# 3) Bir konfigi etiketli sete karşı ölç (plaka%/karakter%/yanlış-pozitif/kaçan/gecikme):
dotnet run -c Release --project Anpr.Bench -- evaluate dataset/s1 dataset/s1/labels.json \
    --ocr cct_xs_v2_global.onnx --charvote true --minocr 0.45
```

**Ölçülen A/B (etiketli 8 araç, gerçek Erzurum trafiği):**

| Konfig | Plaka doğruluğu | Kaçan | Karakter benzerliği | Gecikme |
|---|---|---|---|---|
| V1, oylama yok (eski) | %87.5 | 1 | %89.8 | 30 ms |
| **V2-xs + karakter oylama** (varsayılan) | **%100** | 0 | %93.4 | 30 ms |
| V2-s + karakter oylama | %100 | 0 | %97.1 | 31 ms |

Yani **V2 modeli + karakter-bazlı oylama** plaka doğruluğunu **%87.5 → %100** çıkardı, kaçan aracı
sıfırladı; gecikme aynı kaldı. (Zayıf hedef PC'de gecikme birkaç kat artar — `evaluate` o makinede
çalıştırılıp `Fps`/model/`MinPlateWidthRatio` ona göre ayarlanmalı.)

**Deskew ölçümü:** `DeskewPlates` açık/kapalı A/B'de, temiz (cepheden) plakalarda deskew **zarar
verdi** (%100 → %87.5 — düz plakayı gereksiz döndürüp bozuyor). Bu yüzden **varsayılan KAPALI**;
yalnızca gerçekten açılı plaka sorunu olan bir veri setinde açıp tekrar ölçmek gerekir. (Ölçüm
düzeneği bu regresyonu yakaladığı için değerli.)

---

## CPU optimizasyonu (uygulananlar)

- ONNX Runtime **CPU EP**, `IntraOpNumThreads=2`, `InterOp=1`, sequential, `ORT_ENABLE_ALL`.
- Tespitten önce kare modelin giriş boyutuna **letterbox** ile küçültülür (gereksiz büyük işleme yok).
- OCR yalnızca `MinConfidence` üstündeki ve `MinPlateWidthRatio` boyutundaki tespitlere uygulanır.
- **Uyarlanabilir fps**: araç varken `Fps`, boştayken `IdleFps` — boşta CPU yanmaz, araç varken çok kare.
- Her kamera ayrı Task + ayrı `AnprEngine` (ONNX session thread-safe değildir); ffmpeg kare çözümünü
  ayrı süreçte yapar. Daha fazla tasarruf için `Ffmpeg.ScaleWidth` veya `ExtraInputArgs="-discard nokey"`.
- **INT8 kuantizasyon (opsiyonel, hedef PC'de ölç):** modern x86'da (VNNI) 2-4× hız. ONNX Runtime
  kuantizasyon aracıyla offline üretilip `models/`'a konur; `evaluate` ile hız+doğruluk A/B yapılır.

---

## Proje yapısı

```
OtoparkAnpr/
├─ Anpr.Core/         # ONNX detect+OCR + TR doğrulama (SkiaSharp + OnnxRuntime, OpenCvSharp YOK)
│  ├─ Detection/      #   LicensePlateDetector (YOLOv9 end2end)
│  ├─ Recognition/    #   LicensePlateRecognizer (CCT) + PlateOcrConfig (yaml)
│  ├─ TurkishPlates/  #   TurkishPlateNormalizer
│  ├─ Confirmation/   #   PlateConfirmer (çok kareli onay + karakter oylama)
│  ├─ Imaging/        #   SkiaImaging (decode/resize/crop)
│  └─ AnprEngine.cs   #   uçtan uca orkestrasyon (+ tetik bölgesi)
├─ Anpr.Service/      # ASP.NET Core minimal API + kamera worker'ları
│  ├─ Frames/         #   FfmpegFrameSource (RTSP→MJPEG, en güncel kare, yeniden bağlanma)
│  ├─ Workers/        #   CameraWorker (uyarlanabilir fps, onay) + AnprCameraSupervisor
│  ├─ Events/         #   IEventSink, Console/InMemory/Webhook sink'ler, PlateEvent
│  ├─ appsettings.json         (.gitignore'da — GERÇEK şifreler)
│  └─ appsettings.example.json (şablon — commit edilir)
├─ Anpr.Bench/        # Ölçüm/doğrulama: label (etiketleme) + evaluate (A/B metrik)
├─ Anpr.Tests/        # xUnit: TR doğrulama + PlateConfirmer + örnek görselle tanıma
├─ dataset/           # (.gitignore'da) benchmark kareleri + labels.json
└─ models/            # ONNX modelleri (yolo-v9-t-*, cct_xs/s v1+v2) + yaml + örnek görsel
```

---

## Windows hedefe dağıtım

Kod tamamen cross-platform (SkiaSharp/OnnxRuntime/ffmpeg her iki platformda da var). Self-contained
yayın:
```bash
dotnet publish Anpr.Service -c Release -r win-x64 --self-contained true -o publish-win
```
Hedef Windows makinede **ffmpeg.exe** kurulu olmalı (PATH'te ya da `Anpr:Ffmpeg:Path` ile). IIS
yerine bir **Windows Service** olarak çalıştırmak önerilir (`sc create` / NSSM). `appsettings.json`'u
hedefte oluşturup kamera bilgilerini girin.

---

## Sorun giderme

- **401 Unauthorized:** kullanıcı adı/şifre yanlış. Doğrusu `admin` / `ertansa143623`.
- **Kare gelmiyor / sürekli yeniden bağlanıyor:** RTSP yolu yanlış olabilir; `102` alt akışı deneyin,
  `ffprobe` ile URL'i doğrulayın, `RtspTransport`'u `udp` deneyin.
- **`ffmpeg bulunamadı`:** `Anpr:Ffmpeg:Path`'e tam yol verin.
- **Plaka okunmuyor:** araç bariyere yakın değilken plaka birkaç piksel olur (tespit edilemez) — bu
  normaldir; araç yaklaşınca okunur. Uzak/zor sahnelerde `DetectorModelPath`'i `512`'ye alın.
```
