# Ölçüm Bataryası Geçmişi (/anpr-regression skill'i bu dosyayı okur)

Bayrak seti (canlı üretimle aynı): `--fuzzy 1 --minconfirm 2 --spatialiou 0.35 --skipchar 0.3 --qblur 600 --qdark 0.85 --qbright 0.80`

| tarih | model | diag4 birebir | gürültü | val kırpıntı | gece kırpıntı | not |
|---|---|---|---|---|---|---|
| 2026-07-10 | cct_xs_v2_erz | 22/26 | 9 | %76.4 (382/500) | %82.4 (103/125) | uzamsal+skip+kalite kapısı sonrası taban |
| 2026-07-11 | cct_xs_v2_erz_qdq (OCR) | 22/26 | 11 | %76.2 dengi | dengi | QDQ teknik-başarı; üretime alınmadı (hedef-PC yedeği) |
| 2026-07-13 | cct_xs_v2_erz | 22/26 | 9 | — | — | EarlyEmit 0/0.85/0.95 süpürmesi: diag4 duyarsız |

## 72h Resmi Dayanıklılık Penceresi (skor kartı işi #7)

- **Başlangıç**: 2026-07-13 06:29 UTC (servis 05:41 UTC'de temiz başladı — sabahki kamera-kesintisi
  onarımı + soğuma-modu kodu sonrası). **Bitiş**: 2026-07-16 06:29 UTC.
- Başlangıç anlık görüntüsü: status ok · restartCount 0/0 · p50 33/31 ms · p95 57/56 ms ·
  qualityDropped 2/2 · events 35/40 (giris/cikis, servis-içi sayaçlar).
- **Geçme kriterleri**: (1) plansız süreç ölümü YOK; (2) kamera restartCount yalnız gerçek ağ
  kesintilerinde artar (soğuma modu çalışır); (3) p50 ms/kare pencere boyunca ±%20 bandında
  (bellek sızıntısı/ısınma göstergesi); (4) webhook-queue birikmez; (5) events.jsonl her gün olay alır.
- Kayıt: `health-snapshots.jsonl` (30 dk'da bir /health; nohup kaydedici) + Monitor nöbeti.

## Siyah-plaka önişleme araştırması (2026-07-21, offline — kameralar arızada)

| tarih | deney | siyah exact | beyaz exact | sonuç |
|---|---|---|---|---|
| 2026-07-21 | baseline (tek-kare) | %51.5 (17/33) | %100 (12/12) | zaaf niceliği |
| 2026-07-21 | dual-pass adaptif T0.93 | +12.1pp | −1.0pp | net nötr/negatif |
| 2026-07-21 | dual-pass adaptif T0.90+marj0.10 | +3.0pp | −0.2pp | değmez |

**ELENDİ**: önişleme (invert/CLAHE/germe/dual-pass) siyahı beyazı bozmadan düzeltemiyor. Kök neden:
model beyaz dağılımda eğitilmiş + kırpıntı bağlamı güvenilir koyu-plaka dedektörünü engelliyor.
Gerçek çözüm: saha verisi fine-tune ya da müdavim/whitelist snap. → docs/2026-07-21-siyah-plaka-onisleme-arastirmasi.md
