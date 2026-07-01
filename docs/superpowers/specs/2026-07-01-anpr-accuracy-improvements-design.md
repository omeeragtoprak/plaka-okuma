# OtoparkAnpr — Doğruluk İyileştirmeleri Tasarımı (1 saatlik testten çıkan 3 fix)

Tarih: 2026-07-01

## Bağlam (neden)

1 saatlik gerçek trafik testi yapıldı (108 araç görünümü tek tek görsel doğrulandı):
- **Dayanıklılık:** 60 dk 0 restart, ~%71 CPU tasarrufu (uyarlanabilir fps), 118 canlı olay.
- **Doğruluk:** iyi karelerde (yüksek güven + çok kare) ~%99; genel görünüm-doğruluğu ~%88.
- Kalan hatalar **tamamen zor/tek-kare** yakalamalarda. Ölçümden çıkan 3 net kök neden var.

Bu tasarım o 3 kök nedeni giderir. **Katı kurallar (proje boyunca):** (1) Türkiye plakaları,
(2) zayıf CPU. Üç fix de **CPU-nötr** ve her biri **Anpr.Bench ile önce/sonra A/B** ile ölçülecek;
kazanç kanıtlanmayan/CPU'yu bozan değişiklik geri alınır (deskew/piksel-füzyonu gibi elenenler).

## Hedefler (ölçülebilir)

- **Çift olayları azalt:** aynı araç için il-varyantı kaynaklı tekrarlı emitleri tek doğru olaya indir.
- **Yanlış-pozitifi azalt:** düşük-güven uzlaşı sızıntısını kes.
- **Genel doğruluğu artır** (özellikle tek-kare/uzak plakalar), temiz veride **regresyon olmadan**.

## Kapsam

Yalnızca ölçülen 3 fix. Model/dedektör büyütme ve klasik iyileştirme (CLAHE) **kapsam dışı**
(daha çok CPU / riskli; gerekirse ayrı tur).

---

## Fix 1 — İl-oylama birleştirme (en yüksek değer)

**Sorun.** `PlateConfirmer.IdentityKey` = `il|harf` (`Anpr.Core/Confirmation/PlateConfirmer.cs:157`).
İl ilk-hanesi yanlış okununca aynı araç birden çok ize bölünüyor → çift + yanlış-il emit. Canlı testte
baskın çift-olay deseni buydu: `34/14 NP 9285`, `25/35/55 AFG 450`, `06/16 EJD 080`, `25/75/35 AEP 085`.

**Yaklaşım (onaylı).** Kimlik anahtarını **`harf|rakam`** yap (il'i anahtardan çıkar). Aynı harf+rakam
farklı il okumaları TEK ize düşer. Emit anında zaten çalışan çok-kareli **slot-oylama +
`TrConstrainedDecoder`**, birleşen izin **il slotlarını oylar** → çoğunluk il kazanır
(ör. `25 AFG 450` çoğunluk, `35 AFG 450` azınlık → `25 AFG 450`).

**Ödünleşim.** Aynı harf + aynı rakam + farklı il + 8 sn içinde çakışan İKİ FARKLI araç birleşir
(biri kaybolur). Tek bariyerde astronomik nadir (aynı 3 harf + aynı 3 rakam). Kabul + README'de belgelen.

**Değişiklik.** `PlateConfirmer.IdentityKey`: `parts[0]+"|"+parts[1]` (il|harf) → `parts[1]+"|"+parts[2]`
(harf|rakam). Tek fonksiyon. Fallback: harf/rakam ayrışmıyorsa tüm metin. CharacterVoting +
ConstrainedDecoding zaten açık → il oylaması otomatik.

**Test.** `PlateConfirmerTests`e senaryo: aynı harf+rakam, farklı il çoğunluk/azınlık okumaları →
tek emit, çoğunluk il. Mevcut testler bozulmamalı.

---

## Fix 2 — Emit seviyesinde OCR tabanı (küçük, güvenli)

**Sorun.** `EmitMinOcrConfidence` yalnız *observe*'da (kare-başı) uygulanıyor
(`CameraWorker.cs:117`). Birleşik uzlaşının güveni tabanın altına düşebiliyor → sızıntı
(`61 IP 3925`@0.40, `04 EB 992`@0.29).

**Yaklaşım.** Tabanı **emit anında da** uygula: `CameraWorker.Emit`, yayınlamadan önce
`best.OcrConfidence < _options.EmitMinOcrConfidence` ise atla. Birkaç satır. CPU-nötr.

**Test.** Düşük-güven uzlaşının emit edilmediğini doğrula (Bench A/B'de yanlış-pozitif düşüşü).

---

## Fix 3 — Tetik bölgesi ayarı (ölç + öner)

**Sorun.** Tek-kare uzak/küçük plakalar hataların kaynağı. `MinPlateWidthRatio=0` (kapalı).

**Yaklaşım.** `Anpr.Bench`e plaka-genişlik-oranı (bbox genişliği / kare genişliği) **dağılım analizi**
ekle; live1h'de yakın (bariyerdeki) vs uzak plakaların oran dağılımını çıkar → güvenli bir eşik öner.
`--minwidth` ile A/B: doğruluk + yanlış-pozitif + gecikme. Ölçüm desteklerse konservatif bir
**varsayılan** konur; aksi halde config'te kalır (geometriye bağlı). Geometri farkı nedeniyle nihai
ayar hedef sahada yapılır.

**Değişiklik.** Yeni Bench modu `widthstats <dataset>` (oran histogramı). `MinPlateWidthRatio`
varsayılanı ölçümle belirlenir (muhtemelen ~0.03).

---

## Ölçüm & doğrulama düzeneği

1. **Ground truth:** live1h için tek-tek görsel doğruladığım gerçek-değerlerden `dataset/live1h/labels.json`
   oluştur (kalıcı referans; ileride her değişiklik aynı sette ölçülür).
2. **Önce/sonra A/B (`Anpr.Bench evaluate`):** her fix ayrı ve birlikte, live1h etiketli sette:
   plaka doğruluğu, **ayrı emit (çift) sayısı**, yanlış-pozitif, kaçan, gecikme.
3. **Regresyon kontrolü:** temiz `dataset/s1`'de doğruluk düşmemeli.
4. Tüm birim testleri geçer (mevcut 42 + yeni).

## Çıktılar (deliverables)

- Önce/sonra A/B metrik tabloları (live1h).
- `docs/` altında kısa iyileştirme raporu (bulgular + önce/sonra + kalan riskler).
- README güncellemesi (yeni davranış + ölçülen kazanç + il-birleştirme ödünleşimi notu).
- Kalıcı `dataset/live1h/labels.json`.

## Dokunulacak dosyalar

- `Anpr.Core/Confirmation/PlateConfirmer.cs` — `IdentityKey` (Fix 1).
- `Anpr.Service/Workers/CameraWorker.cs` — emit tabanı (Fix 2).
- `Anpr.Bench/Program.cs` — `widthstats` modu + A/B (Fix 3, ölçüm).
- `Anpr.Service/Configuration/AnprServiceOptions.cs` + appsettings — `MinPlateWidthRatio` varsayılanı (ölçümle).
- `Anpr.Tests/PlateConfirmerTests.cs` — il-birleştirme testi.
- `README.md`, `docs/…rapor.md`, `dataset/live1h/labels.json`.

## Başarı ölçütü

- live1h A/B'de: çift/yanlış-il emitler belirgin azalır, yanlış-pozitif düşer, plaka doğruluğu artar.
- temiz s1'de regresyon yok. CPU (ms/kare) artmaz.
- Kazanç kanıtlanan davranış varsayılan; kanıtlanmayan config'te kalır.
