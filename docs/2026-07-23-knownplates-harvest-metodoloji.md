# KnownPlates harvest — sistematik yanlış-okuma düzeltme (2026-07-23)

**Bağlam:** Kullanıcı somut vaka verdi: Hikvision kamera `25ADM032`'yi doğru okudu, bizim model
`25AUM032` (D→U) okudu. Bu, whitelist snap'in tam hedef vakası: okuma bilinen bir plakaya lev≤1 yakınsa
o plakaya düzeltilir. Sorun: liste EL ile bakımı zor + filo değişir. Bu doküman veriden liste üretmenin
**ölçülmüş** yöntemini kaydeder.

## Neden auto-frequent DEĞİL (ölçülmüş -7)
Yalnız BİZİM sık okumalarımızı snap hedefi yapmak net-negatif: kendi SİSTEMATİK yanlış-okumalarımız
(ör. sürekli `25AUM032` okusaydık) "sık" olup snap hedefi olurdu → başka okumaları yanlışa çekerdik.

## Yöntem: ÇAPRAZ-AGREEMENT
Kaynaklar (KVKK: hepsi yerel, plaka repoya girmez):
- **Kameranın BAĞIMSIZ ANPR kayıtları** (`camera-records.jsonl` + poll) — bizden FARKLI hata modu.
- **Bizim olaylar** (`events.jsonl`), yüksek-güven sayımıyla.
- **Operatör-düzeltmeleri** (`dataset-corrections/<PLAKA>/`) — insan-onaylı, en güvenilir.

Aday katmanları: **OPERATOR** (insan-onaylı) · **AGREEMENT** (hem kamera hem biz sık+güvenle okuyor →
gerçek düzenli araç; bizim sistematik yanlış-okumamızı kamera onaylamaz) · **CAMERA-ONLY** (kameranın sık
okuduğu, bizim kaçırdığımız — kronik) · **OURS-STRONG**. Mevcut veriden: **395 aday** (2 operatör /
389 agreement / 4 camera-only).

## Ölçüm (val+night, C#-birebir snap) — GATE SWEEP
| gate (SnapMaxOcrConfidence) | doğruluk | net | yanlış-snap |
|---|---|---|---|
| baseline (snap yok) | %78.4 | — | — |
| 0.95 | %85.9 | +47 | 1 |
| 0.98 | %87.2 | +55 | 1 |
| **0.99 (OPTİMAL)** | **%87.7** | **+58** | **1 (%0.16)** |
| 1.0 (kapısız) | %86.7 | +52 | **11** |

**Kritik içgörü — 0.99→1.0 uçurumu (1→11 yanlış-snap):** modelin **≥0.99 okumaları DOĞRU** (snap'leme),
**sistematik yanlış-okumalar <0.99'da** oturuyor (snap'le). Gate 0.99 ikisini temiz ayırır. Sonuç:
**%78.4 → %87.7 (+9.3pp)**, %0.16 yanlış-snap. Kullanıcının vakası doğrulandı: `snap(25AUM032)=25ADM032`
(nadir + düşük-güven → snap aktif); ayrıca `25ZGG001→25TGG001` (kronik siyah plaka) da yakalanır.

## Araç + aktivasyon
- **Yeniden üret:** `python3 scripts/harvest_knownplates.py --out /tmp/kp.json` (filo değişince tekrar çalıştır).
- **Aktive et:** çıktıyı **gitignored** `appsettings.json` → `Anpr.KnownPlates`'e koy + `SnapMaxLev: 1`,
  `SnapMaxOcrConfidence: 0.99`. Servisi yeniden başlat.
- **KVKK:** araç (`scripts/`) commit'lenir; plaka listesi (çıktı) ASLA repoya girmez.

## Dürüst uyarılar (şüpheci)
1. val/night = site araçları → +9.3pp **düzenli trafiği** yansıtır; yeni ziyaretçi araç yararlanmaz.
2. Canlı kazanç muhtemelen biraz daha az (oylama + gramer bazılarını zaten düzeltiyor; snap onların
   yakalayamadığı sistematik misread'leri yakalar — tamamlayıcı).
3. **1 kaçınılmaz yanlış-snap:** whitelist-dışı nadir gerçek araç, bir whitelist plakasına lev1 + düşük
   güven (25EB859→25EB858). Bunu tümden çözmenin tek yolu: **canlı kamera çapraz-kontrolü** (kamera vs
   bizim okuma anlaşmazlığında kameranınkini al — deterministik). Whitelist snap bunun offline-pişmiş hâli.
