# PTS Gereksinim Raporu (PTS-RPT-001) × OtoparkAnpr — Karşılaştırmalı Derin Analiz

Tarih: 2026-07-08 · Analiz eden: proje mühendisliği (4+ günlük saha ölçümleri temel alınarak)
Analiz edilen: `docs/otopark-pts-gereksinim-muhendisligi-raporu.md` (PTS-RPT-001 v1.0, Chrome/Claude)
Yöntem: madde madde eşleme (rapor ↔ ölçülmüş durum) + 5 açık kaynak/ticari projenin kod-seviyesi
incelemesi + beyin fırtınası döngüsüyle birleşik backlog.

---

## 1. Yönetici Özeti — Rapor Hakkında Hüküm

**Rapor kaliteli ve büyük ölçüde bizimle aynı yere bakıyor; en büyük değeri mühendislik DİSİPLİNİ
katmanında** (ISO 29148 gereksinim yazımı, MoSCoW, izlenebilirlik matrisi, ISO 31000 risk kaydı,
KPI'lar, faz kapıları). Teknik çekirdek önerilerinin çoğunu biz zaten uygulamış ve ÖLÇMÜŞ durumdayız —
bazılarında rapordan ilerideyiz (tam-dağılımlı TR-kısıtlı çözümleme > FR-23 kural tabanlı düzeltme;
SkiaSharp ile R-06 native-interop riskinin tamamen kaldırılması > OpenCV Mat yaşam döngüsü yönetimi).

Raporun bizden GERİDE olduğu yerler onun suçu değil — F0 öncesi perspektifle yazılmış: AlprNet'i
"birincil aday" sanıyor (biz F0'ı haftalar önce geçtik, AlprNet kitaplığını ölçüp eledik, modellerini
zaten kullanıyoruz), Hikvision'ı güvenilir referans varsayıyor (gece kendi hatalarını ölçtük) ve
entegrasyon yüzeyini `IEventSink` varsayımına bağlıyor (B-01 — bizim Faz-0 bulgumuz: önce mevcut
otopark yazılımının kamerayla GERÇEK protokolü doğrulanmalı).

Rapordan **hemen kapılacak 4 madde bugün uygulandı ve canlıda** (§4.1). 12+ madde önceliklendirilmiş
backlog'a alındı (§4.2). Raporun düzeltilmesi gereken 7 noktası §3'te. Açık kaynak projelerin kod
incelemesi §5'te.

## 2. Raporun Güçlü Yanları (olduğu gibi benimsenenler)

1. **REF-PC tanımı** (4.9): "performans NFR'i referans donanım tanımı olmadan anlamsız" — birebir bizim
   gap-d bulgumuz. i3-9100/AVX2/8GB/SSD tanımını hedef-PC pilot şartnamesine aldık.
2. **EARS + MoSCoW + doğrulama yöntemi kolonlu gereksinim yazımı** — bizim gereksinim dokümanımız
   (2026-07-07) kategori bazlıydı; rapor formatı daha denetlenebilir. Gelecek revizyonda birleştirilecek.
3. **Risk kaydı disiplini** (5.2): özellikle bizde hiç ele alınmamış üç risk: R-10 tek geliştirici
   bağımlılığı (puan 16!), R-13 Windows güncellemeleri, R-14 kamera sabotaj/açı kayması.
4. **KPI tablosu + kış ayrı hedefi** (§7): "kışın karla kaplı plakanın operatöre düşmesi sistem hatası
   değil, tasarlanmış davranıştır" — İzlanda %4.5 bulgumuzla aynı sonuç, KPI diline çevrilmiş: manuel
   onay ≤%8 (kış ≤%15). Aynen alındı.
5. **Faz kapıları + "riskin en ucuz test edildiği yer başlangıçtır"** (F0 mantığı) ve kabulün 4 koşulu
   (gölge eşleşme + sabit set + 72h dayanıklılık + KVKK imza) — kabul çerçevemize eklendi.
6. **Etiketli set şartnamesi** (6.4): ≥5.000 görüntü, ≥%25 gece, ≥%15 yağış/kar, ≥%10 kirli; çift
   bağımsız etiketleme; **model regresyon kapısı: karakter doğruluğu −0,5 puandan fazla gerileyen model
   birleştirilemez** — bizim regression.sh'a sayısal eşik olarak eklenecek.
7. **UC-01 alternatif akışları**: A2 `GecersizFormat` (sessizce atma!), A4 `TespitYok` tanı olayı —
   ikisi de bizim eksiğimiz (aşağıda).

## 3. Raporun Düzeltilmesi/Güncellenmesi Gereken Yerleri (ölçümlerimizle)

| # | Rapor | Bizim ölçülmüş gerçeklik | Önerilen düzeltme |
|---|---|---|---|
| D1 | AlprNet "birincil aday teknoloji" (3.2, ADR-002) | AlprNet kitaplığı OpenCvSharp'a bağımlı (arm64-mac'te native yok; hedef PC'de ek DLL yükü) ve OCR güven hesabında **softmax'ı çifte uygulama hatası** var (probe ile doğrulandı — model çıktısı ZATEN softmax). Biz modellerini (yolo-v9-t + CCT) doğrudan ONNX Runtime + SkiaSharp ile çalıştırıyoruz; F0 çoktan geçildi | ADR-002 şöyle güncellenmeli: "AlprNet modelleri + KENDİ pipeline'ımız; AlprNet kitaplığı kod referansı" |
| D2 | OpenCvSharp + Mat yaşam döngüsü disiplini (6.3, R-06) | SkiaSharp'a bilinçli geçişle R-06'nın native-interop kısmı sınıf olarak YOK; 24h+ soak RSS 150-300MB sızıntısız ölçüldü | K-05 lisans tablosuna SkiaSharp (MIT) yazılmalı; R-06 puanı düşürülmeli |
| D3 | NFR-D1: karakter doğruluğu ≥%99, gündüz plaka ≥%97 | Zor-kırpıntı setimizde karakter %88.8; ama KAPI-kapsamlı canlıda plaka birebir 13/13-22/22. Fark PAYDA tanımı: "tüm zor kırpıntılar" ≠ "kapı geçişi olayları" | NFR-D1'e ölçüm paydası eklenmelidir: "olay-düzeyi, kapı-kapsamlı, çok-kare uzlaşı SONRASI" — aksi hâlde hedef kâğıtta tutturulamaz görünür |
| D4 | Gölge modda Hikvision çıktısıyla eşleşme = geçerleme (4.12) | Kamera gece 15+ UNKNOWN üretiyor, kendi kendine çelişiyor (25FC433/493), çıkışta kendini kopyalıyor; 25ADY647'yi İKİ kameramız güven 1.00 okurken Hikvision hiç kaydetmedi | Gölge eşleşme hedefi "kameranın OKUYABİLDİĞİ plakalarda parite" olarak tanımlanmalı; fark listesi çift yönlü incelenmeli (kamera kaçıranları da sayan) |
| D5 | FR-23 kural tabanlı O→0/I→1 düzeltme | Bizde üstü var: slot×vocab TAM olasılık dağılımı birikimi + TR-gramer kısıtlı arama (8/B'yi kural değil OLASILIK+POZİSYON çözer); UFPR literatürü de çok-kare oylamanın kazancını doğruluyor | FR-23 "asgari çıta", mevcut kısıtlı-çözümleme "hedef uygulama" olarak yazılmalı |
| D6 | B-01: mevcut yazılım `IEventSink` uygular varsayımı | Faz-0 bulgumuz: mevcut otopark yazılımı kamerayla 4 yüzeyden biriyle konuşuyor olabilir (HCNetSDK arming / SDK listening / ISAPI httpHosts push / polling); HCNetSDK ise DLL/IEventSink entegrasyonu OTOPARK YAZILIMINDA kod değişikliği ister | B-01 "doğrulanacak varsayım" işaretlenmeli; Wireshark keşfi (48h) F0'a alınmalı |
| D7 | FR-02 üstel geri çekilme 1→30 sn yeterli | Canlı 3h donma vakamız: ffmpeg süreci ölmeden pipe bloklanabiliyor — yeniden bağlanma YETMEZ, kare-yaşı tabanlı watchdog şart (bizde ölçülüp düzeltildi) | FR-02'ye ek: "işlenen son kare yaşı 3×timeout'u aşarsa bağlantı kopmuş SAYILMALI ve boru hattı zorla yeniden kurulmalı" |

## 4. Rapordan Kapılanlar

### 4.1 Bugün uygulananlar (canlıda, commit b7e3581)

| Rapor maddesi | Uygulama |
|---|---|
| FR-50/52 saklama süresi + silme denetim izi | `EvidenceRetentionDays=30`: süre dolan kanıt JPEG'leri otomatik silinir, silmeler loglanır (adet-FIFO'ya ek) |
| R-16 model bütünlüğü | `DetectorModelSha256`/`OcrModelSha256` yapılandırıldıysa açılışta doğrulanır; uyuşmazsa **servis başlamaz** |
| FR-32 EventId + FR-35 ManuelOnayGerekli | `PlateEvent.EventId` (Guid, idempotent webhook) + `LowConfidence` bayrağı (`ManualReviewMinOcr=0.75` eşiği) |
| FR-42 plaka maskeleme | `MaskPlatesInLogs`: `25***123` deseni (gölge dönemde kapalı; canlıda hukuk kararıyla açılır) |
| 6.3 TR kültür tuzağı | Denetlendi: kodda `ToUpper()` YOK, her yerde `ToUpperInvariant` — zaten sağlanıyor ✅ |

### 4.2 Önceliklendirilmiş backlog (rapor maddesi → bizim işimiz)

**P1 (ikame öncesi şart):**
- **FR-41 yapılandırma doğrulama**: şema + açılışta anlaşılır hatayla reddetme (bugün kısmi: bind adresi).
- **NFR-U1 paketleme kararı**: otopark yazılımı .NET Framework 4.8 ise çekirdek `netstandard2.0`
  katmanı gerekir — E1 keşfiyle birlikte netleşir (D6). Servis-yanında-DLL ikili teslim şekli tasarlanacak.
- **72 saat dayanıklılık koşusu** (4.12): 24h'ı geçtik; 72h resmi koşu + rapor.
- **Etiketli set şartnamesine geçiş** (6.4): mevcut 2.627 kırpıntıyı koşul etiketleriyle (saat/hava/kirlilik)
  yeniden düzenle; hedef dağılım ≥%25 gece, ≥%15 yağış/kar (KIŞ), ≥%10 kirli — kış hasadıyla tamamlanır.
- **Regresyon kapısına sayısal eşik**: "karakter doğruluğu −0.5 puandan fazla gerileyen model yayınlanamaz."

**P2 (profesyonellik/operasyon):**
- **FR-24 GecersizFormat**: format-geçersiz okumaları sessizce atmak yerine sayaç + (ROI içinde ısrarlıysa)
  tanı olayı. Bariyer kanalına girmez; "okuyamadık ama bir şey vardı" görünürlüğü sağlar (A4 `TespitYok` ile birlikte T8/PLAKASIZ_ARAC işimize bağlanır).
- **FR-36 düzeltme geri-besleme API'si**: `POST /api/correction {eventId, correctedPlate}` → kanıt
  kırpıntısı + düzeltilmiş etiket eğitim arşivine düşer (veri fabrikamıza doğal ek; EventId bugün eklendi).
- **6.5 sabah sağlık raporu**: her sabah otomatik özet (kamera başına olay, ortalama güven, restart,
  RSS) — panelin yanına ucuz bir rapor ucu.
- **R-14 sahne-değişim uyarısı**: kamera açısı kaydı/kirlendi tespiti — periyodik kare histogram/kenar
  imzasını taban çizgisiyle karşılaştır; sapmada alarm. (Ucuz, günde birkaç kez yeter.)
- **R-13**: hedef-PC kurulum listesine Windows Update bakım penceresi + güncelleme sonrası sağlık kontrolü.
- **6.7 dağıtım paketi**: DLL/exe + modeller + SHA listesi + varsayılan config + sürüm notu; önceki paket
  geri-alma için sahada tutulur.
- **6.11 hedef-PC güvenlik listesi**: RTSP şifreleri DPAPI, kamera VLAN, kanıt klasörü ACL, BitLocker.

**P3 (izle/fırsat):**
- FR-14 ikinci-geçiş ön işleme (kontrast/keskinlik sonrası tek OCR tekrarı) — dikkat: naif deskew ve
  piksel-füzyon ÖLÇÜMDE KAYBETTİ; bu da ancak ölçümle girer.
- NFR-U2 3 marka kamera doğrulaması (tedarikçi bağımsızlığı kanıtı) — yaygınlaştırma fazında.
- 6.12 TCO modeli: muhasebe verisiyle saha-başına maliyet karşılaştırması.

### 4.3 Bizden rapora geri akması gerekenler (raporun görmediği kanıtlı bulgular)

Gözlem katmanı (ROI dışı bütçeli OCR — kapsam paritesi bariyer kanalını kirletmeden) · kanıt kırpıntısı
+ panel küçük resimleri (ihtilaf adjudikasyonu) · çapraz-kamera eşleşmesi (cadde geçişleri) · kameranın
KENDİ gece hataları (D4) · Hikvision "Barrier Gate Control Offline/By Camera" = hazır rollback ·
RTSP boru hattı gecikme bütçesi (GOP/H264+/nobuffer ayrıntıları) · ölçümle ELENENLER listesi (deskew,
piksel-füzyon, yolo-256/512@fps2, cct_s_v2@fps2, dedup 20/30/90, INT8-dynamic C# ConvInteger) —
raporun F1 pilotunda aynı şeyleri yeniden denemesini önler.

## 5. Açık Kaynak / Ticari Proje Kod İncelemesi (4 paralel kod-seviyesi inceleme)

### 5.1 AlprNet — HÜKÜM: "birincil aday" savunulamaz; kod referansı olarak 3 çalıntı

MIT lisanslı ama: **tek günde yazılmış (2025-10-26, 17 commit), 8.5+ aydır sıfır aktivite, 0 yıldız,
NuGet'te yok, ~550 satır** — tracking/oylama/gramer/ROI/RTSP hiçbiri yok; public API'si OpenCvSharp
`Mat` üzerine kurulu (bizi eleyen arm64 sorunu çekirdeğinde, izole edilemez). İki doğruluk hatası
kodla doğrulandı: (1) **softmax çift-uygulama** — CCT çıktısı zaten olasılıkken tekrar softmax →
karakter güvenleri ~1/vocab'a ezilir (bizim kod bunu baştan doğru yapıyor); (2) **keep_aspect_ratio
config'i parse edilip HİÇ kullanılmıyor** — model stretch ile eğitilmişken daima letterbox →
eğitim/çıkarım uyumsuzluğu. Çalıntılar:
- **Toplu (batched) OCR**: kare başına N kırpıntıyı tek `session.Run` ile işleme — gözlem katmanı +
  kuyruk anlarında %10-25 OCR süresi kazancı adayı (önce fine-tune ONNX'imizin batch ekseni dinamik mi bak).
- **ORT 1.20 → 1.23.2 yükseltmesi**: aynı model ailesiyle uyumluluğu AlprNet fiilen kanıtlıyor; sıfır
  kod değişikliği, olası %5-15 CPU kazancı; **yeni ORT'ta INT8'i BİR KEZ yeniden dene** (ConvInteger durumu değişmiş olabilir).
- Model kataloğu: t-640/s-608 indirme adresleri (aşağıda uzak-alan taraması).

### 5.2 fast-alpr / fast-plate-ocr / open-image-models — asıl değer EĞİTİM tarafında

Üçü de MIT + aktif bakım; **cct_xs_v2'den yeni OCR modeli yok** (bizimki hâlâ güncel); fast-alpr
pipeline'ı bizden geride (marjsız kırpım, tek-kare, oylama/gramer yok). Multiline issue #39 çözümsüz;
maintainer cevabı = bizim zaten yaptığımız "kendi verinle fine-tune". Çalıntılar (5'i sıfır çalışma-zamanı maliyetli):
- **Augmentation boşluğu keşfi**: varsayılan eğitimde `shear=(0,0)` ve **perspektif dönüşümü HİÇ YOK** —
  açılı-plaka/son-hane hatamız (24ACB27_) doğrudan bununla ilişkili. Fine-tune YAML'ına
  `A.Perspective(0.02-0.08, p=0.3)` + `A.Affine(shear=±12°, p=0.3)` ekle → model açıyı öğrenir
  (elenen deskew'in aksine girdiyi bozmadan).
- **Gece/kış agresif augmentation profili**: brightness (-0.4,0.1), gamma (60,140), ISONoise,
  MotionBlur (3,7), Downscale 0.5-0.9 — gece %84→%90+ hedefi için kanıtlanmış fine-tune yolunun gece rejimine taşınması.
- **Boş-plaka negatifleri**: v2 eğitim hilesi — arka plan/çöp kırpıntıları boş etiketle eğitime girer,
  model çöpe "tüm-pad" üretmeyi öğrenir; C#'ta pad-olasılığı yüksek kare oylamaya girmez → dünkü
  25ACS044 tipi çöp emitler ve varyant zincirleri kaynağında kesilir.
- **Eğitim reçetesi hizalama**: EMA (varsayılan açık!), cosine+warmup, label_smoothing 0.01, wd
  bias/LN-istisnası — kalibre olasılık, TAM-dağılım biriktiren oylamamıza doğrudan yansır.
- **Region head ('Turkey' sınıfı, F1>0.99)**: fine-tune'da korunursa bedava "bu gerçekten TR plakası mı" sinyali.
- **ROI-bounding-square dedektör girişi**: 16:9 karenin 384'e letterbox'ında girişin ~%44'ü gri pad!
  ROI'nin kare-sarmalını kırpıp vermek uzak plakayı dedektör girdisinde ~2x büyütür — model değişmeden
  "efektif çözünürlük" (sorun-a için EN umutlu ucuz yol).
- OpenVINO EP somutu: `Intel.ML.OnnxRuntime.OpenVino` NuGet; **TUZAK: OpenVINO 2026.0+ AVX2 zorunlu**,
  AVX'i bilinmeyen hedef PC için 2025.x tabanlı sürüme sabitle + önce CPUID.

**Bugün ölçülen deney — yolo-v9-t-416 (denenmemiş ara nokta) ELENDİ**: diag4'te birebir 19/26 (%73)
vs 384'ün 22/26 (%85); yakalama aynı (23/26), gürültü 14→16. Resmî recall artışı (+3.1pp) bizim
kapı-kapsamlı exact'e ÇEVRİLMİYOR — 256/512 bulgusuyla tutarlı: ana dedektör boyutu kaldıracımız değil.

### 5.3 ort-alpr / WPOD-NET — açılı plaka için gerçek umut (canlı test edildi)

Repo bakımsız ama değer modelde: **lpd.onnx 6.6MB, IR7/opset13, sadece basit op'lar → bizim C# ORT'ta
doğrudan çalışır** (INT8'deki ConvInteger sorunu burada yok). Naif deskew'den (elendi) farklı sınıf:
**öğrenilmiş köşe tespiti + TAM perspektif düzeltme** (affine regresyon → quad → DLT homografi →
%10 kenar marjlı warp). Ajan canlı test etti: ağır eğik plaka frontal rektifiye oldu; plaka-bbox
kırpıntısıyla (0.5-1.5x marj) prob~1.0 çalıştı — "yalnız düşük güvenli/açılı kırpıntıda ikinci geçiş"
senaryomuz doğrulandı. Maliyet: arm64 9-17 ms/çağrı (zayıf Intel tahmini 30-80 ms) — track başına
birkaç çağrıyla bütçe içi. OpenCV gerekmez (homografi ~40 satır Gauss eliminasyonu + SkiaSharp warp).
Lisans: WPOD ağırlıkları GPLv3-türevi (kurum içi kullanım = dağıtım yok = yükümlülük tetiklenmez);
temiz yol **IWPOD-NET (MIT, TITS 2021)** — aynı decode, h5+TF2 eğitim kodu var → tf2onnx çevir +
uzun vadede kendi quad-etiketli verimizle fine-tune kapısı. Bonus çalıntılar: quad'dan bedava
eğiklik/perspektif metriği (oy ağırlıklama + rektifikasyon tetiği + açılı alt-küme otomatik etiketi);
%10 marjlı warp deseni (kanıt kırpıntılarına da uygulanabilir).

### 5.4 OpenALPR post-process + KBY-AI + CodeProject.AI

**OpenALPR** (AGPL — kod alınmaz, SÜREÇ mantığı yeniden yazılır; fiilen bakımsız): post-process
katmanı altın madeni ve OCR-agnostik:
- **SKIP_CHAR silme hipotezi**: tepe olasılığı eşik altı slota "karakter yok" adayı (skor =
  |skip_level−p|) → 7 ve 8 karakterli okumalar YARIŞIR; 24ACB27_ vakasında hayalet son hane silme
  hipoteziyle düşer. Bizim sabit-slot oylamamızda olmayan uzunluk-esnekliği. (~40 satır, ~0 CPU.)
- **Priority-queue k-best aday üretimi** (skor-delta çocuk türetme + erken durdurma) → verimli top-N.
- **Top-N adayların olayda taşınması + gramer-eşleşen adayın terfisi** + çift-emit kuralı: "yeni
  emit'in best'i önceki emit'in top-N'inde varsa bu YENİ olay değil güncellemedir" → varyant zinciri
  tek olaya çöker (sorun-f'nin olay-modeli çözümü).
- **MERGE_COMBINE reçetesi**: track birleşiminde aday birleştirme skoru = (conf−60)×4 + gramer bonusu
  (+150 dengi) + sıra bonusu — iki track farklı stringde karar kılınca deterministik kazanan.
- Not: OpenALPR'ın TR pattern listesi bizimkinden EKSİK (##@@@### yok) — TR gramerimiz daha doğru.

**KBY-AI**: kapalı kutu (şifreli model blob, makine-başına lisans, README'de doğruluk/hız iddiası yok);
tek sinyal: **Intel CPU için openvino.dll taşıması** — ticari SDK'ların Intel-CPU'da OpenVINO'ya
yaslandığının kanıtı. Raporun "ticari kıyas çıpası" nitelemesi düşürülmeli: kıyaslanacak veri yayınlamıyor.

**CodeProject.AI**: (1) **kapılı deskew varyantı** — varsayılan KAPALI, açı karakter kutularından,
largest_rotated_rect güvenlik kontrolü (elenen deskew'imizin ders alınmış hâli; yalnız gramer-skoru
eşik-altı kırpıntıda dene); (2) **optimal karakter yüksekliği normalizasyonu** (~60px hedefe anizotropik
cubic — uzak/küçük plaka için kırpıntı başına ~0.5ms); (3) **EP probe + try/catch zinciri + garantili
CPU fallback + cihaz raporlama** (ObjectDetectionYOLOv5Net — birebir bizim stack); (4) **DenseTensor
havuzlama + Vector\<T\> SIMD ön-işleme** (AVX'siz güvenli düşüşlü; kare başına ~1.7MB alloc sıfırlanır).

## 6. Beyin Fırtınası Döngüsü — Birleşik Öncelikli Backlog

Sorun → çözüm eşlemesi (tümü mevcut ölçüm bataryasıyla A/B doğrulanacak; ölçmeden hiçbiri varsayılan açılmaz):

| Sıra | İş | Hedef sorun | Maliyet | Beklenen kanıt |
|---|---|---|---|---|
| 1 | **SKIP_CHAR silme hipotezi** TR-kısıtlı çözümleyiciye | açılı son-hane (b), çift-emit (f) | ~40 satır, ~0 CPU | diag4 + 24ACB tipi vakalarda exact ↑ |
| 2 | **Top-N aday + olayda taşıma + çift-emit güncelleme kuralı** | çift-emit (f) | ~yarım gün, ~0 CPU | araç başına olay sayısı ↓, recall sabit |
| 3 | **Fine-tune v3: perspektif+shear + gece profili + boş-plaka negatifleri + EMA/label-smoothing + region head** | açı (b), gece (c), çöp (d) | 1 eğitim koşusu, 0 runtime | val + gece/açılı alt-küme exact ↑ |
| 4 | **ROI-bounding-square dedektör girişi** | uzak plaka (a) | ~1 gün, ~0 CPU | uzak-plaka ilk-tespit mesafesi ↑ |
| 5 | **ORT 1.23.2 yükseltme + INT8 yeniden dene** | zayıf CPU (e) | paket değişimi | ms/kare, exact sabit |
| 6 | **WPOD/IWPOD kapılı rektifikasyon ikinci geçişi** (düşük güven + eğik metriği tetikli) | açı (b) | 1-2 gün, 10-80 ms/track | açılı alt-kümede exact ↑, temizde regresyon 0 |
| 7 | **Batched OCR** | CPU marjı (e) | ~1 gün | kırpıntı/sn ↑ |
| 8 | **EP probe + OpenVINO 2025.x + CPU fallback + cihaz raporlama** | hedef PC (e) | Faz-1 pilotunda | hedef donanımda ms/kare |
| 9 | Tensör havuzlama + SIMD ön-işleme · optimal karakter yüksekliği · seyrek t-640 uzak taraması · kapılı deskew varyantı | (e)(a)(d)(b) | düşük-orta | her biri ayrı A/B |

**Elenenler güncellendi**: yolo-v9-t-416 ana dedektör (bugün ölçüldü: exact %85→%73) — 256/512
bulgusuyla birlikte "ana dedektör giriş boyutu exact kaldıracı değildir" artık üç noktayla kanıtlı.

**Rapor + repo incelemesinin ortak dersi**: bizim mimari çekirdeğimiz (tam-dağılım oylama + TR gramer
+ ROI + fuzzy track) incelenen TÜM açık kaynak projelerden ileride; kalan kazanımlar (1) eğitim
reçetesi, (2) olay-modeli (top-N/çift-emit), (3) uzunluk-hipotezi (SKIP_CHAR), (4) geometri
(ROI-square, rektifikasyon), (5) hedef-PC hızlandırma (ORT/OpenVINO/SIMD) katmanlarında.
