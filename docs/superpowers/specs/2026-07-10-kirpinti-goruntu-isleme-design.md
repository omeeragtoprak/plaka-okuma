# Tasarım: Kırpıntı-Seviyesi Uzman Görüntü İşleme ("İkinci-Şans Şeridi") — ✅ ONAYLANDI

Tarih: 2026-07-10 · Onay: kullanıcı ("onay") · Kapsam: kademeli 4 aşama, iz-içi geri besleme
KATI KURALLAR: yalnız CPU (zayıf otopark PC'si) · Türkiye plakaları · kameraya salt-okuma ·
her aşama ölçüm kapılı (kazanmayan elenir ve elenenler listesine yazılır).

## Amaç ve bağlam

Olayların %27'si (894/3.329) uzlaşı güveni <0.85 ile çıkıyor — açılı/karanlık/küçük/bulanık
kırpıntılar. Tespit edilen PLAKA GÖRÜNTÜLERİ üzerinde uzman görüntü işleme teknikleriyle bu dilimi
kurtarmak. Elenen teknikler TEKRAR denenmez: naif deskew, piksel-uzayı çok-kare füzyonu, INT8
dinamik kuant, augmentation-ile-açı (fine-tune v3). Test malzemesi hazır: 3.078 gerçek kanıt
kırpıntısı + events.jsonl güven/koşul verisi.

## Kullanıcı kararları (netleştirme sorularından)

1. **Hedef:** kademeli paket — 4 aşama, ölçüm sırasıyla; her aşama kendi A/B'sinden geçmeden
   sonrakine geçilmez.
2. **Geri besleme yeri:** İZ-İÇİ, EMIT ÖNCESİ — zenginleştirilmiş kırpıntının OCR'ı oylamaya EK
   GÖZLEM olarak katılır. EarlyEmit=0.85'in zaten beklettiği pencere kullanılır → emit gecikmesi
   artmaz, olay sözleşmesi değişmez, kötü zenginleştirme oylamada doğal kaybeder.
3. **Kabul ölçütü (yerleşik disiplin):** hedef diliminde ölçülü kazanç + genel setlerde
   (diag4/live1h/val/gece) sıfır regresyon → varsayılan AÇIK; aksi halde elenir.

## Mimari

Seçilen yaklaşım: **"İkinci-Şans Şeridi"** — sıcak yolun (kare başı tespit+OCR) tamamen dışında,
tek entegrasyon noktalı, bütçeli bir zenginleştirme bileşeni. (Elenen alternatifler: inline
zenginleştirme — kırpıntı başına 10-80 ms zayıf PC bütçesini yer, deskew dersini ihlal eder;
emit-sonrası düzeltme olayı — PMS sözleşmesini karmaşıklaştırır.)

```
Kare → Tespit → [1. KALİTE KAPISI: bulanıklık+pozlama skoru] ──çöp──→ atılır (sayaç+log)
                        │ geçer
                        ▼
                 OCR → oylama → iz (mevcut hat, DEĞİŞMEZ)
                        │
        iz onaylı AMA uzlaşı <0.85 (EarlyEmit bekletiyor) → CropEnhancer (iz başına 1 KEZ)
                        ▼
              2. WPOD rektifikasyon (10-80 ms) → 3. gece normalizasyonu (1-3 ms, karanlıksa)
              → 4. ESPCN-tiny ×2 SR (15-40 ms, genişlik <100 px ise)   [bütçe: EnhanceBudgetMs=150]
                        ▼
              zenginleştirilmiş kırpıntı(lar) → OCR → PlateConfirmer.InjectObservation(...)
                        ▼
              uzlaşı yeniden çözülür → ≥0.85 → hemen emit; değilse settle'da emit (LowConfidence)
```

## Bileşenler

| Bileşen | Yer | Sorumluluk | Bağımlılık |
|---|---|---|---|
| `CropQualityScorer` | `Anpr.Core/Imaging/` | Laplacian varyans (bulanıklık) + histogram uç yüzdeleri (aşırı karanlık/patlamış) → `(blur, exposure)` skoru. Saf, ~0.1 ms, birim testli | SkiaSharp |
| `PlateRectifier2` | `Anpr.Core/Imaging/` | WPOD ONNX çıkarımı (`models/wpod_lpd.onnx`, IR7) → quad → DLT homografi (~40 satır Gauss eliminasyonu) → SkiaSharp perspektif warp, %10 kenar marjı, en-boy korunur. Quad'dan eğiklik metriği de döner | ONNX Runtime, SkiaSharp (OpenCV YOK) |
| `NightNormalizer` | `Anpr.Core/Imaging/` | Gamma düzeltme + %2-%98 histogram germe, gri kanal | SkiaSharp |
| `PlateSuperResolver` | `Anpr.Core/Imaging/` | ESPCN-tiny ×2 ONNX (~30 KB; opset ≤17, ConvInteger'sız doğrulanacak) | ONNX Runtime |
| `CropEnhancer` | `Anpr.Core/Imaging/` | Aşama orkestrasyonu + bütçe sayacı + aşama bayrakları; girdi: izin en iyi kırpıntıları (mevcut `CropBucket`), çıktı: zenginleştirilmiş `SKBitmap` listesi + tanı meta | yukarıdakiler |
| `PlateConfirmer.InjectObservation` | `Anpr.Core/Confirmation/` | Zenginleştirilmiş okumayı var olan ize ek gözlem olarak işler (SlotSum'a katar, uzlaşı tazelenir). Ekleme İKİNCİ bir olay üretmez; emit normal yoldan (onay/settle) tek kez çıkar | mevcut |
| CameraWorker kancası | `Anpr.Service/Workers/` | DrainReady öncesi: onaylı-ama-<0.85 izler için `CropEnhancer` çağrısı (iz başına 1 kez, `Enhanced` bayrağı) | mevcut CropBucket |

Yapılandırma (`AnprServiceOptions` → `AnprOptions`): `CropQualityGate` (bool, vars. önce KAPALI —
ölçüm sonrası açılır), `QualityBlurMin` / `QualityExposureBand`, `EnhanceRectify` / `EnhanceNight` /
`EnhanceSr` (bool bayraklar, vars. KAPALI — kapı geçince açılır), `EnhanceBudgetMs=150`,
`EnhanceTriggerOcr=0.85`, `WpodModelPath`.

## Aşamalar ve kabul kapıları

| # | Aşama | Çalışma noktası | CPU | Kabul kapısı |
|---|---|---|---|---|
| 1 | Kalite kapısı | INLINE her kırpıntı | ~0.1 ms | düşük-güvenli dilimde yanlış-emit ↓, recall sabit (diag4+live1h) |
| 2 | WPOD rektifikasyon | iz-içi, 1 kez | 10-80 ms | açılı dilimde exact ↑, temiz dilimde regresyon 0 |
| 3 | Gece normalizasyonu | iz-içi, karanlık kırpıntıda | 1-3 ms | gece setinde (%84 taban) exact ↑ |
| 4 | ESPCN ×2 SR | iz-içi, genişlik <100 px | 15-40 ms | küçük-plaka diliminde exact ↑ |

Bütçe sözleşmesi: iz başına zenginleştirme EN FAZLA 1 tur; toplam ek süre `EnhanceBudgetMs` ile
sınırlı, aşımda kalan aşamalar atlanır. Sıcak yol değişmez.

## Hata yönetimi

- WPOD/ESPCN modeli yok/yüklenemedi → ilgili aşama kendini kapatır, uyarı loglar, servis düşmez.
- Zenginleştirme çıktısı HER ZAMAN ek gözlemdir; ham gözlemler korunur → kötü çıktı oylamada
  azınlıkta kalır (yapısal deskew-faciası koruması).
- Her zenginleştirme `enhance` etiketiyle loglanır: iz kimliği, aşamalar, süre, öncesi/sonrası
  uzlaşı metni+güveni → sahada denetlenebilir; "kurtarılan iz" sayacı health'e eklenir.
- Kalite kapısı çöpe attıklarını sayaçlar (sessiz kayıp yok).

## Test ve doğrulama

1. **Geçmiş-zamanlı dilimli setler (ilk kanıt):** kanıt arşivi + events.jsonl'dan otomatik
   dilimleme — açılı (WPOD eğiklik metriği), karanlık (pozlama skoru), küçük (<100 px genişlik).
   Her aşama önce kendi diliminde offline A/B (python/C# bench).
2. **Batarya:** diag4 + live1h + val + gece, `--enhance` bench bayrağıyla; sıfır regresyon şartı.
3. **Birim testler:** kalite skoru (bilinen keskin/bulanık örnekler), homografi koordinat
   doğruluğu (sentetik quad → beklenen warp), bütçe aşımı (aşama atlama), model-yokken zarif düşüş,
   InjectObservation'ın emit-tekilliğini bozmadığı.
4. **Canlı:** panel gölge penceresi — "kurtarılan iz" (düşük-güvenli başlayıp ≥0.85 emit olan)
   sayısı önce/sonra; `enhance` logları örneklemle gözle doğrulanır (kanıt JPEG'leriyle).

## Kapsam dışı

Inline (her kare) zenginleştirme · emit-sonrası düzeltme olayları · GPU/OpenVINO'ya özgü yollar
(hedef-PC pilotunun ayrı işi) · çift satır plaka desteği · IWPOD fine-tune (ancak WPOD kapıyı
geçerse ve açılı gerçek veri birikirse gündeme gelir) · piksel-uzayı çok-kare füzyonu (elendi).

## Uygulama sırası

Aşama 1 (kalite kapısı) → ölç → Aşama 2 (WPOD) → ölç → Aşama 3 (gece) → ölç → Aşama 4 (SR, ESPCN
modeli bulunup C# ORT'ta doğrulanırsa) → ölç. Her aşama ayrı commit + gap-analizi güncellemesi.
