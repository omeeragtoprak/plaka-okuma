# Üretim-Hazırlık Skor Kartı — OtoparkAnpr

Tarih: 2026-07-13 · Yöntem: 6 boyutlu kanıt-temelli denetim (paralel denetçiler, kod+doküman+canlı
koşum doğrulamalı) + aynı gün kapanışlar. Hedef sistem: 7/24 · açık VE kapalı otopark ·
karanlık/aydınlık/güneş · TR plakaları · zayıf CPU-only PC.

## Özet Skor

| Boyut | Denetim | Bugünkü kapanışlar | GÜNCEL | Kalan ana boşluk |
|---|---|---|---|---|
| Doğruluk × koşul matrisi | %42 | (veri-toplama altyapısı kuruldu) | **%42** | kar=0, kapalı-otopark=0, gece-canlı yeniden ölçüm, 250-araç kabul |
| 7/24 dayanıklılık | %57 | heartbeat itmesi + kamera-kayıt kalıcılığı + batarya onarımı | **%63** | 72h resmi koşu, Windows-servis kurulumu (hedef PC) |
| Entegrasyon | %34 | webhook disk-kuyruk+retry (E3) + heartbeat (E5) + düzeltme API (FR-36) | **%50** | E1 yüzey keşfi, Hikvision uyumluluk modu, PMS uçtan-uca test |
| Güvenlik / KVKK | %42 | panel token (G3) + events.jsonl saklama/rotasyon (G2) + SHA256SUMS | **%68** | aydınlatma metni/tabela (idari), TLS (hedef PC), rol/denetim izi |
| Performans / hedef-PC | %45 | AVX/CPUID probe + p50/p95-ms health metrikleri + pilot kiti (113MB zip) | **%57** | HEDEF PC'DE SIFIR ÖLÇÜM (tek en büyük bilinmeyen) |
| Test / ölçüm disiplini | %64 | regression.sh onarıldı + üretim bayrakları gömüldü + skill'ler repo'ya taşındı | **%72** | 250-araç kabul koşusu, 1-hafta gölge raporu, dayanıklılık birim testleri |
| **GENEL** | **%47** | **9 kapanış** | **~%59** | |

## Bugün kapatılanlar (kod canlıda, commit'li)

1. **Webhook store-and-forward (E3)**: başarısız POST → `webhook-queue.jsonl` + 2 dk'da bir sıralı
   yeniden teslim; olay kaybı sıfır. Gözlem olayları kanala sızmaz.
2. **Heartbeat itmesi (E5)**: WebhookUrl doluysa 30 sn'de bir kamera durumu/sayaç POST'u.
3. **Düzeltme API'si (FR-36)**: `POST /api/correction {eventId, correctedPlate}` → denetim izi
   (corrections.jsonl) + kanıt kırpıntısı eğitim arşivine (dataset-corrections/<PLAKA>/).
4. **events.jsonl KVKK saklama (G2)**: `EventRetentionDays=30` — açılışta eski satırlar budanır,
   dosya yeniden yazılır (sınırsız büyüme bitti).
5. **Panel/API token koruması (G3)**: `PanelToken` doluysa /panel+/api+/events+/evidence 401;
   /health açık (izleme). Panel JS token'ı tüm iç çağrılara taşır.
6. **AVX/CPUID probe (P1c)**: açılışta loglanır + /health `cpu` alanı; AVX'siz CPU'da kontrollü uyarı.
7. **p50/p95 ms/kare + eFPS teşhisi**: /health kamera başına kayan-pencere işleme süresi
   (bu Mac: p50≈33-37 ms — pilot karşılaştırma tabanı).
8. **Kamera-kayıt kalıcılığı**: `camera-records.jsonl` — haftalık gölge-eşleşme raporu ve gece-parite
   ölçümleri artık retrospektif üretilebilir (veri kendiliğinden birikir).
9. **Batarya onarımı**: regression.sh sözdizimi düzeltildi + üretim bayrakları gömüldü
   (denetçi tespiti: 07-11'den beri bozuktu); skill'ler bin/'den repo `.claude/skills/`e taşındı;
   `models/SHA256SUMS` yazıldı; hedef-PC pilot kiti üretildi (`pilot-kit/OtoparkAnpr-pilot-win64.zip`,
   self-contained win-x64 + modeller + talimat).

## %59 → %85 yolu (kış GEREKTİRMEYEN işler, öncelik sırasıyla)

| # | İş | Boyut etkisi | Nerede | Süre |
|---|---|---|---|---|
| 1 | **Hedef-PC pilotu** (kit hazır): ms/kare, AVX, 2-kamera fps, QDQ/OpenVINO A/B, Windows-servis kurulumu | perf +25, dayanıklılık +8 | hedef PC | 1-2 gün |
| 2 | **E1 entegrasyon yüzeyi keşfi** (48h trafik izleme + httpHosts GET, salt-okuma) → uyumluluk modu kararı | entegrasyon +20 | saha PC erişimi | 2-3 gün |
| 3 | **Gece canlı parite yeniden ölçümü** (tüm iyileştirmeler sonrası; camera-records.jsonl artık biriktiriyor — 2-3 gece sonra analiz) | doğruluk +8 | bu Mac (pasif) | 3 gece + 2 saat |
| 4 | **250-araç retrospektif kabul kümesi** (kanıt JPEG + events arşivinden insan-gözü etiketli; N/N-1 kırılımı) | doğruluk +9, test +10 | bu Mac | 1-2 gün |
| 5 | **1-hafta gölge raporu** (camera-records + events'ten günlük otomatik eşleşme özeti) | test +6, doğruluk +5 | bu Mac (pasif) | script 0.5g + 7 gün takvim |
| 6 | **Kapalı otopark 48-72h gölge kaydı** (tek kamera + ROI + insan-gözü/kamera etiketi) | doğruluk +12 | saha | 2-3 gün |
| 7 | 72h resmi dayanıklılık koşusu (çalışan servis üzerinde pencere ilan et + metrik anlık görüntüleri) | dayanıklılık +6 | bu Mac (pasif) | 3 gün takvim |
| 8 | Aydınlatma metni taslağı + tabela + envanter (hukukla) | KVKK +12 | idari | taslak 2 saat + süreç |
| 9 | T4 kuyruk hedef-araç seçimi (en büyük+en alttaki kuralı) | doğruluk +4 | bu Mac | 1 gün |

**%85 → %100**: kar/kış veri toplama + kar dilimli kabul (mevsim), NASPLE tam kabul koşusu (saha),
cutover tatbikatı ("PC fişini çek" + Hikvision By-Camera dönüşü), PMS ile uçtan uca canlı entegrasyon.

## Dürüstlük notları

- Denetim skorları ben bugünkü kapanışları yazarken ölçüldü; GÜNCEL sütun kapanışların etki
  tahminleriyle düzeltildi — bir sonraki denetimde yeniden doğrulanmalı.
- Doğruluk boyutunun düşüklüğü motor zayıflığından değil KANIT kapsamındandır: gündüz-açık-otopark
  diliminde parite kanıtlı; kar/kapalı-otopark hücreleri hiç ölçülmediği için 0 puan aldı — dürüst skorlama.
- "Augmentation ile koşul dayanıklılığı satın alınamıyor" iki kez kanıtlandı (v3+v4) → kalan koşul
  hücrelerinin TEK kapanış yolu gerçek saha verisidir; bu yüzden 3/5/6 numaralı işler pasif veri
  biriktirmeye bugünden bağlandı.
