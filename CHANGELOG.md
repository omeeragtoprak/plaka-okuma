# Değişiklik günlüğü — `Anpr.Sdk`

Bu dosya **tüketiciye giden yüzeyi** anlatır (NuGet paketi: `Anpr.Sdk`).
Depo içi ölçüm/denetim işleri buraya girmez — onlar `AGENTS.md` §P.4'tedir.
Sürümleme [semver](https://semver.org/lang/tr/): eklemeli değişiklik → MINOR.

---

## [2.0.0] — 2026-10-13

> **Lisans: MIT.** Paket nuget.org'da halka açıktır; `LICENSE` ve `THIRD-PARTY-NOTICES.md` paketle gelir.
> Yayın `nuget-v2.0.0` etiketiyle yapılır (`publish.yml`, Trusted Publishing); sıradan `v*` etiketleri hiçbir
> şey yayınlamaz. Yayından önce iki kapı koşar: testler ve `scripts/audit_package_kvkk.py` (pakete girecek
> metinlerde gerçek plaka olmamalı; yasaklı dosya olmamalı).
>
> ⚠️ **Pakette artık rektifikasyon (WPOD) modeli yok.** `wpod_lpd.onnx` GPL-3.0 lisanslı bir depodan türemiş
> ağırlıklardır ve MIT pakete konamaz (1.0.x paketlerinde vardı). `EnhanceRectify` aşaması dosya yoksa
> kendiliğinden kapalı kalır. Bu bir doğruluk bedelidir (aşamanın ölçülen katkısı: oylama sonrası doğru okuma
> açılı kümede 61 → 67, temiz kümede 341 → 359); modeli kendi lisans değerlendirmenizle `WpodModelPath` ile
> verebilirsiniz.

### Düşük bellek ve işlemci (2026-09-30 → 10-13) — bu sürümün asıl işi

Ölçüm: iki kamera, 4 MP HEVC 25 fps, canlı, donanım çözme açık (Apple M4). İki ayrı ölçümün (10 dk ve 7 dk)
aralığı; fark trafiğe bağlı.

| | 2.0.0 öncesi | 2.0.0 |
|---|---|---|
| ffmpeg (iki süreç) belleği | 498 MB | **110–115 MB** |
| uygulama süreci belleği (ortalama) | 349 MB | **270–343 MB** |
| toplam bellek (ortalama) | 846 MB | **380–459 MB** (tepe 506–584) |

Paket boş bir konsol projesine kurulup iki kamerayla çalıştırıldığında (panel yok, kanıt kapalı) süreç 229 MB tuttu.

Tek kamera, aynı kayıt iki sürümden: uygulama işlemcisi tek çekirdeğin %23–26'sı → **%19**; ffmpeg %10 → **%3**;
uygulama belleği ortalama 205–216 MB → **~140 MB** (tepe 340–360 → ~155 MB).

- **YUV taşıma (`Ffmpeg.OutputFormat = Y4m`, yeni varsayılan):** ffmpeg çözdüğü kareyi renk dönüşümü yapmadan
  YUV 4:2:0 (Y4M) yazar (kare 16,3 MB → 6,1 MB). Motor dedektör girdisini doğrudan bu kareden örnekler; yalnız plaka
  kırpıntısı RGB'ye çevrilir. Doğruluk-nötrlük üç yoldan sınandı: 80 dakikalık kayıt iki yoldan kare kare işlendi
  (geçerli okuma 722 ↔ 719; farklar, yalnız renk yuvarlaması değişen ara kolun farklarıyla aynı büyüklükte), etiketli
  kümede kesinlik/duyarlılık ±1 örnek, olay listeleri aynı-ayar tekrar koşusunun gürültüsü içinde.
  `Bmp` ve `Mjpeg` seçilebilir kalır.
- **Model paylaşımı (`AnprOptions.ShareOnnxSessions`, varsayılan açık):** aynı model dosyasını aynı ayarla kullanan
  kameralar tek ONNX oturumunu paylaşır. Önce her kamera dört modeli ayrı yüklüyordu (toplam ~135 MB). İki kamerada
  ~85 MB kazanç; eklenen her kamera modeller için ~2 MB ister. Sonuçlar paylaşımsız yolla aynıdır (eşzamanlı
  kullanım dâhil testli).
- **Kare kiralama (`IFrameSource.TryLeaseLatestFrame` / `ReturnFrame`):** işlenen karenin tamponu, kimse saklamadıysa
  (kanıt görüntüsü, panel) sıradaki kare için yeniden kullanılır. Önce işlenen her kare çöpe gidiyordu. Canlıda
  karelerin %99'u yeniden kullanıldı; 2. nesil çöp toplama 10 dakikada 284 → 3.
- **Çözücü iş parçacığı sınırı (`Ffmpeg.DecoderThreads`, 0 = en çok 2):** ffmpeg varsayılanı çekirdek başına iş
  parçacığı ve kare tamponu açar. Yazılım çözmede ffmpeg belleği 243 MB → ~100 MB, işlemci aynı.
- **Ekran kartında kare düşürme (`Ffmpeg.GpuFrameDrop`, varsayılan açık):** donanım çözmede `fps` süzgeci kare
  ekran kartındayken uygulanır; saniyede 25 yerine yalnız işlenecek kareler indirilir. Desteklenmezse sırayla düz
  donanım çözmeye, sonra yazılıma düşer (her basamak iki karesiz denemeden sonra).
- **Hareket tetikli zamanlama (`MotionGate = On`, isteğe bağlı; varsayılan `Shadow`):** kapı kaynağın her karesine
  bakar; hareket varken (ve `MotionGateHoldSeconds` kadar sonrasında) her kare işlenir, sahne boşken yalnız
  `MotionGateForcedIntervalSeconds`'ta bir bakılır. Kapı artık bölgenin sınır kutusuna değil **çokgenine** bakar ve
  tek karelik hareketi (kameranın anahtar-kare darbesi) gerçek hareket saymaz. Ölçüm: sakin kamerada işlenen kare
  %40 → %25, uygulama işlemcisi %19 → %14; aynı kayıtlarda yayınlanan araç 49/54 → 52/54. Yalnız gündüz kaydıyla
  doğrulandığı için varsayılan değildir. Yeni sayaçlar: `gateMotionFrames`, `gateHoldFrames`, `gateActiveFrames`,
  `gateForcedHits`.
- **`Anpr.Core`: kopyasız kare görünümü** — `AnprEngine.Recognize(IFrameView)` / `RecognizeWithCrops(IFrameView)`,
  `Yuv420FrameView`, `SkBitmapFrameView`. Mevcut `byte[]` / `SKBitmap` imzaları aynen çalışır (bit düzeyinde aynı çıktı).
- **`AnprOptions.OnnxCpuMemArena`, `OnnxMemoryPattern`** (varsayılan açık): teşhis için kapatılabilir; ölçümde
  kapatmak bellek kazandırmadı.
- **`/health` → `process`:** `gcHeapMb`, `gcCommittedMb`, `serverGc` (bellek "yönetilen mi, yerel mi" ayrımı).

**Davranış değişiklikleri (yükseltirken bakın):**

- Varsayılan kare biçimi `Bmp` → `Y4m`: `VideoFrame.Format` artık `Yuv420p` olabilir. `VideoFrame.Data`'yı kendisi
  okuyan kod `Format`'a bakmalıdır; `ToRgbaBitmap()` / `ToJpeg()` kullananlar etkilenmez.
- `CameraWorker.LastFrame` / `LastFrameJpeg`: son işlenen kare kiralık olduğundan, tam o anda geri verilmişse `null`
  dönebilir (bir sonraki çağrıda gelir). Geri verilmiş bir kareye erişmek `InvalidOperationException` fırlatır
  (sessizce yanlış piksel yerine).
- `IFrameSource`'a dört üye eklendi (`LatestSequence`, `TryInspectLatest`, `TryLeaseLatestFrame`, `ReturnFrame`);
  hepsinin varsayılan uygulaması vardır — kendi kaynağını yazan tüketici değişiklik yapmadan derlenir.
- ASP.NET Core içinde barındıranlar için öneri: `<ServerGarbageCollection>false</ServerGarbageCollection>`
  (ad `‑ion` ile biter; `ServerGarbageCollector` web projelerinde etkisiz kalır).

### Düşük donanım — Faz 2 (2026-09-01)

> Hedef: **doğruluk düşmeden** zayıf, CPU-only bir Windows PC'de çalışmak. Sahadaki gerçek testte
> %94 CPU görülmüştü. Bu bölümdeki her kalem ya **doğruluğa yapısal olarak nötr** (aynı pikseller,
> aynı model girdisi) ya da **varsayılan gölge/kapalı** gelir.

- **Ham kare yolu (`Ffmpeg.OutputFormat = Bmp`; o tarihte varsayılandı, 2.0.0'da yerini `Y4m` aldı):** ffmpeg artık kareyi MJPEG'e kodlamıyor,
  sıkıştırmasız BMP/BGRA yazıyor; C# tarafında kare başına 4 MP JPEG çözme kalktı (BGRA → RGBA tek
  SIMD kopya). Ölçüm (4 MP HEVC 25 fps, kamera başına, tek-çekirdek yüzdesi): ffmpeg **%28 → %18**;
  donanım çözmeyle **%10**. Model girdisi eski yolla aynı biçimde (RGBA8888) — doğruluk yolu değişmedi.
  Eski yol `Mjpeg` ile seçilebilir.
- **Donanım video çözme (`Ffmpeg.HwAccel = "auto"`, varsayılan):** ffmpeg'in `-hwaccels` listesinden
  işletim sistemine uygun yöntem seçilir (Windows: `d3d11va` › `dxva2` › `qsv`; macOS: `videotoolbox`).
  Donanım akışı desteklemezse ffmpeg kendi içinde yazılıma düşer; iki ardışık karesiz denemede kaynak da
  kapatır. `"none"` ile kapalı, açık ad da yazılabilir.
- **Hareket kapısı (`MotionGate = Shadow`, varsayılan):** boş sahnede dedektör koşturmamak için ham karede
  seyrek ızgara farkı. **Gölge** modunda karar yalnız SAYILIR (`/health`: `gateEvaluated`, `gateWouldSkip`,
  `gateMissedDetections`, `gateForced`) — **kare atlanmaz**. `On` yapılmadan önce `gateMissedDetections`
  sıfır kalmalı. Emniyet kemeri: aktif izleme süresinde ve her `MotionGateForcedIntervalSeconds`'ta (2 sn)
  kapı ezilir; JPEG karede kapı hep "işle" der.
- **Kare tamponu geri dönüşümü:** boşta üretilen ama tüketilmeyen karelerin 16 MB'lık tamponları yeniden
  kullanılır (alınan kare asla geri dönüştürülmez — kovalar/panel referans tutabilir). Sayaç: `sourceRecycledBuffers`.
- **`AnprOptions.OnnxAllowSpinning` varsayılanı `true → false`:** boşta dönen ONNX iş parçacıkları zayıf PC'de
  CPU göstergesini şişiriyordu. Kare-başı gecikme birkaç ms artabilir; güçlü/yoğun kurulumda `true` denenebilir.
- **`/health` eklemeleri:** `process { cpuSeconds, workingSetMb, gen2Gc, processorCount }`, kamera başına
  `sourceFormat`, kapı sayaçları, `sourceRecycledBuffers`.
- **Panel:** `/api/snapshot` ve `/api/mjpeg` ham karede JPEG'i istek anında üretir — en çok 1280 px ve ≤5 fps
  (4 MP kodlamak zayıf PC'yi panel açıkken yoruyordu).
- **Ölçülüp REDDEDİLENLER (yeniden denenmesin):** gri kare (detektör recall %78 → %69) · `ScaleWidth 1920`
  (OCR tam eşleşme %77 → %71 eğilimi, n küçük; ayar kalıyor, varsayılan 0).
- **Kırıcı (tüketici için):** `IFrameSource.TryGetLatestFrame()` artık `byte[]` değil `VideoFrame` döner;
  `CameraWorker.PeekSourceFrame()` de öyle (`LastFrameJpeg` korunuyor, ham karede kodlar). `IFrameSource`'u
  kendisi uygulayan tüketici yoksa etkilenmez.

### Neden 2.0.0 (MAJOR)

İki pakette de kırıcı değişiklik var; `publish.yml` ikisini aynı sürümle paketler.

- **`Anpr.Core`:** üç public üye kaldırıldı — `PlateConfirmer.ApproachMinSamples`, `PlateConfirmer.ApproachMinSpan`,
  `PlateReading.SizeTrend` (yerlerini `DirectionTracker` aldı).
- **`Anpr.Sdk`:** `IFrameSource.TryGetLatestFrame()` ve `CameraWorker.PeekSourceFrame()` artık `byte[]` (JPEG) değil
  `VideoFrame` döner; `CameraWorker.CropBucket.BestFrameJpeg` alanı kaldırıldı (yerini `BestFrame` aldı).
- **Paket içeriği:** WPOD modeli çıkarıldı (yukarıda).

**Yalnız `AnprRuntime` + `PlateDetected` / `IEventSink` kullanan tüketici kod değiştirmeden derlenir:** `PlateEvent`
ve `AnprServiceOptions` yalnız eklemelidir (kaldırılan üye yok). Kırılma, kare kaynağını ya da
`Anpr.Core` tiplerini doğrudan kullananları ilgilendirir.

### Eklendi — şema v5, v6 ve v7 (hepsi eklemeli; eski tüketici kırılmaz)

- **`PlateEvent.SnapTargetRank` + `SnapTargetOcr` (şema v7)** — snap hedefinin, snap **ÖNCESİ**
  aday listesindeki 1-tabanlı sırası ve hedefe **modelin KENDİ** verdiği güven. Snap yoksa ya da
  hedef ilk-K'da değilse `null`.
  <br>🔑 **Neden gerekti:** snap, aday listesini yeniden yazıp hedefi başa koyar ve hedefe eşit
  olan özgün adayı **eler** (`Candidates[0] == PlateCompact` sözleşmesi). Bu, *"OCR'ın kendisi bu
  düzeltmeyi destekliyor muydu?"* sorusunu olaydan **yanıtlanamaz** kılıyordu — ölçüldü: 536 canlı
  snap olayının 536'sında hedef 1., kaynak 2. sırada. Bir ölçüm boşluğuydu ve tam da en büyük açık
  kalemin (snap zararı) ortasındaydı.
  <br>**Kullanımı:** `OcrConfidence` snap sonrası hâlâ snap ÖNCESİ dizginin **ödünç** skorudur;
  bu alanla **farkı** alınabilir → snap'in modele ne kadar aykırı olduğu ölçülür.
  `SnapTargetRank == null` yüksek riskli kapıda onay eşiği olarak kullanılabilir.
  <br>⚠️ `null`, *"model sıfır olasılık verdi"* değil, *"ilk-K içinde değildi"* demektir.
  <br>⚠️ Geriye dönük doldurulamaz: `SchemaVersion < 7` kayıtlarda alan yoktur.
  <br>ⓘ Aynı değişiklikte whitelist/müdavim snap'i saf bir fonksiyona (`ApplyFrequentSnap`)
  çıkarıldı — o yol emit içinde satır-içiydi ve küratörlü yolun desenini **kopyalıyordu**;
  kırmızı-yeşil denemesi ikinci kopyanın **test edilemediğini** ortaya çıkardı. Davranış aynı.

- **`AnprOptions.AmbiguousTailRecropPad` + `AmbiguousTailMaxGap`** — *hedefli ikinci okuma*,
  **varsayılan `0` = KAPALI**, hiçbir mevcut davranış değişmez. Açıkken: OCR'ın son karakterden
  sonraki slotunda "dolgu mu, bir karakter daha mı" kararı belirsizse kırpıntı bu oranla yeniden
  alınır ve OCR **bir kez** daha koşar. İkinci okuma yalnız **aynı önek + tam 1 karakter uzun +
  en az o kadar güvenli** ise kabul edilir — yani karakter EKLEYEBİLİR, var olanı DEĞİŞTİREMEZ.
  Yeni sayaçlar `AnprEngine.TailRecropAttempts` / `TailRecropAccepted` ("açık ama etkisiz"
  durumunu görünür kılar).
  <br>**Neden:** ölçülen kusur sınıfı — yayınlanan plaka, tanığın plakasının tam öneki ve tam
  1 karakter eksik (**16/9728 = %0,16** [%0,10–%0,27]); 16/16'sı format olarak **geçerli**
  (7–8 karakter kabul edilir, 8'den 1 düşünce geçerli bir 7 kalır) ve 16/16'sı emit eşiğinin
  üstünde → ne doğrulama ne güven eşiği ayırabilir. **Mekanizma:** OCR kırpıntısı dardır
  (`CameraWorker.SaveEvidence` yorumunda 2026-07-29'dan beri yazılı: sağ pay < 0,20 karakter
  olan okuma oranı %21,4) ve son karakter yeterli piksel almıyor. Per-kare OCR **kesmez** —
  uzunluğu 4/4 doğru bulur, son *rakamı* yanılır; kesilme **iz-düzeyi slot ortalamasında**
  doğar (kareler son rakamda anlaşamayınca dolgu simgesi kazanır). Dolgu 0,15'te 4/4 düzelir,
  güven 0,90 → 0,998.
  <br>**Neden küresel değil:** dolguyu herkes için artırmak yansız örneklemde (n=600) ölçüldü ve
  **zararlı** çıktı (0,22'de eşleştirilmiş net −31, p<0,0001). Kesilme trafiğin %0,16'sı olduğundan
  küresel değişimin azami kazancı +0,16 puan, 0,15'te ölçülen zararı −0,50 puan — kazancın ~3 katı. Bedel yalnız belirsiz
  kuyruklu karede ödenir.

- **`PlateEvent.EarlyEmitHeldAtOcr` (şema v5)** — olay `EarlyEmitMinOcr` tarafından tutulduysa
  **ilk tutulma anındaki OCR güveni**; tutulmadıysa `null`.
- **`PlateEvent.EarlyEmitHeldAs` (şema v6)** — tutma anında okunan **METİN**, yani *tutma
  olmasaydı yayınlanacak dizgi*; tutulmadıysa `null`.
  🔑 **v6 neden gerekti — v5 yarım kaldığı için.** Güven artışı *"tutmak işe yaradı mı?"*
  sorusunu **yanıtlamaz**: okuma hiç değişmeden de güven artabilir, ve değişip yine yanlış
  kalabilir. Karşı-olgusal **dizgi** kaydedilmeden takasın kazanç tarafı ölçülemiyordu.
  Karar veren karşılaştırma: **`EarlyEmitHeldAs` ↔ `PlateCompact` ↔ yer-gerçeği**.
- ⚠️ **İkisi de DENETİM İZİDİR, akış kontrolü değil.** Tutma SDK içinde çözülür; olay bir kez
  ve son hâliyle yayınlanır. Tüketici tarafında bir davranış tetiklemeleri beklenmez.
- ⚠️ **Geriye dönük doldurulamazlar.** `SchemaVersion < 5` / `< 6` kayıtlarda alan **yoktur**;
  `null` *"tutulmadı"* DEĞİL **"bilinmiyor"** demektir. Bu ayrımı gözden kaçıran bir analiz
  tutmayı olduğundan **seyrek** gösterir.

### Düzeltildi

- 🔴 **`OcrConfidence`'ın BELGESİ yanlıştı — snap uygulanan olaylarda skor yayınlanan plakaya ait
  DEĞİL** (davranış değişmedi, sözleşme netleşti). Alan *"OKUNAN METNİN güveni — bariyer kararının
  dayandığı değer budur"* diyordu; oysa `ApplyCuratedCorrection` metni değiştirir ve
  `OcrConfidence`'a **dokunmaz**, yani `SnappedFrom != null` olan bir olayda skor **snap ÖNCESİ**
  dizgiye aittir — model yayınlanan plakayı hiç skorlamamıştır. Aynı şekilde `Candidates[0]`
  yayınlanan plakaya yeniden yazılır ve **aynı ödünç güveni** taşır; hedefe eşit olan özgün aday
  **elenir**, dolayısıyla *"OCR bu düzeltmeyi destekliyor muydu?"* sorusu olaydan yanıtlanamaz
  (ölçüldü: 536 snap olayının 536'sında hedef 1., kaynak 2. sırada).
  📊 Snap ürünü olaylarda medyan güven **0,899**, snapsızlarda **0,996** — ama **her iki kümenin
  de %100'ü emit eşiğinin üstünde**, yani eşik bu ayrımı yapmaz.
  ⇒ Eşik/tahsilat kararında `SnappedFrom`'a da bakın. Belge `PlateEvent`, `Anpr.Sdk/README.md` ve
  `docs/entegrasyon-kilavuzu.md`'de düzeltildi; davranış iki testle kilitlendi.

- **Şema sürümü olmayan eski olaylar artık "en yeni" diye okunmuyor.**
  `PlateEvent.SchemaVersion` alanı M5'te eklendi; **ondan önce üretilmiş JSON'da hiç yok** —
  yayınlanmış `v1.0.1` dönemi buna dâhil. Alanın varsayılanı `5` olduğu için böyle bir belge
  sessizce **"en yeni şema"** diye okunuyordu, oysa `LowConfidenceReasons` gibi alanları hiç
  taşımıyor. Tüketicinin `SchemaVersion >= 4` kontrolü bu yüzden **yanlış pozitif** veriyordu —
  yani sürüm alanının önlemesi gereken sessiz kırılmanın ta kendisi.
  · Varsayılan artık **1** (en eski şema): alan yoksa tüketici muhafazakâr dala girer.
  · Üretim yolu sürümü yeni `PlateEvent.CurrentSchemaVersion` sabitiyle **açıkça** yazar.
  · **Etkilenen tüketici:** yalnız `v1.0.1` döneminden arşiv okuyanlar. Kendi canlı arşivimizde
    alansız olay **yok** (8358 olayın tamamında var: v2/v3/v4).
  · **Kendi kodunuzda `new PlateEvent { … }` kurup sürümü atamıyorsanız** artık `5` değil `1`
    alırsınız — üretilen olayı taklit etmek istiyorsanız `CurrentSchemaVersion` yazın.

- Yayınlanan pakette **eksik olan ayar belgesi** geri kondu; XML doc uyarıları (CS1570/CS1574/
  CS1734) artık **hata** — bozuk bir doc yorumu üyeleri sessizce düşürdüğü için.

### Kaldırıldı (KIRICI — yalnız `Anpr.Core`)

- `PlateConfirmer.ApproachMinSamples`, `PlateConfirmer.ApproachMinSpan`,
  `PlateReading.SizeTrend` — yerlerini `DirectionTracker` (`MinSamples`, `MinSpan`, `MatchGate`)
  aldı. `PlateEvent.SizeTrend` **durmaktadır**: olay alanı kaldırılmadı, yalnız Core'daki
  ara tip değişti.

### Eklendi

- **Olay şeması v2 → v5** (hepsi eklemeli; eski tüketiciler bilinmeyen alanları yoksayar):
  | sürüm | alan | ne işe yarar |
  |---|---|---|
  | v2 | `SnappedFrom` | düzeltme uygulandıysa **öncesi**; denetim izinin iki ucu da görünür |
  | v3 | `FrameBox` | tam kareyle **tutarlı** kutu (`Box` kırpıntıya göredir) |
  | v4 | `LowConfidenceReasons` | bayrağın **gerekçesi** — kuyruk değere göre sıralanabilir |
  | v5 | `EarlyEmitHeldAtOcr` | erken-yayın tutulduysa o andaki OCR skoru |
  | — | `CurrentSchemaVersion` | bu SDK'nın ürettiği sürüm (sabit) |

- **Çapraz-kamera ikame kapısı** — karşı kamerada saniyeler içinde görülen, **aynı uzunlukta
  tek karakter** farklı bir okuma varsa olay `LowConfidence` işaretlenip operatöre düşer.
  Sahada ölçülen isabet: **45 bayrağın 33'ü eşik üstünde**, değerlendirilenlerin **%97'si haklı**
  \[85–99]. ⚠️ Bu bir **panel** ölçümüdür, paket garantisi değil; kendi sahanızda doğrulayın.
- **`PlateCorrections`** — kuratörlü tam-dizgi düzeltme haritası.
- **Seçici kanıt koruması** (`EvidencePreserveFlagged`, `EvidencePreserveMaxFiles`) —
  **varsayılan KAPALI.** Açmadan önce KVKK saklama süresi etkisini değerlendirin.
- Sahaya açılan ayarlar: `SpatialMerge*`, `RequireApproaching`, `ApproachDepartRatio`,
  `KnownPlates`, `SnapMaxLev`, `SnapToFrequent`, `SnapMaxOcrConfidence`, `ManualReviewMinOcr`,
  `LastCharAmbiguityMaxGap`, `EvidencePaddingRatio`, `EvidenceJpegQuality`, `SettleWindowSeconds`,
  `CrossCamera*WindowSeconds`, `MinPlateAspectRatio`, `HttpBindAddress`.
- Paket meta verisi: `PackageProjectUrl`.

### Bilinen sınırlar

- **`SnapToFrequent` asimetrik hata üretir:** yanlış snap, doğru snap'ten daha pahalıdır
  (yanlış araca bariyer açılır). Varsayılanı değiştirmeden önce SDK sözleşmesindeki notu okuyun.
- **Kalan okuma hatası cam (glif) seviyesindedir, metin seviyesinde değil.** Ölçüldü
  (137 olay · 228 ikame): baskın karışma çifti yok (en sık çift %4,4), yönlü yanlılık yok
  (12 çiftte simetri testi, hiçbiri Bonferroni'yi geçmiyor), biçim maskesinin tavanı %1,3.
  → **Metin-sonrası düzeltme tablosu yazmak bu veriyle desteklenmiyor**; kazanç yakalama
  kalitesinden (kamera pozlama/kadraj) gelir.
- Biçim kapısı (`IsValidTr`) tutuyor: yayınlanan yanlış okumaların **199/199'u** biçimsel
  olarak geçerli bir TR plakasıdır — yani "geçersizi ele" kuralından **artık kazanç yok**.

---

## [1.0.1] — 2026-07-14

- Yalnız-yaklaşan-araç filtresi (`SizeTrend`).

## [1.0.0]

- İlk yayın: RTSP → çok-kareli oylama → plaka olayı; gömülebilir `AnprRuntime`.
