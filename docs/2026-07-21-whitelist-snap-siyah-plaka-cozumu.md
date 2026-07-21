# Whitelist/müdavim SNAP — siyah resmi plaka zaafının çözümü

Tarih: 2026-07-21 · Durum: uygulandı, config-gated, **varsayılan güvenli (etkisiz)** — kullanıcı
resmi plakaları girince aktifleşir. Offline doğrulandı; canlı doğrulama kameralar dönünce.

## Problem
Kabul raporunun (2026-07-13) #1 doğruluk zaafı: **siyah resmi/protokol plakaları** (25 LT 424,
25 TGG 001 gibi) sistematik yanlış okunuyor (25TGG001 → 25TCG001 / 05ZGG001 / 25NVG001 / 25TGG004…).
Bugünkü offline araştırma (docs/2026-07-21-siyah-plaka-onisleme-arastirmasi.md): **önişleme
(tersleme/CLAHE/germe/dual-pass) beyazı bozmadan düzeltemiyor → ELENDİ.**

## Çözüm: SNAP
Anahtar gözlem: siyah plakalı araçlar **FREKANSLI** resmi araçlar; plakaları BİLİNİR. Emit anında,
okuma bilinen bir plakaya edit-mesafesi ≤ `SnapMaxLev` yakınsa o bilinen plakaya düzeltilir.

**Offline ölçüm (gözle-etiketli 33 siyah + 400 beyaz-ağırlıklı kırpıntı, whitelist = 4 siyah plaka):**

| eşik | siyah exact | kazanç | beyaz yanlış-snap | beyaz exact |
|---|---|---|---|---|
| baseline | %51.5 | — | 0 | %86.2 |
| **lev≤1** | **%78.8** | **+27.3pp** | **0/400 (%0)** | %86.2 (değişmez) |
| lev≤2 | %97.0 | +45.5pp | 2/400 (%0.5) | %86.0 |

**lev≤1 CLEAN WIN**: siyah +27pp, beyazda SIFIR yanlış-snap. Önişlemenin başaramadığını yaptı çünkü
whitelist küçük/küratörlü → beyaz plakalar nadiren bir whitelist plakasına lev≤1 düşer.

## Uygulama
- `FrequentPlateRegistry.SnapTarget(compact, maxLev, includeFrequent)` — okumayı en yakın bilinen
  plakaya (whitelist önceliği; opsiyonel müdavimler) snap'ler. Erken-çıkışlı bounded Levenshtein.
- `CameraWorker.Emit` — roi olayında yayından ÖNCE snap uygular, düzeltmeyi loglar (`plaka snap: X -> Y`).
- Config (`AnprServiceOptions`):
  - `KnownPlates`: bilinen plaka listesi (ör. resmi araçlar). **Boş = etkisiz (varsayılan).**
  - `SnapMaxLev`: 1 (güvenli, 0 yanlış-snap) · 2 (daha çok düzeltir, ~%0.5 risk) · 0 (kapalı).
  - `SnapToFrequent`: manuel whitelist'e EK olarak auto-öğrenilen müdavimlere de snap. **Varsayılan
    KAPALI** (müdavim kaydı yanlış-okumalarla kirlenebilir; canlı doğrulama sonrası açılır).
- Testler: `Anpr.Tests/FrequentPlateSnapTests.cs` (7 test; lev1/lev2, yanlış-snap yok, opt-in müdavim).

## Nasıl aktifleştirilir (kullanıcı)
`appsettings.json` → `Anpr:KnownPlates`'e sitenin resmi/protokol araç plakalarını ekle:
```json
"KnownPlates": [ "25 LT 424", "25 TGG 001", "25 LT 333", "25 ACV 240" ],
"SnapMaxLev": 1
```
Boşluk/format serbest (normalize edilir). lev≤1 sıfır-risk; daha agresif düzeltme için 2 (ölçülü risk).

## Kalan
- **Canlı doğrulama** (kameralar RTSP arızasından dönünce): snap loglarını izle, yanlış-snap var mı bak.
- Resmi araç plakalarının TAM listesi kullanıcıdan alınmalı (rapor yalnız 2'sini biliyor: 25LT424, 25TGG001).
- Skor etkisi (doğrulama sonrası): doğruluk boyutu, siyah-plaka hücresi büyük kazanç.
