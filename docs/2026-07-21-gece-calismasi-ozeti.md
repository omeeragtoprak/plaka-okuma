# Gece çalışması özeti — 2026-07-21

Kullanıcı Mac'i açık bırakıp "sabaha kadar kameraya/sisteme dokunmadan yalnız kendi projemizi
geliştir" dedi. Kameralar RTSP arızasında (aşağıda), o yüzden tüm iş **offline**.

## 1) Kamera olayı (fiziksel — kullanıcı müdahalesi bekliyor)
- **07-16**: iki kamera takıldı, ffprobe TEMİZ → servis ffmpeg oturumu takılmıştı → temiz restart çözdü.
- **07-21 06:37**: iki kamera birden düştü. Ping ÇALIŞIYOR ama **RTSP (ffprobe) + TCP 554 zaman aşımı**
  = kamera/switch tarafı arıza, servis değil. Restart ÇÖZMEZ. **Yapılacak: switch/PoE ya da kameraları
  güç-döngüsü.** Servis tek-temiz çalışıyor, kameralar dönünce oto-bağlanır.
- Tekrarlayan "degraded" uyarı spam'ini durdurdum (yinelenen kabuk sağlık-döngüleri öldürüldü).

## 2) Siyah resmi plaka zaafı — ÇÖZÜLDÜ (offline doğrulandı)
Kabul raporunun #1 doğruluk zaafı. Gözle-etiketli temiz set (33 siyah / 12 beyaz / 2 kırmızı) kuruldu.

**a) Önişleme (tersleme/CLAHE/germe/dual-pass) → ELENDİ.** Siyahı beyazı bozmadan düzeltemiyor
(en iyi güvenli nokta +3 siyah / −0.2 beyaz). → `docs/2026-07-21-siyah-plaka-onisleme-arastirmasi.md`

**b) Whitelist/müdavim SNAP → CLEAN WIN, uygulandı (config-gated, varsayılan güvenli).**
Okuma bilinen bir plakaya lev≤SnapMaxLev yakınsa düzeltilir. Ölçüm: **lev≤1 siyah +27pp (51.5→78.8%),
beyazda 0/400 yanlış-snap.** lev≤2 siyah +45pp (→%97) / %0.5 risk. → `docs/2026-07-21-whitelist-snap-...`
- Auto-müdavim snap YETMEZ: en kötü siyah plakalar (25TGG001, 25LT424) sürekli yanlış okunduğu için
  müdavim olamıyor; ayrıca genel filoda auto-müdavim net −7 (zararlı). → **manuel `KnownPlates` şart.**

**KULLANICIDAN GEREKEN:** `appsettings.json`'a resmi/protokol araç plakalarını gir:
```json
"KnownPlates": [ "25 LT 424", "25 TGG 001", "25 LT 333", "25 ACV 240" ], "SnapMaxLev": 1
```
(Rapor + gece 4 plaka biliyor; TAM liste sende.) Girince #1 zaaf büyük ölçüde kapanır.

## 3) Yön filtresi — KESİN SONUÇ: per-kamera yörünge imkânsız (canlı kanıtlandı)
Kameralar sabaha karşı toparladı (RTSP arızası geçti). Hareket-yolu (konum) sinyalini de canlı ölçtüm:
6 araç, 5/6 move=(0,0) — boyut gibi DÜZ. **Kök neden**: plaka yalnız yakın-durgun zonda tespit
ediliyor; ayırt edici sinyal (büyüme/hareket) uzakta, orada plaka tespit edilemiyor. Boyut VE konum
yörünge sinyalleri elendi. `RequireApproaching` varsayılan KAPALI. → `docs/2026-07-21-yon-tespiti-kesin-sonuc.md`

**Geriye kalan gerçek seçenekler (karar senin):** (A) ön/arka araç-yüzü sınıflandırıcı (model, per-kamera,
gözlemine uygun), (B) çapraz-kamera dedup (modelsiz, koordineli), (C) yön filtresini bırak + enerjiyi
siyah-plaka fix'ine ver.

## Durum
- Tüm değişiklikler test-geçmiş (85/85), config-gated, **varsayılan davranış değişmedi** (güvenli).
- Commit + GitHub push yapıldı. Kod: DirectionTracker (telemetri), whitelist-snap (aktivasyon bekliyor).
- Canlı servisin ikilisi değiştirilmedi (deneysel kod prod'a dağıtılmadı).

## Sıradaki (öncelik)
1. **Kameraları fiziksel düzelt** (RTSP down) — her şey buna bağlı.
2. **`KnownPlates` doldur** — siyah-plaka fix'ini aktifleştir (offline +27pp doğrulandı).
3. Kameralar dönünce: whitelist-snap canlı doğrula + hareket-yolu yön sinyalini dene.
