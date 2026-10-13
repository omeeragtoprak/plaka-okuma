# Anpr.Sdk

CPU-only **Türk plaka tanıma (ANPR)** çalışma zamanı — otopark yazılımına (PMS) doğrudan gömülmek
için. RTSP kameradan kareyi çeker, plakayı tespit + OCR eder, çok-kareli oylama + TR gramer
kısıtlaması + varyant bastırma uygular ve **tek bir C# olayı** olarak plaka yayınlar. GPU gerektirmez;
otopark ofis PC'lerinde çalışacak şekilde tasarlanmıştır.

Modeller (dedektör + Erzurum saha fine-tune OCR + süper çözünürlük) pakete gömülüdür ve tüketicinin çıktı
dizinine `models/` olarak kopyalanır — ekstra dosya taşımak gerekmez. Lisans: **MIT** (bkz. [Lisans](#lisans)).

## Gereksinimler
- .NET 8.0+ (x64 / arm64)
- `ffmpeg` (PATH'te ya da `options.Ffmpeg.Path`) — RTSP + HEVC çözer
- Bellek: iki kamera (4 MP) için **~0,4–0,6 GB** (uygulama + iki ffmpeg süreci) — ayrıntı aşağıda

## Kaynak kullanımı (bellek ve işlemci)

2.0.0'ın asıl işi budur: aynı doğrulukla daha az bellek ve işlemci. Hiçbir ayar gerekmez; aşağıdakilerin hepsi
varsayılandır.

**Ölçüm** — iki kamera, 4 MP (2688×1520) HEVC 25 fps, canlı gündüz trafiği, donanım video çözme açık, web paneli
olan servis içinde. İki ayrı ölçümün (10 dk ve 7 dk) aralığı verilmiştir; fark trafiğe bağlıdır (kanıt için saklanan
tam kareler çöp toplanana kadar bellekte kalır). Makine: Apple M4. *Zayıf bir ofis PC'sinde bellek benzer kalır; işlemci payı işlemcinin hızına göre birkaç kat
artar — hedef donanımda ölçmeden işlemci için söz vermeyin.*

| | 2.0.0 öncesi | **2.0.0** |
|---|---|---|
| ffmpeg (iki süreç) belleği | 498 MB | **110–115 MB** |
| uygulama süreci belleği (ortalama) | 349 MB | **270–343 MB** |
| **toplam bellek** (ortalama) | 846 MB | **380–459 MB** |
| toplam bellek (tepe) | ölçülmedi | 506–584 MB |
| işlemci (uygulama + ffmpeg, tek çekirdeğin payı) | ölçülmedi | 0,31–0,45 + 0,11–0,13 |

Paketi boş bir konsol projesine kurup iki kamerayla (panel yok, kanıt kaydı kapalı, varsayılan ayarlar) çalıştırınca
uygulama süreci **229 MB** tuttu.

Tek kamera, gömülü kullanım (web paneli yok), aynı 10 dakikalık kayıt iki sürümden geçirildi: uygulama işlemcisi
tek çekirdeğin %23–26'sından **%19**'una, ffmpeg %10'dan **%3**'e; uygulama belleği ortalama 205–216 MB'tan
**~140 MB**'a (tepe 340–360 → ~155 MB).

**Bunu sağlayanlar (hepsi varsayılan):**

| ne | ayar | etkisi |
|---|---|---|
| Kare ffmpeg'den çözücünün kendi biçiminde (YUV 4:2:0) gelir; tam kare hiçbir zaman RGB'ye çevrilmez | `Ffmpeg.OutputFormat = Y4m` | kare 16,3 MB → 6,1 MB; ffmpeg'de renk dönüşümü yok |
| Kameralar yapay zekâ modellerinin **tek kopyasını** paylaşır | `ShareOnnxSessions` (Core) | iki kamerada ~85 MB; eklenen her kamera modeller için ~2 MB ister |
| İşlenen karenin tamponu, kimse saklamadıysa bir sonraki kare için yeniden kullanılır | — | boşta bellek tahsisi sıfır (karelerin %99'u) |
| ffmpeg çözücüsü en çok 2 iş parçacığı açar | `Ffmpeg.DecoderThreads = 0` | yazılım çözmede ffmpeg 243 MB → ~100 MB |
| Donanım çözmede fazla kareler ekran kartındayken atılır | `Ffmpeg.GpuFrameDrop = true` | saniyede 25 yerine 5 kare indirilir |

**Sizin yapabilecekleriniz:**

- **ASP.NET Core içinde barındırıyorsanız** proje dosyanıza `<ServerGarbageCollection>false</ServerGarbageCollection>`
  ekleyin. Web projelerinin varsayılanı "Server GC"dir ve çekirdek başına ayrı yığın açar; bu iş yükünde (saniyede
  birkaç kare) kazandırmaz, bellek harcar. (Konsol / Windows servisi / masaüstü projelerinde varsayılan zaten
  uygundur.) Doğrulama: çıktı dizinindeki `*.runtimeconfig.json` içinde `"System.GC.Server": false`.
- **Boş sahnede işlemciyi daha da düşürmek için** `MotionGate = MotionGateMode.On` (isteğe bağlı): dedektör yalnız
  bölgede hareket varken çalışır; hareket varken kameranın verdiği **her** kare işlenir. Aynı 10 dakikalık
  kayıtlarda ölçüm: işlenen kare sakin kamerada %40 → **%25**, uygulama işlemcisi %19 → **%14** (yaya trafiği
  yoğun kamerada kazanç yok: %20 → %21). Yakalama: kayıtların her karesi motordan geçirilerek çıkarılan 27 araçtan
  zamanlayıcılı yoklama iki koşuda 49/54'ünü, hareket tetikli **52/54**'ünü yayınladı (kare kare benzetim, 80 dk,
  74 araç: ortalama 69,8 → 74). Varsayılan değildir çünkü yalnız gündüz kaydıyla doğrulandı — gece/yağmurda açmadan
  önce `/health`'teki `gateForcedHits` sayacını izleyin (zorunlu bakışta plaka bulunan kare; olay sayısına göre
  küçük kalmalı).
- **Bellek tepesini kısmak için** uygulamanızın `runtimeconfig`'ine `System.GC.ConserveMemory = 7` ekleyebilirsiniz
  (csproj: `<ItemGroup><RuntimeHostConfigurationOption Include="System.GC.ConserveMemory" Value="7" /></ItemGroup>`).
  Aynı serviste 3 dakikalık ölçüm: uygulama ortalama 280 MB, tepe 324 MB; işlemci değişmedi. Kısa ölçümdür — kendi
  kurulumunuzda doğrulayın.
- **Yazılım çözme** (donanım çözme yoksa): `Ffmpeg.HwAccel = "none"` ile de çalışır; ffmpeg işlemcisi kamera
  başına tek çekirdeğin ~%16'sı, belleği ~55 MB olur (aynı makinede ölçüm).

## Hızlı başlangıç

```csharp
using Anpr.Sdk;
using Anpr.Sdk.Configuration;

var runtime = new AnprRuntime(new AnprServiceOptions {
    Cameras = {
        new CameraOptions { Id = "giris", Name = "Giriş",
                            RtspUrl = "rtsp://kullanici:sifre@ip:554/Streaming/Channels/101" },
        new CameraOptions { Id = "cikis", Name = "Çıkış", RtspUrl = "rtsp://..." },
    }
}, loggerFactory);   // ILoggerFactory opsiyonel

runtime.PlateDetected += e => {
    if (e.Scope != "roi") return;               // ROI dışı "gözlem" okumaları karar üretmez
    if (e.LowConfidence) OperatorOnayinaDusur(e); // güven < eşik → manuel onay
    else PlakaKaydet(e.PlateCompact, e.Timestamp, e.CameraId);
    // e.EvidencePath  -> plaka kırpıntısı JPEG (evidence/…)
    // e.FramePath     -> aracın da göründüğü TAM KARE JPEG (son okunan aracı görselleme)
};

await runtime.StartAsync();
// … uygulama ömrü boyunca çalışır …
await runtime.StopAsync();
```

## `PlateDetected` kontratı (önemli)
İşleyici **TEK bir iç dispatcher thread'inde SERİLEŞMİŞ** çağrılır — eşzamanlı çağrı **yoktur** ve
işleyiciniz okuma boru hattını **bloklamaz** (yavaş işleyici kare düşürmez).
- Yine de makul hızlı dönün: iç kuyruk (256) dolarsa **yeni olaylar DÜŞER** — `runtime.DroppedEvents`
  sayar ve uyarı loglanır. Düşen olay, bariyerde **bildirilmeyen bir araç** demektir; bu sayacı izleyin.
- Uzun işi (bariyer I/O, DB, ağ) işleyicide değil kendi kuyruğunuzda yapın.
- İşleyicideki istisnalar yutulur + loglanır (boru hattı düşmez).
- Kendi thread'lerinizden de eriştiğiniz paylaşılan durumu kilitleyin; UI güncellemesini kendi
  senkronizasyon bağlamınıza gönderin (çağrı UI thread'inde değildir).

## ⚠️ Kanıt saklama: hangi ayar GERÇEKTEN bağlıyor?

`EvidenceRetentionDays` (varsayılan **30**) bir **üst sınırdır** — "30 günden eskisini sil" der.
Pratikte saklamayı belirleyen **`EvidenceMaxFiles`** (varsayılan **6000**) olur ve tipik hacimlerde
çok daha önce bağlar. İkisi de tek başına yanlış değil; **birlikte yanıltıyor**.

**Ölçüm (2026-08-05, orta ölçekli otopark):** saatte ~37 ROI olayı · olay başına **2,26 dosya**
(`SaveFullFrame` açık) → 6000 tavanı **efektif 4,5 gün** saklama veriyor. Belgelenen 30 günün **1/7'si**.

**Kendi sahanız için:** `gereken tavan ≈ saatlik_olay × 24 × hedef_gün × olay_başına_dosya`

| hedef | gereken `EvidenceMaxFiles` | yaklaşık disk (medyan kanıt 61 KB) |
|---|---|---|
| 4,5 gün (bugünkü varsayılan) | 6.000 | ~377 MB |
| 30 gün | ~59.500 | **~3,7 GB** |

Formül muhafazakârdır (sabit hız varsayar); gece/hafta sonu düşüşü gerçek saklamayı uzatır —
ölçümde formül 3,0 gün derken gerçek 4,9 gün çıktı (sınandı ✓).

🔴 **AMA "TAVANI YÜKSELTİN" TAVSİYESİ KVKK AÇISINDAN TERS YÖNDEDİR.** Kişisel Verileri Koruma
Kurumu'nun yayımlanmış ilkesi: *"Gereğinden uzun süre kayıtların saklanması 6698 sayılı Kanuna
aykırılık teşkil edebileceğinden mümkün olan en kısa süre yeterli görülmeli, sistemde otomatik
imha mekanizması bulunmalıdır"* ve *"Bir olay yaşanması halinde hukuki süreç boyunca **sadece
ilgili olan** kayıt saklanmalıdır."* Kurum sabit gün sayısı vermez; ölçüt **amaç-bağlantılı ve
orantılı** olmaktır.

✅ **DOĞRU DESEN:** kısa toptan saklama **+ uyuşmazlık çıkınca o kaydı koruma**. Plaka görüntüsü
kişisel veridir; 30 günlük toptan saklama günde ~1800 aracın görüntüsünü gereksizce tutmak demektir.
⚠️ **Bu SDK'da seçici koruma mekanizması YOKTUR** — tüketici, itiraz konusu olayın `EvidencePath`
ve `FramePath` dosyalarını kendi arşivine **kopyalamalıdır**. Asıl boşluk budur; tavanı büyütmek
onun yerine geçmez.
⚠️ Bağlayıcı süreyi **hukuk birimi** belirler; bu bölüm hukuki görüş değildir.

## Olay sözleşmesi (`PlateEvent`)
- `EventId` — idempotency anahtarı (aynı olay tekrar teslim edilirse yoksayın).
- `Plate` / `PlateCompact` — normalize plaka / boşluksuz biçim (eşleştirme için bunu kullanın).
- **`OcrConfidence` — bariyer kararının dayandığı güven.** `LowConfidence` tam olarak
  `OcrConfidence < ManualReviewMinOcr` ile hesaplanır; kendi eşiğinizi kuracaksanız **bu alanı** kullanın.
- ⚠️ **`Confidence` bir ÇARPIMDIR** (`DetectorConfidence × OcrConfidence`) ve `ManualReviewMinOcr` ile
  **aynı ölçekte değildir** — eşik/bariyer kararında **kullanmayın**. (Canlı ölçüm: olayların %87,8'inde
  bu değer 0,85'in altındayken olay otomatik-açma için uygundu.) Yalnız sıralama/teşhis içindir.
- **`LowConfidenceReasons`** (yeni, şema **v4**) — bayrağın NEDENİ. `LowConfidence == (Count > 0)`;
  bool'un anlamı değişmedi. **Operatör kuyruğunu bu alana göre SIRALAYIN** — nedenlerin ölçülmüş
  isabeti 5 kat farklı:

  | neden | kuyruktaki payı | isabet (n) | eylem |
  |---|---|---|---|
  | `cross-camera-substitution` / `-truncation` | %8 | **%96** [81–99] (n=33 hükümlü / 34 bayrak) | karşı kameranın aynı geçişteki okumasıyla karşılaştır |
  | `weak-reading` | %77 | %57 ham → **%67 post-strat** (n=124) | kırpıntıya bak (⚠️ bu grubun **~%42'si insan gözüyle de okunamıyor**) |
  | `last-char-ambiguity` | %42 | **%0 ya da %73** — hücreye bağlı (n=27) | önce karşı kamerayı sor; onayladıysa neredeyse hep boşuna |

  ⚠️ **PAYLAR TOPLAMI %100'DEN BÜYÜKTÜR — bir olayın birden çok gerekçesi olabilir** (ölçülen:
  olay başına 1,26 gerekçe, kuyruğun %26'sı çok gerekçeli). Tablo kuyruğu BÖLMEZ; her satır
  "kuyruğun yüzde kaçı bu gerekçeyi TAŞIYOR" der. Kuyruğu sıralarken olayı en yüksek isabetli
  gerekçesine göre ele alın. (Paylar 185 olaylık v4 kuyruğundan; şema v4 öncesinde alan yazılmıyordu.)

  🔑 **ÜCRETSİZ TRİYAJ — İKİ KAMERALI KURULUMDA KUYRUĞUN ~%25'İ BEDELSİZ DÜŞÜYOR (ölçüldü):**
  bir olayda **KENDİ diğer kameranız aynı plakayı ±60 sn içinde okuduysa**, bayrak bu sahada
  **hiç haklı çıkmadı**. İki bağımsız gözlem aynı dizgide buluşmuşsa zayıf güven yanıltıcıdır.

  🔴 **2026-08-23'te BİR İSTİSNA BULUNDU ve kural ONSUZ UYGULANMAMALIDIR (§P.4 (388)):**
  **onay, taraflardan biri SNAP ürünüyse (`SnappedFrom != null`) GEÇERSİZDİR.** Snap deterministik
  bir whitelist eşlemesidir ve **iki kamerada da aynı şekilde** uygulanır; o hâlde "karşı kamera
  onayladı" bağımsız bir gözlem değil, **aynı düzeltmenin iki kez görünmesidir**.
  · Ölçüldü: karşı-kamera onaylı **214** olayın **87'si (%41** [34–47]**)** en az bir tarafta snap ürünü.
  · ⚠️ **Bu oran POPÜLASYON değeri DEĞİL:** tüm çapraz-kamera mutabakatlarında sahte mutabakat
    yalnız **%7,9** [7,1–8,7] (n=4196). Bayraklı dilimde **5× zenginleşiyor**, çünkü snap ile
    bayrak aynı vakalarda buluşuyor. **Kural tam da riskin en yüksek olduğu dilimde uygulanıyor.**
  · **SAF onaylarda kural AYAKTA: bayrak 0/26 haklı** [%0–13].
  · **SNAP KİRLİ onaylarda karşı örnek VAR: 1/12** [%1–35] — ve o vaka tam da bu mekanizmadır:
    `25RRZ42` (giriş) ile `25RRZ42` (çıkış) birbirini "onaylıyor", ama **ikisinin de ham okuması
    `25RRZ46`'ydı** ve whitelist ikisini birden çekmişti. Gerçek plaka `25RRZ46`; bayrak HAKLIYDI.
    Kural istisnasız uygulansaydı **sessiz + mali** bir hata sessizce bastırılacaktı.
  ⚠️ İki hücrenin farkı **istatistiksel olarak gösterilmiş DEĞİLDİR** (n=26 ve 12). Dayanak
  sayı değil **mekanizma**: ortak bir deterministik adımdan geçen iki okuma bağımsız değildir.
  ✅ **Uygulaması bedava:** `SnappedFrom` alanı v2'den beri olayda; onayı saymadan önce
  **her iki olayda da** `SnappedFrom == null` olduğunu doğrulayın.

  | gerekçe | karşı kamera ONAYLADI | onaylaMADI | bastırılabilir |
  |---|---|---|---|
  | `weak-reading` | **0/42 haklı** | 74/89 (%83) | 68/323 |
  | `last-char-ambiguity` | **0/16 haklı** | 8/11 (%73) | 33/77 |
  | `cross-camera-*` | **0/1 haklı** | **31/31 (%100)** | 1/34 |
  | **birleşik** | **0/64 = %0** (üst sınır %6) | | **95 olay = kuyruğun %25'i** |

  ⚠️ `cross-camera-*` satırında onay hücresi küçüktür (n=1) — **beklendiği gibi**: kapı zaten karşı
  kameraya bakar ve *benzer ama farklı* plaka görünce tetiklenir, dolayısıyla "aynı plaka da okundu"
  durumu nadirdir. Ama o tek vaka, kapının **bilinen tek yanlış pozitifidir** — ve kural onu gözle
  inceleme gerektirmeden ayırır.

  İki gerekçe **bağımsız örneklemlerde aynı yöne gitti** ve ikincisi *önceden yazılmış* bir
  ölçütle sınandı (onaylı hücrenin üst sınırı < onaysız hücrenin alt sınırı: %19 < %43 ✅).
  ⚠️ **"Asla" DEMİYORUZ — n=58 ve üst sınır %6.** ⚠️ Bulgu pencereye duyarlı DEĞİL (15/30/60/120/300
  sn'de de 0). ⚠️ İki kamera **aynı modeli** kullanır; sistematik (hep aynı yöne) bir yanlış okuma
  bu kontrolü kandırabilir — bu yüzden bastırma değil **önceliklendirme** önerilir. ⚠️ Tek kameralı
  kurulumda bu sinyal **YOKTUR**.

  🔑 **OPERATÖR ÖNCELİKLENDİRME — "TERS ŞEKİL" (ölçüldü 2026-08-05, n=25 hükümlü kapı bayrağı):**
  bir `cross-camera-*` bayrağında **bayraklanan plakanın kendisi sisteminizde SIK GÖRÜLEN** biriyse,
  o bayrağı **öncelikli** inceleyin. Ölçülen:

  | bayrağın şekli | isabet | yanlışsa ZARAR SINIFI |
  |---|---|---|
  | bayraklanan plaka **nadir** (sistemde <5 olay) | **19/19 = %100** | abone eşleşmez → **elle müdahale** (gürültülü) |
  | bayraklanan plaka **yerleşik** (≥5 olay) | 5/6 = %83 [44–97] | ücret **gerçek başka aboneye** yazılır, denetim izi geçerli görünür → **sessiz + mali** |

  ⚠️ **Ters şeklin isabeti DAHA DÜŞÜK ama zararı DAHA AĞIR** — öncelik isabetten değil zarardan
  gelir. Bu sahada 2026-08-05'te bu şekilden **iki vaka canlı yakalandı** (`25UA973`→`25UA976`
  ocr 0,984 · `25HPD211`→`25HPD217` ocr 0,983); ikisi de eşiğin üstündeydi, yani kapı olmasa
  bariyer açılıp fatura yanlış aboneye giderdi.
  ⚠️ Eşik "≥5 olay" **keyfîdir**; ölçüt sayım değil **zarar sınıfıdır**. Kendi abone listenizle
  eşleştirme yapıyorsanız daha iyisi: bayraklanan plaka **abone listesinde varsa** öncelikli işleyin.

  ⚠️ Bu alan olmadan gerekçe **geriye dönük hesaplanamaz**: `Candidates` sırasızdır, dolayısıyla
  `last-char-ambiguity` kuralı (aday[0] vs aday[1]) olay günlüğünden yeniden üretilemez.

  ⚠️ **`SchemaVersion < 4` kayıtlarda `LowConfidence=true` + BOŞ liste olabilir** (alan o dönemde
  yazılmıyordu). Geçmişi de okuyorsanız `Reasons` boşken "bayrak sebepsiz" sonucuna VARMAYIN —
  önce `SchemaVersion`'a bakın.
- `LowConfidence` — true ise otomatik açma yok, operatör onayına düşürün. Üç nedenden biriyle olur:
  `OcrConfidence < ManualReviewMinOcr` · **son-karakter belirsizliği** · çapraz-kamera bozulma şüphesi.
  ✅ **"İsabet %13 mi %50 mi" ÇELİŞKİSİ ÇÖZÜLDÜ — ikisi de doğruymuş, KARIŞIM farklıymış.** Kuralın
  isabeti tek bir sayı değildir; **karşı kameranın onaylayıp onaylamadığına göre %0 ile %73 arasında
  değişir** (yukarıdaki tablo). Bir örneklemde onaylı hücrenin payı ne kadar yüksekse ölçülen isabet
  o kadar düşük çıkar — erken n=15 örneklemiyle sonraki n=27 örneklemi arasındaki fark budur.
  **Post-stratifiye isabet: %42** (n=27, ön-kayıtlı tabakalı tasarım). Tek bir "isabet" rakamı
  yayımlamak yerine hücre bazında okuyun.
  Bilinçli olarak korunur çünkü **tek kameralı** kurulumda kesilme sınıfının TEK korumasıdır; sahaya
  özel sıkılaştırma için `LastCharAmbiguityMaxGap` düşürülebilir. ⚠️ Kuyruktaki payı da erken
  tahminden (~%13) belirgin yüksek çıktı: **%42** (paylar örtüşür, yukarıdaki nota bakın).
- `Candidates` — alternatif adaylar. **Sırasızdır**; ilki her zaman `PlateCompact`'tir. **En yüksek
  güvenliyi seçmeyin** (canlı: %36,4'ünde bu, son karakteri düşmüş kesik varyant). Abone/whitelist
  eşleşmesinde yalnız **küme üyeliği** olarak kullanın.
- `SnappedFrom` — **null değilse plaka whitelist'e göre DÜZELTİLMİŞTİR**; bu alan snap öncesi ham
  okumadır. Yüksek riskli kapılarda bu olayları denetim izine yazmayı ya da onaya düşürmeyi değerlendirin.

  🔴 **VE SNAP, GÜVENİN ANLAMINI DEĞİŞTİRİR — bu, alanın belgesinde YOKTU (eklendi 2026-08-22, §P.4 (365)):**
  snap metni değiştirir ama **`OcrConfidence`'a dokunmaz**. Yani `SnappedFrom != null` olan bir olayda
  yayınlanan skor **snap ÖNCESİ dizginin** güvenidir; model, yayınlanan plakayı hiç skorlamamıştır.
  Aynı şekilde `Candidates[0]` yayınlanan plakaya yeniden yazılır ve **aynı ödünç güveni** taşır;
  snap hedefine eşit olan özgün aday listeden **elenir** → *"OCR'ın kendisi bu düzeltmeyi destekliyor
  muydu?"* sorusu olaydan **artık yanıtlanamaz** (ölçüldü: 536 snap olayının 536'sında hedef 1. sırada).
  📊 Ölçülen dağılım: snap ürünü olaylarda medyan güven **0,899**, snapsızlarda **0,996** —
  ama **her iki kümenin de %100'ü emit eşiğinin üstünde**, yani eşik bu ayrımı yapmaz.
  ⇒ **Eşik/tahsilat kararında `SnappedFrom`'a da bakın**; null değilse skoru "yayınlanan plakaya
  duyulan güven" olarak okumayın.

  🔴 **SNAP NET KAZANÇLIDIR AMA HATASI ASİMETRİKTİR — FİLO PLAKALI SAHALARDA OKUYUN (ölçüldü 2026-08-04,
  n=100 gözle doğrulanmış snap olayı):** snap 75 okumayı düzeltti, **25'ini bozdu**; bozulanların **5'i,
  sahadaki GERÇEK BAŞKA bir aracın plakasını yayınladı**. O beş vakada zarar "tanınmayan plaka" değil
  **yanlış aboneye tahsilat**tır ve denetim izi **geçerli görünür** — yani sessizdir.
  **Kök neden ardışık numaralı filo plakalarıdır:** bu sahada snap edilen olayların **%35'inde** hedefin
  lev≤1 komşusu da bağımsız doğrulanmış gerçek bir plakaydı (`25YBN279`↔`25YBN278`, `25MT297`↔`25MT299`,
  `25GE895`↔`25GE891`). Kurumsal/belediye otoparklarında bu yapı tipiktir.
  ⚠️ **"Whitelist'ten zehirli girdiyi sil" bu sorunu KAPATMAZ** — bozan dizgiler listede yoktu.
  **Öneri:** aboneye tahsilat yapan kurulumlarda `SnappedFrom != null` olayları otomatik tahsilata
  sokmayın; hiç değilse abone eşleşmesini snap ÖNCESİ dizgiyle de deneyin ve iki sonuç ayrışıyorsa
  onaya düşürün. (Paket varsayılanı **değiştirilmedi**; bu bir yapılandırma/entegrasyon önerisidir.)

  ⚖️ **DÜZENLEYİCİ EMSAL (bilgi amaçlı — hukuki tavsiye DEĞİL, ve bu paket için bağlayıcı değil):**
  Birleşik Krallık özel otopark sektör kodu, ANPR/CCTV kanıtının bir **park ücreti kesilmeden önce
  elle kalite kontrolünden** geçirilmesini (plaka ve zaman damgası kontrolü dâhil) şart koşuyor.
  Bu kural **başka bir yargı alanına** aittir ve buraya aktarılamaz; buradaki değeri şudur:
  bağımsız bir düzenleyici de *"ANPR kanıtı, MALİ sonuç doğurmadan önce insan kontrolünden geçmeli"*
  sonucuna varmış. Yukarıdaki öneriyle aynı yöne işaret eder — **operasyonel** sınıf ile **mali**
  sınıfın ayrı ele alınması gerektiğine.
  ⓘ Aynı kaynak ANPR için **hiçbir doğruluk/hata oranı rakamı vermiyor**; bu alanda yayınlanmış
  sektör eşiği bulunamadı (ikinci kez teyit edildi).

  🔑 **SNAP GÜVENLİĞİ `SnapMaxLev`'İN DEĞERİ DEĞİL, WHITELIST'İNİZİN YAPISIDIR — kendi sahanızda
  ölçün.** Bu sahada ölçülenler: listenin **%14'ünün** liste-içi lev≤1 komşusu var; daha kritiği
  **%5'inin komşusu listede YOK ama sahada gerçek** — liste o aracı tanımadığı için okumasını
  komşusuna çeker. Ve liste ortalaması yanıltıcıdır: fiilen kullanılan hedeflerde belirsizlik **%24**,
  snap edilen **olaylarda %35**. Risk liste boyutuyla **karesel** büyür (n(n−1)/2 çift).
  Çözüm girdiyi silmek değil, **eksik aracı listeye eklemek** ya da o girdide snap'i kapatmaktır.
- **`EarlyEmitHeldAtOcr`** (yeni, şema **v5**) — olay `EarlyEmitMinOcr` tarafından **tutulduysa**
  ilk tutulma anındaki OCR güveni; tutulmadıysa `null`. Beklemenin kazancı = `OcrConfidence` − bu değer.
  Tutma bir **gecikme–doğruluk takasıdır**: bariyer daha geç açılır, karşılığında daha iyi okuma beklenir.
  Bu alan olmadan takasın kazanç tarafı yer-gerçeğiyle eşleştirilemiyordu (süreç sayaçları restart'ta
  sıfırlanır ve *hangi* olayın tutulduğunu söylemez).
  ⚠️ **Güven kazancı bir VEKİLDİR, sonuç değil** — bu sahada ortalama kazanç yalnız **+0,010** (giriş)
  ile **+0,050** (çıkış) çıktı. Kararı kazanca göre değil, yer-gerçeğine göre verin.
  ⚠️ `SchemaVersion < 5` kayıtlarda alan **yazılmamıştır**: `null` "tutulmadı" DEĞİL **"bilinmiyor"**
  demektir. Bu ayrımı gözden kaçıran bir analiz, tutmayı olduğundan seyrek gösterir.
- **`SnapTargetRank` / `SnapTargetOcr`** (yeni, şema **v7**) — yalnız **snap uygulanan** olaylarda dolu:
  hedefin snap **ÖNCESİ** aday listesindeki 1-tabanlı sırası ve hedefe **modelin KENDİ** verdiği güven.
  Hedef ilk-K'da yoksa ikisi de `null` (= *"ilk-K'da değildi"*, **"olasılık sıfır" DEĞİL**).
  🔑 Yukarıdaki ödünç-güven sorununun ölçülebilir hâle gelmesi: `OcrConfidence` − `SnapTargetOcr`
  farkı, snap'in modele ne kadar aykırı olduğunu verir. `SnapTargetRank == null` yüksek riskli
  kapıda onay eşiği olarak kullanılabilir. `SchemaVersion < 7` kayıtlarda alan yoktur.

- **`EarlyEmitHeldAs`** (yeni, şema **v6**) — tutma anında okunan **METİN**, yani *tutma olmasaydı
  yayınlanacak dizgi*. `null` = tutulmadı **ya da** olay v6 öncesi.
  🔑 **Neden ayrı bir alan:** `EarlyEmitHeldAtOcr` tek başına *"tutmak işe yaradı mı?"* sorusunu
  **yanıtlayamaz** — güven, okuma hiç değişmeden de artabilir, ve değişip yine yanlış kalabilir.
  Kararı veren karşılaştırma şudur: **`EarlyEmitHeldAs` ↔ `PlateCompact` ↔ yer-gerçeği.**
  Üçü birlikte dört sonucu ayırır:
  | | anlamı |
  |---|---|
  | `HeldAs ≠ Plate` ve `Plate` doğru | tutma **DÜZELTTİ** |
  | `HeldAs ≠ Plate` ve `HeldAs` doğru | tutma **BOZDU** |
  | `HeldAs == Plate` | dizgi değişmedi → yalnız **GECİKME** eklendi |
  | ikisi de yanlış | tutma sonucu değiştirdi ama düzeltmedi |
  ⚠️ Net oran, **sonucu DEĞİŞTİREN** olaylar üzerinden okunur; *"değişmedi"* vakaları paydaya
  girmez (neti kıpırdatmazlar).
  ⚠️ `SchemaVersion < 6` kayıtlarda alan **yoktur** ve **geriye dönük doldurulamaz** — o
  dönemdeki tutmalar için bu soru **kalıcı olarak yanıtsızdır**.

- **`TrackFrames` / `TrackAbsorbed`** (yeni, şema **v8**) — bu geçişte plakanın **bulunup okunduğu**
  kare sayısı ve ize soğurulan diğer okuma sayısı. Uzlaşının ne kadar veriyle kurulduğunu gösterir:
  `TrackFrames == 1` ise oylama devreye girmemiştir, tek karelik bir okuma yayınlanmıştır.
  ⚠️ İşlenen TÜM kareler değil — dedektörün kaçırdığı kareler sayılmaz, yani bir **alt sınır**.
  ⚠️ `null` = alanı taşımayan eski kayıt; **`1` ile karıştırmayın** (`1` = gerçekten tek kare).
  `SchemaVersion < 8` kayıtlarda alan yoktur.

- `SchemaVersion` — şu an **8** (`TrackFrames`/`TrackAbsorbed` ile 2026-08-24'te 7'den yükseltildi;
  v7 `SnapTargetRank`/`SnapTargetOcr` ile 2026-08-22'de 6'dan yükseltilmişti;
  v6 `EarlyEmitHeldAs` ile 2026-08-21'de, v5 `EarlyEmitHeldAtOcr` ile 2026-08-07'de,
  v4 `LowConfidenceReasons` ile 2026-08-03'te gelmişti);
  alan eklendikçe artar. Bilinmeyen sürümde alanları yoksayın, **kırılmayın**. ⚠️ Arşiv okuyorsanız
  tek bir dosyada birden çok sürüm bulunur (bu sahada: v2, v3, v4 bir arada) — sürüm başına dallanın.
  ⚠️ **Alan hiç yoksa `1` sayın** (SDK artık böyle yapıyor; 2026-08-10'a kadar en yeniyi sayıyordu —
  bu, `SchemaVersion >= 4` kontrolünde yanlış pozitif üretiyordu). Ürettiğiniz olaylarda sürümü
  `PlateEvent.CurrentSchemaVersion` ile açıkça yazın, varsayılana bırakmayın.

  > 🔴 **SÜRÜM KONTROLÜNÜ `==` İLE YAZMAYIN — `>=` KULLANIN.**
  > `if (e.SchemaVersion == 5)` yazan bir tüketici, bir sonraki alan eklendiğinde **sessizce tüm
  > olayları reddeder** ve bariyer hiç açılmaz. Bu, gerçek bir sürüm yükseltmesinde (v4 → v5,
  > 2026-08-07) somut olarak uyarılan kırılma biçimidir.
  > **Sözleşme:** alanlar **EKLENİR**; var olanların **anlamı DEĞİŞMEZ**; alan **KALDIRILMAZ**.
  > Dolayısıyla ihtiyacınız olan alanın geldiği sürümü TABAN alın:
  > ```csharp
  > if (e.SchemaVersion < 4) { /* LowConfidenceReasons yok — BOŞLUĞU 'güvenli' SAYMAYIN */ }
  > // tanımadığınız YENİ alanları yok saymak güvenlidir; eksik alanı VARSAYMAK değildir
  > ```
  > Çalışan referans: `samples/PmsOrnek/Program.cs` (bu deseni ve iki tuzağını gösterir).
- `Scope` — `roi` (bariyer kararı) / `gozlem` (ROI dışı; **karar üretmez**, yalnız izleme).
- `EvidencePath` / `FramePath` — plaka kırpıntısı / aracın da göründüğü tam kare.
- `SizeTrend` — yön göstergesi (`-1` = karar verilemedi).

## Ölçülmüş doğruluk (gözle doğrulanmış, kamera referansı KULLANILMADAN)

> 📅 **Ölçüm penceresi **2026-09-30'da kapandı**.** Bu bölümdeki doğruluk sayıları, o tarihte biten 30 günlük
> pencerede insan gözüyle hükme bağlanmış olaylardan hesaplandı. Servis olayları 30 gün tutar; etiketleme
> o tarihten sonra sürmediği için tablo **o günkü hâliyle donduruldu** ve artık canlı sistemden yeniden
> üretilemiyor. Sayılar o günün ölçümü olarak doğrudur; "bugünkü" doğruluk olarak okunmamalıdır.

> ⚠️ **BU SAYILAR HANGİ AYARLARLA ÖLÇÜLDÜ (önemli):** ölçüm sahası aşağıdaki üç ayarda paket
> varsayılanından FARKLI çalışıyor. Varsayılanlarla çalıştırırsanız sistem ölçtüğümüzle **birebir aynı
> değildir**:
>
> | ayar | paket varsayılanı | ölçüm sahası | etkisi |
> |---|---|---|---|
> | `EarlyEmitMinOcr` | **0 (kapalı)** | **0,85** | varsayılanda zayıf okuma settle beklemeden HEMEN yayınlanır; 0,85'te daha iyi okuma için `SettleWindow` kadar bekler |
> | `Fps` | 2 | 5 | araç varken tüketim hızı |
> | `IdleFps` | 1 | 2 | boştaki yoklama hızı |
>
> Aynı davranışı istiyorsanız bu üçünü açıkça verin. Varsayılanları değiştirmedik çünkü `Fps`/`IdleFps`
> saha-özgüdür (CPU bütçesine bağlı) ve `EarlyEmitMinOcr` bir GECİKME–DOĞRULUK takasıdır: açmak
> olayların ~%7'sini (ocr < 0,85) `SettleWindow` kadar geciktirir.

Kanıt görsellerinden gözle doğrulama, tabakalı örneklem, Wilson %95 aralığı — **n=400**:

| OCR güven bandı | Popülasyon payı | Doğruluk | %95 aralık |
|---|---|---|---|
| ≥ 0,99 | %54 | %93 (77/83) | 85–97 |
| 0,95–0,99 | %18 | %65 (43/66) | 53–76 |
| 0,85–0,95 | %18 | %54 (67/123) | 46–63 |
| < 0,85 | %11 | %15 (19/128) | 10–22 |
| **Ağırlıklı** | | **%72,9** | **64,6–79,1** |

> 🔴 **`< 0,85` SATIRI ŞİŞKİN — KENDİ ÖRNEKLEME HATAM (2026-08-02).** O bandın doğrulanmış
> örnekleminin **%23'ü** (27/117) bilinçli olarak yüksek doğruluklu bir alt hücreden seçildi (karşı
> kamera onaylı olaylar; skor 27/27), oysa hücre bandın popülasyonunun **%20'si**. Post-stratifiye
> doğru değer **%33,7**, yani ham **%44** ~10 puan şişkin. **Bariyeri açan karar ETKİLENMEZ**
> (%93,7 → post-strat %95,7). Ham sayılar bilerek bırakıldı — ölçüm silinmez, düzeltilir.
>
> ✅ Bu bandın **okunamazlık** oranı ise artık projeksiyon değil **SAYIM**: 181 olayın 172'si (%95)
> gözle açıldı, kalan 9'un 8'inin kanıtı budanmış → **%30** (üst sınır %31).
> 🔴 **AMA BU %30 AŞAĞI YANLI (2026-08-04):** örneklemin %28'i "karşı kamera onaylı" hedefli alt
> hücreden gelir ve orada okunamazlık yalnız **%4** (2/45); hücre dışında **%42** (50/118). Aynı
> hedefli seçim doğruluğu yukarı, okunamazlığı aşağı çekmiş. **Temiz örneklemde ~%42.** Bu gece
> yapılan hedefsiz bant derinleştirmesi (12 kayıt) %75 verdi. ⚠️ *(Bu bölüm önce farkı "hafta içi
> trafiği" diye açıklıyordu — olgusal olarak yanlıştı ve GERİ ÇEKİLDİ: hafta içi %37 vs hafta sonu
> %40, fark yok.)*

- **Bariyeri açan kararda doğruluk %93,4 [90,6–95,3]** — bu, `ocr ≥ 0,85` bandıyla sınırlı
  ölçümdür (popülasyonun %93'ü) ve ürün için asıl sayıdır; genel doğruluk operatör kuyruğunu da içerir.
- ⚠️ Yukarıdaki tablo, gün içinde KAPATILMIŞ İKİ kusurun canlı olduğu dönemi de kapsar
  (çapraz-kamera **kesilme**, 04:59 UTC · çapraz-kamera **ikame**, 20:18 UTC). Kesilme sonrasıyla
  sınırlı ölçüm **%98,2 [91,9–99,2] (n=190)**. **İkame kapısı sonrası pencere de artık ölçülüyor**
  (aşağıda).
- ⚠️ **ÖRNEKLEM YANLI ve yanlılık BİZİM ALEYHİMİZE (ölçüldü):** doğrulama emeği rastgele değil,
  **şüpheli** olaylara yönlendirildi. Doğrulanmış olayların **%17,5'inde** kameranın bağımsız kaydı
  farklı okumuş; doğrulanmamışlarda yalnız **%6,4** (p < 1e-5). Örneklem hatalarda zenginleştiği
  için yayınlanan sayı **karamsar**dır: bu hücrelere göre post-stratifiye kestirim genel
  **%91,5 [88,4]** · bariyer kararı **%95,8 [92,9]** (yayınlanandan **1,0–1,6 puan yukarıda**).
  Sayı bilinçli olarak düzeltilmedi — düzeltme kamerayı kovaryat olarak kullanır, kamera ise referans
  DEĞİLDİR. Ürün iddiasında karamsar taraf doğru taraftır. (`scripts/audit_sample_bias.py`)
  **DÖRT kontrollü (hedefsiz) parti** — eser yalnız doğrulama yoğunluğu **%10'un altındaki**
  dilimlerde çıkıyor: 07-28/29 · 0,85–0,95 (yoğunluk %9,6) → %48,6 yerine **%86,4** · 07-28/29 ·
  ≥0,99 (yoğunluk %6,0) → %93,9 yerine **%100 (12/12)**. Yoğunluk %30'un üstündeyken hedefsiz parti
  yayınlananı DOĞRULUYOR: `<0,85` → %40,0 vs **%37,5** · `0,95–0,99` → %90,8 vs **%91,7**.
  **Ortak desen:** 9 hatanın 9'u kameranın farklı okuduğu kayıtlarda; kameranın aynı okuduğu
  34 kaydın 34'ü doğru (kampanya geneli 542/542).
  → İkinci bağımsız okuyucu **uyuşuyorsa** hata olasılığı ölçülemeyecek kadar düşük, **uyuşmuyorsa**
  inceleme değerli. (Bu bir ÜRÜN GARANTİSİ değil, bu sahanın ölçümüdür.)
- **"Okunamadı" konvansiyonunun yarattığı iyimserlik SINIRLI (ölçüldü):** insan gözüyle de okunamayan
  60 kayıt paydadan çıkarılır (model hatası sayılamaz). Hepsi hata sayılsaydı bariyer kararı
  **%93,5 → %92,7 [90,3 → 89,4]**, genel doğruluk %90,4 → %88,8 olurdu — yani **sapma en fazla
  0,8 puandır** (genelde 1,7). Küçük olmasının sebebi yapısal: okunamayanların 26'sı `< 0,85`
  bandında (bariyeri hiç açmaz) ve `≥ 0,99` bandında yalnız **1** var.
- **`LowConfidence`'ın SINIRI:** < 0,85 bandındaki kayıtların **%44'ü** [38–50] insan gözüyle DE
  okunamıyor (üst bantlarda hızla sıfırlanır: 0,85–0,95 → %4 · 0,95–0,99 → %2 · ≥0,99 → %0).
  Operatöre düşürmek bir çözüm değil, bir KUYRUKtur; **üçte birinden fazlası** çözümsüzdür.
  Nedeni ÇÖZÜNÜRLÜK DEĞİL, **hareket bulanıklığı**.
  ⓘ Bu satır uzun süre **%30** yazdı; ham ortalama bileşim artefaktıydı (§P.4 (418)) — örneklem
  çok-bayraklı (daha okunaklı) vakalara yığılmıştı, katman-ağırlıklı doğru değer %42 — ⚠️ eksik
  katman doldukça hâlâ yukarı gidiyor (%36 → %40 → %42 → %44; adımlar +3,9/+2,0/+1,6 ile
  küçülüyor), yani ALT SINIR gibi okunmalı.
- **Gece performansı DOĞRULANMAMIŞ** — 11 gece olayı / 1927 roi olayı = %0,6; bu sahada gece trafiği yok.
- **Recall/F1 verilemez** — bunlar yalnız etiketli sürekli videodan gelir; yukarısı precision tarafıdır.
- **Ama KAÇIRMA tarafı için sınırlı bir ÜST SINIR ölçüldü (2026-08-02): %2,0.** Kameranın bağımsız
  kaydındaki geçişlerin **%2,0'ında** (35/1752) hiçbir katmanda (roi ya da gozlem, her iki kamera)
  okumamız yok. Kalanı: %83,4 aynı kamera+aynı plaka · %7,6 aynı kamera farklı plaka · %7,0 yalnız
  karşı kamera (kapsam farkı, kaçırma değil) · %0,1 yalnız ROI dışı.
  ⚠️ **Bu RECALL DEĞİLDİR**: kamera referans değil, o da kaçırır/uydurur ve bizim ROI'mizin dışını
  görür. Sayı **kameranın kendi tespitlerine göre bir üst sınırdır**. Ölçüm üç tuzağı kapatarak
  yapılır (servis kapalı dönemler · kamera kaydının %43'ünün KOPYA olması · ROI/kamera kapsam farkı) —
  `scripts/audit_miss_rate.py`. Kendi kurulumunuzda ikinci bir okuyucunuz varsa aynı ölçüm yapılabilir.
- **Kaynak maliyeti:** güncel ölçüm yukarıda, [Kaynak kullanımı](#kaynak-kullanımı-bellek-ve-işlemci) bölümünde
  (2.0.0: iki kamera ortalama 380–459 MB). 1.x sürümlerinde ölçülen değer 0,54 çekirdek + 1,30 GB / 2 kamera idi. Kapasite
  **kamera sayısına** göre planlanır, trafiğe göre değil. `frameMsP50` gerçek maliyeti eksik gösterir (yalnız
  tanıma süresidir; kare taşıma ve ffmpeg dâhil değildir) — süreç düzeyinde ölçün.

## Hataların anatomisi — son-işlemenin TAVANI ölçüldü (2026-08-03)

119 gözle doğrulanmış yanlış okumanın her birinde soruldu: *gerçek plaka, çözümleyicinin
`Candidates` listesinde var mıydı?*

| grup | pay | anlamı |
|---|---|---|
| doğru cevap top-5 adayda **hiç yok** | **%87,4** | yeniden sıralama/eşik/aday-sayısı bu kütleye dokunamaz |
| çözümleyici doğruydu, **snap bozdu** | %8,4 | üçü de zehirli `KnownPlates` girdisi — **temizlendi**, bugün oluşmaz |
| gerçek **sıralama hatası** | %4,2 | doğru cevap #2'de, Δguven 0,012–0,079 |

**`Candidates`'a dayanan her iyileştirmenin toplam tavanı %12,6'dır** ve bunun 8,4 puanı zaten
kapatıldı. Kalan %4,2'nin beşi de UZUNLUK hatası ve **yönü tutarsız** (3 vaka kısaltmak, 2 vaka
uzatmak ister) — tek yönlü bir kural birini düzeltip diğerini bozar.

⚠️ Dahası, "hiç yok" grubunun **%60'ı bizim okumamızdan lev≥2 uzakta**: doğru cevap makul bir aday
bile değildi. **Yani hata kütlesinin ezici çoğunluğu çözümleyicinin YUKARISINDA** — görüntü kalitesi
ve model. PMS tarafında `Candidates` üzerinde kurulacak akıllı bir seçim mantığı, ölçülen tavana göre
en fazla birkaç puan getirir; asıl kazanç pozlama/aydınlatmadadır.

_(Sınır: `CandidateCount`=5, yani "top-5'te yok" ile "üretilemez" aynı şey değildir.)_

## Doğruluk artırma: KnownPlates (sitenin bilinen araçları)
Otoparkta düzenli araçlar (abone/personel/protokol) baskındır. Bu plakaları `KnownPlates`'e verirseniz,
sistematik yanlış-okumalar (ör. bir kare `25 RDR 964` yerine `25 RUR 964` okunursa) bilinen plakaya
otomatik düzeltilir (lev≤1).

> ⚠️ **Kutudan çıkan üründe kazancı SIFIRDIR** — `KnownPlates` varsayılan olarak BOŞTUR. Fayda ancak
> sahanın gerçek plaka listesi verilirse oluşur.
>
> ⚠️ **"+9,3pp" rakamı KAMERA REFERANSLIDIR ve bir ÜST SINIRDIR.** Kamera ANPR'ı da yanılır ve çoğu
> zaman aynı yönde yanılabilir, bu
> yüzden "snap doğru muydu" sorusunu kamerayla yanıtlamak kazancı OLDUĞUNDAN BÜYÜK gösterir.
>
> ⚠️ **Liste kendi hatanızı KANONLAŞTIRABİLİR.** Whitelist'i "iki sistem de sık okuyor" mantığıyla
> üretirseniz korele hata listeye girer ve snap DOĞRU okumayı yanlışa çeker — bu sahada tam olarak
> yaşandı. Listeyi gözle doğrulanmış plakalardan kurun; lev1 komşusu olan girdileri (bu sahada %16)
> ayrıca gözden geçirin. `SnappedFrom` alanı her düzeltmeyi denetlenebilir kılar — düzenli inceleyin.
>
> ℹ️ **Snap'lenen olayların %19'u yine de operatöre düşer** (131 snap'te 25) — çünkü son-karakter
> belirsizliği ve çapraz-kamera kuralları snap'ten BAĞIMSIZ çalışır. Doğrulananlarda snap 16 kez
> doğru 1 kez yanlıştı; yani bu bir **savunma katmanıdır** ve bilinçli korunur: dış bilgiyle
> (whitelist) plaka değiştiren bir işlemin insan onayını kaldırmak, listenin bozuk olduğu durumda
> tek koruma noktasını yok eder.
>
> ⚠️ **SNAP, "hata başka bir GERÇEK aracın plakasına düşer" sınıfının bir NEDENİDİR.** Ölçüldü
> (127 yayınlanmış snap): aynı geçişte karşılaştırılabilir komşusu olan 87 snap'in **%90'ı komşu
> tarafından onaylanıyor, %10'u onunla ÇELİŞİYOR**. Canlı vaka: ham `25AC481` → `25AC471` (gerçek
> filo plakası) snap'lendi, oysa geçen araç `25AC490`'ti; olay yalnızca varyant bastırması sayesinde
> yayınlanmadı. Bu yüzden **lev-1 komşusu olan plaka listeye EKLENMEZ** ve `SnapMaxLev=1` korunur.

```csharp
var options = new AnprServiceOptions {
    Cameras = { /* … */ },
    KnownPlates = { "25 RDR 964", "25 UDZ 493", /* sitenizin düzenli araçları */ },
    SnapMaxLev = 1,               // lev≤1 (lev≤2 ölçüldü: bariyer için fazla yanlış-snap)
    SnapMaxOcrConfidence = 0.99f, // varsayılan; ≥0.99 güvenli okumalar korunur, altındakiler düzeltilir
};
```
Listeyi elle bakmak yerine **veriden üretebilirsiniz**: kameranın kendi ANPR kayıtları + kendi
olaylarınızın ÇAPRAZ-agreement'ı (bkz. `scripts/harvest_knownplates.py`) — filo değiştikçe yeniden çalıştırın.

## `PlateCorrections` — güven skorunun göremediği sistematik hata için son çare

`KnownPlates` bulanık eşleşmedir ve `SnapMaxOcrConfidence` (0,99) ile **kasıtlı olarak** sınırlanmıştır:
kapıyı kaldırmak ölçüldü ve yanlış-snap'i 1 → 11 yaptı, çünkü bulanık eşleşme lev1 komşuya kayabilir.

Teorik olarak bir hata sınıfı bu kapının **üstünde** kalabilir: model **0,999** güvenle yanılır,
kamera da aynı yönde yanılır ve `SnapMaxOcrConfidence=0,99` kapısı okumayı geçirir. Böyle bir vakada
ne güven skoru, ne kamera çaprazı, ne bulanık snap işe yarar.

> ⚠️ **Bu sahada "ölçülmüş vaka" diye sunulan örnek YANLIŞ ÇIKTI ve geri çekildi (2026-08-01).**
> `25LMV166` okumasının hatalı olduğu sanılıp haritaya girdi eklenmişti; oysa aracın plakası gerçekten
> `25 LMV 166`. Sonuç: **doğru okunan bir plaka sistematik olarak bozuldu.** Girdi kaldırıldı.
> Yani bu sınıfın gerçekten var olduğu bu sahada **HENÜZ KANITLANMADI** — ve harita, kanıtsız
> kullanıldığında kendisi bir hata kaynağıdır.

`PlateCorrections` **tam-dizgi** eşleşmesidir ve bu yüzden güven kapısı **uygulanmaz** — o kapı bulanık
eşleşmenin korumasıydı, tam-dizgi eşleşme lev1 komşuya kayma riskini yapısal olarak taşımaz.

```csharp
var options = new AnprServiceOptions {
    // yalnız KANIT GÖRSELİNDEN GÖZLE DOĞRULANMIŞ, sistematik yanlış okumalar:
    PlateCorrections = { /* boş bırakın — girdi ancak çoklu bağımsız kanıtla eklenir */ },
};
```

> ⚠️ **Bu harita bir okumayı KANONLAŞTIRIR ve YANLIŞ GİRDİ DOĞRU OKUMALARI KALICI BOZAR — bu sahada
> YAŞANDI.** Tek bir bulanık kırpıntıya dayanan bir hüküm yüzünden gerçek plakası `25 LMV 166` olan
> araç sistematik olarak `25LMY166` yayınlandı. Bir girdi eklemeden önce **birden çok bağımsız,
> yüksek çözünürlüklü** kanıt şarttır; tek kırpıntı asla yeterli değildir. Harita insan hükmünü
> tek hata noktası hâline getirir. Geçersiz TR hedefi, `KnownPlates` ile çelişki, kendine
> eşleme ve zincir (A→B→C) girdileri **reddedilir** ve başlangıçta UYARI olarak loglanır.
> Uygulanan her düzeltme `SnappedFrom` ile denetlenebilir. Varsayılan **boştur** (davranış değişmez).

## `CrossCameraPrefixWindowSeconds` — çapraz-kamera kesilme kapısı (varsayılan 15 sn, açık)

Bir okuma, bu pencere içinde **herhangi bir kameranın** yayınladığı tam 1 karakter daha uzun ve
**daha güvenli** bir okumanın tam ön-eki ise `LowConfidence = true` yapılır → bariyeri otomatik açmaz,
kararı operatör verir. `0` = kapalı.

Gerekçe (1766 canlı olay): kesik okumanın güveni **şişer** (düşen karakterin "yokluğu" kesin sayılır),
ölçümde 0,998'e kadar çıktı. Kamera-içi koruma her kamera ayrı worker olduğu için çapraz vakayı
göremiyordu: **5 vaka, 5'i de bariyeri kesik plakayla otomatik açtı.** Yanlış-pozitif riski ölçüldü ve
**0** çıktı (11 ön-ek çiftinin 11'inde de kısa taraf tek-seferlik, uzun taraf yerleşik).

> Eylem bilinçli olarak **bastırma değil**: giriş ve çıkış ayrı kapılardır, olayı bastırmak aracın o
> kapıdaki kaydını kaybettirirdi. Olay yayınlanır, plaka **değiştirilmez**, yalnız otomatik açmaz.
> Yön şartı zorunludur: uzun okuma daha güvenli olmalı — aksi hâlde `25 A 6104` (polis plakası, 0,991)
> gibi **kısa tarafın doğru olduğu** vakalar yanlışlıkla operatöre düşerdi.

**Kapsam (ölçüldü, 9 gözle doğrulanmış kesilme):** kesilme iki alt sınıfa ayrılır ve ikisi arasında
belirsizlik yok — doğru okumanın uzaklığı ya **0–9 saniye** ya da **saatler**:

| sınıf | pay | doğru okumanın uzaklığı | bu kapı |
|---|---|---|---|
| **(a) aynı geçişte** | **8/9 (%89)** | 0–9 sn | doğru okuma ÖNCE geldiyse yakalar |
| (b) izole geçişte | 1/9 (%11) | 4049 sn | yapısal olarak yakalayamaz |

> ⚠️ **DÜZELTME (2026-08-01) — (a) sınıfının TAMAMI yakalanmaz.** Kapı **geriye dönüktür**: bir okumayı
> yalnız DAHA ÖNCE yayınlanmış daha uzun+güvenli bir okumayla karşılaştırabilir. Ölçüm (2567 roi olayı):
> kesilme çiftlerinin **9/13'ünde KISA (bozuk) okuma ÖNCE** geliyor, dolayısıyla kapı onları göremez.
> Gerçek kapsama: **8 doğrulanmış kesilme hatasının 2'si** kapı tarafından yakalanır (isabet 2/2 = %100,
> ama kapsama %25). Önceki sürüm bu tabloyu "kapı (a)'yı yakalar" diye özetliyordu; o ifade
> **kapının çalışma zamanında BİLEBİLECEĞİNİ değil, veriyi geriye dönük tarayan bir simülasyonu**
> yansıtıyordu.

Zayıf-okuma-önce vakalarında SDK tarafındaki tek koruma `ManualReviewMinOcr` eşiğidir. Sınır
kaldırılamaz: ileriye bakmak, emiti geciktirmek demektir (bariyerde gecikme).

### PMS TARAFINDA UZLAŞTIRMA (önerilen — bu boşluğu KAPATAN tek yol)

Zayıf okuma önce geldiğinde SDK olayı **geri çağıramaz**, ama PMS iki olayı da almıştır ve aynı
kuralı kendisi uygulayabilir. ÖLÇÜLDÜ (2567 canlı olay): "zayıf önce" vakası **26**; bunların
yalnız **%31'inde** güçlü olayın `Candidates` kümesi zayıf plakayı taşıyor (ve o sekizin hepsi
KESİLME çiftidir — çözümleyici doğal olarak iki uzunluğu da üretir). **İkame çiftlerinde bu bağ
YOKTUR**, yani `Candidates`'a güvenerek uzlaştırma yapılamaz.

📊 **2026-08-25 — BU KURALIN KAPSAMASI VE RİSKİ ÖLÇÜLDÜ (n=246 kurtarılabilir geçiş, n=201 gerçek iki-araç çifti):**

| kural | kapsanan geçiş | yanlış birleştirme riski | seçim isabeti |
|---|---|---|---|
| **lev = 1** (aşağıdaki hâli) | 128 (**%52**) | **1** çift | **%86** [79–91] |
| **lev ≤ 2** ← *önerilen genişletme* | **177 (%72)** | **2** çift | **%84** [71–91] |
| mesafe sınırsız | 246 (%100) | **201** çift 🔴 | %77 [66–85] |

🔑 **Mesafe sınırı KEYFİ DEĞİL — konvoyu parçalanmadan ayıran tek şey:** ±20 sn içinde iki AYRI gerçek aracın bulunduğu 201 çiftin **%99'u lev≥3**. Sınırı kaldırmak kuralı gerçek araçları birleştirmeye başlatır. ✅ Ama **lev-2'ye çıkarmak** +20 puan kapsama karşılığında **+1** riskli çift — ve seçim isabeti o bantta bozulmuyor (%86 ↔ %84, örtüşüyor).
⚠️ Risk sayısı bir **ÜST SINIRDIR**: kural birleştirmeden önce ek bir *aynı-araç* şartı (yörünge/kamera yönü) arayabilir; burada hiçbir ek şart varsayılmadı.
⛔ Bu ölçüm kuralın **nasıl** kurulacağını iyileştirir; **yapılmalı mı** sorusu PMS'in bugünkü mükerrer davranışına bağlıdır (bkz. ana README #87/#92).

Uygulanacak kural (SDK'nın kapısının aynası, ama İLERİ yönlü — PMS geçmişi düzeltebilir):

```csharp
// Yeni olay E geldiğinde: son 15 sn içinde AYNI ya da DİĞER kameradan yayınlanmış,
// E'den DAHA DÜŞÜK güvenli ve E'ye 1 karakter uzaklıkta bir kayıt var mı?
//   · aynı uzunluk, tam 1 karakter farklı   → İKAME
//   · tam 1 karakter kısa ve E'nin ön-eki    → KESİLME
// Varsa: o kaydı E ile DÜZELT (ya da operatör kuyruğuna al). Bariyer zaten açılmıştır;
// düzeltilmesi gereken şey KAYIT ve ÜCRETLENDİRMEdir.
```

**Çalışan referans uygulama `samples/PmsOrnek/Program.cs` içindedir** (`UzlastirGecmisi` +
`BozulmusMu`): son 15 sn penceresini tutar, yön şartını uygular ve hem İKAME hem KESİLME
biçimini kapsar. Kopyalayıp kendi kayıt katmanınıza bağlayabilirsiniz.

⚠️ **BU DÜZELTME SDK'NIN KENDİ KAPILARINA YAYILMADI — çünkü orada aynı kusur YOK (ölçüldü).**
SDK'nın çapraz-kamera kapıları da aynı yön şartını kullanıyor, ama nedensel tetiklemeleri
incelendiğinde **Δ < 0,02 olan 5 bayrağın 5'i de HAKLI** çıktı; tek haksız bayrak **Δ = 0,092**'de.
Δ eşiği eklemek kapının isabetini **%94 → %92'ye DÜŞÜRÜR** (17 → 12 bayrak, haksız hâlâ 1).
**Sebep, eylemlerin farklı olması:** PMS kuralı bir kaydı **ÜZERİNE YAZAR** — yön yanlışsa doğru
kayıt bozulur, o yüzden yönün güvenilir olması şart. Kapı ise yalnızca **insana gösterir** —
iki okuma çelişiyorsa hangisinin bayraklandığı önemli değil, bakılması zaten doğrudur.
*Aynı koşul, iki farklı yerde, farklı hata modu.* Bir düzeltme yayılmadan önce hedefteki
mekanizmada da geçerli olup olmadığı ÖLÇÜLMELİDİR.

⚠️ **YÖN ŞARTI TEK BAŞINA YETMİYOR — asgari GÜVEN FARKI şartı eklendi (2026-08-02, ölçüldü).**
Yalnız "yeni okuma daha güvenli" demek, iki okuma neredeyse eşit güvendeyken **bilgi taşımıyor**:
23 canlı tetiklemenin **2'si DOĞRU bir kaydı bozardı** ve ikisi de **Δ güven < 0,01** idi
(`34YF7170`→34YFT170 Δ0,008 · `25GE895`→25GE891 Δ0,001). `Δ ≥ 0,02` şartıyla:

| kural | tetikleme | doğru düzeltme | ZARARLI |
|---|---|---|---|
| yalnız yön şartı | 23 | 18 | **2** |
| **+ Δ ≥ 0,02** | 17 | 16 | **0** |

İki doğru düzeltme kaybedilir ama **doğru bir kaydı bozmak, eksik düzeltmeden kötüdür** — takas
bilinçlidir. Eşik 0,02–0,15 aralığının tamamında isabet %100 olduğu için bıçak sırtında değil;
yine de veri büyüdükçe yeniden ölçün.

⚠️ **lev-2'ye AÇILMADI (ölçüldü ve reddedildi).** lev-2 sınıfı gerçektir ve yakalanmayan hataların
çoğu oradadır — ama uzlaştırmayı `lev ≤ 2` yapmak **artımlı** olarak 20 yeni tetikleme getirip yalnız
**4'ünde** gerçek bir hatayı düzeltiyor ve **2 yeni zararlı** düzeltme üretiyor (artımlı isabet ~%20;
lev-1'in isabeti %90). lev-2 bu kuralın değil **insan incelemesinin** işidir.

⚠️ Bu, bariyer kararını değil **kaydı** düzeltir — asıl zarar zaten orada (yanlış araca yazılan
giriş/ücret). Ölçülen etki: 26 vakanın 25'i bariyeri otomatik açmıştı.

#### Aynı pencereden bedava gelen bir TRİYAJ ölçütü: **iç mutabakat**

Aynı 15–20 sn'lik pencere, düzeltmenin yanında bir de **güven derecelendirmesi** verir: karşı kamera
aynı geçişte **birebir aynı dizgiyi** okudu mu? Bu, dış referans ya da "aynı araç" varsayımı
gerektirmez — yalnız dizgi eşitliği. Ölçüldü (**4162** canlı roi olayı, n=**1101** gözle doğrulama;
ilk ölçüm 2591 olay / n=981 idi — **örneklem 1,6× büyüdü, tablo değişmedi**):

| iç mutabakat | popülasyon payı | doğruluk | %95 alt sınır | doğrulanmış hataların payı |
|---|---|---|---|---|
| **karşı kamera AYNI dizgiyi okudu** | **%75,4** | **%98,5** (737/748) | **%97,4** | **%6,9** |
| karşı kamerada farklı okuma var | %13,8 | %36,8 (75/204) | %30,4 | %80,6 |
| karşı kamerada okuma yok | %10,8 | %86,6 (129/149) | %80,2 | %12,5 |

Yani trafiğin **dörtte üçü**, doğrulanmış hataların yalnız **%6'sını** taşıyan bir kovaya düşüyor.

**Ve bu bilgi OCR güveninin ÜSTÜNE geliyor** — aynı güven bandı içinde bile fark büyük:
`≥0,99` **+14,3 puan** (407/409 vs 46/54) · `0,95–0,99` **+61,9** (145/150 vs 8/23) ·
`0,85–0,95` **+68,8** (146/150 vs 18/63) · `<0,85` **+95,3** (**39/39** vs 3/64).
> ✅ **REPLİKASYON (2026-08-04):** bu dört fark ilk kez n=981'de ölçülmüştü (+15,7 / +62,3 / +64,2 / +100).
> Örneklem 1101'e çıktığında dördü de **aynı yerde kaldı**. `<0,85` hücresi 15/15 → **39/39** oldu ve
> alt sınırı **%91**'e yükseldi — yani "düşük güvenli ama karşı kamera onaylı" okumalar bu sahada
> hâlâ **sıfır hata** veriyor.

⚠️ **"Çelişki = hata" DEĞİLDİR** ve öyle kullanılmamalıdır: o kovadaki karşı-kamera okumalarının
**%45'i lev ≥ 4 uzaklıkta**, yani büyük olasılıkla o sırada geçen **başka bir araçtır**. Ölçüm
yalnız *mutabakat* yönünde kullanılabilir: mutabakat GÜÇLÜ bir doğruluk işareti, çelişki ise
yalnız "onaylanmadı" demektir.

⚠️ **Döngüsellik kontrol edildi:** hükümlerin bir kısmı zayıf kırpıntılarda karşı kamera kanıtıyla
verildi. Yalnız kutu alanı ≥ 10.000 px² olan (yani hükmü kendi kırpıntısından verilebilen) olaylarla
sınırlandığında desen aynen sürüyor: mutabakat **%98,3 [95,8]** vs çelişki **%46,5**.

**Nasıl kullanılır:** bariyer davranışını DEĞİŞTİRMEZ (emit anında karşı kamera okumasının ancak
yarısı gelmiştir). Denetim izinde ve ücretlendirmede **triyaj** için kullanılır: mutabakatlı kayıtlar
düşük öncelikli, mutabakatsızlar insan incelemesine önceliklidir. Sahaya özel bir sayı olduğu için
kendi kurulumunuzda yeniden ölçün.

#### Aynı ölçüt DIŞ bir ikinci okuyucuyla da sınandı — desen aynı, sinyal daha da keskin

Bu sahada kameranın kendi ANPR'ı bağımsız bir ikinci okuyucudur (ürün ona bağımlı DEĞİLDİR; yalnız
ölçüm için kullanıldı). Aynı geçişte (±20 sn, aynı kamera) onun okumasıyla karşılaştırıldığında,
**gözle doğrulanmış** olaylarımızda:

| durum | popülasyon payı | bizim doğruluğumuz | %95 alt sınır | kapsama |
|---|---|---|---|---|
| ikinci okuyucu **AYNI** plakayı okudu | %78 | **579/579 = %100** | %99,3 | %28 |
| ikinci okuyucu **FARKLI** okudu (**lev ≤ 3**) | %7 | **17/119 = %14,3** | %9,1 | %76 |
| ikinci okuyucu **HİÇ okumadı** | %10 | **96/105 = %91,4** | %84,5 | %40 |
| *(karşılaştırma)* lev ≥ 4 farklılık | %5 | 24/35 = %68,6 | %52,0 | %35 |

**Üçüncü satır önemli:** ikinci okuyucunun geçişi hiç kaydetmemesi bizim doğruluğumuz hakkında
**neredeyse hiçbir şey söylemiyor** (%91,4, genel ortalamanın yakınında). Yani sinyal "kaydın
varlığı" değil, **iki okumanın uyuşup uyuşmadığı**.

**Uzaklık arttıkça haklılığımız MONOTON düşüyor** (kapsama ~%76'ya çıktıktan sonra):
`lev 1` → **%18,3** · `lev 2` → **%11,8** · `lev 3` → **%8,0** · `lev ≥ 4` → **%68,6**.
Yani iki okuma birbirine ne kadar yakınsa hata bizde olma ihtimali o kadar düşük; uzaklaştıkça
artıyor — ta ki `lev ≥ 4`'te sıçrayana kadar, çünkü orası artık **başka araç** bölgesidir.

**`lev ≤ 3` kısıtı zorunludur ve ölçümle konmuştur.** Filtresiz bakıldığında "uyuşmazlıkta %29,7
haklıyız" çıkıyor; lev'e göre ayrıştırınca **lev ≥ 4 farklılıklarda %68,6 ile BİZ haklıyız** görülüyor
— yani o kayıtlar aynı araç hakkında bir uyuşmazlık değil, o sırada geçen **BAŞKA bir araç**. Onları
karıştırmak sinyali yaklaşık üçte bir oranında sulandırıyordu.

Gerçek tablo: **mutabakat neredeyse kusursuz bir doğruluk işareti; aynı araç hakkındaki uyuşmazlıkta
hatalı taraf yaklaşık %85 ihtimalle BİZİZ**, ve biz yanıldığımızda ikinci okuyucu vakaların
**%90'ında** gerçeği okumuştur.

⚠️ Bu, ikinci okuyucuyu referans yapmaz — o da yanılır (lev ≤ 3 uyuşmazlıkların %15'inde haklı olan
biziz). Sonuç şudur: **ikinci bir okuyucunuz varsa, aynı araç olması MAKUL (lev ≤ 3) uyuşmazlıkları
bir hata-tespit tetikleyicisi olarak kullanın**; mutabakatı düşük öncelikli kabul edin, uzak
farklılıkları yok sayın (başka araçtır). Ürün bunu gerektirmez, ama varsa çok ucuz bir kazançtır.

(b) sınıfı için tek mekanizma `KnownPlates` snap'idir (lev≤1 ekleme) — **düzenli araçlar için kapanır,
ziyaretçiler için kapanmaz.** Bu, ürünün bilinen ve yapısal bir sınırıdır. Pencereyi büyütmek çözüm
değildir: ölçümde (a) vakalarının tamamı ≤9 sn, (b) ise 4049 sn.

## `CrossCameraVariantWindowSeconds` — çapraz-kamera **ikame** kapısı (varsayılan 15 sn, açık)

Kesilme kapısının kardeşi ama **ayrı knob**. Bir okuma, pencere içinde yayınlanmış **aynı uzunlukta,
tam 1 karakter farklı ve daha güvenli** bir okumanın bozulmuş hâli ise `LowConfidence = true` yapılır.
Eylem yine bastırma değil, plaka yine değiştirilmez. `0` = kapalı.

**İki ayrı sayı — karıştırmayın:**
- **SINIFIN BÜYÜKLÜĞÜ** (veride bu hata ne kadar var): 2567 roi olayında **38 çapraz-kamera lev-1 çifti**,
  bunların çoğu bariyeri otomatik açmıştı. Gözle doğrulananlarda zayıf taraf **%92** oranında gerçekten
  yanlıştı. Bu ölçüm zaman yönünden bağımsızdır (soru: "bu sınıf veride var mı").
- **KAPININ KAPSAMASI** (kapı bunların kaçını GÖREBİLİR): **23 bayrak · 16 doğrulanmış hata · isabet %94**.
  Kapı geriye dönük olduğu için sınıfın yalnız yarısını görebilir (ayrıntı aşağıda).

> **Neden ayrı knob:** iki kapının ölçülmüş isabeti farklıdır — kesilme yakaladıklarında **karşı örnek
> yok** (2/2; kapsaması %25, bkz. yukarısı);
> ikame **17/19 (%89), 2 karşı örnekli** (`34YF7170` ← `34YFT170` · `25GE891` ← `25GE895`). Sahada
> gürültülü bulunursa ikame kapısı tek başına kapatılabilmeli, kesilme kapısı kaybolmamalıdır.
> Güven farkı iki sınıfı **ayırmıyor** (karşı örnek 0,092 farkla gelirken hatalar 0,003–0,166'ya
> yayılıyor) → ek eşik denendi ve ölçümle reddedildi.

**Bu kapının asıl değeri:** bu sınıf, bir OCR hatasının **başka bir gerçek aracın plakasına**
düşebildiği tek doğrulanmış yoldur — üç örnek: `25MT297` ← `25MT299` · `25MT245` ← `25MT246` ·
`25YBN279` ← `25YBN278`; altı dizgi de bu sahada gerçek filo aracı
(29 ve 43 olay). Orada sonuç geçerli bir TR plakası, whitelist'te var ve güveni eşiğin üstünde
(0,894) → **gramer, snap ve güven korumalarının hiçbiri göremez.** Bu sahada yerleşik araçların
**%17'sinin** lev-1 komşusu da gerçek bir araçtır, yani sınıf yapısal olarak mevcuttur.

`lev≤2` ölçüldü ve reddedildi. **Güncellenmiş ölçüm (2507 roi olayı, düzeltilmiş yer-gerçeği):**
**nedensel (kapının gerçekten görebildiği) ölçümle:** lev≤1 → **23 bayrak, +0,74 puan** ·
lev≤2 → **55 bayrak, +1,60 puan**. Sınıf büyüklüğü ölçümünde (zaman yönünden bağımsız) isabetler
lev≤1 %92 · lev≤2 %85–86 çıkmıştı; yani lev≤2 daha çok hata görür ama isabeti düşer ve maliyeti
iki katından fazla olur.

**KAPININ BİLİNEN SINIRI (canlı örnek):** `25TZZ368` (ocr 0,867) aslında `25 TZV 369`'ün yanlış
okumasıydı ve bariyeri otomatik açtı; aynı geçişte karşı kamera 4 sn önce `25TZV369`'ü 0,997 ile
net okumuştu. Aradaki fark **2 karakter** olduğu için kapı tetiklenmedi. Bu sınıf bilinçli olarak
kapsam dışıdır — yakalamanın bedeli yukarıdaki isabet düşüşüdür.

**KAPSAMA — kapı bu sınıfın YAKLAŞIK YARISINI görebilir (yapısal sınır).** Kapı **geriye dönüktür**:
bir okumayı yalnız DAHA ÖNCE yayınlanmış daha güvenli bir okumayla karşılaştırabilir. Zayıf okuma
**önce** gelirse (karşı kamera aracı sonra okursa) kapı onu göremez. ÖLÇÜM (2567 roi olayı): ±15 sn
içindeki 38 çapraz-kamera lev-1 çiftinin **17'sinde zayıf taraf ÖNCE** geldi ve 16'sı otomatik açtı.
Kapının gerçek kapsamı: **23 bayrak · 16 doğrulanmış hata · isabet %94** (n=17).

> ⚠️ Bu sayı bir DÜZELTMEDİR. Önceki sürümde "44 bayrak / 31 hata" yazıyordu; o simülasyon pencereyi
> İKİ YÖNLÜ tarıyordu, oysa çalışma zamanında yalnız geçmiş okumalar bilinir. İsabet aynı (%94) ama
> **kapsama yarı yarıya**. Zayıf-önce vakaları için tek koruma `ManualReviewMinOcr` eşiğidir.

**Operatör yüküne maliyeti (ölçüldü, 2567 roi olayı):** **+0,74 yüzde puanı** (%7,36 → %8,10).
Karşılaştırma: aynı hatayı `ManualReviewMinOcr`'ı 0,85 → 0,95 yaparak azaltmak operatör yükünü
%6,6 → %14,2'ye (iki katından fazla) çıkarıyordu ve ölçülüp reddedilmişti. `lev≤2` seçilseydi
maliyet **+1,60 puan** olurdu (55 bayrak).

## BİLİNEN SINIR: kuyruktaki ikinci araç (ve onu izleyen sayaç)

Kapıda **arka arkaya** geçen iki aracın plakaları **1 karakter farklıysa** (ör. aynı filonun
`25 ABC 123` / `25 ABC 124` seri plakaları), ikinci okuma birincinin izine katılabilir ve **ikinci
araç için olay üretilmeyebilir**. Ayırma yalnız **üç kanıt birlikteyken** yapılır: metin farklı
(lev>0) · okuma güçlü (`ocr ≥ 0,99`) · kutular **ayrık** (IoU = 0). Güven eşiğinin altındaki
ikinci okuma bilinçli olarak birleşir — aksi hâlde aynı aracın yanlış-okuma varyantları çift olay
üretirdi (ölçülmüş, daha maliyetli bir hata).

**Bu sahada ölçülen taban oran (2026-08-02):** kameranın bağımsız kaydında aynı kameraya ≤8 sn
arayla gelen **255 farklı-plaka çiftinin yalnız 2'si (%0,8) lev≤1**. Yani tehlike sınıfı doğal
olarak nadirdir — ama **sıfır değildir** ve seri plakalı filolarda payı artar.

**`absorbedAfterEmit` (yeni, 2026-08-02):** bu riskin **tek gözlenebilir imzası**. Bastırılan okuma
hiçbir olay üretmez, dolayısıyla olay günlüğünden görülemez; sayaç tam bu boşluğu kapatır.

- Sıfırdan büyük olması **kusur DEĞİLDİR** — aynı aracın varyantları da oraya düşer (istenen birleştirme).
- Kusur işareti, eşlik eden çiftlerin (`RecentAbsorbed`, son 16) gözle bakıldığında **iki AYRI araca**
  ait çıkmasıdır. Yani sayaç bir alarm değil, bir **avlanma yeri**.
- Seri plakalı bir filoya hizmet veriyorsanız bu sayacı izleyin; artıyorsa çiftleri inceleyin.

  📊 **BU SAHADA ÖLÇÜLEN SONUÇ (2026-08-06, 93 ayrık absorpsiyon çifti):** bastırılan okumanın
  **gerçek bir ikinci aracı** yuttuğu **tek bir vaka bile** görülmedi → gerçekleşme oranı **%0,
  %95 üst sınırı %4,0**. Senaryo hiç oluşmadığı için değil: 2026-08-05'te **üç filo aracı 10 saniye
  içinde** geçti (konvoy) ve sistem üçünü ayrı olay olarak yayınladı; ayrıca bir geçişte yutulan
  varyantın dizgisi (`25MT246`) sahada **gerçek bir araçtı ve o gün gerçekten geçti** — ama
  **133 saniye sonra**, ve kendi iki olayını sorunsuz aldı.

  🔑 **RİSKİNİZİ SİTENİZDEN OKUYUN — bu bir AYAR değil SAHA ÖZELLİĞİDİR.** Bu sahada ölçüldü:

  | plaka sınıfı | lev≤1 komşusu da sahada GERÇEK |
  |---|---|
  | **ardışık seri (filo) plakası** | **53/172 = %31** [24–38] |
  | tekil plaka | 21/311 = **%7** [4–10] |

  **Filo plakası olmak bu riski 4,6× artırıyor**, ve bu sahadaki gerçek plakaların **%36'sı** bir
  filo serisinde. Kendi sahanızda aynı sayıyı `scripts/audit_whitelist.py` ile hesaplayabilirsiniz.
  Filo payı yüksekse `absorbedAfterEmit`i **izlemek zorunludur**; düşükse taban oran ihmal
  edilebilir düzeydedir.

## BİLİNEN SINIR: **tek araç, İKİ olay** (yukarıdakinin AYNA GÖRÜNTÜSÜ)

Yukarıdaki sınır iki aracın **tek olaya birleşmesini** anlatır. Bunun tersi de olur ve
**daha sıktır**: tek bir araç, tek bir geçişte **iki farklı plakayla iki olay** üretebilir.

**Mekanizma.** Araç kadrajda ilerlerken OCR okuması kareden kareye değişebilir. İz kimliği
**il + harf bloğundan** kurulduğu için, harf bloğu değişen her okuma **YENİ BİR İZ** açar;
karakter oylaması o izlerin hiçbirinde birikmez ve her iz kendi olayını yayınlar.
Görsel olarak doğrulanmış uç örnek: tek bir araç, tek geçişte **beş ayrı iz**
(`25HME747` · `25HRE647` · `25HME641` · `25HMK631` · `26HMK65`) — kutu koordinatları düzgün bir
yörünge çiziyordu, yani kalabalık sahne değil, **tek aracın izinin dağılması**.

**Ölçülmüş oranlar (bu sahada, 24 gün, 10.840 roi olayı):**

| | oran |
|---|---|
| parçalanmış olay (aynı kamera, ≤2 sn, >1 iz) | **%9,2** [8,7–9,7] ⓘ *08-21'de %10,9 yazıyordu; meşru iki-araç geçişleri ayıklandı* |
| parçalanmış kümede **eşik üstü olay ≤1** | **%59,5** [55–63] ← eşik zaten tekleştiriyor |
| parçalanmış kümede **eşik üstü olay ≥2** | %39,3 (230 küme) |
| ⤷ ⚪ **MEŞRU iki-araç geçişi** (tanık İKİ plakayı da gördü) | **104** → *kusur DEĞİL* |
| ⤷ 🔴 **GERÇEK çift bariyer kararı** | **126** → **günde ~5,1** |
| ⤷⤷ biri gerçek biri hayalet | %72 → günde ~3,7 |
| ⤷⤷ **ikisi de sahada GERÇEK plaka** | **%13** → **günde ~0,6** |

> 🔴 **DÜZELTME (2026-08-22):** bu tablo 2026-08-21'de *"günde ~9,4 çift karar, yarısında iki
> plaka da gerçek (~5,2/gün)"* diyordu. **Yanlıştı.** Ölçüm, bir kapıda **2 saniye arayla geçen
> İKİ AYRI ARACI** da çift karar sayıyordu; oysa orada iki olay **DOĞRUDUR**. Bağımsız tanıkla
> ayıklandı (kamera aynı pencerede iki plakayı da kaydettiyse iki araç var): **104 küme meşrumuş.**
> En ağır alt sınıf (*"ikisi de gerçek abone"*) **~8× şişkindi** — 5,2 değil **0,6/gün**.
> ⚠️ Ayırt edici **dizgi farkı DEĞİL tanıktır**: bu sahada tek bir aracın beş parçaya bölünüp
> birbirinden çok farklı beş dizgi ürettiği belgelidir, yani *"dizgiler uzak → iki araçtır"*
> demek yanlış olurdu.

🔴 **TÜKETİCİNİN BİLMESİ GEREKEN:** aynı geçiş için elinize **iki farklı plakalı iki olay**
gelebilir ve **ikisi de `ocr ≥ ManualReviewMinOcr` olabilir** — yani ikisi de "bariyeri aç"
kalitesindedir. Yarısında **her iki plaka da sahada gerçek bir araca aittir**, dolayısıyla
"hangisi hayalet" diye ayıklamak İŞE YARAMAZ.

⚠️ **Bu, doğruluk metriğinde HATA olarak GÖRÜNMEZ.** Doğruluk olay başına ölçülür ve o iki olaydan
**biri doğrudur**. Zarar olay düzeyinde değil **GEÇİŞ düzeyinde** doğar: bir geçiş, iki kayıt.

✅ **PMS tarafında uzlaştırma (bu boşluğu kapatan tek yol).** SDK bu olayları bilerek yayınlar
(bastırmak, gerçekten arka arkaya geçen iki aracı kaybetmek demektir — yukarıdaki sınır).
Tüketici tarafında önerilen desen:
- aynı `CameraId` üzerinde **≤2 sn** içinde gelen olayları **tek geçiş adayı** say;
- aralarından birini seç (en yüksek `OcrConfidence` makul bir başlangıçtır) ve diğerini
  **denetim kaydı** olarak sakla — silme;
- 🔑 **SEÇİM KURALI İÇİN ÖLÇÜLMÜŞ TEK GERÇEK İPUCU (2026-08-22):** çiftin bileşimi üç türlü
  olabiliyor — **biri gerçek biri hayalet %72** · **ikisi de gerçek %13** · ikisi de hayalet %15.
  **Hayalet/gerçek çiftlerinde `OcrConfidence` bilgi TAŞIYOR:** en yüksek güven, sahada gerçek
  olan plakayı **%88 [80–93]** seçiyor. **Ama "ikisi de gerçek" çiftlerinde TAŞIMIYOR** — orada
  dört ayrı seçim kuralı da yazı-turadan ayırt edilemedi.
  ⇒ Pratik sonuç: en yüksek güveni seçmek **hayaleti elemekte iyi**, **doğru aboneyi seçmekte
  değil.** Bu yüzden **diğerini SİLMEYİN** — bugün elinizde iki kayıttan biri doğru; tekleştirip
  yanlışını tutarsanız o güvence de gider.
- ⚠️ **seçim kuralını ölçmeden "iyileştirmeye" çalışmayın:** bu sahada kutu boyutu, ocr ve
  ikisinin çarpımı üzerinden dört seçim kuralı denendi, **dördü de %50'den ayırt edilemedi**
  (en iyisi %60 [41–77]). Eşik zaten kümelerin %59,5'ini tekleştirdiği için kazanılacak alan dardır.

⛔ **Ölçümün sınırı:** bu satırlar ANPR'ın **çıktısını** anlatır. Çift olayın fatura/bariyer
tarafında ne yaptığı **tüketici sisteme bağlıdır** ve burada ölçülmemiştir.

## 🔎 KAMERA YERLEŞİMİ ÖZ-TANISI — yer-gerçeği ve referans kamera GEREKTİRMEZ

**İlk günden çalıştırabileceğiniz tek tanı budur.** Doğruluk ölçmek için gözle doğrulama gerekir
(pahalı, haftalar sürer); ama **hangi kameranın kötü yerleştirildiğini** üç sayaçtan okuyabilirsiniz.
`AnprRuntime.CameraStatus` → `ICameraStatus` üzerinden süreç içinde, ek altyapı olmadan:

```csharp
foreach (var c in runtime.CameraStatus)
{
    double per1k = 1000.0 / Math.Max(c.FramesProcessed, 1);
    Console.WriteLine($"{c.CameraId}  şekil={c.AspectDropped * per1k:F2}  " +
                      $"kalite={c.QualityDropped * per1k:F2}  gramer={c.TrRejected * per1k:F2}");
}
```

**Üç kapı fiziksel olarak FARKLI şeyler ölçer, dolayısıyla sorunu LOKALİZE eder:**

| sayaç | ne eler | yüksekse sorun nerede |
|---|---|---|
| `AspectDropped` | kutu şekli plakaya benzemiyor | **çerçeveleme / AÇI** (plaka eğik görülüyor) |
| `QualityDropped` | kırpıntı bulanık · çok karanlık · patlamış | **POZLAMA / ODAK / enstantane** |
| `TrRejected` | OCR çıktısı TR gramerine uymuyor | aşağı akış **semptomu** (üsttekilerin sonucu) |

**BU SAHADA ÖLÇÜLEN (2026-08-06, 2 gün 11 saat · kamera başına ~360.000 kare, 1000 kare başına):**

| kapı | giriş | çıkış | oran |
|---|---|---|---|
| `AspectDropped` | 1,02 | 4,39 | **4,3×** |
| `QualityDropped` | 0,24 | 4,43 | **18,6×** |
| `TrRejected` | 0,47 | 2,14 | **4,6×** |
| *yayınlanan olay* | *836* | *979* | *1,17×* |

🔑 **EN ÖNEMLİ SATIR SONUNCUSU: sağ kalan olaylar neredeyse eşit.** Olay bazlı kalite metrikleri
çıkış kamerasını yalnız **1,1–1,5×** kötü gösteriyor — **çünkü üç kapı, çıkıştan 4–19× daha çok
okumayı zaten elemiş.** Yani **ön-filtre sayaçları, olay bazlı metriklerden çok daha DUYARLI bir
kamera sağlığı göstergesidir**; açığı onlar soğurduğu için olay akışında görünmez.

⚠️ **BU SAYILAR SİZİN SAHANIZ İÇİN GEÇERLİ DEĞİLDİR** — mutlak eşik vermiyoruz, çünkü sahne
(ışık, açı, mesafe, trafik) her sahada farklıdır. Anlamlı olan **kameralar ARASINDAKİ orandır**.
Tek kameralı kurulumda bu tanı çalışmaz; iki kameradan biri diğerinden birkaç kat yüksekse,
o kamera bakım listesinin başındadır.

⚠️ **NEDENSELLİK KURULMUŞ DEĞİLDİR.** Sayaçlar birlikteliği gösterir; ROI'de plaka olmayan yazılı
bir nesne de (tabela) benzer bir imza üretebilir. Ama bu sahada en büyük asimetri **bulanıklık/
pozlama** kapısında ve bağımsız olarak ölçülen plaka genişliği de aynı yönü gösteriyor
(çıkış medyan 150 px · giriş 164 px) — üç işaret aynı yere bakıyor.

### ⚠️ Gece performansı: kötü ama sahaya göre değişen ÖNCELİK

Karanlıkta okuma kalitesi belirgin biçimde düşüyor: `ocr < 0,85` payı **karanlıkta %49,1**,
aydınlıkta **%7,2** (6,8×). Bu gerçek bir bozulmadır.

Ama bu sahada **trafiğin yalnız %0,9'u karanlıkta** geçiyor (55/6164 olay) — gündüz çalışan bir
belediye otoparkı. Toplam etkisi **tüm olayların %0,44'ü**. Yani *bu saha için* gece iyileştirmesi
düşük önceliklidir.

> 🔴 **BU SİZİN SAHANIZ İÇİN GEÇERLİ OLMAYABİLİR.** 24 saat çalışan bir otoparkta tablo tersine
> döner ve gece **birincil** sorun hâline gelir. Kendi dağılımınızı ölçmeden bu önceliği devralmayın.
> Ayrıca NASPLE gibi kabul çerçeveleri **gece testini zorunlu tutar**: "bizde gece trafiği yok"
> kabul ölçütünü karşılamaz, yalnız *iyileştirme sırasını* değiştirir.

> ⚠️ **Saat dilimi tuzağı (yaşandı):** olay damgaları **UTC**'dir. Gün-saatine dayanan her
> tabakalamayı önce yerele çevirin. Bu projede `h>=19 or h<6` (UTC) ile yapılan bir "gece"
> tanımı, kovanın **%97'sini gün ışığı olaylarıyla doldurmuş** ve gerçek gece etkisini
> (6,8×) **tamamen gizlemişti** — yanlış tanımla etki "yok" görünüyordu.

### Peki NEYİ hedefleyeceksiniz? — ölçülmüş doz-yanıt

Yukarıdaki sayaçlar **hangi kameranın** kötü olduğunu söyler; bu tablo **ne kadar iyi olması
gerektiğini** söyler. `Box.Width` her olayda vardır, yer-gerçeği gerektirmez.

| `Box.Width` | gerçek plaka ≈ | doğruluk | %95 aralık | n |
|---|---|---|---|---|
| <120 px | <107 px | **%75,1** | [69–80] | 213 |
| 120–160 px | 107–143 px | %86,3 | [83–89] | 553 |
| 160–200 px | 143–179 px | %89,5 | [86–92] | 399 |
| **≥200 px** | **≥179 px** | **%97,1** | [90–99] | 68 |

*(2026-08-07, n=611 gözle doğrulanmış olay. `Box` her iki yandan %6 dolgu içerir → gerçek
detektör kutusu ≈ `Box.Width / 1,12`.)*

**Bu bir birliktelik değil, KATMANLI olarak doğrulanmış bir etkidir:** kutu şeklini (en/boy)
sabit tutunca genişlik farkı her iki katmanda da **aynı yönde ve büyük** kalıyor (+11,4 ve
+14,8 puan); genişliği sabit tutunca şekil farkının **işareti oynuyor** (+2,2 / −4,5 / +2,9).
Yani sürücü **boyut**, şekil değil.

Sektör kılavuzlarının asgarileri (Eocortex 120 px, Dahua 140 px, yaygın 100–150 px) bizim
eğrimizde **%88–90** bandına düşüyor: bunlar **"çalışır" tabanıdır, "doğru okur" hedefi değil.**

> ⚠️ **BUNU BİR OLAY FİLTRESİ OLARAK KULLANMAYIN.** Genişlik popülasyon düzeyinde sürücüdür,
> tek olay düzeyinde öngörücü **değildir**. Ölçüldü: "dar plaka" bir düşük-güven sebebi yapılsa
> kesinliği yalnız **%13–22** olurdu (taban hata oranı %7,9 — kaldıraç 1,4–2,8×), yani
> bayrakların çoğu **doğru** okumaya takılırdı. `MinPlateWidthRatio` ile eleme yapmak da
> okuma sayısını düşürür (**capture rate** feda edilir). Doğru müdahale **yukarıdadır**:
> zoom / odak / konum / ROI. `python3 scripts/plate_size_response.py` bu tabloyu yeniden üretir.

> ⚠️ ≥200 px satırında yalnız **2 hata** var; nokta tahmini kırılgandır, aralığa bakın.
> `Box`'ın plakayı sıkı sardığı da kanıtlanmış değil (en/boy medyanı ~0,43, fiziksel TR
> plakası 0,231) — kutu düşeyde gevşek, dolayısıyla genişlik değerlerini **üst sınır** okuyun.

### Sahanızın "tehlikeli hata" maruziyeti — plaka komşuluk yoğunluğu

Bir yanlış okumanın **ne kadar zarar vereceği** sahanıza bağlıdır ve bunu ölçebilirsiniz:

- Yanlış okunan dizgi **sahada yoksa** → abone eşleşmez, operatör görür. Gürültülü ama görünür.
- Yanlış okunan dizgi **başka bir gerçek aracın plakasıysa** → ücret o aboneye yazılır ve
  denetim izi **geçerli görünür**. Sessiz ve mali. 🔴 Asıl tehlike budur.

İkincisinin olasılığı, sahanızdaki plakaların **birbirine ne kadar benzediğiyle** belirlenir.
`python3 scripts/plate_neighbourhood.py` bunu yer-gerçeği olmadan ölçer. Bu sahada:

| komşuluk | komşusu olan plaka | ort. komşu |
|---|---|---|
| lev ≤ 1 | %16 | 0,41 |
| **lev ≤ 2** | **%49** | 1,71 |
| lev ≤ 3 | %84 | 8,17 |

Bunlar **OCR karmaşası değil, gerçekten ayrı araçlar**: ayırt edici test (*iki araç aynı anda
iki yerde olamaz*) 158 doğrudan komşu çiftinin **0'ında** birlikte geçiş bulmuş. Yani sahada
seri tescilli / filo plakalar var ve bir yanlış okuma kolayca gerçek bir komşuya düşüyor.

**Ne yapmalı:** yoğunluğu yüksek sahalarda (a) düşük-güvenli okumaları bariyer açmak için tek
başına yeterli saymayın, (b) abone eşleşmesi olan olayları da `LowConfidence` bayrağına göre
ayırt edin — bayrak, "abone eşleşti" sinyalinden daha bilgilidir; (c) plaka piksel genişliğini
yukarıdaki tabloya göre büyütün: hata sayısı düşünce tehlikeli hata da düşer.

> ⚠️ Küme *büyüklüğü* raporlamayın: lev≤2 bağlı bileşeni tek-bağlantı zincirlemesiyle karşılıklı
> benzerliği abartır (bu sahada 28 çiftlik bir "kümenin" yalnız 12'si gerçekten lev≤2 çıktı).
> Karara esas olan **doğrudan komşu sayısıdır**.

### Kanıt saklama: seçici koruma (opsiyonel, varsayılan KAPALI)

Kanıt dosyaları iki sınırla budanır: **gün** (`EvidenceRetentionDays`) ve **adet** (`EvidenceMaxFiles`).
Pratikte bağlayan ikincisidir — bu sahada 30 gün ayarlıyken **efektif saklama 4,5 gün** ölçüldü.

Bunun somut bedeli ölçüldü: bir kapı kararına doğrudan bağlı **91 olayın 20'si (%19,8)** kanıtı
silindiği için **kalıcı olarak doğrulanamaz** hâle gelmişti. Silinenler rastgele değildi —
popülasyonun **en eskileriydi**, yani tam da kararın olgunlaşması için bekleyenler. *Karar vermek
için gereken kanıt, karar verilene kadar siliniyordu.*

`EvidencePreserveFlagged = true` yapıldığında `LowConfidence` bayraklı olayların kanıtı
`evidence/preserved/` altına alınır ve **adet tavanının erken tahliyesinden muaf** tutulur.

> 🔑 **KVKK saklama süresi DEĞİŞMEZ.** Korunan dosyalara da `EvidenceRetentionDays` gün kesimi
> **aynen** uygulanır; ayrı bir "koruma günü" ayarı yoktur (teste bağlandı). Bu ayar hiçbir kanıtı
> yasal üst sınırdan uzun tutmaz — yalnız *"adet doldu diye erken silinme"*yi engeller. Korunan
> alanın da kendi tavanı vardır (`EvidencePreserveMaxFiles`, varsayılan 1000): **sınırsız birikim yok.**

Maliyet: bayrak oranı bu sahada olayların **~%3'ü**, yani korunan alan yavaş dolar. Tavan dolduğunda
korunan alan da FIFO ile budanır ve uyarı loglanır — koruma bir **önceliktir**, sonsuz garanti değil.

## Kalıcılık / zero-loss (önemli)
SDK **in-process** olay verir; kalıcılık **PMS'in sorumluluğudur** (SDK sizin DB'nizi bilmez, disk kuyruğu tutmaz):
- **`PlateDetected`** = kolaylık yolu (bounded-dispatcher; çok yavaş işleyicide olay DÜŞEBİLİR — `DroppedEvents`).
- **Zero-loss için `extraSinks` (IEventSink)** geçin: her olay için awaited çağrılır (DÜŞMEZ). DB/dosyaya
  yazan bir `IEventSink` implement edin (hızlı/async yazın; retry/idempotency `EventId` ile sizde). Örnek:
```csharp
class DbSink : IEventSink {
    public async Task PublishAsync(PlateEvent e, CancellationToken ct) => await _db.UpsertAsync(e.EventId, e, ct);
}
var runtime = new AnprRuntime(options, loggerFactory, extraSinks: new[] { new DbSink() });
```

## Lisans

Paketin kendi kodu **MIT** lisanslıdır (`LICENSE`). Pakete gömülü modeller ve bağımlılıklar kendi lisanslarına
tabidir — dosya dosya döküm `THIRD-PARTY-NOTICES.md` içindedir (paketle birlikte gelir).

**Rektifikasyon (WPOD) modeli pakette YOKTUR.** 1.0.x paketlerinde bulunan `wpod_lpd.onnx`, GPL-3.0 lisanslı bir
depodan türemiş ağırlıklardı; MIT lisanslı pakete konamaz. `EnhanceRectify` aşaması model dosyası bulunmadığında
kendiliğinden kapalı kalır, tanıma hattının geri kalanı etkilenmez. **Bu bir doğruluk bedelidir:** aşamanın ölçülen
katkısı, oylama sonrası doğru okumada açılı kümede 61 → 67, temiz kümede 341 → 359 idi; aşağıdaki "Ölçülmüş
doğruluk" tabloları bu aşama AÇIKKEN ölçüldü. Aşamayı kullanmak isteyen, modeli kendi lisans değerlendirmesiyle temin edip
`WpodModelPath` ile gösterir. ffmpeg pakete dâhil değildir; kurduğunuz ikilinin lisansı geçerlidir.

## Notlar
- Web/panel bu pakette YOKTUR (yalnız motor + orkestrasyon). Canlı izleme paneli ayrı serviste kalır.
- Kameralara yalnız salt-okuma RTSP erişimi gerekir; kamera ayarları değiştirilmez.
- KVKK: kanıt kırpıntıları/tam kareler `evidence/` altına yazılır; saklama süresi ve adet sınırı ayarlanabilir.

Sürüm notları ve ayrıntılı entegrasyon: proje deposundaki `docs/entegrasyon-kilavuzu.md`.
