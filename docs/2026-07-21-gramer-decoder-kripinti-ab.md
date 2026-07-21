# Faz A gramer-decoder + SkipCharLevel — kırpıntı-seviye A/B (2026-07-21)

`Anpr.Bench cropab` komutu eklendi: aynı OCR slot-dağılımından **ham argmax** ile **TrConstrainedDecoder**
çıktısını gerçek-değere karşı karşılaştırır ve üretim emit-floor'unu (`--minocr`) taklit eder. Amaç:
varsayılan AÇIK olan Faz A (ConstrainedDecoding) tekniğinin daha önce hiç yapılmamış kırpıntı-seviye
A/B doğrulaması. Set: `dataset/finetune/val.csv` (500 gerçek plaka + 32 negatif), `night_all.csv` (125).
Model: üretim `cct_xs_v2_erz.onnx`.

## Bulgu 1 — Gramer-decoder (skipchar=0) GÜVENLİ ve net-pozitif (emit floor ile)

| Set | floor | argmax | gramer | net | negatif halüsinasyon (floor ile geçitlenen) |
|-----|-------|--------|--------|-----|------|
| val | 0.00 | 78.9% | 73.1% | **−31** | 32/32 (0 geçitlenen) |
| val | 0.45 | 78.9% | 79.1% | **+1** | 32/32 (**32 geçitlenen**) |
| gece | 0.45 | 84.0% | 84.0% | 0 | 0/0 |

- **floor'suz** gramer decoder 32 negatif (boş-plaka) görüntünün HEPSİNDE geçerli-görünen plaka
  *halüsine eder* (argmax doğru şekilde boş verir) → net −31.
- **Üretim floor'u 0.45** ile: decoder bu halüsinasyonlara DÜŞÜK güven verdiğinden 32/32'si geçitlenir.
  Geriye gerçek plakalarda **+1** kalır (52ADRA12→52ADR412, pozisyon-tabanlı A→4 düzeltmesi).
- **Sonuç:** ConstrainedDecoding=true (varsayılan) emit floor ile net-pozitif ve halüsinasyon-güvenli.
  Bariyer-açan hayalet plaka riski yok. Faz A doğrulandı.

## Bulgu 2 — SkipCharLevel=0.3 tek-karede meşru son haneleri düşürebilir (bilinen tradeoff, nicelendi)

`--skipchar 0.3` (üretim varsayılanı, SDK + appsettings) tek-karede val'da **net −10**:

    25ADM033 → 25ADM03   (conf 1.00)   66ACG230 → 66ACG23 (conf 1.00)  ×4
    25ADN780 → 25ADN78   (conf 1.00)   25AEB370 → 25AEB37 (conf 1.00)  ×2
    52ADR412 → 52ADR41   (conf 1.00)   25TOG101 → 25TOG10 (conf 0.96)

- **Mekanizma** (kod izlendi, `TrConstrainedDecoder.PadLogP`): son hanenin gerçek olasılığı `pad` ile
  `skipCharLevel` (0.3) ARASINDAysa, "karakter yok" hipotezi (log 0.3) o zayıf haneyi yener → kısa
  uzunluk seçilir, gerçek son hane düşer. Emit floor bunu YAKALAMAZ (ilk 7 karakter güvenli → conf 1.00).
- **Bu YENİ bir hata değil:** kabul-250 raporu (07-13) case_0062'yi (34CEA422→34CEA42, güven 0.997)
  zaten skipchar kurbanı olarak kaydetmiş. skipchar 0.3, açılı/kenar *hayalet* son haneleri bastırmak
  için bilinçli eklendi; bedeli bazı zayıf-gerçek son haneler.
- **Neden tek-kare adil değil:** skipchar'ın hayalet-vs-gerçek ayrımı **çok-kareli ortalamaya** dayanır.
  Üretimde decoder MEAN dağılımda çalışır: çok karede görülen gerçek hane güçlü kalır (bastırılmaz);
  tutarsız hayalet zayıflar (bastırılır). Tek kırpıntı bu ortalama korumasından yoksun → bedel abartılı.
- **Risk (gerçek):** AZ karede görülen (hızlı geçen) araçlarda ortalama koruması zayıf → skipchar gerçek
  son haneyi düşürebilir. Bu projenin hedefi olan uzak/az-kare plakalarda önemli.

## Öneri (kullanıcı / canlı)
- ConstrainedDecoding=true KALSIN (doğrulandı, güvenli).
- **SkipCharLevel=0.3 canlı çok-kareli veriyle yeniden doğrulanmalı** — kameralar dönünce `evaluate`
  ile `--skipchar 0.3` vs `0.2` vs `0` A/B'si; hayalet-kazanç ile gerçek-hane-kaybı dengesine bakılır.
  Az-kare araçlarda 0.2'ye düşürmek daha güvenli olabilir. Varsayılan bu doğrulamaya dek değiştirilmedi.

## Araç
`Anpr.Bench cropab <csv> [--ocr f] [--minocr floor] [--skipchar L]` — kalıcı kırpıntı-A/B harness.
CSV kolonları: `image_path,plate_text` (CSV klasörüne göreli; boş plate_text = negatif örnek).
