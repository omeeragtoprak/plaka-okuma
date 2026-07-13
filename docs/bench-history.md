# Ölçüm Bataryası Geçmişi (/anpr-regression skill'i bu dosyayı okur)

Bayrak seti (canlı üretimle aynı): `--fuzzy 1 --minconfirm 2 --spatialiou 0.35 --skipchar 0.3 --qblur 600 --qdark 0.85 --qbright 0.80`

| tarih | model | diag4 birebir | gürültü | val kırpıntı | gece kırpıntı | not |
|---|---|---|---|---|---|---|
| 2026-07-10 | cct_xs_v2_erz | 22/26 | 9 | %76.4 (382/500) | %82.4 (103/125) | uzamsal+skip+kalite kapısı sonrası taban |
| 2026-07-11 | cct_xs_v2_erz_qdq (OCR) | 22/26 | 11 | %76.2 dengi | dengi | QDQ teknik-başarı; üretime alınmadı (hedef-PC yedeği) |
| 2026-07-13 | cct_xs_v2_erz | 22/26 | 9 | — | — | EarlyEmit 0/0.85/0.95 süpürmesi: diag4 duyarsız |
