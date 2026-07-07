# Otopark Plaka Tanıma Sistemi (PTS)
## Gereksinim Mühendisliği, Risk Mühendisliği ve Yazılım Mühendisliği Disiplinleri Raporu

| Alan | Bilgi |
|---|---|
| Doküman No | PTS-RPT-001 |
| Sürüm | 1.0 (Taslak — gözden geçirme bekliyor) |
| Tarih | 7 Temmuz 2026 |
| Hedef sistem | Belediye otoparkları için tamamen yerel (on-premise), yalnızca CPU üzerinde çalışan, C#/.NET DLL olarak mevcut otopark yönetim yazılımına entegre edilen, sadece Türkiye plakalarını okuyan plaka tanıma motoru |
| Referans standartlar | ISO/IEC/IEEE 29148:2018 (gereksinim müh.), ISO/IEC 25010 (yazılım kalite modeli), ISO 31000:2018 (risk yönetimi), SWEBOK v4.0 (IEEE CS, 2024), 6698 sayılı KVKK |

---

## 1. Yönetici Özeti

Bu rapor, Hikvision ANPR kameralarına olan bağımlılığı ortadan kaldırarak plaka tanıma zekâsını otopark bilgisayarlarındaki yerel bir C#/.NET DLL'ine taşıyan projenin gereksinim mühendisliği, risk mühendisliği ve diğer yazılım mühendisliği disiplinlerini tek çatı altında ele alır. Hazırlık aşamasında Türk plakalarına odaklı akademik makaleler ve tezler, açık kaynak ve ticari ANPR projeleri, CPU üzerinde ONNX çıkarım performansı verileri ve KVKK Kurulu'nun otopark işletmecilerine ilişkin emsal kararları taranmış; bulgular Bölüm 3'te özetlenip gereksinimlere ve risklere girdi olarak kullanılmıştır.

Raporun ana çıktıları şunlardır: ISO/IEC/IEEE 29148 sürecine göre yazılmış, kimlikli ve doğrulanabilir bir gereksinim kümesi (Bölüm 4: iş gereksinimleri, kullanım senaryoları, 6 grupta fonksiyonel gereksinimler, ISO 25010 başlıklarında ölçülebilir fonksiyonel olmayan gereksinimler, DLL API sözleşmesi ve Türk plaka format kuralları); ISO 31000 çerçevesinde olasılık×etki puanlı 16 kalemlik bir risk kaydı ve kırmızı riskler için azaltım planları (Bölüm 5); SWEBOK v4 bilgi alanlarına göre mimari, tasarım, test, operasyon, bakım, konfigürasyon yönetimi, güvenlik ve ekonomi disiplinlerinin projeye uygulanması (Bölüm 6); ölçülebilir başarı kriterleri ve dört fazlı yol haritası (Bölüm 7–8).

Öne çıkan üç mühendislik kararı özellikle vurgulanmalıdır. Birincisi, bariyerli otopark senaryosunda araç saniyelerce durduğu için sistemin sürekli tam kare hızında çalışması gerekmez; hareket tetiklemeli, 3–6 FPS'lik bir boru hattı düşük donanımlı CPU'larda hedef doğruluğa ulaşmak için yeterlidir ve bu, gereksinimlere ölçülebilir biçimde yansıtılmıştır. İkincisi, doğruluğun asıl kaldıracı model değil süreçtir: çoklu kare oylaması, Türk plaka formatına dayalı konum-duyarlı karakter düzeltme ve düşük güvenli okumaların operatör onayına düşmesi birlikte, tek kare doğruluğunun üzerine çıkar. Üçüncüsü, plaka verisi KVKK kapsamında kişisel veridir; Kurul'un bir otopark işletmecisine aydınlatma yükümlülüğünü yerine getirmediği için 75.000 TL idari para cezası verdiği 2023/924 sayılı kararı, hukuki gereksinimlerin teknik gereksinimlerle eşit öncelikte ele alınmasını zorunlu kılmıştır.

---

## 2. Proje Tanımı, Vizyon ve Kapsam

### 2.1 Problem tanımı

Mevcut otoparklarda plaka tanıma, ANPR özellikli Hikvision kameralarının kamera içinde yaptığı tanımaya dayanmaktadır. Bu mimari üç temel sorun üretir. Tedarikçi kilidi: tanıma yeteneği kameraya gömülü olduğundan her yeni nokta aynı markanın pahalı ANPR modelini gerektirir ve entegrasyon, üreticinin SDK/protokolüne bağımlıdır. Maliyet: ANPR kameralar, aynı optik kalitedeki standart IP kameralara göre belirgin biçimde pahalıdır ve nokta sayısı arttıkça fark büyür. Esneklik: tanıma parametrelerine, güven skorlarına ve hata ayıklama verilerine erişim üreticinin sunduğuyla sınırlıdır; kurumun kendi doğrulama, tekilleştirme ve iş kuralı katmanını inşa etmesi zorlaşır.

### 2.2 Çözüm vizyonu

Tanıma zekâsını kameradan çıkarıp otopark bilgisayarına taşıyan, tedarikçiden bağımsız bir yazılım katmanı: RTSP yayını verebilen herhangi bir IP kameradan görüntüyü alan, ONNX modelleriyle CPU üzerinde plaka tespiti ve OCR yapan, Türk plaka kurallarıyla sonucu doğrulayıp düzelten ve olayları `IEventSink` soyutlaması üzerinden mevcut C#/.NET masaüstü ve web otopark yönetim yazılımına ileten bir sınıf kitaplığı (DLL). Sistem internetsiz, tamamen yerel çalışır; hiçbir görüntü veya plaka verisi kurum dışına çıkmaz.

### 2.3 Kapsam

Kapsam içi (v1): RTSP akış alımı ve otomatik yeniden bağlanma; kare örnekleme ve hareket tetikleme; ONNX tabanlı plaka tespiti ve OCR; Türk plaka format doğrulama ve konum-duyarlı karakter düzeltme; çoklu kare konsensüsü ve zaman-pencereli tekilleştirme; `IEventSink` üzerinden olay üretimi ve sink erişilemezken diskte kalıcı kuyruk; plaka kırpma görüntüsünün saklanması ve süre sonunda otomatik silinmesi; JSON yapılandırma, yapısal loglama, sağlık/metrik uç noktası; operatör düzeltmelerinin eğitim arşivine geri beslenmesi.

Kapsam dışı (v1): ücretlendirme ve tahsilat mantığı (mevcut sistemde kalır); bariyer donanım sürücüsü (mevcut sistem olaya göre tetikler); araç marka/model/renk tanıma; yabancı ülke plakalarının okunması; serbest akışta (durmayan araç) yüksek hızlı okuma; hız/ihlal tespiti; GPU hızlandırma; bulut veya merkezi sunucuda tanıma. Bu maddeler bilinçli olarak dışarıda bırakılmıştır; her biri Bölüm 5'teki kapsam kayması riskinin (R-11) kontrol noktasıdır.

### 2.4 Varsayımlar ve bağımlılıklar

| ID | Varsayım / Bağımlılık | Geçersiz çıkarsa etkisi |
|---|---|---|
| V-01 | Kameralar H.264/H.265 RTSP yayını verir; ONVIF desteği tercih edilir | Kamera değişimi veya ek protokol adaptörü gerekir |
| V-02 | Araçlar bariyer/gişe önünde 1–5 sn durur veya belirgin yavaşlar | Serbest akış senaryosu FPS ve doğruluk hedeflerini geçersiz kılar; v2 kapsamına alınır |
| V-03 | Otopark PC'leri Windows 10/11 x64, ≥4 iş parçacıklı CPU, ≥8 GB RAM (bkz. 4.9 referans donanım) | Donanım yenileme bütçesi veya hedeflerin yeniden kalibrasyonu gerekir |
| V-04 | İnternet erişimi garanti değildir; sistem çevrimdışı tam işlevseldir | — (zaten tasarım ilkesi) |
| V-05 | Gece okuma için IR aydınlatmalı kamera veya harici IR projektör kullanılır | Gece doğruluk hedefi (NFR-D1) tutturulamaz |
| B-01 | Mevcut otopark yönetim yazılımı olayları `IEventSink` uygulamasıyla tüketecek | Entegrasyon sözleşmesi (4.7) yeniden müzakere edilir |
| B-02 | Model ince ayarı için saha görüntüsü arşivi (mevcut kayıtlar + gölge modda toplanacak veri) KVKK'ya uygun biçimde kullanılabilir | Doğruluk hedeflerine ulaşma süresi uzar (R-12) |

### 2.5 Sözlük

ANPR/ALPR: otomatik plaka tanıma. ROI: kare içinde tespitin çalıştığı ilgi alanı. OCR: optik karakter tanıma. ONNX: modeller için açık değişim formatı; ONNX Runtime bu modelleri CPU'da çalıştıran motor. EP (Execution Provider): ONNX Runtime'ın donanıma özgü arka ucu (CPU, OpenVINO, DirectML). EARS: gereksinim cümleleri için kalıp dili. MoSCoW: Must/Should/Could/Won't önceliklendirmesi. Gölge mod: yeni sistemin, karar yetkisi olmadan mevcut sistemle paralel çalışıp çıktılarının karşılaştırıldığı işletim biçimi. Store-and-forward: hedef erişilemezken olayların diskte bekletilip sonra sırayla iletilmesi. KVKK: 6698 sayılı Kişisel Verilerin Korunması Kanunu.

---

## 3. Literatür ve Saha Araştırması (Web)

Bu bölüm, gereksinimlere ve risklere doğrudan girdi sağlayan web araştırmasının özetidir. Tüm kaynaklar Bölüm 9'da listelenmiştir.

### 3.1 Türk plakalarına odaklı akademik çalışmalar ve tezler

Türkiye'de plaka tanıma, hem klasik görüntü işleme döneminde hem de derin öğrenme döneminde yoğun çalışılmış bir konudur ve bulgular bu projeye doğrudan uygulanabilir niteliktedir. Kılıç ve Aydın'ın Fırat Üniversitesi çalışması (2018), üniversite araç kapılarındaki IP güvenlik kameralarından ön işleme yapılmadan alınan 34.580 adet 640×360 görüntüyle Türk plakalarına özgü etiketli veri seti oluşturmuş ve TensorFlow/Keras ile eğitilen ağın plakaları yüksek doğrulukla tanıdığını raporlamıştır; aynı ekibin Bigailab-trvlp-20k çalışması 20.404 görüntülük bir Türk plaka tespit veri setini literatüre kazandırmıştır. Bu iki çalışmanın projeye en önemli mesajı, kapı/gişe kamerası görüntülerinin — tam da bizim senaryomuzun — ince ayar için ideal ve yeterli veri kaynağı olduğudur. Bayram (2020, Politeknik Dergisi) Mask R-CNN tabanlı bir otomatik plaka tanıma sistemi sunmuş; Düzce Üniversitesi kaynaklı bölgesel-ESA çalışması (2023) plaka bulma ve karakter ayrıştırmada iki ayrı YOLO modeli, karakter sınıflandırmada ayrı bir CNN kullanan üç aşamalı mimariyle çok yüksek başarı bildirmiştir. 2024 tarihli bir IEEE çalışması, YOLOv2 + LPRNet iki aşamalı mimariyi düşük güçlü bir RISC-V mikrodenetleyicide (Kendryte K210) çalıştırmış; tespit doğruluğu yüksek bulunurken tanıma tarafında çıkarım süresinin gerçek zaman için optimizasyon ve kuantizasyon gerektirdiği sonucuna varmıştır — bu bulgu, düşük donanımlı CPU hedefimiz için kuantizasyonun neden gereksinim düzeyinde ele alındığını (4.9, R-03) açıklar. Daha eski tarihli yerli çalışmalar (ör. Özbay 2006 Gaziantep, ODTÜ 2007 kısıt-tabanlı sistem, DergiPark'taki Türk taşıt plaka standartları makalesi) klasik görüntü işleme kullanmakla birlikte kalıcı bir ders bırakır: Türk plakasının harf-rakam düzeni (1 harfse 4 rakam, 2 harfse 3–4 rakam, 3 harfse 2 rakam) belirsiz karakterleri çözmede güçlü bir sonradan-doğrulama sinyalidir ve bu ilke FR-20/FR-23 gereksinimlerine dönüştürülmüştür.

### 3.2 Açık kaynak ve ticari proje taraması

| Proje | Tür | Teknoloji | CPU uygunluğu | .NET ile kullanım | Proje için değerlendirme |
|---|---|---|---|---|---|
| **AlprNet** (Temppus) | Açık kaynak .NET kitaplığı | ONNX çıkarımı; yolo-v9-t-512 tespit + cct_s_v1_global OCR modelleri, OpenCvSharp | İyi (ONNX Runtime CPU) | Doğrudan (yerli .NET) | **Birincil aday** — hedef mimariyle birebir örtüşür; daha önce yapılan seçimle uyumlu. Lisans dosyası ve bakım aktifliği kullanım öncesi teyit edilmeli |
| **fast-alpr / fast-plate-ocr / open-image-models** (ankandrew) | Açık kaynak (Python + ONNX modeller) | YOLOv9-t uçtan-uca tespit + CCT OCR; CPU, OpenVINO, DirectML kurulum seçenekleri | Çok iyi; Intel CPU'larda OpenVINO önerisi belgeli | ONNX modelleri Microsoft.ML.OnnxRuntime ile C#'tan doğrudan çalıştırılabilir | **Model tedarik ve eğitim hattı** — fast-plate-ocr kendi veri setiyle ince ayarı resmi olarak destekler; TR ince ayarının aracı budur |
| **ort-alpr** | Açık kaynak | WPOD-NET tespit + Paddle OCR, tümü ONNX'e çevrilmiş, CPU çıkarımı için optimize | İyi | Modeller üzerinden | CPU-öncelikli ONNX boru hattının uygulanabilirliğine ikinci kanıt |
| **OpenALPR (açık sürüm)** | Açık kaynak (C++) | OpenCV + Tesseract, klasik yöntem | Orta | Resmî .NET bağlayıcısı var | Tarihî referans; açık sürümün bakımı durgun, modern derin öğrenme modellerine göre zorlu koşullarda geride. Ticari devamı Rekor Scout |
| **CodeProject.AI ALPR (YOLO11) modülü** | Açık kaynak sunucu modülü | YOLO11 ONNX; CPU + DirectML dönüşü, oturum yöneticisi deseni | İyi | REST üzerinden | Mimari referans (ONNX oturum yönetimi, donanım geri dönüş stratejisi) |
| **KBY-AI .NET SDK** | Ticari | Derin öğrenme, makine başına lisans | İyi | Doğrudan .NET | Ticari kıyas çıpası; makine başına lisans modeli tedarikçi kilidini geri getirir |
| Yerli ticari PTS ürünleri | Ticari | Derin öğrenme OCR; ör. 0,08 sn okuma iddiası (pazarlama verisi) | — | Genelde kapalı | Piyasa beklentisi ve fiyat kıyası için; iddialar bağımsız testle doğrulanmadan hedefe dönüştürülmemeli |

Lisans notu: Ultralytics YOLOv8/YOLO11 kod tabanı AGPL-3.0 lisanslıdır; kapalı kaynak kurumsal kullanımda hukuki inceleme gerektirir. Bu nedenle tespit modeli seçiminde MIT/Apache lisanslı alternatifler (fast-alpr ekosistemindeki modeller gibi) tercih nedenidir ve lisans envanteri risk kaydına (R-07) alınmıştır.

### 3.3 CPU üzerinde performans bulguları

Taranan kaynaklardaki ortak tablo şudur: hafif tespit modelleri (YOLOv9-t sınıfı, 384–512 giriş) ve kompakt OCR modelleri (CCT/CNN sınıfı) ONNX Runtime ile orta sınıf masaüstü CPU'larda kare başına yüzlerce milisaniyenin altında çalışabilmekte; plaka OCR adımı tek başına milisaniyeler mertebesindedir. fast-alpr belgeleri Intel CPU'larda OpenVINO Execution Provider'ı açıkça önermektedir; bu, eski Intel işlemcili otopark PC'lerinde denenecek ilk optimizasyondur. Genel ONNX kıyaslamaları, aynı modelin uç CPU'larda (ör. mobil Ryzen 7'de ~11,5 FPS, masaüstü i9'da ~36 FPS gibi farklı iş yüklerinde) donanıma göre 3–4 kat değişebildiğini gösterir; bu değişkenlik, performans gereksinimlerinin "referans donanım" tanımı olmadan anlamsız olduğunu kanıtlar ve 4.9'daki REF-PC tanımının gerekçesidir. Kritik senaryo avantajımız ise şudur: bariyerli otoparkta araç durur; sistemin serbest akıştaki gibi 25–30 FPS işlemesine gerek yoktur. Hareket tetiklemeli 3–6 FPS'lik işleme, çoklu kare oylamasıyla birleştiğinde hem CPU bütçesine sığar hem doğruluğu yükseltir; bu, FR-05/FR-06/FR-30 gereksinimlerinin araştırma temelidir.

### 3.4 Hukuki araştırma (KVKK)

Araç plakası, resmî kayıtlarla eşleştirildiğinde gerçek kişiye ulaşmayı mümkün kıldığı için Kurul kararlarında ve doktrinde kişisel veri olarak kabul edilmektedir. Projeyi doğrudan ilgilendiren iki emsal karar vardır. KVKK Kurulu'nun 01.06.2023 tarih ve 2023/924 sayılı kararında, bir otopark işletmecisinin park borcunu ispat için aracı fotoğraflayıp plaka üzerinden kimlik tespiti yapması Kanun'un 5/2(e) bendindeki "bir hakkın tesisi, kullanılması veya korunması" şartına dayandığı için hukuka uygun bulunmuş; ancak aydınlatma yükümlülüğü yerine getirilmediği için işletmeciye 75.000 TL idari para cezası verilmiştir. 2021/1110 sayılı kararda ise plaka ve kimlik verilerinin herkese açık bir internet sitesinde yayımlanması 200.000 TL ceza ile sonuçlanmıştır. Bu iki karardan üç doğrudan gereksinim türetilmiştir: otopark girişlerinde ve web'de aydınlatma metni bulunması; plaka görüntüleri için tanımlı saklama süresi ve otomatik silme (FR-50); plaka ile borç sorgulama gibi ekranlar yapılacaksa bunların kimlik doğrulamasız ve herkese açık olmaması (NFR-S1, 6.11). Belediyenin veri işleme envanterinin ve aydınlatma metinlerinin bu sistemi kapsayacak şekilde güncellenmesi hukuk/KVKK birimiyle birlikte yürütülmelidir.

---

## 4. Gereksinim Mühendisliği

### 4.1 Süreç ve standartlar

Gereksinimler, ISO/IEC/IEEE 29148:2018'in beş temel faaliyetine göre yönetilir: ihtiyaç çıkarımı (elicitation), analiz, belgeleme (specification), doğrulama/geçerleme (verification & validation) ve gereksinim yönetimi. Bu projede kullanılan çıkarım teknikleri şunlardır: mevcut Hikvision sisteminin olay ve hata kayıtlarının analizi (hangi koşullarda okuma kaçıyor, hangi saatlerde manuel müdahale artıyor); gişe/saha operatörleriyle görüşme ve iş başında gözlem; saha ölçümü (kamera açısı, mesafe, aydınlatma, plaka piksel boyutu); doküman analizi (plaka mevzuatı, KVKK kararları); literatür ve proje taraması (Bölüm 3); prototipleme ve gölge modda veri toplama. Gereksinim cümleleri EARS kalıplarıyla yazılmıştır: koşulsuz gereksinimler "Sistem … yapmalıdır", olay güdümlü olanlar "X olduğunda sistem … yapmalıdır" biçimindedir. Her gereksinimin bir kimliği, MoSCoW önceliği (M=Zorunlu, S=Olmalı, C=Olabilir) ve doğrulama yöntemi (T=Test, A=Analiz, İ=İnceleme, G=Gösterim) vardır; değişiklikler sürüm kontrolünde değişiklik kaydıyla izlenir ve izlenebilirlik matrisi (4.11) üzerinden iş hedeflerine bağlanır.

### 4.2 Paydaş analizi

| Paydaş | Rolü | Temel beklenti / başarı ölçütü |
|---|---|---|
| Belediye yönetimi | Sponsor | Maliyet düşüşü, tedarikçi bağımsızlığı, vatandaş şikâyetlerinin artmaması |
| Otopark işletme birimi | Ürün sahibi | Gelir sızıntısının azalması, kesintisiz işletim, doğru giriş-çıkış kayıtları |
| Gişe / saha operatörü | Birincil kullanıcı | Az manuel müdahale; düşük güvenli okumaların tek tıkla düzeltilebilmesi |
| Sürücü / vatandaş | Etkilenen taraf | Hızlı geçiş, doğru ücretlendirme, kişisel verilerinin korunması |
| BT ve ağ birimi | İşletici | Kolay kurulum, izlenebilir sağlık durumu, mevcut ağ altyapısıyla uyum |
| KVKK irtibat / hukuk birimi | Uyum otoritesi | Aydınlatma, saklama, erişim ve envanter yükümlülüklerinin karşılanması |
| Muhasebe / gelir birimi | İç müşteri | Olay kayıtlarının ücretlendirmeyle tutarlılığı, itiraz durumunda kanıt görüntüsü |
| Geliştirme (tek geliştirici) | Üretici | Net kapsam, sabit API sözleşmesi, ölçülebilir kabul kriterleri |
| Bakım / donanım | Destek | Standart kamera/PC donanımı, arıza runbook'ları |

### 4.3 İş gereksinimleri

| ID | İş gereksinimi | Ölçüt |
|---|---|---|
| İG-01 | Tedarikçi bağımsızlığı: yeni otopark noktası açılışında ANPR özellikli kamera zorunluluğu kalkmalı; RTSP veren standart IP kamera yeterli olmalıdır | En az 3 farklı kamera markasıyla doğrulanmış çalışma (NFR-U2) |
| İG-02 | Nokta başına toplam maliyet (kamera + lisans + kurulum) mevcut ANPR kameralı çözüme göre düşmelidir | 5 yıllık TCO karşılaştırması; hedef oran muhasebe verisiyle F0 sonunda sabitlenecek (6.12) |
| İG-03 | Gelir sızıntısı ve manuel iş yükü azalmalıdır: okunamayan/yanlış okunan plaka kaynaklı manuel düzeltme oranı tanımlı eşiğin altında kalmalıdır | Manuel onay oranı ≤ %8 (pilotta kalibre edilecek, KPI-3) |
| İG-04 | Mevcut masaüstü/web sistemine entegrasyon minimum kod değişikliğiyle olmalı; `IEventSink` sözleşmesi kararlı kalmalıdır | Entegrasyon eforu tek sprint içinde; sözleşme değişikliği SemVer'e tabi (4.7) |
| İG-05 | KVKK yükümlülükleri (aydınlatma, saklama, güvenlik, envanter) eksiksiz karşılanmalıdır | Hukuk birimi onaylı kontrol listesi; denetimde sıfır bulgu hedefi |

### 4.4 Kullanım senaryoları

Senaryo listesi: UC-01 Araç girişi (plaka okunur, olay üretilir, bariyeri mevcut sistem tetikler) · UC-02 Araç çıkışı ve giriş kaydıyla eşleştirme · UC-03 Abone/beyaz liste aracı geçişi · UC-04 Düşük güvenli okuma → operatör onayı · UC-05 Kamera arızasında işletimin sürmesi · UC-06 Sink/veritabanı erişilemezken yerel kuyruk · UC-07 Yapılandırma ve ROI tanımı · UC-08 Sağlık izleme · UC-09 Operatör düzeltmesinin eğitim arşivine geri beslenmesi.

**UC-01 Araç girişi (detay).** Aktörler: sürücü, PTS DLL, otopark yönetim yazılımı, (dolaylı) gişe operatörü. Ön koşul: giriş kamerası bağlı ve sağlıklı. Ana akış: (1) Araç ROI'ye girer, hareket tetiklenir; (2) sistem ardışık karelerde plakayı tespit eder ve OCR uygular; (3) format doğrulama ve karakter düzeltme çalışır; (4) çoklu kare oylaması konsensüs plakayı üretir; (5) tekilleştirme penceresi kontrol edilir; (6) `PlateEvent` sink'e iletilir; (7) yönetim yazılımı kaydı açar ve bariyeri tetikler. Alternatif akışlar: A1 — konsensüs güveni eşik altında: olay `ManuelOnayGerekli` bayrağıyla üretilir, operatör ekranda kırpma görüntüsünü görüp onaylar/düzeltir (UC-04'e bağlanır); A2 — format doğrulaması başarısız: olay `GecersizFormat` bayrağı ve ham OCR metniyle iletilir, sessizce atılmaz; A3 — aynı plaka tekilleştirme penceresi içinde tekrar okunur: yeni olay üretilmez, sayaç güncellenir; A4 — ROI'de araç var ama N saniye içinde tespit yok: `TespitYok` tanı olayı loglanır (kirli plaka / kamera kayması işareti). Son koşul: her araç geçişi için en fazla bir olay üretilmiş ve kanıt görüntüsü saklanmıştır.

### 4.5 Fonksiyonel gereksinimler

Sözdizimi: her satır "Sistem, …" öznesiyle okunur. Öncelik: M/S/C (MoSCoW). Doğrulama: T/A/İ/G.

**Grup A — Görüntü alma**

| ID | Gereksinim | Önc. | Doğr. |
|---|---|---|---|
| FR-01 | Kamera başına yapılandırılmış RTSP URL'sinden H.264 ve H.265 video akışını almalıdır | M | T |
| FR-02 | Akış koptuğunda üstel geri çekilme ile (başlangıç 1 sn, üst sınır 30 sn) otomatik yeniden bağlanmalı ve kopma/bağlanma olaylarını loglamalıdır | M | T |
| FR-03 | Aynı PC üzerinde en az 2 kamerayı (giriş + çıkış) eşzamanlı işleyebilmeli; kamera sayısı yapılandırılabilir olmalıdır | M | T |
| FR-04 | Kamera başına ROI tanımına izin vermeli ve tespiti yalnızca ROI içinde çalıştırmalıdır | S | T |
| FR-05 | İşlenen kare hızını yapılandırılabilir kılmalı (varsayılan 3–6 FPS) ve CPU'yu doyurmamak için kare atlama uygulamalıdır | M | T |
| FR-06 | ROI'de hareket/araç varlığı yokken tam tanıma boru hattını çalıştırmamalıdır (hareket tetikleme) | S | T |

**Grup B — Tespit ve OCR**

| ID | Gereksinim | Önc. | Doğr. |
|---|---|---|---|
| FR-10 | Karedeki plaka bölgelerini ONNX tabanlı tespit modeliyle bulmalıdır | M | T |
| FR-11 | Tespit edilen plaka kırpmasını ONNX OCR modeliyle karakter dizisine dönüştürmeli; dizi ve karakter düzeyinde güven skorları üretmelidir | M | T |
| FR-12 | Model dosyalarını diskten yüklemeli; model dosyası ve sürümü yeniden derleme gerektirmeden yapılandırmayla değiştirilebilmelidir | M | T |
| FR-13 | Tek kare için uçtan uca işleme süresi referans donanımda (4.9) ortalama ≤ 300 ms olmalıdır | S | A/T |
| FR-14 | Düşük güvenli kırpmalarda kontrast/keskinlik ön işlemesi sonrası OCR'ı bir kez daha deneyebilmelidir (ikinci geçiş) | C | T |

**Grup C — Türk plaka doğrulama ve düzeltme**

| ID | Gereksinim | Önc. | Doğr. |
|---|---|---|---|
| FR-20 | OCR çıktısını, yapılandırma dosyasında tanımlı Türk plaka format tablosuna (4.8) göre doğrulamalıdır; tabloya yeni desen eklemek kod değişikliği gerektirmemelidir | M | T |
| FR-21 | Plakanın il kodunu 01–81 aralığında doğrulamalıdır | M | T |
| FR-22 | Harf pozisyonlarını Türk plakalarında kullanılan harf kümesiyle sınırlamalı; Q, W, X ile Türkçeye özgü Ç, Ğ, İ, Ö, Ş, Ü karakterlerini geçersiz saymalıdır (küme yapılandırılabilir) | M | T |
| FR-23 | Konum-duyarlı karakter düzeltme uygulamalıdır: rakam pozisyonunda O→0, I→1, B→8, S→5, Z→2; harf pozisyonunda 0→O, 1→I, 8→B, 5→S, 2→Z dönüşümlerini güven skoru eşiğine bağlı olarak denemelidir | M | T |
| FR-24 | Format doğrulamasını geçemeyen sonuçları sessizce atmamalı; olayı `GecersizFormat` bayrağı ve ham OCR metniyle iletmelidir | M | T |
| FR-25 | Standart dışı serileri (resmî, diplomatik, geçici, askerî) v1'de `OzelSeri` bayrağıyla işaretlemelidir; ayrıntılı sınıflandırma v2 kapsamındadır | S | İ/T |

**Grup D — Konsensüs, tekilleştirme ve olay üretimi**

| ID | Gereksinim | Önc. | Doğr. |
|---|---|---|---|
| FR-30 | Aynı araç geçişine ait ardışık kare sonuçları üzerinde çoklu kare oylaması yapmalı; olayı pencere içindeki en yüksek konsensüsle üretmelidir | M | T |
| FR-31 | Aynı kamera ve aynı plaka için yapılandırılabilir tekilleştirme penceresi (varsayılan 10 sn) içinde tek olay üretmelidir | M | T |
| FR-32 | `PlateEvent` en az şu alanları içermelidir: plaka metni, toplam güven, karakter güvenleri, UTC zaman damgası, kamera kimliği, yön (giriş/çıkış), plaka kırpma JPEG'i, kare kimliği, bayraklar | M | T |
| FR-33 | Olayları `IEventSink`'e asenkron iletmeli; sink çağrısı tanıma boru hattını bloklamamalıdır | M | T |
| FR-34 | Sink erişilemezken olayları diskte kalıcı kuyruğa yazmalı; bağlantı dönünce sırayla ve mükerrer üretmeden iletmelidir (store-and-forward) | M | T |
| FR-35 | Konsensüs güveni yapılandırılabilir eşiğin altındaysa olayı `ManuelOnayGerekli` bayrağıyla üretmelidir | M | T |
| FR-36 | Operatörün düzelttiği plakayı geri bildirim API'siyle kabul etmeli ve [kırpma görüntüsü + düzeltilmiş etiket] çiftini eğitim arşivine kaydetmelidir (KVKK saklama kurallarına tabi) | S | T |

**Grup E — Yapılandırma, izleme ve günlükleme**

| ID | Gereksinim | Önc. | Doğr. |
|---|---|---|---|
| FR-40 | Tüm ayarları (kamera listesi, eşikler, pencereler, model yolları, saklama süreleri) JSON yapılandırma dosyasından okumalıdır | M | T |
| FR-41 | Yapılandırma şemasını sürümlemeli ve açılışta doğrulamalı; geçersiz yapılandırmada anlaşılır hata mesajıyla başlatmayı reddetmelidir | M | T |
| FR-42 | Seviyeli ve yapısal loglama yapmalı; günlükleri boyut/tarih bazlı döndürmelidir. Plaka metni loglarda varsayılan olarak maskelenmeli (örn. 25***234), tam yazım yalnızca ayarla açılabilmelidir | M | T |
| FR-43 | `GetHealth()` çağrısında kamera bağlantı durumları, son olay zamanı, anlık FPS, kuyruk derinliği, model sürümleri ve bellek/CPU bilgisini döndürmelidir | M | T/G |
| FR-44 | Sayaç metrikleri üretmelidir: işlenen kare, tespit, olay sayısı, düşük güven oranı, ortalama uçtan uca gecikme | S | T |

**Grup F — Veri yönetimi**

| ID | Gereksinim | Önc. | Doğr. |
|---|---|---|---|
| FR-50 | Plaka kırpma görüntülerini yapılandırılabilir saklama süresiyle yerelde saklamalı ve süre sonunda otomatik silmelidir (varsayılan öneri 30 gün; bağlayıcı süreyi hukuk birimi belirler) | M | T |
| FR-51 | Tam sahne karesi saklamayı varsayılan kapalı tutmalı; açılırsa ayrı ve daha kısa bir saklama süresine tabi kılmalıdır (veri minimizasyonu) | M | T |
| FR-52 | Otomatik silme işlemlerini denetim izi olarak loglamalıdır | S | T |
| FR-53 | Kendi başına ağa/buluta veri göndermemelidir; tek dışa veri akışı `IEventSink` olmalıdır | M | İ/T |

### 4.6 Fonksiyonel olmayan gereksinimler (ISO/IEC 25010 başlıklarında)

| ID | 25010 kategorisi | Gereksinim ve hedef | Doğr. |
|---|---|---|---|
| NFR-P1 | Performans verimliliği | Aracın ROI'de sabitlenmesinden olayın sink'e iletilmesine uçtan uca gecikme: REF-PC'de p50 ≤ 1,0 sn; p95 ≤ 2,0 sn | T |
| NFR-P2 | Performans verimliliği | 2 kamera sürekli çalışmada ortalama CPU ≤ %50, tepe ≤ %85 | T |
| NFR-P3 | Performans verimliliği | Kalıcı bellek ≤ 1,5 GB; 72 saatlik koşuda büyüme ≤ %5 (sızıntı testi) | T |
| NFR-D1 | Fonksiyonel uygunluk (doğruluk) | Plaka-düzeyi tam eşleşme: gündüz temiz plaka ≥ %97; gece (IR) ≥ %90; genel saha karışımı ≥ %93. Karakter-düzeyi doğruluk ≥ %99. Ölçüm: ≥ 5.000 örnekli, mevsim/saat dengeli etiketli saha test seti | T |
| NFR-D2 | Fonksiyonel uygunluk | Yanlış-pozitif (araç yokken/yanlış nesneden plaka üretme) oranı ≤ %0,5 | T |
| NFR-G1 | Güvenilirlik | 7/24 çalışma; çökme sonrası watchdog ile otomatik toparlanma ≤ 30 sn | T |
| NFR-G2 | Güvenilirlik | 30 günlük kesintisiz koşuda plansız yeniden başlatma ≤ 1 | A/T |
| NFR-U1 | Uyumluluk / esneklik | Hedef çatı .NET 8 (LTS); mevcut .NET Framework 4.8 uygulamasıyla birlikte çalışma gerekiyorsa çekirdek mantık netstandard2.0 uyumlu katmanda tutulur; Windows 10/11 x64 | İ/T |
| NFR-U2 | Uyumluluk / esneklik | RTSP/ONVIF destekli en az 3 farklı marka kamera ile doğrulanmış çalışma (tedarikçi bağımsızlığının kanıtı) | T |
| NFR-S1 | Güvenlik | Olay ve görüntü verilerine erişim, ana uygulamanın kimlik doğrulamasına tabidir; kırpma klasörü yalnızca servis hesabına ACL ile kısıtlanır; plaka ile sorgu sunan hiçbir ekran kimlik doğrulamasız/halka açık olamaz | İ/T |
| NFR-S2 | Güvenlik (KVKK) | Aydınlatma, saklama, silme ve denetim izi gereksinimleri (FR-42, FR-50..53) karşılanır; işleme faaliyeti belediye veri envanterine işlenir | İ |
| NFR-B1 | Bakım yapılabilirlik | Doğrulama, düzeltme, konsensüs ve tekilleştirme çekirdeğinde birim test kapsamı ≥ %80 | T |
| NFR-B2 | Bakım yapılabilirlik | Tespit ve OCR sağlayıcıları arayüz arkasında değiştirilebilir (AlprNet ↔ doğrudan OnnxRuntime geçişi uygulama kodunu etkilemez) | İ |
| NFR-K1 | Etkileşim yeteneği | Düşük güvenli olay, operatör ekranında kırpma görüntüsüyle birlikte tek tıkla onaylanabilir/düzeltilebilir (ana uygulama tarafı sorumluluğu) | G |
| NFR-Ö1 | Performans / kapasite | Tek PC'de 4 kameraya kadar, kamera başına yaklaşık doğrusal CPU artışıyla ölçekleme | T |

### 4.7 Arayüz gereksinimleri — DLL API sözleşmesi (taslak)

Sözleşme SemVer'e tabidir: kırıcı değişiklik büyük sürüm gerektirir (İG-04). Aşağıdaki taslak, tasarımın başlangıç noktasıdır; alan adları F0 sonunda dondurulur.

```csharp
public interface IPlateRecognizer : IAsyncDisposable
{
    Task StartAsync(RecognizerConfig config, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    RecognizerHealth GetHealth();
    // Operatör düzeltmesi geri beslemesi (FR-36)
    Task ReportCorrectionAsync(Guid eventId, string correctedPlate, CancellationToken ct);
}

public interface IEventSink
{
    // Uygulama tarafınca uygulanır; DLL'in tek dışa akışıdır (FR-53)
    Task HandleAsync(PlateEvent evt, CancellationToken ct);
}

public sealed record PlateEvent(
    Guid EventId,
    string Plate,               // Normalize edilmiş: boşluksuz, büyük harf (invariant)
    string RawOcrText,          // FR-24: ham OCR çıktısı
    double Confidence,          // 0..1 konsensüs güveni
    IReadOnlyList<double> CharConfidences,
    DateTime TimestampUtc,
    string CameraId,
    LaneDirection Direction,    // Giris / Cikis / Bilinmiyor
    byte[] PlateCropJpeg,
    long FrameId,
    PlateEventFlags Flags);     // ManuelOnayGerekli | GecersizFormat | OzelSeri | Duzeltildi

[Flags]
public enum PlateEventFlags { None = 0, ManuelOnayGerekli = 1, GecersizFormat = 2, OzelSeri = 4, Duzeltildi = 8 }
```

Ek arayüz kuralları: `HandleAsync` içindeki istisnalar DLL'i düşürmemeli, kuyruklama devreye girmelidir (FR-34). `PlateCropJpeg` boyutu yapılandırılabilir üst sınıra tabidir. Tüm metin karşılaştırmaları kültürden bağımsız (invariant) yapılır — Türkçe kültürde `i/İ` dönüşümü plaka metnini bozar (bkz. 6.3).

### 4.8 Veri gereksinimleri — Türk plaka format kuralları ve KVKK envanteri

**Format tablosu (yapılandırma verisi; FR-20).** Türk sivil plakası "il kodu (2 rakam) + harf grubu (1–3 harf) + sıra numarası" yapısındadır; harf sayısı rakam sayısını belirler. Aşağıdaki tablo başlangıç yapılandırmasıdır; yeni seriler tabloya satır eklenerek tanımlanır:

| Desen | Örnek | Toplam karakter |
|---|---|---|
| 2R + 1H + 4R | 25 A 1234 | 7 |
| 2R + 2H + 3R | 25 AB 123 | 7 |
| 2R + 2H + 4R | 34 AB 1234 | 8 |
| 2R + 3H + 2R | 25 ABC 12 | 7 |
| 2R + 3H + 3R | 34 ABC 123 | 8 (araç sayısı artışıyla açılan yeni seri) |

Harf kümesi: Türk plakalarında Türk alfabesindeki noktalı/çengelli harfler (Ç, Ğ, İ, Ö, Ş, Ü) ile Q, W, X kullanılmaz; geçerli küme A B C D E F G H I J K L M N O P R S T U V Y Z olarak yapılandırılır (FR-22). İl kodu 01–81'dir (FR-21). Resmî plakalar (siyah zemin/beyaz karakter), diplomatik (yeşil), geçici ve askerî (tümü rakam) seriler standart desene uymayabilir; v1'de bunlar `OzelSeri`/`GecersizFormat` bayrağıyla akışta tutulur, sınıflandırma yapılmaz (FR-25). Konum-duyarlı düzeltmenin (FR-23) mantığı bu tabloya dayanır: desen gereği rakam olması gereken pozisyondaki "O" güvenle "0"a çevrilebilir; bu, literatürdeki Türk plaka çalışmalarının da kullandığı köklü bir tekniktir (bkz. 3.1).

**KVKK veri envanteri (özet; hukuk birimi ile nihaileştirilecek).**

| Veri | Amaç | Olası hukuki dayanak | Saklama | Erişim |
|---|---|---|---|---|
| Plaka metni + zaman + kamera + yön | Otopark giriş-çıkış kaydı ve ücretlendirme | Sözleşmenin kurulması/ifası; bir hakkın tesisi-kullanımı (KVKK m.5/2-c, e; 2023/924 emsali) | Mali mevzuat ve itiraz süreleriyle uyumlu; hukuk birimi belirler | Yetkili operatör/muhasebe, kimlik doğrulamalı |
| Plaka kırpma görüntüsü | Okumanın kanıtı, itiraz çözümü, model iyileştirme | Aynı dayanaklar + meşru menfaat | Varsayılan öneri 30 gün, otomatik silme (FR-50) | Servis hesabı + yetkili operatör |
| Tam sahne karesi | (Varsayılan kapalı) arıza teşhisi | Meşru menfaat, ölçülülük ilkesiyle sınırlı | Kısa (öneri ≤ 7 gün) | BT |
| Operatör düzeltme etiketi | Model ince ayarı | Meşru menfaat; amaçla sınırlılık belgelenir | Eğitim arşivi politikasına göre | Geliştirme |

Zorunlu idari tedbirler: otopark girişlerinde ve kurum web sitesinde plaka tanımaya özgü aydınlatma metni; veri işleme envanterinin güncellenmesi; ihlal bildirim sürecine bu sistemin eklenmesi. 2023/924 kararının dersi nettir: işleme hukuka uygun olsa bile aydınlatma eksikliği tek başına ceza sebebidir.

### 4.9 Donanım ve ortam kısıtları

**Referans donanım (REF-PC).** Tüm performans NFR'leri şu tanımda ölçülür ve raporlanır: 4 çekirdek/4 iş parçacığı, ≥ 3,0 GHz, AVX2 destekli x64 CPU (i3-9100 sınıfı ve muadilleri), 8 GB RAM, SSD, Windows 10/11 Pro x64, ekran kartı yok. Saha PC'leri REF-PC'nin altındaysa hedefler o donanımda yeniden ölçülür; AVX2 bulunmayan çok eski CPU'larda INT8 kuantizasyon kazancı sınırlı kalacağından donanım envanter taraması F0'da yapılmalıdır (R-03).

**Kısıtlar (constraint).** K-01: GPU yok; tüm çıkarım CPU'da (gerekirse OpenVINO EP ile). K-02: Teslimat biçimi C#/.NET sınıf kitaplığı (DLL); barındırma ana uygulama içinde veya Windows Servisi'nde olabilir. K-03: Yalnızca Türkiye plaka formatı hedeflenir. K-04: İnternet bağımsız çalışma; model indirme dahil hiçbir çevrimiçi bağımlılık çalışma zamanında olamaz. K-05: Kullanılan tüm üçüncü taraf kod ve modellerin lisansları kurum kullanımına uygun olmalıdır (AGPL bileşenler hukuk onayı olmadan kullanılamaz).

**Kamera yerleşim kılavuzu (tasarım önerisi; pilotta ölçülüp gereksinime dönüştürülecek).** Plaka görüntü genişliği ≥ ~130 piksel veya karakter yüksekliği ≥ ~20 piksel olacak şekilde odak/mesafe; yatay ve dikey bakış açısı tercihen ≤ 30°; gece için IR aydınlatma ve IR'ye duyarlı sensör; Erzurum kış koşulları için ısıtmalı dış muhafaza, mercek buğu/kar birikimine karşı siperlik ve periyodik temizlik planı. Bu değerler literatür ve sektör pratiğinden alınmış başlangıç noktalarıdır; F1 pilotunda kendi sahamızda doğrulanmadan sözleşme şartnamesine yazılmamalıdır.

### 4.10 Gereksinim kalite kriterleri ve doğrulama

ISO/IEC/IEEE 29148'in tekil gereksinim kriterleri (gerekli, uygun, tekil, açık/tek anlamlı, eksiksiz, tutarlı, uygulanabilir, doğrulanabilir, izlenebilir) ve küme kriterleri (bütünlük, çelişmezlik) bu dokümandaki yazım kurallarına dönüştürülmüştür: her gereksinim tek cümle ve tek yükümlülük taşır; "hızlı", "kullanıcı dostu" gibi ölçülemeyen sıfatlar sayısal hedeflere bağlanmıştır; sayısal hedefi henüz sahadan doğrulanamayanlar açıkça "pilotta kalibre edilecek" diye işaretlenmiştir; her gereksinimin doğrulama yöntemi tabloda tanımlıdır. Doğrulama (verification) test/analiz/inceleme ile, geçerleme (validation) ise paydaşların gölge mod ve pilot sonuçlarını kabul ölçütlerine (4.12) göre onaylamasıyla yapılır.

### 4.11 İzlenebilirlik matrisi (örnek kesit)

| İş gereksinimi | İlgili NFR | İlgili FR | Test durumu (örnek) |
|---|---|---|---|
| İG-01 Tedarikçi bağımsızlığı | NFR-U2 | FR-01, FR-02, FR-03 | TC-KAM-01..05 (3 marka kamera ile akış/yeniden bağlanma) |
| İG-02 Maliyet | NFR-P1..P3 | FR-05, FR-06, FR-12 | TC-PERF-01..04 (REF-PC ölçümleri) |
| İG-03 Gelir sızıntısı / manuel yük | NFR-D1, NFR-D2 | FR-23, FR-30, FR-35, FR-36 | TC-DGR-01..08 (etiketli set + gölge mod eşleşme) |
| İG-04 Entegrasyon | NFR-B2 | FR-32, FR-33, FR-34 | TC-ENT-01..03 (sözleşme + kuyruk senaryoları) |
| İG-05 KVKK | NFR-S1, NFR-S2 | FR-42, FR-50..53 | TC-KVK-01..04 (maskeleme, otomatik silme, ACL, envanter incelemesi) |

Tam matris, gereksinim yönetim dosyasında (depo içinde `docs/rtm.md`) tutulur ve her sürümde güncellenir.

### 4.12 Önceliklendirme ve kabul kriterleri

MoSCoW dağılımı: M gereksinimleri v1 teslimatının tanımıdır; S gereksinimleri v1 hedefidir ama kabulü bloklamaz; C gereksinimleri kapasite kalırsa alınır. Sistem kabulü dört koşulun birlikte sağlanmasıyla gerçekleşir: (1) 4 haftalık gölge modda, mevcut Hikvision çıktısıyla dakika-pencereli olay eşleşme oranının hedefin üzerinde olması ve farkların analiz raporu; (2) NFR-D1 doğruluk hedeflerinin sabit etiketli test setinde tutması; (3) 72 saatlik kesintisiz dayanıklılık testinde NFR-P2/P3 ve NFR-G1'in sağlanması; (4) KVKK kontrol listesinin hukuk birimi tarafından imzalanması. Kabul kararı işletme birimi + BT + hukuk temsilcisinin ortak onayıdır.

---

## 5. Risk Mühendisliği

### 5.1 Yöntem

Risk yönetimi ISO 31000 çerçevesine göre yürütülür: bağlam belirleme → risk tanımlama → analiz → değerlendirme → işleme → izleme. Teknik riskler ayrıca FMEA (Hata Türleri ve Etkileri Analizi) mantığıyla bileşen düzeyinde ele alınmıştır. Puanlama: Olasılık (O) 1–5, Etki (E) 1–5, Risk Puanı = O × E. Eşikler: 15–25 kırmızı (derhal azaltım planı zorunlu), 8–14 sarı (planlı azaltım), 1–7 yeşil (izle). Risk kaydı canlı bir dokümandır; her faz kapısında (Bölüm 8) yeniden puanlanır.

### 5.2 Risk kaydı

| No | Risk | Kategori | O | E | Puan | Azaltım stratejisi | Sahip |
|---|---|---|---|---|---|---|---|
| R-01 | Gece/düşük ışıkta doğruluğun hedefin altına düşmesi | Teknik/Ortam | 4 | 4 | **16** | IR aydınlatma, pozlama ayarı, gece alt-hedefi ayrı ölçüm (bkz. 5.3) | Geliştirici + Saha |
| R-02 | Erzurum kışında kar/çamur/buzla kapalı plakaların okunamaması | Ortam | 5 | 4 | **20** | Operatör onay akışı (FR-35), kamera siperliği, "okunamadı" olay tipi (bkz. 5.3) | Saha + İşletme |
| R-03 | Saha PC'lerinin CPU gücünün yetmemesi (REF-PC altı donanım) | Teknik | 3 | 4 | 12 | F0'da donanım envanteri, INT8 kuantizasyon, OpenVINO EP, kare atlama | Geliştirici |
| R-04 | Global OCR modelinin TR fontunda sistematik karakter hatası | Teknik | 3 | 4 | 12 | Konum-duyarlı düzeltme (FR-23), karışıklık matrisi analizi, gerekirse TR ince ayarı | Geliştirici |
| R-05 | RTSP akış kararsızlığı (kopma, bozuk kare, gecikme) | Teknik | 4 | 3 | 12 | Otomatik yeniden bağlanma (FR-04), watchdog, bozuk kare filtresi | Geliştirici |
| R-06 | Bellek sızıntısı / native interop (OpenCV, ONNX) kaynak yönetimi hatası | Teknik | 3 | 4 | 12 | 72 saat dayanıklılık testi, `IDisposable` disiplini, bellek profili | Geliştirici |
| R-07 | AGPL lisanslı bileşenin (ör. Ultralytics YOLOv8/11) farkında olmadan kullanımı | Hukuki | 2 | 5 | 10 | Lisans envanteri (K-05), bağımlılık taraması, hukuk onayı | Geliştirici + Hukuk |
| R-08 | KVKK uyumsuzluğu (aydınlatma eksikliği, süresiz saklama) | Hukuki | 3 | 5 | **15** | Aydınlatma metni, saklama/silme politikası (FR-50..53), envanter (bkz. 5.3) | Hukuk + BT |
| R-09 | Yanlış plaka okuma → hatalı ücretlendirme → vatandaş şikâyeti | İş | 3 | 4 | 12 | Güven eşiği altında operatör onayı (FR-35), itiraz sürecinde kanıt görseli (FR-51) | İşletme |
| R-10 | Tek geliştirici bağımlılığı (bilgi tekeli, izin/ayrılma riski) | Organizasyonel | 4 | 4 | **16** | Dokümantasyon disiplini, ADR kayıtları, kod içi açıklama, yedek personel eğitimi (bkz. 5.3) | Yönetim |
| R-11 | Kapsam kayması (v1'e araç tipi, renk, marka tanıma gibi eklemeler) | Yönetim | 4 | 3 | 12 | Kapsam dışı listesi (Bölüm 2), değişiklik talebi süreci, faz kapıları | Yönetim |
| R-12 | Etiketli TR veri setinin doğrulama/ince ayar için yetersiz kalması | Teknik/Veri | 4 | 4 | **16** | Gölge modda otomatik veri toplama, etiketleme şartnamesi (6.4), açık setlerle başlangıç (bkz. 5.3) | Geliştirici |
| R-13 | Windows güncellemelerinin servisi durdurması/bozması | Operasyon | 2 | 3 | 6 | Watchdog + otomatik başlatma, bakım penceresi, güncelleme sonrası sağlık kontrolü | BT |
| R-14 | Kamera sabotajı, çarpma sonucu açı kayması | Operasyon | 2 | 3 | 6 | Periyodik görüntü kalite kontrolü, sahne değişim uyarısı (C önceliği), fiziksel koruma | Saha |
| R-15 | Olay alıcısının (sink/veritabanı) kesintisinde olay kaybı | Teknik | 3 | 3 | 9 | Diske kalıcı kuyruk (FR-33), yeniden gönderim, kuyruk doluluk alarmı | Geliştirici |
| R-16 | Model dosyasının bozulması/yanlış sürümle değiştirilmesi | Operasyon | 2 | 4 | 8 | SHA-256 doğrulama (6.7), açılışta bütünlük kontrolü, imzalı dağıtım paketi | Geliştirici |

### 5.3 Kırmızı risklerin ayrıntılı azaltım planları

**R-02 — Kış koşulları (puan 20, en yüksek risk).** Erzurum'da kasım–mart arası kar, çamur ve buz nedeniyle plakaların fiziksel olarak okunamaz hâle gelmesi, algoritmayla çözülemeyecek bir gerçekliktir; hiçbir OCR kapalı karakteri okuyamaz. Bu yüzden azaltım stratejisi teknik değil operasyoneldir: sistem "okunamadı/düşük güven" durumunu birinci sınıf olay tipi olarak üretir (FR-35), bariyer operatör onayına düşer ve operatörün girdiği plaka, görüntüyle birlikte düzeltme kaydı olarak saklanır (FR-36). KPI tarafında kış ayları için ayrı bir manuel onay oranı hedefi tanımlanır (Bölüm 7); yaz hedefiyle kış performansını aynı çizgide değerlendirmek sistemi haksız yere başarısız gösterir. Kamera tarafında ısıtmalı muhafaza ve siperlik zorunludur; giriş yolunda plaka hizasına kar birikmesini azaltacak fiziksel düzenleme (saçak, hız kesici konumu) saha ekibiyle planlanır. Pilot dönemi bilinçli olarak en az bir kış ayını kapsayacak şekilde zamanlanmalıdır.

**R-01 — Gece performansı (puan 16).** Gece doğruluğu gündüzden ayrı ölçülür ve ayrı hedefe bağlanır (NFR-D1: gece ≥ %90). Azaltım üç katmanlıdır: donanımda IR aydınlatma ve IR kesim filtresi doğru yapılandırılmış kamera; kamera ayarında plaka bölgesini patlatmayan pozlama (shutter öncelikli mod, WDR); yazılımda gece kareleri için ayrı ön işleme profili (kontrast/gamma) ve çoklu kare oylamasında gece için daha yüksek kare sayısı. F0 PoC'sinde gece görüntüleri özellikle toplanır; gece doğruluğu %90'ın belirgin altında kalırsa OCR modelinin gece görüntüleriyle ince ayarı F1 kapsamına alınır.

**R-08 — KVKK uyumsuzluğu (puan 15).** Kurul'un 2023/924 kararı, otopark plaka işlemenin m.5/2-e kapsamında hukuka uygun olabildiğini ama aydınlatma yükümlülüğü ihlalinin tek başına 75.000 TL ceza doğurduğunu göstermiştir. Azaltım: yazılım teslimatının bir parçası olarak aydınlatma metni taslağı, veri işleme envanteri güncellemesi ve saklama/silme politikası hukuk birimine sunulur; FR-50..53 gereksinimlerinin kabul testleri hukuk temsilcisi gözetiminde koşulur; sistem devreye alınmadan aydınlatma tabelaları asılır. Bu risk "yazılım bitince bakarız" denecek bir konu değildir — kabul kriterlerine (4.12) bağlanmıştır.

**R-10 — Tek geliştirici bağımlılığı (puan 16).** Projenin tek kişiyle yürümesi kısa vadede hız, uzun vadede kırılganlıktır. Azaltım: her mimari karar ADR olarak yazılır (6.1); depo README'si sıfırdan kurulum ve dağıtımı adım adım anlatır; runbook'lar (6.5) operasyon bilgisini geliştiriciden bağımsızlaştırır; kurumdan en az bir yedek personel F1 pilotu sırasında sisteme dahil edilir ve dağıtım/geri alma tatbikatını bizzat yapar. Bilgi aktarımının ölçütü belgenin varlığı değil, yedek personelin belgeyle işi tek başına yapabilmesidir.

**R-12 — Veri seti yetersizliği (puan 16).** Doğruluk hedefleri (NFR-D1) ancak temsilî bir etiketli setle kanıtlanabilir; set yoksa hedefler kâğıt üzerinde kalır. Azaltım: F0'dan itibaren gölge modda her olayın kırpılmış plaka görüntüsü + Hikvision okuması + (varsa) operatör düzeltmesi otomatik biriktirilir; 6.4'teki etiketleme şartnamesine göre en az 5.000 görüntülük, mevsim/saat/hava dağılımı dengeli bir set F1 sonuna kadar tamamlanır. Başlangıç kıyaslaması için Bigailab-trvlp-20k gibi açık TR setleri kullanılır ancak kendi sahamızın dağılımını temsil etmedikleri bilinerek yalnızca ön eleme amaçlı değerlendirilir.

### 5.4 FMEA kesiti (boru hattı bileşenleri)

| Bileşen | Hata türü | Etki | Tespit mekanizması | Önlem |
|---|---|---|---|---|
| RTSP alıcı | Akış kopması | Olay kaçırma | Watchdog kare sayacı | Otomatik yeniden bağlanma + alarm (FR-04, FR-43) |
| Kod çözücü | Bozuk kare | Yanlış tespit | Kare bütünlük kontrolü | Bozuk kareyi at, sayacı logla |
| Tespit modeli | Plakayı kaçırma (FN) | Olay kaçırma | Gölge mod karşılaştırma | Eşik ayarı, hareket tetikleme penceresi |
| Tespit modeli | Yanlış bölge (FP) | Gereksiz OCR yükü | Alan/oran filtresi | Geometri doğrulama (FR-11) |
| OCR | Karakter karışıklığı (O↔0, B↔8, I↔1) | Yanlış plaka | Format doğrulama (FR-20..22) | Konum-duyarlı düzeltme (FR-23) |
| Konsensüs | Erken karar (az kare) | Düşük güvenli olay | Güven skoru eşiği | Minimum kare sayısı şartı (FR-30) |
| Olay kuyruğu | Taşma | Olay kaybı | Doluluk metriği | Kalıcı kuyruk + geri basınç (FR-33) |
| Sink | Veritabanı kesintisi | Olay iletilemez | Teslim onayı (ack) | Yeniden deneme + diskte bekletme (FR-33) |

---

## 6. Yazılım Mühendisliği Disiplinleri (SWEBOK v4 Kesitleri)

Bu bölüm, gereksinimlerin (Bölüm 4) ve risklerin (Bölüm 5) diğer mühendislik alanlarına nasıl yansıdığını SWEBOK v4 bilgi alanları çerçevesinde özetler. Amaç ders kitabı tekrarı değil, bu projeye özgü kararların kaydıdır.

### 6.1 Yazılım mimarisi

Mimari stil, tek süreç içinde üretici–tüketici boru hattıdır: RTSP alıcı → kod çözücü → hareket/tespit → OCR → doğrulama/düzeltme → konsensüs → olay yayınlama. Aşamalar `System.Threading.Channels` üzerinde sınırlı (bounded) kanallarla bağlanır; kanal dolduğunda en eski kare düşürülür (görüntü kaybı kabul edilebilir, olay kaybı edilemez — bu yüzden olay kuyruğu kalıcıdır, kare kuyruğu değildir). Kamera başına bir boru hattı örneği çalışır; 4 kameralı sahada CPU bütçesi NFR-P2 ile sınırlandığı için tespit aşaması kameralar arasında zaman dilimli paylaştırılabilir.

Anahtar mimari kararlar ADR (Architecture Decision Record) olarak depoda tutulur. Başlangıç seti:

| ADR | Karar | Gerekçe özeti |
|---|---|---|
| ADR-001 | Çıkarım motoru olarak ONNX Runtime (gerekirse OpenVINO EP) | Tedarikçi bağımsızlığı, CPU optimizasyonu, .NET birinci sınıf desteği |
| ADR-002 | Başlangıç modelleri AlprNet uyumlu (YOLO tespit + CCT global OCR) | Hazır .NET entegrasyonu; TR ince ayarı sonradan mümkün |
| ADR-003 | Aşamalar arası iletişim bounded Channel; olaylar kalıcı disk kuyruğu | Geri basınç kontrolü + olay kaybı sıfır hedefi |
| ADR-004 | Kütüphane (DLL) + isteğe bağlı Windows Servisi barındırıcı ayrımı | Mevcut masaüstü/web uygulamalarına gömülebilme (K-02) |

### 6.2 Yazılım tasarımı

Tasarım SOLID ilkelerine, özellikle bağımlılığın tersine çevrilmesine yaslanır: boru hattı `IPlateDetector`, `IPlateOcr`, `IPlateValidator`, `IEventSink` soyutlamalarına bağlıdır; AlprNet, ONNX Runtime veya ileride başka bir motor bu arayüzlerin arkasına takılır. Böylece model/kitaplık değişimi boru hattını etkilemez (İG-01'in kod düzeyindeki karşılığı). Geri basınç, kanal kapasiteleriyle bildirimseldir; hiçbir aşama bloklayarak kamera alımını durduramaz. Tüm görüntü tamponları havuzlanır (`ArrayPool<byte>`) ve `IDisposable` zinciri deterministiktir (R-06 azaltımı).

### 6.3 Yazılım inşası

Hedef çerçeve .NET 8 (LTS), kitaplık `netstandard2.0` uyumluluğu ayrıca değerlendirilir (NFR-U1). `Nullable` etkin, analizörler (`Microsoft.CodeAnalysis.NetAnalyzers`) uyarı=hata modunda. Görüntü işleme OpenCvSharp ile; native bellek `Mat` yaşam döngüsü kurallarına sıkı bağlıdır. Türkçe'ye özgü bir inşa tuzağı bilinçli olarak yönetilir: `ToUpper()` TR kültüründe `i → İ` üretir ve plaka normalizasyonunu bozar; tüm plaka metin işlemleri `ToUpperInvariant()` ve `StringComparison.Ordinal` ile yapılır, bu kural analizör ve kod incelemesiyle denetlenir.

### 6.4 Yazılım testi

Test stratejisi piramittir: birim testleri (doğrulama/düzeltme mantığı, format tabloları — hızlı ve bol), bileşen testleri (ONNX modelleri sabit "golden" görüntü setiyle), sistem testleri (MediaMTX ile kayıtlı RTSP akışlarının tekrarı — kameraya bağımlı olmadan uçtan uca), saha testleri (gölge modda Hikvision ile dakika-pencereli olay eşleştirme).

Etiketli set şartnamesi (R-12): en az 5.000 plaka görüntüsü; dağılım hedefi ≥ %25 gece, ≥ %15 yağış/kar, ≥ %10 kirli/kısmen kapalı plaka; her kayıt = kırpılmış görüntü + tam kare + doğru plaka metni + koşul etiketleri (saat, hava, kamera). Etiketler iki kişi tarafından bağımsız girilir, çelişkiler üçüncü gözle çözülür. Model regresyon kapısı: herhangi bir model/parametre değişikliği, sabit set üzerinde önceki sürümden anlamlı gerileme (karakter doğruluğu −0,5 puandan fazla) gösterirse birleştirilemez.

### 6.5 Yazılım operasyonu

Barındırma Windows Servisi + watchdog'dur: servis çökerse otomatik yeniden başlatılır, kare akışı 60 saniye durursa alarm üretilir (FR-43). Günlük operasyon rutini: her sabah otomatik sağlık raporu (kamera başına olay sayısı, ortalama güven, kuyruk derinliği, CPU/RAM); haftada bir rastgele 50 olayın operatörce doğruluk örneklemesi (Bölüm 7 KPI'larını besler). Runbook'lar en az şu senaryoları kapsar: kamera değişimi, PC değişimi, model sürüm geri alma, kuyruk taşması müdahalesi, KVKK veri silme talebi. Runbook'un geçerlilik ölçütü, yedek personelin (R-10) senaryoyu belgeden yürütebilmesidir.

### 6.6 Yazılım bakımı

Bakımın ana döngüsü veri odaklıdır: operatör düzeltmeleri (FR-36) hata örneği olarak birikir → her 3 ayda karışıklık matrisi analiz edilir → sistematik hata varsa (ör. belirli fontta 5↔S) OCR ince ayarı yapılır → yeni model sabit sette regresyon kapısından geçer → önce tek sahada 1 hafta, sonra tüm sahalarda aşamalı dağıtılır. Düzeltici bakım (hata), uyarlayıcı bakım (yeni kamera modeli, OS güncellemesi) ve iyileştirici bakım (doğruluk artışı) ayrı etiketlerle izlenir.

### 6.7 Yapılandırma yönetimi

Tek Git deposu; SemVer sürümleme (kitaplık API'si kırılırsa major). Model dosyaları büyük ikili olduğundan sürüm etiketiyle eşlenmiş ayrı depolama alanında tutulur; her modelin SHA-256'sı yapılandırmaya yazılır ve servis açılışta doğrular (R-16). Dağıtım paketi = DLL + modeller + varsayılan yapılandırma + sürüm notu; her sahada bir önceki paket geri alma için saklanır. Yapılandırma değişiklikleri (eşikler, kamera adresleri) de sürümlenir — "hangi sahada hangi eşik" sorusunun cevabı depodadır, kimsenin hafızasında değil.

### 6.8 Yazılım mühendisliği yönetimi

Fazlar ve kapılar Bölüm 8'de; yönetim ilkeleri şunlardır: tek geliştirici gerçeği nedeniyle tahminlere %30 tampon eklenir; her faz sonunda kapı toplantısı (işletme + BT) yapılır ve risk kaydı yeniden puanlanır; kapsam değişiklikleri yalnızca kapı toplantısında ve İG etkisiyle birlikte değerlendirilir (R-11). İlerleme ölçüsü yazılan kod değil, kapanan gereksinim ve geçen kabul testidir.

### 6.9 Yazılım mühendisliği süreci

Süreç iteratif-artımlıdır: her iterasyon 2 hafta, çıktısı çalışan ve ölçülen bir dilimdir (ör. "RTSP + tespit uçtan uca", sonra "+ OCR", sonra "+ konsensüs"). Şelale benzeri tek büyük teslim bilinçli olarak reddedilmiştir çünkü doğruluk hedefleri ancak sahada ölçülerek kalibre edilebilir; erken ve sık saha ölçümü sürecin çekirdeğidir.

### 6.10 Yazılım kalitesi

Kalite güvence mekanizmaları: statik analiz + kod incelemesi (yedek personel devreye girene kadar öz-inceleme kontrol listesiyle), test kapsamı hedefi ≥ %80 (NFR-B1), sabit set regresyon kapısı (6.4), 72 saat dayanıklılık testi (4.12), gölge mod eşleşme raporu. Kalite verisi KPI panosunda (Bölüm 7) görünür; "kalite" soyut bir söz değil, izlenen sayılardır.

### 6.11 Yazılım güvenliği

RTSP kimlik bilgileri düz metin yapılandırmada tutulmaz; Windows DPAPI ile makine kapsamında şifrelenir. Kameralar ayrı VLAN'da, PC'den yalnızca gerekli portlara erişim açıktır. Diskteki görüntü klasörleri BitLocker ile korunur (NFR-S2). Log'larda tam plaka maskelenir (FR-42: `25***234` biçimi), hata ayıklama gerektiğinde maskesiz log yalnızca geçici ve onaylı olarak açılır. Dağıtım paketleri imzalanır; servis yalnızca imza doğrulanan paketi kabul eder.

### 6.12 Yazılım mühendisliği ekonomisi

Projenin ekonomik gerekçesi tedarik maliyeti farkıdır: ANPR özellikli kamera yerine standart IP kamera + yazılımsal tanıma. Kaba TCO kıyası (saha başına, 4 kamera): ANPR kamera yaklaşımında donanım farkı kamera başına belirgin biçimde yüksektir ve marka kilidi sürer; yazılımsal yaklaşımda fark, geliştirme eforunun sahalara bölünmesiyle amorti edilir ve her yeni saha marjinal maliyetle eklenir. Kesin rakamlar tedarik birimiyle F0 sonunda netleştirilir; bu bölümün amacı karar çerçevesini kaydetmektir: geliştirme maliyeti sabit, tasarruf saha sayısıyla ölçeklenir — sistem ne kadar çok sahaya yayılırsa iş gerekçesi o kadar güçlenir (İG-02).

---

## 7. Başarı Ölçütleri (KPI)

| KPI | Hedef | Ölçüm yöntemi | Periyot |
|---|---|---|---|
| Genel plaka doğruluğu | ≥ %93 (gündüz ≥ %97, gece ≥ %90) | Haftalık 50 olaylık rastgele örneklem + sabit set | Haftalık |
| Karakter doğruluğu | ≥ %99 | Sabit etiketli set | Sürüm başına |
| Uçtan uca gecikme (p95) | ≤ 2 sn | Servis içi telemetri | Sürekli |
| Manuel onaya düşen olay oranı | ≤ %8 (kış ayları ≤ %15) | Olay bayrak istatistiği | Aylık |
| Yanlış kabul (FP) oranı | ≤ %0,5 | Gölge mod / örneklem karşılaştırma | Aylık |
| Servis çalışırlık (uptime) | ≥ %99,5 | Watchdog + sağlık raporu | Aylık |
| Arıza sonrası toparlanma (MTTR) | ≤ 30 dk | Olay kayıtları | Olay başına |
| Olay kaybı (kalıcı kuyruk sonrası) | 0 | Kuyruk denetim logu | Sürekli |
| KVKK denetim bulgusu | 0 kritik bulgu | İç denetim kontrol listesi | 6 aylık |

Kış ayları için manuel onay hedefinin ayrı tutulması bilinçlidir (R-02): Erzurum koşullarında karla kaplı plakanın operatöre düşmesi sistem hatası değil, tasarlanmış davranıştır.

---

## 8. Yol Haritası ve Karar Kapıları

| Faz | Süre | İçerik | Kapı kriteri (geçiş şartı) |
|---|---|---|---|
| **F0 — PoC** | 2–4 hafta | AlprNet + ONNX Runtime ile tek kamera, kayıtlı akış üzerinde uçtan uca prototip; REF-PC performans ölçümü; donanım envanteri; lisans taraması | REF-PC'de p95 ≤ 2 sn ve kabul edilebilir ilk doğruluk; K-05 lisans onayı; aksi hâlde model/motor alternatifine dönülür |
| **F1 — Pilot (gölge mod)** | 4–6 hafta (en az bir kış ayını kapsamalı) | Tek sahada 4 kamera, Hikvision ile paralel çalışma; veri toplama ve etiketli set inşası; aydınlatma metni ve KVKK hazırlığı; yedek personel eğitimi | 4 haftalık gölge eşleşme hedefi; NFR-D1 sabit sette tutuyor; 72 saat dayanıklılık; KVKK kontrol listesi hukuk imzalı (4.12 kabul kriterleri) |
| **F2 — Geçiş (cutover)** | 2 hafta | Pilot sahada Hikvision'dan yazılımsal sisteme geçiş; operatör onay akışının canlı kullanımı; runbook tatbikatı | 2 hafta canlıda KPI hedefleri tutuyor; geri alma tatbikatı başarılı |
| **F3 — Yaygınlaştırma** | Saha başına ~1 hafta | Diğer otoparklara aşamalı kurulum; merkezi sağlık izleme; 3 aylık bakım döngüsünün başlatılması (6.6) | Her sahada 1 haftalık KPI doğrulaması; risk kaydının güncellenmesi |

Her kapıda "devam / düzelt / dur" kararı işletme + BT ortak toplantısında verilir. F0 başarısız olursa kayıp yalnızca 2–4 haftalık efordur — riskin en ucuz test edildiği yer başlangıçtır; bu, F0'ın varlık sebebidir.

---

## 9. Kaynakça

**Akademik yayınlar**

1. Kılıç, İ. & Aydın, G., "Turkish Vehicle License Plate Recognition Using Deep Learning", Fırat Üniversitesi, 2018 (IEEE IDAP) — 34.580 görüntülük kapı IP kamerası veri seti. https://ieeexplore.ieee.org/document/8620744/ · https://www.researchgate.net/publication/331422356_Turkish_Vehicle_License_Plate_Recognition_Using_Deep_Learning
2. Kılıç, İ. & Aydın, G., "Bigailab-trvlp-20k: Turkish License Plate Detection Using Deep Learning With A New Benchmark Dataset" — 20.404 görüntülük açık TR plaka seti. https://www.researchgate.net/publication/338790719
3. Bayram, F., "Derin Öğrenme Tabanlı Otomatik Plaka Tanıma", Politeknik Dergisi, 2020 (Mask R-CNN yaklaşımı). https://dergipark.org.tr/tr/pub/politeknik/issue/57048/515830
4. "Bölgesel Tabanlı Evrişimli Sinir Ağı ile Araç Plaka Tanıma", DÜBİTED, 2023 (3 aşamalı YOLO+CNN boru hattı). https://dergipark.org.tr/en/pub/dubited/article/1058850
5. IEEE 2024, YOLOv2 + LPRNet ile Kendryte K210 üzerinde gömülü plaka tanıma (kuantizasyonun düşük donanımda zorunluluğu). https://ieeexplore.ieee.org/document/10710693/

**Açık kaynak projeler ve araçlar**

6. AlprNet — .NET/ONNX plaka tanıma kitaplığı (yolo-v9-t-512 tespit + cct_s_v1 global OCR); bu raporun birincil aday teknolojisi. https://github.com/Temppus/AlprNet
7. fast-alpr / fast-plate-ocr (ankandrew) — ONNX tabanlı, Intel CPU'da OpenVINO EP önerisi, kendi veriyle ince ayar desteği. https://github.com/ankandrew/fast-alpr · https://github.com/ankandrew/fast-plate-ocr
8. ort-alpr — WPOD-NET + Paddle OCR, CPU-ONNX örneği. https://github.com/tharakarehan/ort-alpr
9. OpenALPR (açık kaynak sürümü; bakımı durmuş, Tesseract tabanlı — neden seçilmediğinin kaydı). https://github.com/openalpr/openalpr
10. KBY-AI ANPR .NET SDK (ticari alternatif, kıyas noktası). https://github.com/kby-ai/Automatic-License-Plate-Recognition-CSharp-.NET

**Hukuk ve mevzuat**

11. KVKK Kurul Kararı 2023/924 — otopark plaka işlemede m.5/2-e hukuka uygunluk, ancak aydınlatma ihlaline 75.000 TL ceza. https://www.kvkk.gov.tr/Icerik/7763/2023-924
12. KVKK Kurul Kararı 2021/1110 — plaka + kimlik verisi ifşasına 200.000 TL ceza. https://www.kvkk.gov.tr/Icerik/7265/2021-1110

**Alan bilgisi**

13. Türkiye plaka formatı ve il kodları (01–81), harf grubu kuralları. https://www.axasigorta.com.tr/blog/turkiye-il-plaka-kodlari · https://rsotoekspertiz.com/plaka-harf-grubu-sorgulama/

**Standartlar ve gövde bilgisi**

14. ISO/IEC/IEEE 29148:2018 — Gereksinim mühendisliği (Bölüm 4'ün çerçevesi).
15. ISO/IEC 25010 — Yazılım kalite modeli (NFR sınıflandırması).
16. ISO 31000 — Risk yönetimi (Bölüm 5'in çerçevesi).
17. SWEBOK v4 (IEEE CS) — Yazılım mühendisliği bilgi alanları (Bölüm 6'nın çerçevesi).

---

*Rapor sonu — Sürüm 1.0, Temmuz 2026. Bu doküman canlıdır: her faz kapısında gereksinimler, riskler ve KPI hedefleri saha verisiyle güncellenir.*
