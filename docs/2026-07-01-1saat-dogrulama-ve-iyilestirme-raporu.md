# OtoparkAnpr — 1 Saatlik Doğrulama + İyileştirme Raporu

Tarih: 2026-07-01 · Veri: `dataset/live1h` (2 kamera × 7200 kare = 14,400 kare, gerçek Erzurum trafiği)

## 1. Dayanıklılık (canlı servis, 60 dk kesintisiz)

| Metrik | Sonuç |
|---|---|
| Uptime | 60 dk, çökme yok |
| Yeniden bağlanma (restart) | 0 / 0 |
| Alınan / işlenen kare | 14,380 / 4,220 → **uyarlanabilir fps ~%71 CPU tasarrufu** |
| Canlı olay | 118 |
| Gecikme | ~28-30 ms/kare (Mac; OCR yalnız tespitte) |

## 2. Doğruluk — 108 araç görünümü TEK TEK görsel doğrulandı

| Grup | Sayı | Sonuç |
|---|---|---|
| Yüksek güven + çok kare | 82 | ~%99-100 doğru |
| Şüpheli (düşük güven / tek kare) | 26 | 13 doğru · 8 yanlış · 5 okunamadı |
| **Toplam** | 108 | **~%88 (95/108)**; iyi karelerde ~%99 |

8 yanlışın hepsi **tek-kare / düşük-güven / karanlık** plaka; çoğu 1 karakter hatası. Gerçek-değer
`dataset/live1h/labels.json`'a işlendi (101 etiketli, 7 belirsiz, 6 model-yanlışı düzeltildi) — kalıcı
referans.

## 3. İncelenen 3 iyileştirme (hepsi A/B ile ölçüldü, live1h etiketli set)

### Fix 1 — İl-oylama birleştirme (kimlik = harf|rakam) → NET KAZANÇ YOK, varsayılan KAPALI

| Konfig | Doğruluk | Toplam emit | Yanlış emit | Kaçan |
|---|---|---|---|---|
| il\|harf (eski) | %86.1 | 173 | 60 | 14 |
| harf\|rakam (Fix1) | %87.1 | **208** | **92** | 13 |

Doğruluk +1pp arttı ama emit sayısı/yanlış-emit **arttı**. Neden: `harf\|rakam` il-varyantlarını
birleştirirken **rakam-varyantlarını AYIRIR** (eski `il\|harf` bunları iyi okumaya birleştiriyordu).
Rakam-hataları daha çok → net etki ters. **Karar:** varsayılan KAPALI, config opsiyonu olarak kalır.

### Fix 2 — Emit seviyesinde OCR tabanı → İHMAL EDİLEBİLİR ama zararsız, KALDI

Kare-başı taban (0.45) düşük okumaların çoğunu zaten eliyor; emit-tabanı yalnız birkaç uzlaşı sızıntısı
yakaladı (208→205 emit). Zararsız olduğu için kod kaldı.

### Fix 3 — Tetik bölgesi (MinPlateWidthRatio) → GERÇEK KALDIRAÇ (precision/recall dengesi)

| Eşik | Doğruluk | Yanlış emit | Yanlış-poz | Karakter benz. | Kaçan |
|---|---|---|---|---|---|
| 0 (kapalı) | %86.1 | 60 | 23 | %86.1 | 14 |
| **0.04** | %83.2 | **33** | 18 | **%90.1** | 17 |
| 0.05 | %59.4 | 15 | 9 | %92.5 | 41 |

`0.04`'te yanlış emit ~yarıya iner, karakter doğruluğu %86→%90; bedeli ~%3 recall (3 araç). `0.05` çok
agresif. **Kritik:** kaçanlar bariyere hiç yaklaşmayan **uzak trafik**; gerçek bariyerde araç yakında
durduğu için yakalanır. **Karar:** config olarak kalır, varsayılan 0; sahada kamera geometrisine göre
`~0.03-0.04` önerilir (widthstats: düşük-güvenlilerin medyan genişlik-oranı 0.044).

## 4. Genel sonuç (dürüst)

- **Sağlamlık kanıtlandı:** 60 dk 0 restart, ~%71 CPU tasarrufu, iyi karelerde ~%99 doğruluk.
- Yazılım-içi kurnaz teknikler (deskew, piksel-füzyon, gramer, il-birleştirme) bu problemde **marjinal
  ya da ters** çıktı — ölçüm bunları eledi. Sağlam kazançlar önceki turda gelmişti: **V2 model +
  olasılık oylaması** (%87.5→%100 temiz sette).
- **En güçlü kalan kaldıraç yakalama tarafı:** plakayı uzaktan değil **yakınken (büyükken) oku** →
  **tetik bölgesi** + kamera konumu. `0.04` eşiği yanlış emitleri yarıya indirdi.
- **Öneri:** hedef sahada `MinPlateWidthRatio`'yu bariyerdeki plaka boyutuna göre ayarla; gerisi zaten
  optimal (V2 + oylama + uyarlanabilir fps + OCR tabanı).

## 5. Değişen defaultlar

- `MergeAcrossProvince = false` (Fix1 net kazanç vermedi).
- Emit-seviyesi OCR tabanı kodda (Fix2, zararsız).
- `MinPlateWidthRatio = 0` (tetik bölgesi config; sahada ~0.03-0.04 önerilir).
