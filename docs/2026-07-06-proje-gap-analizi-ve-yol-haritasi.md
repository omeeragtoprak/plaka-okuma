# OtoparkAnpr — Proje Geneli Gap Analizi ve Yol Haritası (Karar Dokümanı)

Tarih: 2026-07-06 · Kapsam: 2026-07-01 → 07-06 tüm ölçümler · Kurallar: **Türkiye plakaları + zayıf CPU
+ otopark exe entegrasyonu** · Referans dokümanlar: [gece raporu](2026-07-03-gece-calismasi-raporu.md),
[fine-tune runbook](finetune-runbook.md), [dağıtım listesi](deployment-checklist.md)

---

## 1. Yönetici Özeti

**Soru:** Kendi CPU-ANPR yazılımımız, Hikvision kameranın yerleşik ANPR'ını ikame edebilir mi?

**Cevap: Evet — tetik-bölgesi kapsamı ve izleme şartıyla.** 4 günlük kamera-referanslı ölçümlerde kendi
modelimiz (sahaya fine-tune edildikten sonra) kameranın yakaladığı araçların **gündüz %85'ini birebir**
(canlı akşam doğrulamasında 8/9), **gece %84'ünü** doğru okuyor; 24+ saat kesintisiz canlı çalışmada
**0 çökme**, bellek sınırlı (150-300MB), gecikme zayıf-CPU bütçesinde (Mac'te 27.6 ms/kare). Kameranın
kalan üstünlüğü ağırlıkla **donanımsal** (plakaya-optimize pozlama) ve **koşulsal** (alçak güneş/gece).
En kritik bilinmeyen **hedef zayıf PC'nin gerçek hızı** — dağıtım Faz-1 pilotuyla doğrulanmalı.

Ana kazanımlar: sahaya özel **fine-tune'lu model** (kamera kayıtlarıyla otomatik etiketlenen 1468
kırpıntı/127 plakayla eğitildi, her koşulda ölçülü kazanç), **çift-emit kök nedeni** bulunup düzeltildi,
kendi kendine büyüyen **veri fabrikası** ve tek-komut **regresyon bataryası** kuruldu.

## 2. Kanıt Tabanı

### 2.1 Koşula bağlı parity (kamera = %100 referans, birebir eşleşme)

| Koşul | Eski model | Fine-tune (erz) | n | Not |
|---|---|---|---|---|
| Öğleden sonra (16:10) | %81 | **%85** | 26 | GA %66-94 |
| Canlı akşam (4h soak) | — | **%89 (8/9)** | 9 | tek "kaçan" kameranın kendi çift-kaydı |
| Sabah alçak güneş (Cuma 08:35) | %64 | — | 22 | eski modelle |
| Sabah alçak güneş (Pzt 08:31) | %71 | %71 | 14 | **berabere**; aynı 2 aracı ikisi de kaçırdı |
| Gece/IR (kırpıntı-exact) | %75.2 | **%84.0** | 125 kırpıntı | görülmemiş plakalarda %83→%86.4 |

*Küçük n'lerde Wilson %95 GA geniştir; oranlar gösterge. Tüm karşılaştırmalar aynı karelerde (adil A/B).*

### 2.2 Fine-tune etkisi (bağımsız setlerde, aynı gecikmede)

| Ölçüm | Önce | Sonra |
|---|---|---|
| val kırpıntı-exact (n=188, plaka-ayrık) | %67.0 | **%75.0** |
| live1h recall (101 araç) | 86/101 | **87/101** |
| live1h yanlış-emit | 42 | **31 (−%26)** |
| ms/kare | 27.6 | **27.6** |

### 2.3 Stabilite ve kaynak

- **24+ saat canlı soak, 0 restart** (10h + 4h + 10h; fps=2, 2 kamera).
- RSS 150-300MB salınımlı, monoton büyüme yok → **bellek sızıntısı yok** (10 saatlik analiz).
- CPU (Mac): ort %13, boşta ~%4.5. Uyarlanabilir fps çalışıyor.
- Yazılım düzeltmeleri ölçümle doğrulandı: varyant-seti + köprü-birleştirme → diag4 gürültü 17→15,
  live1h yanlış-emit 46→42, recall değişmedi; 49/49 birim test.

### 2.4 Ölçümle ELENENLER (yanlış yatırımı önleyen kanıt)

fps artırma (gürültüyü 2-3× artırıyor, kaçanları kurtarmıyor) · yolo-256/512 · büyük OCR (s_v2) ·
INT8 (Mac'te hız kazancı yok; ConvInteger C#'ta desteklenmiyor) · deskew · piksel-füzyon · dedup
20/30/90 · güven-kapılı erken emit · spinning=false · il-birleştirme kimliği.

## 3. Gap Envanteri

| # | Gap | Kanıt | Kök neden | Kapatma yolu | Maliyet | Etki | Öncelik |
|---|---|---|---|---|---|---|---|
| a | **Gece/IR** | gece exact %84 (fine-tune sonrası; önce %75) | karanlık/far parlaması; kamera IR-optimize pozluyor | gece kırpıntısı birikimi (şu an ~130) → yeniden fine-tune; kamera gece-ANPR'ını ISAPI'den yedek kaynak alma opsiyonu | düşük (hat hazır) | orta | **1** |
| b | **Sabah alçak güneş** | Cuma %64 (eski), Pzt %71=%71 (İKİ model de) | doğu güneşi parlaması — model-bağımsız optik sorun | sabah kırpıntısı birikimi + fine-tune; kalıcı kısmı donanımsal | düşük | orta | **2** |
| c | **Donanımsal pozlama farkı** | zor plakalar iki modelde de aynı kaçıyor (25AFK950, 34NJY035 çapraz-kamera) | kamera plaka-bölgesine pozlama/enstantane optimize ediyor; biz genel akış karesi alıyoruz | **kapatılamaz** (yazılımla); azaltım: tetik-bölgesi (yakın=iyi kare) + veri birikimi | — | düşük-orta | kabul |
| d | **Hedef-PC bilinmeyenleri** | hız katsayısı x3-6 TAHMİN; INT8/spinning Mac'te nötr | henüz hedef donanımda ölçüm yok | Faz-1 pilot: `regression.sh` + deployment-checklist ölçüm sırası | 1 gün | **yüksek** | **1** |
| e | **Kapsam farkı (şerit vs tüm-kare)** | "fazla" emitlerin ~yarısı gerçek ama şerit-dışı (park/kuyruk; görsel kanıtlı, halüsinasyon=0) | kamera şerit-tetikli, biz tüm kareyi okuyoruz | ürün kararı: exe bariyer mantığı için `MinPlateWidthRatio`/ROI (sahada ~0.03-0.04) | yarım gün | orta | **2** |
| f | **Köprüsüz varyant zincirleri** | diag4'te ~5-6 çift-emit kaldı (köprü okuması hiç oluşmayan zincirler) | değişik açılardan tutarlı-yanlış okumalar ayrı iz açıyor | veri büyüdükçe azalır (daha iyi okuma); agresif birleştirme DENENDİ ve recall'e zarar verdi → mevcut denge korunur | — | düşük | izle |
| g | **Tekrarlayan zor plakalar** | `25ACV952` her oturumda sorunlu | muhtemelen kirli/eğik fiziksel plaka | sahada fiziksel kontrol; genel çözüm değil | — | düşük | izle |
| h | **Veri seti sınırları** | 127 plaka, tek saha, tek mevsim (yaz), val n=188 | 4 günlük birikim | hasat sürsün (kış/yağış/kar kritik!); aylık re-tune; val büyüsün | düşük (otomatik) | orta-yüksek (mevsimsel) | **2** |

## 4. Risk Matrisi

| Risk | Olasılık | Etki | Azaltım |
|---|---|---|---|
| Hedef PC beklenenden yavaş (fps=2 bütçeye sığmaz) | orta | yüksek | Faz-1 pilot ölçümü; Fps=1 geri-çekilme (bariyer beklemesi recall'ü korur); INT8 orada yeniden denenir |
| Gece/kış koşullarında regresyon | orta | orta | koşul-etiketli hasat + aylık `regression.sh`; kamera-ANPR ISAPI yedeği |
| Kamera firmware/ISAPI değişikliği (poll-truth kırılır) | düşük | orta | harness kamera-bağımsız RTSP ile çalışır; poll sadece etiketleme/izleme için |
| Tek-model bağımlılığı (erz kötüleşirse) | düşük | düşük | eski global model repo'da; 1 satır rollback; regression.sh kapısı |
| Veri drift (yeni plaka fontu/formatı) | düşük | orta | TR normalizasyon merkezi; hasat sürekli taze örnek toplar |
| Bellek/kararlılık uzun dönem | düşük | orta | 24h kanıtlı; hedefte ilk hafta RSS izleme (checklist'te) |

## 5. Yol Haritası

**Faz 1 — Hedef-PC pilotu (1-2 gün):** deployment-checklist sırasıyla: ms/kare ölç → fps kararını
doğrula → INT8/spinning yeniden dene → `MinPlateWidthRatio` saha ayarı → 1 günlük gölge-çalışma
(kamera ile yan yana, `camera_compare` kabulü: gündüz birebir ≥%80).

**Faz 2 — Exe entegrasyonu (2-3 gün):** `/events/recent` veya webhook bağlantısı → bariyer mantığı
kapsam kararı (ROI) → canlı izleme panosu/log → 1 haftalık paralel çalışma (kamera referanslı haftalık
rapor).

**Faz 3 — Sürekli iyileştirme (aylık döngü):** hasat (özellikle gece + kötü hava) → veri ≥%25 büyüyünce
re-fine-tune → `regression.sh` kapısı (tüm setlerde gerileme yoksa yayınla) → koşul tablosu güncelle.
Kış gelmeden kar/yağış verisi toplamak **kritik**.

## 5b. Güncelleme (2026-07-06 akşamı): Panel + ROI sahada

Canlı karşılaştırma paneli (`/panel`) teslim edildi; kullanıcı iki kameraya da kapı-koridoru ROI'si
çizdi (yalnız BİZİM yazılımın filtresi; kameraya yazılmaz). İlk canlı pencere (10 dk, 5 araç):
**yakalama 5/5, birebir 4/5, gürültü 0** — gap (e) "kapsam farkı" ROI ile sahada kapandı; park-araç
fazlaları bitti. Not: şerit ÇİZGİLERİNDEN otomatik türetilen çokgenler ölçümde zararlıydı (çıkışta
recall çöküşü) → elle çizim doğru yol. Gap (g) örneği yine görüldü (25AFM687→681, tek düşük-güven varyant).

**İkinci canlı pencere (30 dk, 17:10-17:40):** kamera 26 kayıt; 13'ü cadde-geçişi imzalı (2 kamera
≤20 sn — kapıya gelmeyen akşam trafiği). **Kapı trafiğinde: 13/13 birebir (%100), kaçan 0, fazla 4**
(hepsi varyant-kopyası, gap-f). Derin-testte "kaçan" görünen 25AFK950 bu kez kapıdan girdi ve birebir
okundu — cadde-geçişi sınıflandırmasını doğrular. Bariyer kapsamında kamera-parity bu pencerede TAM.

## 5c. Güncelleme (2026-07-07 sabahı): Gece gerçeği, hızlı-araç hipotezi ve operasyonel bulgular

**Gece (20:37→06:35, ROI aktif, ~4 kapı aracı):** biz 1 birebir (`25AFM687` — haftanın zor plakası,
gece 0.94 güvenle doğru!) + 2-3 kaçak. AMA kamera tarafı: **15+ UNKNOWN kaydı** (araç algılandı, plaka
OKUNAMADI), kendi yanlış-okuması (`25DG000`) ve kendi iç çelişkisi (aynı araca `25FC433`/`25FC493`,
53 sn arayla iki kamera). **Gece iki sistem için de zayıf** — kameranın gece "üstünlüğü" UNKNOWN seli
görülünce büyük ölçüde görünürlük yanılsaması (gap-a değerlendirmesi güncellendi: hedef mutlak gece
başarısı değil, kameranın OKUYABİLDİĞİ gece plakalarında parity).

**Hızlı-araç hipotezi (kullanıcı gözlemi, 07.07):** ROI daralınca hızlı aracın ROI-içi süresi kısalıyor;
Fps=2 + boşta-1fps aktivasyonuyla geçiş başına 1-2 kare kalıyor → tek bulanık okuma. Ham sabah-koşusu
taraması (n=18 giriş): fps 1→2→5'te birebir 7→10→11 (fps artışı ROI'li dünyada İŞE YARIYOR — ROI'suz
dünyadaki eski "fps işe yaramaz" bulgusunun takas dengesi ROI ile değişti; ROI kapsam-dışı OCR'ı kestiği
için kare maliyeti de düştü). Kapı-trafiği filtreli kesin tarama SONUCU: giriş 5 kapı aracında fps 1-3 → 4/5, **fps=5 → 5/5**
(hızlı araç kurtuldu; bedel varyant-gürültüsü 13→19). Çıkıştaki 4 "kaçan" görsel teşhiste ÇIKIŞ
KORİDORUNDA HİÇ GÖRÜNMEYEN uzun-menzil yukarı-yol okumaları çıktı (kameranın uzak okuma sınıfı;
bizim kaçağımız değil). **Uygulanan karar: `Fps=4`, `IdleFps=2`** (ROI kapsam-dışı OCR'ı kestiği için
kare maliyeti düştü; Mac'te bütçe rahat, hedef-PC pilotunda yeniden doğrulanacak). Sınıflandırıcıya
not: cadde imzasına ek 'tek-kamera uzun-menzil' sınıfı var (koridor-boş görsel doğrulamayla).

**Operasyonel bulgular (canlı işletmeden):**
- **RTSP oturum çekişmesi:** servis + hasat + yoğun-yakalama aynı kameradan eşzamanlı ana-akış isteyince
  kamera oturum limiti serviste kilitlenme yarattı (giriş kaynağı 70 restart, akış dondu). Dağıtım notu:
  servis çalışırken aynı kameranın ana akışına ek yoğun yakalama açma; hasadı ayrı pencereye planla.
- **Watchdog açığı (yazılım hatası, düzeltilecek):** çıkış kaynağı 'connected=true' görünürken son kare
  3 saat eskiydi ve restart sayacı 0'dı — ffmpeg asılı kalınca kare-yaşı bekçisi tetiklenmemiş.
  Düzeltme: IsConnected/health, son-kare yaşına bağlanacak + kare-yaşı eşiğinde zorunlu restart.
- Kameranın çıkış tarafında tekrar-filtresi kapalı (config'te görüldü) → kameranın kendi çift/çelişkili
  kayıtları; karşılaştırma metodolojisinde dikkate alınıyor.

## 6. Ekler

### 6.1 Metodoloji dersleri (tekrar-üretilebilirlik için)
- Kamera ISAPI araması **son ~20 kaydı** döndürür (pozisyon/zaman filtresi çalışmaz) → gerçek-değer
  yalnız POLL birikimiyle kurulur; karşılaştırmada **zaman filtresi + saat-farkı düzeltmesi şart**
  (kamera ~+61-63 sn ileri). Filtresiz karşılaştırma %38 gibi sahte-düşük sonuç verdi.
- Emit-anı ≠ görülme-anı: görsel doğrulama kırpıntıları **son-görülme** anından alınmalı (ilk montaj
  bu yüzden yanıltmıştı; düzeltilince "gürültü"nün gerçek plakalar olduğu görüldü).
- Küçük n'de tek pencere karar veremez (Cuma %64 vs Pzt %71 aynı koşul) → koşul başına birden çok
  pencere + GA raporlama.
- Torch-backend ONNX export bu mimaride çalışmaz; TF backend + `--no-simplify` + statik batch şart.

### 6.2 Doküman haritası
- Ölçüm günlüğü ve tüm A/B'ler: `docs/2026-07-03-gece-calismasi-raporu.md`
- Eğitim akışı: `docs/finetune-runbook.md` · Dağıtım: `docs/deployment-checklist.md`
- Araçlar: `scripts/camera_compare.py`, `scripts/regression.sh`, `scripts/autolabel.py`, `Anpr.Bench`
- Veri: `dataset/finetune` (1468 kırpıntı/127 plaka), `dataset/diag4` (kamera-etiketli referans set)

## 5d. Güncelleme (2026-07-07 öğleden sonra): Gözlem katmanı + kanıt kırpıntısı + çapraz-kamera

Kullanıcı hedefi netleştirdi: panelde kameranın okuduğu HER plaka bizde de yeşil düşmeli. Üç geliştirme:

1. **Gözlem katmanı**: ROI dışı tespitlerden kare başına en büyük 2'sine OCR (`ObservationMaxPerFrame`);
   olaylar `Scope=gozlem` ile yayınlanır, webhook/bariyer kanalına GİRMEZ, panelde "uzak" rozetiyle
   eşleşir. Aktif-fps yalnız bölge-içi tespitle tetiklenir (cadde trafiği CPU'yu boğmasın).
   (Not: kullanıcı 14:29'da iki ROI'yi panelden temizledi → şu an tüm-kare modu; ROI yeniden çizilirse
   gözlem katmanı devreye girer ve bariyer kanalı yine temiz kalır.)
2. **Kanıt kırpıntısı (E2)**: her emitte en iyi kırpıntı `evidence/` altına JPEG yazılır, panelde
   satır yanında küçük resim. 1-fark ihtilafları artık gözle karara bağlanıyor (ör. 24ACB277/270:
   kanıtta son hane açıdan kesik — kamera muhtemelen haklı; 06FL4666: girişte 4656 okuduk ama
   çıkışta 4666 birebir — açıya bağlı).
3. **Çapraz-kamera eşleşmesi**: cadde-geçişi araçları kamera A'da kayıtlıyken biz B'de okuyabiliyoruz;
   panel artık kalan kırmızıları diğer kameranın okumasıyla eşleyip "⇄ diğer kamera" rozetiyle
   yeşile çeviriyor (dürüst: aynı fiziksel araç bizim sistemce okundu).

**Çok-kareli PİKSEL füzyonu ÖLÇÜLDÜ ve ELENDİ**: s1 setinde degrade 0-3'ün tümünde tek-kare 8/8'e
karşı füzyon 3-5/8 (hizasız kırpıntı ortalaması karakter bulanıklaştırıyor). `MultiFrameFusion`
varsayılan KAPALI; bizim bulanıklık silahımız zaten olasılık-uzayında birikimli oylama + TR-kısıtlı
çözümleme (varsayılan açık).

**Canlı pencere (14:44-15:01, tüm-kare modu)**: kamera 30 kayıt → 22 birebir, 3 tek-fark, 5 kaçan;
kaçanların 4'ü cadde-imzalı ve 3'ünü diğer kameramız okudu; tek gerçek kaçanı (25LK99x) kameranın
kendisi de iki kamerada FARKLI okudu. **25ADY647'yi iki kameramız güven 1.00 ile okudu — Hikvision
hiç kaydetmedi** (kamerayı geçtiğimiz ölçülü örnek). Panel (giriş): birebir %69, sadece-kamera 1.
diag4 regresyonu değişmedi (22/26). CPU %25, RSS ~265MB (tüm-kare + 2 gözlem OCR dahil).

## 5e. Güncelleme (2026-07-10): "Bir araç = bir olay" paketi (uzamsal iz + SKIP_CHAR + adaylar + %85)

Kullanıcı vakası (07-08 16:12, çıkış): aynı araç 4 emit üretti (26ADD884/26ADJ888/25DDS84/25DUS88 —
gerçek plaka 25DDS84, başka geçişlerde defalarca güven 1.00). Kök neden: varyantlar arası lev>1 →
fuzzy birleşme köprü kuramadı; her iz 2 onayla erken emit oldu. Çözüm paketi (hepsi ölçüldü):

1. **Uzamsal iz birleştirme** (`SpatialMergeIou=0.35`, gap ≤1.5 sn, lev ≤4): metin köprüsü olmasa da
   kutusu çakışan taze iz = aynı fiziksel plaka → oylar tek havuzda. diag4: birebir 22/26 KORUNDU,
   gürültü 14→11 (−%21). Birim test gerçek 25DDS84 vakasını yeniden üretiyor: 5 gözlem → TEK emit.
2. **SKIP_CHAR** (`SkipCharLevel=0.3`): eşik-altı hayalet son hane "karakter yok" hipotezine yenilir
   (24ACB27_ sınıfı). 0.5 agresifti (diag4'te −1 birebir), 0.3 birebiri koruyor.
3. **Top-K adaylar** (`CandidateCount=5`): uzlaşı olayında alternatif okumalar taşınır (whitelist
   toleransı + bastırma köprüsü); panelde satır tooltip'i.
4. **Emit-sonrası varyant bastırma** (`VariantSuppressSeconds=30`): yeni emit önceki emitin aday
   listesindeyse VEYA düşük güvenli (<0.85) yeni emit, ≥0.9 güvenli yakın (lev≤2) emitin komşusuysa
   olay yayınlanmaz (loglanır). Kamera-ayrımı korunur (giriş/çıkış ayrı).
5. **EarlyEmitMinOcr=0.85 + ManualReviewMinOcr=0.85** (kullanıcı istedi): iz, uzlaşı güveni %85'e
   ulaşana dek takip edilir (araç yaklaştıkça daha iyi kare); en geç settle'da emit — geç kalan
   düşük güvenliler `LowConfidence` bayrağıyla çıkar (panelde sarı "düşük güven" rozeti).

Regresyon: live1h 87/101 recall + emit sayıları özellikler açık/kapalı BİREBİR AYNI (nötr);
diag4 gürültü −%21; 60/60 birim test. Not: eski "31 yanlış-emit" referansı farklı bayraklı koşuymuş —
güncel taban 34; bataryaya bayraklar sabitlendi (`--spatialiou --skipchar`).
