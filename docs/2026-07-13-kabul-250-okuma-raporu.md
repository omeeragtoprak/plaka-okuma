# 250-Araç Retrospektif Kabul Raporu — Kör Okuma Karşılaştırması

Tarih: 2026-07-13 · Küme: events arşivinden **en yeni 280 ardışık roi olayı**
(2026-07-11 11:54 → 2026-07-13 06:17; çıkış 175 / giriş 105; 30'u LowConfidence).
Yöntem: kanıt kırpıntıları nötr adlarla (case_0000..0279) kopyalandı — dosya adındaki plaka
metni kör etiketlemeye SIZDIRILMADI; 14 bağımsız okuyucu ajan, manifest'e erişmeden, kırpıntı
başına TR plakasını okudu (emin olmadığı karakteri `?` işaretledi); sonra emit edilen metinle
eşleştirildi ve tüm uyuşmazlıklar tek tek insan-gözü hakemliğinden geçirildi.

> **Kapsam dürüstlüğü**: bu ölçüm **READ doğruluğunu** (kanıt kırpıntısı → doğru metin) ölçer;
> **CAPTURE doğruluğunu** (geçen her araca olay üretildi mi?) ÖLÇMEZ — onun aracı gölge-raporu
> (`scripts/shadow_report.py`, camera-records.jsonl birikiyor) ve sahadaki NASPLE kabulüdür.
> Küme yalnız gündüz+alacakaranlık açık-otopark koşulunu temsil eder (kar/kapalı-otopark yok).

## Sonuç Özeti

| Metrik | Ham | Hakem-düzeltmeli* |
|---|---|---|
| Toplam olay | 280 | 280 |
| İnsan-okunur kırpıntı | 268 | 268 |
| İnsanın da okuyamadığı | 12 (%4.3) | 12 |
| **Birebir (exact)** | 224 (**%83.6**) | 234 (**%87.3**) |
| 1-karakter fark (N-1) | 33 (%12.3) | 24 (%9.0) |
| ≥2 karakter fark | 11 (%4.1) | 10 (%3.7) |
| **exact+N-1 birleşik** | **%95.9** | **%96.3** |

\* Hakemlik: `06TLH64?` kümesi (9 olay) kesik-kırpıntı belirsizliğiydi — aynı aracın 0.998 güvenli
tekrarları emit'i destekliyor, exact sayıldı; case_0179'da kör okuyucu yanlıştı (M/H), emit doğru.
Karşı yönde case_0004 (FZ→FL, güven 0.925) ve case_0062 (34CEA422→34CEA42 son hane düşmüş,
güven 0.997) **kesin motor hatası** olarak onaylandı.

## Güven Kalibrasyonu (sağlıklı ✓)

| Güven kovası | exact uyum |
|---|---|
| ≥0.99 | 157/167 = **%94.0** |
| 0.95–0.99 | 40/48 = %83.3 |
| 0.85–0.95 | 25/35 = %71.4 |
| <0.85 | 2/18 = %11.1 |

**LowConfidence bayrağı işini yapıyor**: işaretsiz 250 olayda exact %88.8; işaretli 18 olayda %11.1.
Yani `ManualReviewMinOcr=0.85` eşiği hatalıları manuel-inceleme kuyruğuna doğru ayırıyor;
işaretsizlerde kalan hataların çoğu aşağıdaki tek sistematik kaynaktan geliyor.

## Bulgu 1 — SİYAH (resmi) PLAKA sistematik zayıflığı ⚠ (en önemli bulgu)

44 uyuşmazlığın **16'sı yalnızca İKİ siyah-zemin/altın-harf resmi plakalı araçtan**:

- **25 LT 424** (hakem: kesin, 8 olayda kör okuyucular tutarlı): emit savrulması →
  25TT424, 25II424, 25ZT424, 25LT4444, 25TT4444… tek doğru okuma 14:34'te (0.982).
- **25 TGG 001** (hakem: kesin): 07-11'de doğru (0.922), sonraki 6 olayda →
  25FD607, 05ZGG001, 25NVG001, 25ANG600, 25TGG004, 25TCG001.

Kök neden: eğitim verisi ağırlıkla beyaz plaka; siyah plakada kontrast ters ve düşük.
Belediye otoparkı bağlamında resmi araç trafiği DÜZENLİ — bu hücre kapatılmalı:

1. **Gerçek veri hasadı** (augmentation dersi: v3+v4'te kanıtlandı, tek yol gerçek veri):
   bu iki aracın arşivdeki tüm kırpıntıları + bundan sonra birikecekler etiketlenip
   fine-tune setine eklenecek (`dataset-corrections/` mekanizması hazır).
2. Kısa vadede müdavim kuralı (FrequentPlateRegistry) bu araçları öğrenirse varyantları
   bastırır — 25LT424 için 14:34'teki 0.982'lik doğru okuma kayda girmişti.

## Bulgu 2 — Varyant bastırma kaçakları (mükerrer olay)

Aynı geçişte lev≥2 varyantlar ayrı olay üretebiliyor (kurallar lev≤1'de kalıyor):

- 25TT424 → 2 sn sonra AYNI kamerada 25II424 (lev=2) — iki olay.
- 25NVG001 + 25ANG600 aynı SANİYEDE, aynı kamerada — iki olay.
- 25ACE719 (0.998) → 5 sn sonra 21DEE719 (0.672, LOW) — lev=3, kaçtı.

Öneri (A/B ölçümü yapılmadan AÇILMAYACAK): `VariantSuppressSeconds` penceresi içinde aynı
kamerada **il kodu + rakam bloğu birebir aynı, yalnız harf bloğu farklı** ise düşük güvenliyi
bastır. Rakam bloğu çakışması iki farklı aracın tesadüfü olamayacak kadar ayırt edici; yine de
diag4 + canlı A/B ile doğrulanmadan varsayılan açılmaz (elenenler listesi disiplini).

## Bulgu 3 — Yüksek güvenli tekil hatalar (nadir ama gerçek)

| Olay | Emit | Gerçek (hakem) | Güven | Tür |
|---|---|---|---|---|
| case_0062 | 34CEA42 | 34CEA422 | 0.997 | son hane düşmesi |
| case_0004 | 25FL069 | 25FZ069 | 0.925 | Z→L |
| case_0151/0152 | 25AEC886 | 25AEC806? | 0.995/0.989 | 8/0 (hakem kararsız) |

Son-hane düşmesi (0062) değişken-uzunluk çözümlemesinin bilinen riski; `?`li/kararsız vakalar
dahil tümü `dataset-corrections/` adayı.

## Skor etkisi

- **Doğruluk**: 42 → **50** (gündüz-açık-otopark READ hücresi artık ölçülü: exact %87.3 /
  N-1 dahil %96.3; siyah-plaka zayıflığı keşfedildi ve kapatma yolu belli).
- **Test disiplini**: 72 → **80** (250-araç kabul koşuldu; gölge-raporu betiği yazıldı ve
  bugünün kısmi verisiyle duman testinden geçti).

Sıradaki doğruluk adımları: siyah-plaka kırpıntı hasadı → fine-tune v5 (yalnız gerçek veri),
gece canlı parite (camera-records birikiyor), kapalı-otopark kaydı (saha).

## Yeniden üretim

```
scratchpad/kabul250/           # kırpıntılar + manifest.json + blind_labels.json (KVKK: repoya girmez)
scratchpad/kabul250/analyze.py # karşılaştırma + güven kovaları + uyuşmazlık listesi
scripts/shadow_report.py       # capture-parite (kamera-kayıt ↔ olay, günlük)
```
