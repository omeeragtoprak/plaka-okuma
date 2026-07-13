# Otopark Yazılımı (PMS) Entegrasyon Kılavuzu

Tarih: 2026-07-13 · Muhatap: otopark yazılımı geliştiricisi / entegratör.
Mimari karar: OtoparkAnpr, PMS'e **kod modülü (DLL) olarak GÖMÜLMEZ**; aynı PC'de **ayrı bir
Windows servisi** olarak çalışır ve HTTP ile konuşur (çökme yalıtımı, CPU kontrolü, bağımsız
güncelleme). PMS tarafında gereken tek geliştirme: aşağıdaki POST'ları alan bir endpoint.

## Topoloji (otopark başına)

```
Otopark PC'si
┌──────────────────────────────────────────────────────────┐
│ OtoparkAnpr (Windows servisi, self-contained exe)        │
│   RTSP ◄── giriş kamerası + çıkış kamerası (salt-okuma)  │
│   POST olay + heartbeat ───────► PMS (WebhookUrl)        │
│   ◄─── POST /api/correction (operatör düzeltmesi)        │
│   ◄─── GET /health · /events · /evidence/{dosya}         │
└──────────────────────────────────────────────────────────┘
```

- Kameralara YALNIZ salt-okuma RTSP erişimi; kamera ayarına dokunulmaz.
- PMS aynı PC'deyse `WebhookUrl` localhost olur; merkezi sunucudaysa ağ üzerinden POST edilir
  (kesintide store-and-forward devreye girer, bkz. Teslim Garantisi).

## 1) PMS'in alacağı POST'lar (tek URL — `WebhookUrl`)

### 1a. Plaka olayı (alan adları PascalCase)

```json
{
  "EventId": "3f2b7c9e-...",              // Guid — idempotency anahtarı
  "Timestamp": "2026-07-13T09:14:05.12+03:00",
  "CameraId": "giris",                     // "giris" | "cikis"
  "CameraName": "Giriş",
  "Plate": "25 ABC 123",                   // normalize, boşluklu
  "PlateCompact": "25ABC123",              // eşleştirme/sorgu için
  "RawText": "25ABC123",                   // OCR ham çıktısı
  "Confidence": 0.97,                      // uzlaşı güveni (0-1)
  "DetectorConfidence": 0.91,
  "OcrConfidence": 0.99,
  "Box": { "X": 1693, "Y": 690, "Width": 120, "Height": 72 },
  "Scope": "roi",                          // webhook'a YALNIZ "roi" gelir
  "EvidencePath": "evidence/xxx.jpg",      // kanıt: GET http://<anpr>:8090/evidence/... 
  "LowConfidence": false,                  // true → operatör onayına düşür
  "Candidates": [                           // top-5 aday; whitelist toleransı için
    { "Plate": "25ABC123", "Confidence": 0.97 }
  ]
}
```

PMS iş kuralları:
- **İdempotency**: `EventId` daha önce işlendiyse yoksay (retry teslimi mükerrer POST üretebilir).
- **LowConfidence=true** → bariyer otomatiği YOK; operatör ekranına kanıt görseliyle düşür
  (`/evidence/{EvidencePath}`), onay/düzeltme iste.
- **Whitelist/abone eşleşmesi**: önce `PlateCompact` birebir; yoksa `Candidates` içinde ara
  (1-karakter OCR belirsizliğini kapatır).
- Yanıt: 2xx = teslim alındı. 2xx dışı her şey bizde "teslim edilemedi" sayılır ve yeniden denenir.

### 1b. Heartbeat (30 sn'de bir; alan adları camelCase, `type` ile ayırt edilir)

```json
{
  "type": "heartbeat",
  "timestamp": "2026-07-13T09:14:30+03:00",
  "cameras": [
    { "id": "giris", "connected": true, "framesProcessed": 5585,
      "eventsEmitted": 35, "qualityDropped": 2, "restartCount": 0 }
  ]
}
```

Ayrım kuralı: gövdede `"type":"heartbeat"` varsa heartbeat, yoksa plaka olayıdır.
PMS, heartbeat **2-3 periyot üst üste kesilirse** "ANPR servisi düştü" alarmı üretmelidir.

## 2) Teslim garantisi (PMS tarafında kayıp-olay derdi yok)

Webhook POST'u başarısız olursa olay ANPR tarafında diske kuyruklanır
(`webhook-queue.jsonl`, üst sınır 10.000) ve 2 dakikada bir SIRAYLA yeniden teslim edilir.
Yani PMS/ağ saatlerce kapalı kalsa bile olaylar kaybolmaz, sıra bozulmaz — gecikmeli gelir.
⚠ Bu kanal KAYIT kanalıdır: **bariyer AÇ kararı gecikmeli teslime bağlanmamalıdır**
(aşağıda "Bariyer kararı" bölümü).

## 3) PMS'ten ANPR'a çağrılar

| Uç | Amaç |
|---|---|
| `GET /health` | durum + kamera sayaçları + p50/p95 ms (izleme; token gerekmez) |
| `GET /evidence/{dosya}` | olayın kanıt JPEG'i (operatör ekranı) |
| `GET /events?n=100` | son olaylar (arıza sonrası mutabakat) |
| `POST /api/correction` `{ "eventId": "...", "correctedPlate": "25 ABC 124" }` | operatör düzeltmesi → denetim izi + eğitim verisi (model zamanla sahaya uyum sağlar) |

`PanelToken` yapılandırıldıysa `/health` dışındaki uçlara `Authorization: Bearer <token>` gerekir.

## 4) Bariyer kararı — aşamalı devir

- **Faz 1 (gölge, ŞİMDİ yapılabilir)**: Hikvision bariyeri açmaya devam eder; PMS bizim
  webhook'u yalnız kayıt/rapor için tüketir. Risk sıfır; gölge-raporu pariteyi ölçer.
- **Faz 2 (pilot kapı)**: kapı trafiğinde 7 gün üst üste ≥%98 eşleşme + hedef-PC ölçümü +
  siyah-plaka kapanışı sonrası TEK kapıda bariyer kararı ANPR olayına bağlanır; Hikvision
  yedekte kalır (geri dönüş: PMS'te tek anahtar).
- Bariyer kararı için PMS *canlı* olayı kullanmalı (ör. Timestamp ≤ 10 sn); kuyruktan gelen
  gecikmiş olay bariyer AÇTIRMAMALIDIR (`Timestamp` kontrolü PMS tarafında).

## 5) Kurulum (otopark PC'si — pilot kitiyle)

1. `pilot-kit/OtoparkAnpr-pilot-win64.zip` aç → `PILOT-TALIMAT.md` izle.
2. `appsettings.json`: kamera RTSP bilgileri (CAM_PW ortam değişkeni), `WebhookUrl` (PMS
   endpoint'i), `HeartbeatSeconds: 30`, gerekiyorsa `PanelToken`.
3. Windows servisi olarak kur (`sc create` komutu talimatta; otomatik başlama + çökmede
   yeniden başlatma).
4. Duman testi: `GET /health` → iki kamera `connected:true`; test aracı geçir → PMS'e olay
   düştüğünü doğrula.

## 6) Alternatif: Hikvision uyumluluk modu (PMS'e dokunulamıyorsa)

PMS kapalı/üçüncü-parti ise ve yalnız Hikvision'ın gönderdiği yüzeyi tüketebiliyorsa, ANPR
servisi o yüzeyi taklit edebilir. Bunun için önce **E1 keşfi** gerekir: mevcut kamera→PMS
trafiğinin 48 saat izlenmesi (salt-okuma) ile formatın (ISAPI alarm push / HTTP listening /
başka) belirlenmesi. Keşif yapılmadan bu mod GELİŞTİRİLMEZ — skor kartı işi #2.

## Bilinen pürüzler / karar bekleyenler

- Olay JSON'u PascalCase, heartbeat camelCase — PMS geliştirmesi başlamadan tek casing'e
  standardize edilebilir (şu an dış tüketici olmadığı için kırılım maliyeti sıfır; PMS işi
  başlarken kararlaştırılacak, sonra SÖZLEŞME DONDURULUR).
- Bariyer-kararı kanalı (retry'siz, canlılık-garantili ayrı uç) Faz 2 öncesi eklenecek.
- TLS: PC-içi (localhost) entegrasyonda gereksiz; ağ üzerinden merkezi PMS'e gidilecekse
  Faz 2 öncesi zorunlu.
