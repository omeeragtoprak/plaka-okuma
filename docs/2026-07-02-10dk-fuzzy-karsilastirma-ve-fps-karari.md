# Otopark Bariyeri ANPR — Kamera Yerine Kendi Model: FINAL Rapor

Kapsam: 10 dakikalik tek test (kamera referans). Sonuclar **gosterge** niteligindedir; ornek kucuktur (~22 arac). Genel guven: **ORTA**. Recall (kacan arac) onceliklidir; asagida buna gore degerlendirildi.

---

## 1. Guncel performans (10 dk, fuzzy=1) — kamera karsilastirmasi

Kamera 10 dakikada **22 farkli plaka** gordu (referans kabul edildi).

| Metrik | Onceki tur | Fuzzy=1 (simdi) | Degisim |
|---|---|---|---|
| Birebir dogru (22'nin) | ~49% | **13/22 = %59** | +10 puan |
| 1-karakter yakin | — | 2/22 = %9 | — |
| **Kacan (sadece kamera)** | ~43% | **7/22 = %32** | −11 puan (iyilesme) |
| Sadece biz (kamerada yok) | — | 5 | — |
| Ikisinin de gordugu araclarda dogruluk | — | 13/15 = %87 | — |

**Fuzzy=1 ne degistirdi:** Birebir eslesme %49 → %59'a cikti, kacan %43 → %32'ye dustu. Yon dogru: fuzzy eslesme (bosluk/case/1-karakter toleransi) hem dogrulugu artirdi hem kacani azaltti.

**Onemli uyari — sayilar KESIN degil (n kucuk):**
- Ortusen araclarda dogruluk 13/15 = %87 → Wilson %95 GA **~[%62, %96]**. "%87" tek basina yaniltici; alt sinir %62'ye inebilir.
- Recall (arac yakalama, yakinlar dahil) 15/22 = %68 → GA **~[%47, %84]**.
- Salt birebir recall 13/22 = %59.
Yani gercek deger genis bir bantta; "yuksek dogruluk" iddiasi n=15 ile yumusatilmali.

**Giris/Cikis kirilimi (asil zayiflik cikista):**

| | Kacan (sadece kamera) | Sadece biz |
|---|---|---|
| GIRIS | 2 (25AEC871, 25AG010) | 3 (25A5411, 25ACK852, 25AFH146) |
| CIKIS | 5 (25ABG057, 25ADS034, 25AFH148, 25AG010, 25EJ179) | 2 (19MM107, 34FLR591) |

- Kacan 7 plakanin 5'i **CIKIS** tarafinda → cikis kamerasi/gecis akisi daha zorlu.
- 1-karakter yakinlar: 25ABD057/25ABG057 ve 34PLR531/34PLK531 → muhtemelen ayni arac, ancak zamansal teyit yok (bkz. Bolum 4, saat kaymasi).
- **Duzeltme (onceki iddia yanlisti):** "Sadece biz" plakalari (25A5411, 19MM107, 34FLR591) **bozuk format DEGIL** — ucu de format-gecerli TR plakasi. Yanlis-pozitif olabilirler, ama gerekce "bozuk format" degil; ancak arac-yoklugu/il-tutarsizligi ile aciklanabilir.

---

## 2. fps → recall iliskisi (olculen)

Sabit: minConfirm=2, minocr=0.45, emitfloor=0.45, fuzzy=1, merge=on. Kaynak yakalama sabit **5 fps**, 3000 kare (10 dk). `step = round(5/fps)`.

| fps | step | Islenen kare | Sure | Distinct emit | Recall (14 uzerinden) | Gurultu | Precision |
|---|---|---|---|---|---|---|---|
| 1 | 5 | 600 | 20.9s | 14 | 8/14 = %57 | 6 | %57 |
| 2 | 2 | 1500 | 51.6s | 20 | 9/14 = %64 | 11 | %45 |
| 3 | 2 | 1500 | 55.4s | 20 | 9/14 = %64 (fps=2 ile **BIREBIR AYNI**) | 11 | %45 |
| 5 | 1 | 3000 | 104.7s | 29 | 8/14 = %57 | 21 | %28 |

**Saglam (guvenilir) bulgular:**
1. **fps=2 == fps=3.** `round(5/3)=2` oldugundan ikisi ayni kareleri isler; fps'i 3'e cikarmak bosuna (repoda dogrulandi).
2. **Gurultu fps ile SERT ve monoton artiyor:** 6 → 11 → 21. Precision cokuyor: %57 → %45 → %28.
3. **fps'i artirmak kacanlari kurtarmiyor.** fps=5'te bile kacan 6 plaka (21GB050, 25AEC871, 25AFB518, 25EJ175, 25EJ179, 66ACG230) var. Bunlar **kaynak-5fps yakalama + OCR limiti**, islem fps'i degil. Kaynak zaten 5 fps oldugu icin islem fps'ini yukseltmenin recall'a katkisi yok, gurultuye katkisi buyuk.

**Saglam OLMAYAN bulgu (durust olmali):**
- "Recall fps=2'de tepe yapiyor" iddiasi **zayif.** fps=2 (9/14) ile fps=5 (8/14) farki **tek plaka** (n=14 icinde gurultu seviyesinde). Ustelik repodaki eski diag dokumaninda fps=5 en iyi recall'i (7/9) bulmustu — **ters yonde.** Yani "tepe fps=2" istatistiksel dayanaksiz ve dataset-bagimli. **Soylenebilecek tek sey:** recall fps ile monoton artmiyor; artan fps net fayda getirmezken gurultuyu kesin artiriyor.

---

## 3. ZAYIF PC icin fps/CPU karari (EN ONEMLI BOLUM)

### Olculen temel (Mac, dev makine)
- CPU **doygun degil:** ort %28 (0.28 cekirdek), tepe %68.5, medyan %26. Cok cekirdekli kutuda genis pay var.
- Islenen 2.37 kare/s, alinan 7.97 kare/s → islenen/alinan %30. **Adaptif throttle calisiyor** (bosta dusuk fps, arac gelince patlama). Olculen 2.37 fps kapasite tavani DEGIL, talep sinirli.
- Kare basi maliyet: **~118 ms** (tek-cekirdek esdegeri, ekstrapolasyon icin muhafazakar taban). Mac duvar-saati 2 ONNX thread ile ~60-80 ms.

### Zayif x86 ofis PC'ye olcekleme (TAHMIN — olculmedi)
AVX'siz ONNX FP32 cikarim ~3-6x yavas (tipik x4-5):

| Katsayi | CPU-is/kare | Surdurulebilir toplam fps (2 kam, 2-cekirdek, %50-65 pay) |
|---|---|---|
| x4 | ~472 ms | ~2.1-2.8 fps (~1.0-1.4/kam) |
| x5 | ~590 ms | ~1.7-2.2 fps (~0.85-1.1/kam) |

Kutuyu %100 pegleyerek ~3.4-4.2 fps cikilabilir ama **surdurulemez** (ffmpeg kare duser, gecikme sisar). **Guvenli surdurulebilir hedef: ~2 kare/s toplam (~1/kam aktifken).**

### NET ONERI

> **IdleFps = 1, Fps (aktif) = 2.** MinConfirmations=2, CharacterVoting=true, ActiveHoldSeconds=4, OnnxIntraOpThreads=2 korunsun.
> **Simdiki `Fps=4` zayif PC icin FAZLA — 4 → 2'ye dusurulmeli.**

**Gerekce:**
1. **Recall kaybi yok:** Bariyerli gecişte arac 3-8 sn durur; aktif 2 fps'te bile 6-16 kare gorunur → MinConfirmations=2 + CharacterVoting icin fazlasiyla yeterli. Plaka fps yuzunden kacmaz (kacanlar yakalama/OCR limiti, Bolum 2).
2. **CPU guvenli:** Talep = 2 kam × 2 fps = 4 kare/s; zayif PC'de ~1.9-2.4 core-s/s → 2-cekirdegi doldurur ama iki kamera ayni anda nadiren aktif oldugu icin kisa patlamalar surdurulebilir. Fps=4 ise talebi 8 kare/s'ye cikarir (~4 core-s/s) → 2-cekirdegi pegler, kare dusme + gecikme.
3. **Gurultu az:** Bolum 2 olcumu net — dusuk fps = dusuk gurultu, yuksek precision. Yanlis-pozitif bariyer acilmasi riski azalir.
4. **Adaptif tasarim korunur:** IdleFps=1 ile bosta CPU serbest; arac gelisini ≤1 sn'de yakalar, aktife ziplar.

**Guvenli sinir / adaptif mantik:**
- Alt sinir: CPU cok kisitliysa (tek-cift zayif cekirdek, gecikme sisiyorsa) **fps=1**'e dus — 1 gercek plaka kaybi pahasina en iyi precision (%57) ve yarisi kadar yuk. fps=1 hala 3-8 sn'lik gecişte 3-8 kare gorur.
- Ust sinir: Hedef PC **4-cekirdek veya AVX2** ise fps=3'e cikilabilir (ama fps=3 kareleri fps=2 ile ayni → pratikte fps=2 yeterli; asil ust sinir donanim varsa fps=4/5 denemesi, ONLY olctukten sonra).
- **Karar donanimda olcume baglanmali:** Asagidaki katsayilar tahmin. Hedef PC'de `Anpr.Bench evaluate` ile gercek ms/kare olcup config'i kesinlestir.
- **INT8 deploy edilmemis** (config FP32 `cct_xs_v2`). INT8 quantize edilirse zayif CPU'da ciddi hizlanma potansiyeli var — bu ayri bir kazanim kaldiraci, degerlendirilmeli.

---

## 4. Dogrulama uyarilari + guven

**Genel guven: ORTA.** Nitel sonuclar savunulabilir (asil acik recall, ozellikle cikis; fuzzy=1 makul; zayif CPU icin fps=2/IdleFps=1 guvenli). Ancak yuzdeler kucuk n'de asiri kesin ve iki premis dogrulanmadi.

**Ciddi uyarilar:**
1. **Ornek cok kucuk (n≈15-22).** Tum yuzdeleri GA ile ve "gosterge" olarak oku: dogruluk %87 → [%62, %96], recall %68 → [%47, %84]. Tek testten kesin karar cikarma; ideali 2-3 gunluk log.
2. **Karsilastirma harness'i repoda YOK.** ISAPI pull, pagination, zaman-penceresi ve edit-distance mantigi kaynaktan teyit edilemedi. Q1-Q2 dogrulanamadi. Tekrar-uretilebilirlik icin harness'i repoya ekle, T0/T1 ve cekilen sayfa sayisini logla.
3. **Saat kaymasi:** Plaka-tabanli eslesme saatten etkilenmez (dogru), AMA pencere SECIMI etkilenir. Kamera captureTime ~+60s ileri; [T0,T1] kenarlarinda ~%10 kayma sahte "kacan"/"sadece-biz" uretebilir. Filtreye +60s eklendi mi dogrula ya da pencereyi ±90s padle.
4. **Pagination kirilgan:** "<20 kayit = son sayfa" sezgisi hatali olabilir; MORE/OK bayragi kullanilmali. Eksik sayfa kamerayi eksiltir → **kacan 7'den fazla olabilir, recall acigi raporlanandan kotu olabilir.** totalMatches vs cekilen log ile teyit sart.
5. **Zayif-PC katsayisi (x3-6) TAHMIN.** Son 10 yilin cogu ofis PC'sinde AVX2 var → "AVX'siz" en-kotu-durum; ama Apple Silicon baz cizgisi cok guclu oldugundan x4-5 orta degeri iyimser de olabilir. **Hedefte olcmeden fps=2 rakamsal olarak kesinlestirilmemeli** (fps=2 onerisi yine de guvenli tarafta).

**Ek notlar:**
- 1-karakter farklar (25ABD057/25ABG057, 34PLR531/34PLK531; ayrica 25AFH146/25AFH148 capraz-kapi) "ayni arac" varsayimidir; saat kaymasi nedeniyle zamansal teyit yok → ortusme/recall bir miktar sismis olabilir.
- Latans metodolojisi: 35 ms/kare (Mac duvar-saat, yogun bench) vs 118 ms/kare (tek-cekirdek esdegeri) → 3.4x fark, farkli olcum. Ekstrapolasyonda 118 ms kullanildi (muhafazakar, gecerli).
- Uc analiz farkli pencerelerden (karsilastirma 22, fps 14, eski doc 9/37 plaka) → tek tutarli hikaye gibi capraz-atif YAPMA.

**Ic tutarlilik:** Karsilastirma aritmetigi tutarli (7+6=13 exact, 3+2=5 sadece-biz, 2+5=7 kacan, 10+12=22). FPS kare sayilari (600/1500/3000 @5fps) uyumlu. Bu yonleriyle veri saglikli.

---

### Karar ozeti
- **Kullanilabilir mi?** Yon dogru, ama **cikis recall'i (kacan %32) tek basina bariyer icin riskli.** Bariyer, kacan araci fiziksel olarak durduracagi (kart/tetik) icin tolere edilebilir, ancak plaka-tabanli otomatik gecis/ucret yapiyorsan cikis tarafi iyilestirilmeden tam guvenilir degil.
- **fps karari:** `Fps=4 → 2`, `IdleFps=1`. Zayif CPU'da guvenli, recall kaybetmez, gurultu/precision iyilesir.
- **Sonraki adim:** (1) hedef PC'de gercek ms/kare olc, (2) harness'i repoya ekle + pencere/pagination'i logla, (3) cikis kamerasi acisini/OCR esigini incele, (4) 2-3 gunluk daha genis test.