# OtoparkAnpr — Yerel CPU Plaka Tanıma (ANPR) Servisi

Erzurum Belediyesi otopark kapıları için, **Hikvision kamera-içi ANPR bağımlılığını kaldırmayı**
hedefleyen, tamamen yerel çalışan C#/.NET 8 plaka tanıma servisi. İki RTSP kameradan kare alır,
Türkiye plakalarını tespit edip okur, çok-kareli uzlaşıyla tek ve güvenilir olay üretir; canlı
karşılaştırma paneli ile kameranın kendi ANPR'ına karşı sürekli ölçülür.

**Katı kurallar:** yalnız Türkiye plakaları · yalnız CPU (GPU yok; zayıf ofis PC hedefi) ·
kameraya salt-okuma erişim (hiçbir kamera ayarı değiştirilmez) · internet bağımsız.

## Mimari

```
RTSP (ffmpeg) → en güncel kare → YOLOv9-t-384 tespit (ROI-kırpımlı giriş)
   → ROI içi: tümü OCR (cct_xs_v2_erz — sahaya fine-tune edilmiş CCT)
   → ROI dışı: kare başına en büyük 2'si OCR = "gözlem" katmanı (bariyer kanalına girmez)
   → TR format doğrulama → çok-kareli iz (uzamsal IoU + metin-fuzzy birleştirme)
   → slot×vocab TAM olasılık birikimi → TR-gramer KISITLI çözümleme (+SKIP_CHAR, top-K aday)
   → güven %85'e dek takip (EarlyEmit) → varyant bastırma → PlateEvent
       Scope=roi|gozlem · EventId · LowConfidence · Candidates · kanıt JPEG
   → sink'ler: konsol / bellek+events.jsonl / webhook (yalnız roi) / panel (SSE)
```

- **Anpr.Core** — motor: tespit, OCR, TR plaka dilbilgisi, iz/uzlaşı (`PlateConfirmer`),
  ROI çokgeni, kırpıntı araçları. Servis bağımsız, birim testli.
- **Anpr.Service** — barındırıcı: kamera işçileri, HTTP API (`/health`, `/events/recent`),
  canlı karşılaştırma paneli (`/panel`), kanıt görüntüleri (`/evidence/...`), Hikvision kayıt
  yoklayıcı (salt-okuma ISAPI).
- **Anpr.Bench** — ölçüm: etiketli set değerlendirme, kamera-referanslı karşılaştırma, hasat,
  füzyon/degrade deneyleri.

Teknoloji: Microsoft.ML.OnnxRuntime (CPU) + SkiaSharp (MIT; OpenCV bağımlılığı YOK — arm64 macOS
dahil her yerde derlenir) + ffmpeg. Modeller
[ankandrew/fast-plate-ocr](https://github.com/ankandrew/fast-plate-ocr) ekosisteminden; OCR modeli
kamera etiketli saha verisiyle fine-tune edildi (`models/cct_xs_v2_erz.onnx`).

## Ölçülmüş durum (kamera = %100 referans)

| Koşul | Sonuç |
|---|---|
| Gündüz, kapı-kapsamlı (ROI) | yakalama 22/22; 30 dk pencerede 13/13 birebir |
| diag4 bataryası (15 dk, 26 araç) | birebir 22/26 (%85), gürültü 11 |
| live1h (101 etiketli araç) | recall 87/101 |
| Gece (IR, kırpıntı-exact) | %84 (kamera da gece hata yapıyor — UNKNOWN'lar, kendi çelişkileri) |
| Stabilite | 24+ saat 0 restart; RSS 150-300 MB sızıntısız; ~30 ms/kare (M-serisi Mac) |
| Varyant-zincirli geçiş | %24.1 → %16.3 (uzamsal iz + bastırma sonrası) |

Ölçümle **elenenler** (tekrar denemeyin): naif deskew, piksel-uzayı çok-kare füzyonu, INT8 dinamik
kuant (ORT 1.23'te de ConvInteger yok), yolo-256/416/512 ana dedektör, cct_s_v2, augmentation ile
açı dayanıklılığı (fine-tune v3). Ayrıntı: `docs/2026-07-06-proje-gap-analizi-ve-yol-haritasi.md`.

## Hızlı başlangıç

```bash
# 1) Yapılandırma: şablonu kopyala, kamera IP/kimlik bilgilerini gir (gerçek dosya gitignore'da)
cp Anpr.Service/appsettings.example.json Anpr.Service/appsettings.json
#    -> Cameras[].Ip/Username/Password ve RtspUrl alanlarını doldur

# 2) Derle + test
dotnet build -c Release && dotnet test

# 3) Çalıştır
cd Anpr.Service/bin/Release/net8.0 && ./Anpr.Service
# Panel:  http://localhost:8090/panel
# Sağlık: http://localhost:8090/health
# Olaylar: http://localhost:8090/events/recent?count=50   (scope=roi filtresi mevcut)
```

Gereksinimler: .NET 8 SDK (arm64 macOS'ta .NET 9 + RollForward ile de çalışır), `ffmpeg` PATH'te.

### Windows hedefe dağıtım

```bash
dotnet publish Anpr.Service -c Release -r win-x64 --self-contained true -o publish-win
```
Hedefte `ffmpeg.exe` PATH'te (veya `Anpr:Ffmpeg:Path`); Windows Servisi olarak çalıştırma ve
kurtarma ayarları için `docs/deployment-checklist.md`.

## Ölçüm bataryası

```bash
CAM_PW='<kamera-şifresi>' bash scripts/regression.sh          # tam batarya (birim + diag4 + live1h + kırpıntı)
python3 scripts/camera_compare.py --pw "$CAM_PW" ...          # kamera-referanslı karşılaştırma
```

Kurallar: her değişiklik önce/sonra A/B ile ölçülür; hedef dilimde kazanç + genel setlerde sıfır
regresyon olmadan hiçbir özellik varsayılan açılmaz. Fine-tune akışı: `docs/finetune-runbook.md`
(eğitim ortamı `.venv-train/`, augmentation `dataset/finetune/aug_v3.yaml`).

## Dokümantasyon

- `docs/2026-07-07-gereksinim-muhendisligi-otopark-anpr.md` — gereksinim seti × ölçülmüş durum,
  koşul matrisi, kabul metodolojisi (NASPLE uyarlaması), Faz 0-5 yol haritası
- `docs/2026-07-06-proje-gap-analizi-ve-yol-haritasi.md` — karar dokümanı + günlük güncellemeler (5a-5f)
- `docs/2026-07-08-pts-raporu-karsilastirmali-analiz.md` — PTS raporu analizi + 5 açık kaynak
  projenin kod incelemesi + öncelikli backlog
- `docs/deployment-checklist.md` · `docs/finetune-runbook.md` · `docs/research/` (ham bulgular,
  Hikvision ISAPI kılavuzları)

## Güvenlik / KVKK notları

- Gerçek `appsettings.json` (kamera kimlik bilgileri) **gitignore'dadır**; repoda yalnız şablon var.
  Ölçüm betikleri şifreyi `CAM_PW` ortam değişkeninden alır.
- Plaka görüntüleri ve olay kayıtları (`dataset/`, `bin/`, kanıt JPEG'leri) **repoya girmez** —
  plaka verisi KVKK kapsamında kişisel veridir (Kurul kararları 2023/924, 2021/1110).
- Kanıt kırpıntıları sahada `EvidenceRetentionDays` (varsayılan 30 gün) ile otomatik silinir;
  loglarda plaka maskeleme (`MaskPlatesInLogs`) mevcuttur; model bütünlüğü SHA-256 ile doğrulanabilir.
