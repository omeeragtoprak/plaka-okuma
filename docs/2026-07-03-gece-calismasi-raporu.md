# Gece Çalışması Raporu (2026-07-02 → 07-03) — Kamera-Parity Derinleştirme

Gece boyunca otonom test/geliştirme (kural: kamera ayarlarına dokunmadan, yalnız kendi yazılımımız;
TR plaka + zayıf CPU). Tüm sayılar ölçüm; n küçükken Wilson %95 GA ile.

## 1. DÜZELTME: "Gürültü" aslında gürültü değilmiş (görsel kanıt)

Fazla-emitlerin kırpıntılarını ÖNCE emit anından almıştım → boş yol görünüyordu (yanlış an: emit,
araç geçtikten ~3 sn sonra tetikleniyor). **Son-görülme anından** alınca (bench EMIT logu düzeltildi):

> **"Fazla" emitlerin neredeyse tamamı GERÇEK araç + GERÇEK plaka.** Park halindeki / şerit-dışı /
> karşı-yön araçları veya bariyerdeki aracın ikinci-okuma varyantları. Halüsinasyon ≈ 0 (bu pencerede).

Sonuçlar:
- Kamera ANPR **şerit/tetik-kapsamlı**; biz **tüm kareyi** okuyoruz → kamera-referanslı "fazla" metriği
  yanlış-pozitif DEĞİL, kapsam farkı. Bariyer entegrasyonunda kapsam daraltma (MinPlateWidthRatio/ROI)
  bir doğruluk düzeltmesi değil, spec kararıdır.
- Gerçek hata sınıfları: (a) **çift-emit varyantları** — bariyerde bekleyen aracın 8 sn'lik dedup
  penceresi dolunca değişik okumayla yeniden emit olması (`25AFB52`⊂`25AFB525`, `75E5131`≈`25EG131`);
  (b) dikey-kutu okumaları (ar<0.9; gerçek plaka en-boyları bu pencerede 0.96-1.98) — aday filtre,
  veri birikince eşiklenecek.

## 2. INT8 quantization (zayıf-CPU hız kaldıracı) — ÖLÇÜLDÜ

- Tam dinamik INT8: C#'ta **ÇALIŞMAZ** — ORT 1.20 CPU'da `ConvInteger` implementasyonu yok (0 emit).
- **MatMul-only INT8 (`cct_xs_v2_global_mm8.onnx`): doğruluk FP32 ile AYNI (birebir 21/26), ama bu
  Mac'te hız kazancı YOK (35.6 vs 35.5 ms/kare).** ARM'de FP32 zaten hızlı; kazanç ancak hedef x86'da
  olabilir (VNNI yoksa TERSİNE yavaşlayabilir — literatür) → **hedef PC'de ölçülmeden kullanılmaz.**
- Statik QDQ (kalibrasyonlu) denenmedi; runbook'ta yol olarak duruyor.

## 3. Model boyutu deneyleri @fps=2 (kamera-referans, n=26)

| Config | Birebir | Kaçan | Not |
|---|---|---|---|
| **xs_v2 + yolo-384 (mevcut)** | **21/26 (%81)** | 3 | en iyi |
| büyük OCR (s_v2) | 20/26 | 3 | fark GA içinde, kazanç yok |
| yolo-256 | 14/26 (%54) | 12 | **ELENDİ** (hız +%37 ama recall çöküyor) |
| yolo-512 | 16/26 (%62) | 6 | daha önce elendi |
| minwidth=0.04 | 20/26 | 5 | gürültü −%37 ama 2 gerçek araç kaçıyor → varsayılan 0 |

Latency (Mac, 4500 kare): FP32 35.5 · mm8 35.6 · yolo256 22.5 ms/kare.

## 4. Dedup penceresi A/B — HİPOTEZ YANLIŞLANDI

Hipotez "bekleme >8s → iz süresi doluyor → varyant çift-emit" idi. Ölçüm: dedup=20 ve 30, baseline(8)
ile BİREBİR AYNI (birebir 21, kaçan 3, fazla 17) → mekanizma pencere değil. **Gerçek kök neden:**
minConfirm=2, aracın UZAKTAN gelen 2 kırpık/dandik okumasıyla İZİ ERKEN emit ediyor; yakın-mesafe iyi
okumalar emit'ten SONRA geliyor ve artık işe yaramıyor (kamera tetik çizgisinde yakından okuyor — exact
farkının ana kaynağı bu). → Çözüm adayı: **güven-kapılı erken emit** (aşağıda, ölçümlü).

**Güven-kapılı erken emit (EarlyEmitMinOcr) de ölçüldü → BU SETTE ETKİSİZ** (0.75 ve 0.85, baseline ile
birebir aynı: 21/3/17). Yani varyant çift-emitler DÜŞÜK-güvenli erken okumalar değil; **yüksek-güvenli
tutarlı yanlış-okumalar** ve ana izden ÇOK SONRA geliyorlar. Rafine hipotez: bariyer bekleme süresi
(30-60 sn) sonrası **arka-plaka** okumaları (giriş kamerası önce ön plakayı, araç geçerken arka plakayı
görür; dedup 8-30 sn dwell'i kapsamıyor; kamera şerit-tetikli olduğundan çift-loglamıyor). Test: dedup=90.
Mekanizma yine de sağlam olduğu için EarlyEmitMinOcr kodda kaldı (varsayılan 0=KAPALI, 47/47 test).

**Zaman-çizelgesi gözlemi (EMIT logları) kök nedeni buldu:** `25AFB52` (sec 315.6, x=1811) ile
`25AFB525` (sec 316.0, x=2260) **0.4 sn arayla farklı konumlarda** — dwell/arka-plaka değil; ya kare
içinde hızla ilerleyen aynı plaka İZ BÖLÜNMESİ ya da kuyrukta ardışık-plakalı filo araçları. İz bölünmesi
mekanizması bulundu ve düzeltildi: fuzzy eşleşme yalnız izin Best.Text'ine bakıyordu; Best geçiş sırasında
başka varyanta kayınca (drift) aynı araç ikinci ize bölünüp çift emit üretiyordu. **Düzeltme: iz, gördüğü
TÜM varyantları tutar; eşleşme herhangi birine yapılır** (birim test: drift senaryosu; 48/48).
Ölçüm: fuzzy=1+varyant-seti → fazla 17→16, exact aynı (21); fuzzy=2+varyant-seti → fazla 12 ama exact
21→19 (over-merge) → **karar: fuzzy=1 + varyant-seti varsayılan.**

**Kesin mekanizma (--trace ile gözlendi):** hızlı araç 3 karede 3 farklı okuma verdi:
`25AFB625`(izA) → `25AFB52`(A'ya lev2 → AYRI iz B) → `25AFB525`(hem A'ya hem B'ye lev1 = KÖPRÜ).
Eski kod köprü okumayı yalnız A'ya bağlıyordu; B tek başına settle'da ikinci (yanlış) emiti üretiyordu.
**Düzeltme: köprü-birleştirme** — bir okuma birden çok ize uyarsa izler BİRLEŞİR (sayaç+oylama+varyant;
biri yayınlandıysa birleşik iz yayınlanmış sayılır). Slot hizalaması pozisyonel olduğundan 7/8 karakterli
okumaların oylaması doğal toplanır → konsensus doğru plaka. Birim test gerçek vakayı üretiyor (49/49).
**Ölçüm: gürültü 16→15, ölçülen vaka (25AFB52) kayboldu, exact/kaçan AYNI (21/3) = sıfır recall bedeli.**
Gece toplamı: fazla 17→15, hep exact=21.

**Regresyon (live1h, 101 etiketli araç, fuzzy=1):** recall AYNI (86/101 = %85.1), **yanlış emit 46→42**,
toplam emit 153→149, karakter %85.3→%85.5 → düzeltmeler ikinci bağımsız sette de aynı yönde kazandırdı.

### Fazlaların görsel sınıflandırması (yakın-plan kırpıntı, 18 satır tek tek okundu)

| Sınıf | Adet | Örnekler | Ürün anlamı |
|---|---|---|---|
| **Halüsinasyon** | **0** | — | dedektör+gramer sağlam |
| Gerçek, kapsam-dışı (park/kuyruk/çapraz-kamera) | ~7 | `25HH255`(park, iki kamerada), `25AV215`, `25TC051` | ROI/tetik-bölgesi spec kararı |
| Yanlış-okuma çift-emiti (köprüsüz varyant zinciri) | ~6 | `14PFF63`+`74FFF071`(→`34PFF631`), `11IR115`(→`25TC051`) | köprü-birleştirme kısmen çözdü; kalanlar köprü okuması hiç oluşmayan zincirler |
| İl-hanesi tek-emit yanlışı (near-miss) | 2 | `15EG131`, `15AEN189` (2→1 açıda karışıyor; 15 geçerli il → gramer düzeltemez) | oylamaya rağmen tek-yön hata; fine-tune adayı |
| Kaçan-truth'un yanlış okunuşu | 1 | `25AFV922`(→`25ACV952`) | 3 kaçağın 1'inin kökü |

## 5. Fine-tune hattı kuruldu (kamera = bedava etiketleyici)

- Bench `harvest` modu + `scripts/autolabel.py`: yoğun kare → plaka kırpıntıları → kamera poll
  gerçek-değeriyle (±12 sn + edit≤2, belirsizler atılır) otomatik etiket → `dataset/finetune`.
- Gece döngüsü (night2) çalışıyor: her ~16 dk'da 6 dk yakala→hasat→etiketle→kareleri sil.
- Pretrained ağırlıklar indirildi: `models/train/cct_xs_v2_global.keras` (+ mimari yaml) —
  kaynak: github.com/ankandrew/cnn-ocr-lp release `arg-plates`. Runbook: `docs/finetune-runbook.md`.

## 6. Soak — 10 saat canlı, SONUÇ

| Metrik | Değer |
|---|---|
| Uptime / restart | 10 saat 3 dk, **0 restart** (600 örnek) |
| CPU (Mac, fps=2, spinning=true) | ort %13 · medyan %11 · p90 %24 · boşta ~%4.5 |
| Bizim emit | 81 |
| **Gece parity (kamera=70 distinct)** | **birebir 40 (%57)** · 1-fark 1 · kaçan ~29 · bizde-fazla 10 |

Gece recall (%57) gündüzden (%81) belirgin düşük → **kalan en büyük açık gece/IR koşulları** (far
parlaması, karanlık plaka). Çözüm adayları: gece kırpıntılarıyla fine-tune (hasat hattı hazır; gece
trafiği az olduğundan veri günlerle birikir) ve/veya kameranın gece için ayrı ANPR'ını ISAPI'den tüketme.
**Spinning A/B sonucu** (aynı boş-gece saat dilimleri, 90'ar örnek): spinning=false ort %10.7 / medyan
%9.3 / p90 %19.9 — spinning=true ort %11.6 / medyan %9.4 / p90 %25.3. **Bu makinede anlamlı fark yok**
(IdleFps=1'de motor zaten her saniye gerçek iş yapıyor; spin arası boşluk küçük). Karar: varsayılan
`OnnxAllowSpinning=true` kalır; zayıf 2-çekirdek hedef PC'de yeniden ölçülecek (orada busy-wait görece
daha pahalı olabilir).

## 7. Sabah parity ölçümleri (taze, tüm düzeltmelerle)

- **07:03-07:20 (boş otopark):** kamera 0 — biz 0. 15 dk boş sahnede sıfır emit = **negatif kontrol ✓**
  (halüsinasyon-yok bulgusunu bağımsız doğrular).
- **08:35-08:51 (sabah yoğunluğu, n=22):** birebir **14/22 (%64**, GA %43-80), kaçan 7, fazla 5.
  Kaçanların 3'ü her iki kamerada aynı (25ACV952 — dün de kaçtı, 25AEC879, 25EB105) → alçak sabah
  güneşi/parlama + tekrarlayan zor plakalar. Kareler silinmeden önce hasat edildi (aşağıda).

**Koşula-bağlı parity tablosu (kamera=referans, birebir):**

| Koşul | Birebir | n |
|---|---|---|
| Öğleden sonra (16:10) | **%81** | 26 |
| Sabah alçak güneş (08:35) | %64 | 22 |
| Gece/IR (10 saat) | %57 | 70 |

Kameranın kalan avantajı **donanımsal/koşulsal** (plakaya-optimize pozlama). Yazılım tarafında ana
kaldıraç: koşul-özel verilerle fine-tune (hasat hattı) + ROI/tetik-bölgesi hizalaması.

## 8. Fine-tune: VERİ → EĞİTİM → C# KANITI (gündüz devamı, 2026-07-03)

Gündüz hasat döngüsü 16:45'e kadar çalıştı: **1071 etiketli kırpıntı / 96 benzersiz plaka** (kamera =
bedava etiketleyici; belirsizler atıldı). 30-epoch fine-tune (Mac CPU, dakikalar) + TF-backend export →
**`models/cct_xs_v2_erz.onnx`**. Ölçümler (hepsi aynı gecikmede, 27.6 ms/kare):

| Ölçüm | Orijinal | Fine-tune |
|---|---|---|
| val kırpıntı-exact (python, n=188) | %67.0 | **%75.0** |
| diag4 kamera-referans birebir (C#) | 21/26 (%81) | **22/26 (%85)** |
| live1h recall (C#, 101 araç) | 86/101 | **87/101** |
| live1h yanlış-emit | 42 | **31 (−%26)** |
| live1h karakter benzerliği | %85.5 | **%88.8** |

**GECE A/B (2026-07-04 sabahı, gece hasadı 125 kırpıntı/14 plaka):** tümü %75.2→**%84.0**;
eğitimde-görülmemiş plakalar (88 kırpıntı/7 plaka, sızıntısız) %83.0→**%86.4**. **Her koşulda kazanç,
gerileme yok → `OcrModelPath` varsayılanı `models/cct_xs_v2_erz.onnx` yapıldı** (smoke test ✓; geri
dönüş = appsettings'te tek satır, eski model dosyada). Ayrıntı: `docs/finetune-runbook.md`.

## 8b. Yeni varsayılanın canlı doğrulaması (2026-07-04 akşamı, 4 saat soak)

erz modeli CANLI serviste: **0 restart, birebir 8/9 (%89)** (zaman-filtreli kamera-referans; tek "kaçan"
kameranın kendi çift-logladığı varyant `34PJ0170/34PJ070` → fiilen 8/8 gerçek araç). Ölçüm dersi:
poll'un ilk son-20 çekimi T0 öncesi kayıtlar içerir → karşılaştırmada zaman filtresi ŞART (filtresiz
%38 gibi sahte-düşük çıkıyordu). `scripts/regression.sh` eklendi: her model/config değişikliği için
tek-komut batarya (birim test + diag4 + live1h + val/gece kırpıntı).

## 9. Disk notu (kullanıcı kararı)

`dataset/` altında eski büyük kareler duruyor (silinMEdi — kural gereği): live1h 11G · diag4 8.4G ·
s1 3.9G · s2 3.8G. diag4 (kamera-etiketli referans) ve live1h/s1 (etiketli regresyon setleri)
değerli; s2 en az kritik. Yer gerekirse hangilerinin silineceği kullanıcı kararı.

## 10. Eklenen araçlar/ayarlar (hepsi ölçüm-kapılı)

- `OnnxAllowSpinning` (Core+Service+config): boşta CPU kaldıracı; soak sonrası A/B ile karar.
- Bench: `harvest`, `--dedup`, EMIT detay satırları (son-görülme anı + kutu).
- `scripts/camera_compare.py`: `--det/--minwidth/--dedup/--poll-dir/--frames-root` geçişleri.
- Derleme temiz, 45/45 test yeşil (her değişiklik sonrası).
