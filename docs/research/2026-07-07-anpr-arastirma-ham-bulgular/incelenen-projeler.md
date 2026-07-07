# İncelenen Projeler/Ürünler

## OpenALPR (açık kaynak) (acik-kaynak) [acik-kaynak]
AGPL-3.0, C++ + resmi C# binding, Tesseract OCR, ülke konfigürasyonu (eu/us), bölgesel regex pattern matching, top-N aday + güven skoru, ~58 ms/kare. Son sürüm 2016, fiilen bakımsız; Rekor kıyasında ilk tahminde yalnızca %42.16 doğruluk. Ders: klasik OCR yetersiz, DL şart; AGPL nedeniyle kod alınmaz ama pattern-matching ve alprd daemon mimarisi iyi referans.
kaynak: https://github.com/openalpr/openalpr

## Rekor Scout (OpenALPR ticari) (ticari) [acik-kaynak]
İlk tahminde %99.02 doğruluk; i7-6700 CPU'da 720p 17.3 fps; düşük hız için kamera başına 5-10 fps kılavuzu. En değerli desenler: plaka gruplama (4500 ms pencere, min 2 okuma, ≥%80 güven, 7500 ms maks gecikme), motion detection + 60 karelik buffer (CPU yetişmese de hareket bitince kareler işlenir), kanıt JPEG kalite 85 + 8000 MB FIFO disk kotası, yerel beanstalkd kuyruk + webhook. Otopark ANPR servis mimarimizin birebir şablonu.
kaynak: https://docs.rekor.ai/scout/agent/benchmarking

## ultimateALPR SDK (Doubango) (ticari) [acik-kaynak]
CPU'da en hızlı iddiası: i7-4790K + OpenVINO ile 41.7-131.8 fps (OpenVINO kapalıysa 18-23.5 fps → hızlandırma 2-3x kazandırıyor), Raspberry Pi 4'te bile 7-12 fps. IENV gece görüntü iyileştirme 720p'de ~2 ms. 150+ ülke Latin charset, karakter bazlı güven, lisans anahtarsız/İnternetsiz tamamen cihaz-içi çalışma. C++ SDK; deneme sürümü kısıtlı. Bizim için CPU performans çıtası ve gece-iyileştirme referansı.
kaynak: https://github.com/DoubangoTelecom/ultimateALPR-SDK

## CodeProject.AI Server ALPR modülü (acik-kaynak) [acik-kaynak]
YOLO (v5/v11 ONNX) tespit + PaddleOCR (pp_ocrv3/v4) tanıma; Windows/Linux/ARM64, CPU/CUDA/DirectML; REST API ile Blue Iris entegrasyonu (otopark/erişim kontrolünde yaygın hobi kurulumu). DİKKAT: LICENSE dosyası SSPL — kapalı ticari üründe kullanılamaz. Ders: YOLO+PaddleOCR ikilisi zayıf CPU'da çalışan kanıtlanmış kombinasyon; REST modül mimarisi entegrasyonu kolaylaştırıyor.
kaynak: https://github.com/codeproject/CodeProject.AI-ALPR

## fast-alpr + fast-plate-ocr + open-image-models (ankandrew) (acik-kaynak) [acik-kaynak]
Üçü de MIT lisanslı, saf ONNX Runtime (CPU/OpenVINO/DirectML provider'ları) — bizim .NET+ONNX CPU stack'ine en uygun yapı taşları. Tespit: YOLOv9-t 256-640 px varyantları (640'ta 0.966 precision). OCR: CCT mimarisi global modeller (cct-xs-v2 0.47 ms/2144 plaka-sn RTX3090'da; CPU'da da hafif), return_confidence ile karakter güveni, eğitim/fine-tune CLI + ONNX export → TR plakasına ince ayar mümkün. Tracking/dedup/webhook YOK — o katmanı biz yazacağız.
kaynak: https://github.com/ankandrew/fast-alpr

## Frigate NVR 0.16 LPR (acik-kaynak) [acik-kaynak]
MIT; YOLOv9 tespit + PaddleOCR; ön şart AVX+AVX2 ve 4GB RAM. En zengin açık kaynak konfigürasyon referansı: detection_threshold 0.7 / recognition_threshold 0.9 / min_area 1000 px² / min_plate_length, format regex + sıralı replace_rules (O→0), known plates + match_distance=1 tolerans, dedicated LPR kamera modu (araç tespiti olmadan tam karede plaka arama — bariyer kamerası için ideal), gece için eşik düşürme + enhancement 0-10, timestamp maskeleme, debug_save_plates, MQTT olayları. GPU'nun LPR'a kayda değer kazanç sağlamadığını belirtiyor (CPU yeterli).
kaynak: https://docs.frigate.video/configuration/license_plate_recognition/

## Plate Recognizer Snapshot/Stream (ParkPow) (ticari) [acik-kaynak]
On-prem Docker SDK, GPU'suz çalışır; Snapshot 50-100 ms çıkarım, Stream orta sınıf CPU'da 4-10 kamera. Türkiye resmen destekli ülke. Otopark parametreleri ders niteliğinde: max_prediction_delay (parking=3 sn), memory_decay=300 sn dedup, sample=2 kare atlama, webhook 3 retry + disk cache + 2 dk tekrar, kanıt görüntüsü seçimi (orijinal/araç/plaka kırpıntı), CSV/JSONL, PNG maske ROI, RTSP üstel yeniden bağlanma (2→600 sn), Health Score <%70 uyarısı. Kamera kılavuzu: plaka ~100 px genişlik hedefi, 30 px alt sınır, <45° açı. Abonelik lisansı ayda birkaç kez internet ister (perpetual hariç).
kaynak: https://guides.platerecognizer.com/docs/stream/configuration/

## LPRNet (sirius-ai/LPRNet_Pytorch vb.) (acik-kaynak) [acik-kaynak]
Segmentasyonsuz, RNN'siz hafif OCR mimarisi (~0.11 ms tanıma, gömülü cihazlarda gerçek zamanlı); değişken uzunluklu plakaları destekler. Hazır TR modeli yok, sıfırdan eğitim gerekir — fast-plate-ocr'ın CCT modelleri daha güncel ve pratik alternatif. Model mimarisi/CPU bütçesi referansı olarak değerli.
kaynak: https://github.com/sirius-ai/LPRNet_Pytorch

## Türkiye'ye özgü açık kaynak projeler ve veri setleri (tr-yerli) [acik-kaynak]
Semihocakli/turkish-plate-recognition-w-yolov8 (YOLOv8+ONNX, C++, TR plaka odaklı), utkuatasoy/License-Plate-Recognition-System (YOLO+EasyOCR, 52k+ etiketli hibrit veri seti), Roboflow 'Turkish Number Plates' (2246 görüntü + hazır model), Kaggle sentetik TR plaka üreteci. Hiçbiri üretim kalitesinde uçtan uca çözüm değil; asıl değerleri TR fine-tune için veri kaynağı ve TR karakter seti/format doğrulama örnekleri.
kaynak: https://github.com/Semihocakli/turkish-plate-recognition-w-yolov8-onnx-to-engine-cpp

## ALPR-Database (algertc) (acik-kaynak) [acik-kaynak]
Blue Iris + CodeProject.AI üstüne tam özellikli plaka veritabanı platformu: olay arşivi, arama, bilinen plaka yönetimi, bildirimler. Ders: ANPR motoru tek başına yetmez — olay DB'si, arama UI'ı ve retention yönetimi ayrı bir katman olarak tasarlanmalı.
kaynak: https://github.com/algertc/ALPR-Database

## LPR kamera kurulum kılavuzları (Plate Recognizer / Axis / Vivotek) (standart) [acik-kaynak]
Ölçülebilir kurulum standartları: plaka genişliği ~100 px hedef (30 px minimum), yatay/dikey açı <45°, hızlı araç için yüksek shutter, IR aydınlatma ve gece ayrı profil. Hikvision yerleşik ANPR'ı ikame ederken kameranın optik kurulumunun yazılım kadar belirleyici olduğunu gösteriyor — kabul testi bu metriklerle yapılmalı.
kaynak: https://platerecognizer.com/lpr-camera-resolution-guide/

## Hikvision DS-TCG406-E (Access ANPR) (ticari) [ticari-otopark]
İkame etmek istediğimiz sınıfın referansı: yakalama ≥%99 / tanıma ≥%98.5 / yön ≥%98 (koşullu), video+loop+radar tetik, multi-frame LPR, kirli plaka, IR 850nm + ılık beyaz (10-20 m), 2 röle + 4 Wiegand formatı + RS485, 100k çevrimdışı beyaz liste ile bariyer açma, bariyer durum geri bildirimi, normally-open otomatiği, ANR + 1TB TF tampon, ISAPI/ONVIF/ISUP, Türkiye modeli mevcut. Bizim yazılım en az 'çevrimdışı beyaz liste + kanıt fotoğraf + röle/olay' üçlüsünü karşılamalı.
kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000078840/DS-TCG406-E_Datasheet_20251106.pdf

## Dahua ITC413-PW4D (Access ANPR) (ticari) [ticari-otopark]
Yakalama ≥%99, AB plaka tanıma ≥%98 (Türkiye baseline destekli); 110k allow/blocklist, 2 röle (2A 30VDC), 2x RS485, Wiegand SHA-1 26bit, IR 850nm veya çift aydınlatıcı (730nm+ılık beyaz), yakalama mesafesi 3-8/8-20 m, şerit genişliği 3-4/6-8 m, doğrulanabilir filigran, plakasız araç algılama, -30..+65°C. Kanıt görüntüsü filigranı ve plakasız araç senaryosu bizim tasarıma alınmalı.
kaynak: https://material.dahuasecurity.com/uploads/cpq/prm-os-srv-res/smart/datasheetzipfiles/ITC413-PW4D-Series_datasheet_20230407.pdf

## Survision (Nanopak 5 + CDK/SSWS API) (ticari) [ticari-otopark]
En öğretici API modeli: New/Decision/End olayları, 0-100 güven + karakter bazlı güvenilirlik, JSON WebSocket + HTTP Push, standalone modda Levenshtein toleranslı (matchTolerance) yerel beyaz liste ve openBarrier röle darbesi, ~2100 olay tamponu, waitForEnd/reemitDecision tekilleştirme, 20 ms tanıma, 60 fps tetiksiz motor, N/N-1/N-2 doğruluk ölçümü ve sözleşmesel okuma oranı garantisi söylemi. Olay şemamızı ve fuzzy whitelist eşleştirmeyi buradan modellemeliyiz.
kaynak: https://fr.survisiongroup.com/documentations/integration_doc.html

## Genetec AutoVu SharpV + Free-Flow (ticari) [ticari-otopark]
Uçta işlem (sunucu çökse de kesintisiz), monokrom global shutter ALPR sensörü + ayrı renkli bağlam kamerası, darbeli LED 0 lux (940/850/740/590nm), yerleşik sanal loop/hız/yön analitiği, plakaya bağlı kapı açma kimliği, -40..+65°C, IP67. Free-Flow modülü bariyersiz giriş/çıkış eşleştirme + doluluk panosu — belediye için faz-2 vizyonu.
kaynak: https://videoguard.nl/assets/pdf/EN-Genetec-Autovu-Sharpv-Datasheet.pdf

## Nedap ANPR Lumo (ticari) [ticari-otopark]
Erişim kontrolü odaklı altın standart: 2-10 m menzil, 100.000 kayıtlı zaman bazlı beyaz/kara/yoksay listeleri (standalone), REST API ile liste yönetimi ve son okuma sorgusu, Wiegand 26 SHA1/64/custom + OSDP v2 güvenli kanal + RS485, 2 röle + 2 opto giriş, IR 850nm, 10 GB yerel depolama. 'Kamera = tek başına erişim kontrolcüsü' konsepti çevrimdışı gereksinimimizi tanımlar.
kaynak: https://resources.nedapidentification.com/web/ANPR/Documentation/Datasheets/Datasheet_ANPR_LUMO_EN.pdf

## Plate Recognizer Stream + ParkPow + Gate Opener (ticari) [ticari-otopark]
CPU-only fizibilite kanıtı: 2 kamera Passmark ~2440 CPU'da (i3-4100M), 50-100 ms/kare, AVX şart, sample=2-3 ile yük kontrolü; webhook → yerel accessList.csv → röle (Gate Opener) mimarisi bizim tasarımın birebir emsali; ParkPow'a webhook ile park yönetimi/uyarı. Kamera kurulum kılavuzu (plaka ≥80-100 px, açı ≤45°, 1/300-1/600 enstantane, AGC/DNR/BLC kapalı, IR mesafe eşleşmesi) doğrudan uygulanabilir.
kaynak: https://platerecognizer.com/stream/

## Milestone XProtect LPR (ticari) [ticari-otopark]
Ölçülebilir görüntü gereksinimlerinin kaynağı: 2.7 px/stroke, AB plakası ≥170 px (duran) / 280 px (hareketli); ülke modülü yaklaşımı (200+ modül) = bizim TR şablon doğrulaması; güven eşiği ayarı (yüksek eşik=az ama doğru okuma); 'kar ve parlak güneş özel yapılandırma gerektirir' resmi uyarısı.
kaynak: https://doc.milestonesys.com/2021r2/en-US/add-ons/add-on_lpr/lpr_platewidthrecommendat.htm

## TagMaster / Quercus SmartLPR Access (ticari) [ticari-otopark]
HRR (High Reliability Recognition) ile %99+ başarı iddiası (İngiltere plakaları), çift lamba (renkli + IR aynı ünitede), motorlu lens ile zor açılarda okuma, REST API ile otopark sistemine entegrasyon, all-in-one (harici yazılım yok), IP65/67; CT45 modeli 'kışın en kötüsünde çalışmak için tasarlandı' — kar pazarlaması yapan tek üretici söylemlerinden.
kaynak: https://quercus-technologies.com/products/smartlpr

## HUB Parking JUPITER / JMS (ticari) [ticari-otopark]
Otopark operatörü perspektifi: plaka = sanal bilet, bilet+plaka eşleşmesi dolandırıcılığı engeller, kayıp bilette plakadan giriş zamanı bulunur, kara liste yönetimi; JMS bulutta bile 'Jupiter Business Logic sahada kalır' — internet kesilse de giriş/çıkış/abone erişimi sürer; üçüncü parti LPR kameralar açık API ile entegre edilir. Bizim servis de otopark yazılımına 'kamera' gibi açık API/webhook sunmalı.
kaynak: https://www.hubparking.com/features/license-plate-recognition-lpr/

## SKIDATA Power.Gate / LPR (ticari) [ticari-otopark]
Kar/soğuk uzmanı: kayak merkezi otoparklarında ön+arka çift LPR kamera ile 'donma noktasında ve kar fırtınasında bile' plaka okuma; biletli+biletsiz karma çalışma, Pay-Per-Use/Pay-Later bariyersiz modlar. Erzurum kışı için 'tek açı yetmezse ikinci açı' ve okumama durumunda alternatif kimlik (bilet) dersleri.
kaynak: https://www.skidata.com/en-us/skidata-blog/double-your-guests-convenience-with-parking-management-for-ski-resorts

## Amano McGann + INEX ALPR entegrasyonu (ticari) [ticari-otopark]
Ayrık mimari örneği: INEX kamerası IR + renkli çift görüntü üretir, olay IZCentral ara katmanından iParc Professional otopark yönetimine geçer, kapıyı ANPR değil otopark yazılımı açar (bilet ödendiyse). Bizim hedef mimariyle aynı: ANPR servisi okur, bariyer kararını otopark yazılımı verir.
kaynak: https://www.inextechnologies.com/wp-content/uploads/2019/08/AMANO_INEX_INTEGRATION.pdf

## Traffix (Türkiye) (tr-yerli) [ticari-otopark]
Yerli pazar iddiaları: AI-Vision serisinde %99.8 doğruluk, tam KVKK uyumu, 24 saat destek, bulut panel; global markaları 'döviz bazlı pahalı, KVKK kısmi uyumlu' diye konumlar. Belediye ihalesi/karşılaştırması yapılırsa rakip söylemi bu; KVKK uyumu Türkiye'de satış/kabul kriteri.
kaynak: https://www.traffix.com.tr/blogdetay/2026-nin-en-iyi-5-plaka-tanima-sistemi-ozellik-ve-fiyat-karsilastirmasi.html

## Türkiye PTS pazarı genel (Nemesis, Elra, Cosmopark vb.) (tr-yerli) [ticari-otopark]
Standart TR akışı: kamera okur → merkez veritabanında yetkili/kara/misafir listesiyle karşılaştırılır → yetkiliye bariyer açılır, misafire giriş saati yazılır ve çıkışta ücret hesaplanır, kara listede sesli/görsel alarm. Plaka verisi KVKK'da kişisel veri; şifreli saklama beklenir. Bizim MVP'nin işlevsel tanımıyla birebir örtüşür.
kaynak: https://nemesisalarmsis.com/plaka-tanima-sistemleri/

## Wiegand / OSDP v2 / röle kuru kontak (standart) [ticari-otopark]
Bariyer/geçiş kontrol entegrasyonunun fiili standartları: Wiegand 26 bit (plaka→SHA-1 ID dönüşümü yaygın), modern kurulumlarda şifreli OSDP v2 (RS485 üstünde, secure channel), en basiti yapılandırılabilir darbeli kuru kontak röle. PC tabanlı çözümümüz için en ucuz yol USB/IP röle kartı + webhook; Wiegand ancak mevcut panel varsa gerekir.
kaynak: https://resources.nedapidentification.com/web/ANPR/Documentation/Datasheets/Datasheet_ANPR_LUMO_EN.pdf

## Uludağ Üniversitesi Ücretli Otopark PTS İhale Şartnamesi (2016) (standart) [tr-pazari]
En değerli bulgu: gerçek kamu şartnamesi. %95 doğruluk eşiği, tetiksiz araç tespiti + loop entegrasyonu, 25 fps/720p gerçek zamanlı işleme, tek PC'de 9 kamera, kare/dikdörtgen plaka, operatör düzeltmesi, 2 karakterle sorgu, kara liste alarmı+mail, acil durum butonu, yangında bariyer açma, antipassback, vardiya sonu PDF rapor, DLL API, %100 yerli yazılım, dongle lisans. Bizim kabul kriterlerimizin şablonu olarak birebir kullanılabilir.
kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf

## ISSD POINTR (P3000 / Moonlight / MALL / B3000) (tr-yerli) [tr-pazari]
7/24 %95 doğruluk, IP66/IK10/NEMA4x muhafaza, dahili IR + harici IR aydınlatıcı varyantı (Moonlight, gece için), bağlantı kesintisinde bellek yedekleme + FTP aktarım, EGM PolNet merkez yazılımıyla tam uyumlu (31 il emniyet müdürlüğü referansı). Ders: offline tampon ve harici IR, sahada ciddiye alınan özellikler.
kaynak: https://www.issd.com.tr/en/number-plate-recognition-system/

## Perkotek PTS/ANPR (tr-yerli) [tr-pazari]
20 ms tanıma süresi iddiası; çift satır + tek satır, yabancı/askeri/siyah/mavi plaka desteği; min. 2MP IP kamera + WDR şartı; STRIKE MX4 IP tetik paneli ile bariyer tetikleme; site (ücretsiz) ve ücretli otopark sürümleri; antipassback, SMS, doluluk izleme. Ders: plaka türü kapsamı ve IP tabanlı röle tetikleme pazar standardı.
kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr

## Traffix AI-Vision (tr-yerli) [tr-pazari]
%99,8 doğruluk + araç marka-model-renk analizi iddiası, KVKK uyumluluğunu pazarlama argümanı yapıyor, bulut yönetim paneli. Karşılaştırma yazısında giriş seviyesi standart kameraların %85'te kaldığını, global markaların (Axis/Bosch) %99,5 verdiğini söylüyor. Ders: KVKK uyumu TR pazarında satış özelliği.
kaynak: https://www.traffix.com.tr/blogdetay/2026-nin-en-iyi-5-plaka-tanima-sistemi-ozellik-ve-fiyat-karsilastirmasi.html

## Arma Kontrol Plaka Okumalı Otopark Sistemi (tr-yerli) [tr-pazari]
Giriş/çıkışta plaka okuma + süre hesabı + ücretin operatöre gösterimi; tüm geçişlerin yıl/ay/gün bazlı veritabanında fotoğraflı arşivi ve dönemsel sorgu. Ders: TR otopark akışının kanonik modeli (giriş kaydı → çıkışta eşleştirme → ücret).
kaynak: https://www.armakontrol.com/arma-kontrol-plaka-okumali-otopark-sistemi

## Hikvision ANPR ekosistemi (DS-TCG205-B, iDS-TCM403-A) (ticari) [tr-pazari]
İkame edeceğimiz referans: kamera üzerinde ANPR + röle çıkışıyla doğrudan bariyer kontrolü, Wiegand ile erişim kontrol entegrasyonu, NVR/ISAPI olay akışı, 4 röleli panelle 4 bariyer kumandası. Ders: bizim yazılım aynı olay+röle sözleşmesini sağlamalı ki bariyer tarafı değişmeden kalsın.
kaynak: https://hikwiki.com/hikvision-plaka-okuma-kamerasi-anpr-kurulumu/

## İSPARK / İzmir Büyükşehir belediye otopark pratiği (tr-yerli) [tr-pazari]
İSPARK: abone sorgusu milisaniyeler mertebesinde, bariyer otomatik; hukuki dayanak olarak açık rıza değil sözleşme (m.5/2-c) + meşru menfaat (m.5/2-f) kullanıyor. İzmir: 28 kapalı otoparkta tam otomatik PTS ('Akıllı Şehir, Akıllı Otopark'). Ders: belediye emsalleri hem UX hem KVKK kurgusu için hazır şablon.
kaynak: https://ispark.istanbul/mobil-uygulama-kisisel-verilerin-islenmesine-iliskin-aydinlatma-metni/

## KVKK Kurulu 2023/924 sayılı otopark kararı (standart) [tr-pazari]
Otopark işletmecisine 75.000 TL ceza: plaka işlemesi m.5/2(e) ile hukuka uygun bulundu ama aydınlatma yükümlülüğü ihlal edildi; sadece plakayla sorgu yapılabilen tek faktörlü sistem güvenlik açığı sayıldı, çift faktör istendi. Ders: aydınlatma tabelası + sorgu ekranında ikinci faktör bizim için uyum maddesi.
kaynak: https://www.kvkk.gov.tr/Icerik/7763/2023-924

## Alışveriş Merkezleri Hakkında Yönetmelik (PTS zorunluluğu, RG 30.12.2016) (standart) [tr-pazari]
AVM otopark giriş-çıkışlarında plaka tanıma kurulumu zorunlu, veriler kollukla ANLIK paylaşılır, teknik esasları İçişleri Bakanlığı belirler; gerekirse araç altı görüntüleme de istenebilir. Ders: TR'de PTS'nin mevzuatla zorunlu kılındığı tek net alan; belediye otoparkında kolluk paylaşım arayüzü ileride talep edilebilir.
kaynak: https://www.aa.com.tr/tr/turkiye/avmlere-plaka-tanima-sistemi/716865

## Karayolları Trafik Yönetmeliği plaka hükümleri + TR plaka formatı (standart) [tr-pazari]
Plaka boyutları (11x52, 21x32, 15x24 cm), alüminyum DIN 1745, 1-3 mm kabartma, 5 mm bordür, sol 4x10 cm mavi TR şeridi; format desenleri 99 X 9999...99 XXX 999; Ç,Ğ,İ,Ö,Ş,Ü kullanılmaz; resmi/polis/diplomatik/geçici renk varyantları. Not: 'TS 8438' standardı web'de doğrulanamadı, bağlayıcı kaynak yönetmeliğin kendisi.
kaynak: https://tr.wikipedia.org/wiki/T%C3%BCrkiye_ta%C5%9F%C4%B1t_plaka_kodu

## Buhur PTS Teknik Şartnamesi (standart) [tr-pazari]
Satıcı şartname şablonu: %96 okuma başarısı, 4MP bullet kamera, 2.8-12mm motorize lens, 30 m gece görüş, H.264, PoE/12VDC, i7+8GB tanıma PC'si, cihaz başına 2 bariyer tetikleme. Ders: piyasadaki 'şartnameye uygunluk' dilinin ve donanım tabanının özeti.
kaynak: https://www.buhur.com.tr/plaka-tanima-sistemleri-teknik-sartnamesi/

## HGS/OGS serbest geçiş plaka tanıma pratiği (standart) [tr-pazari]
RFID etiket birincil, plaka tanıma yedek: etiket okunamazsa plaka fotoğrafı çekilip geçiş plakaya yazılır, ödenmezse yasal takibe kanıt olur. Ders: çift kanal (birincil sensör + plaka yedeği) ve her geçişte kanıt fotoğrafı saklama deseni otopark için de geçerli.
kaynak: https://www.anadoluotoyolisletmesi.com/sss

## NASPLE — National ANPR Standards for Policing and Law Enforcement (UK Home Office, v3.4 2026) (standart) [standartlar]
Dünyadaki en somut resmi ANPR eşikleri: sabit sistem %98 capture / %95 read, CCTV-entegre %85/%95, hareketli araçtan %80/%95; 'insan gözüyle görülebilirlik' tanımı, yıllık 250-okumalı doğrulama, saklama süreleri (yerel 90 gün / merkez 12 ay) ve %95 oranında 4-6 s okuma→alarm süresi. Kabul kriterlerimizin ana şablonu.
kaynak: https://assets.publishing.service.gov.uk/media/69c25599d588c92c483e4c1f/NASPLE.v3.4.pdf

## Home Office 'Guidance on ANPR Performance Assessment and Optimisation' v2.0 (2020) (standart) [standartlar]
Test metodolojisinin tam tarifi: ground-truth video prosedürü (250 ardışık araç, insan gözüyle karşılaştırma), capture/read formülleri, tek karakter hatası = misread kuralı, çoklu okuma tekilleştirme, bilinen-plaka spot testi (20 geçişte 2+ kaçırma → düzeltici işlem), günlük sayı baseline izleme ve hata nedeni taksonomisi (Appendix A). Bizim kabul testi dokümanı buradan uyarlanmalı.
kaynak: https://assets.publishing.service.gov.uk/government/uploads/system/uploads/attachment_data/file/909024/ANPR_-_Evaluation_Approved_Version_2.0.pdf

## IEC 62676-4 (DORI) ve 2025 revizyonu (OODPCVS) (standart) [standartlar]
Plaka okuma için piksel yoğunluğu eşiği: 2014 sürümünde 250 px/m ('Identification'), 2025 sürümünde düşük ışık/hareket bulanıklığı gerekçesiyle 500 px/m ('Validate'). Kamera FOV/çözünürlük yeterliliğini sahada ölçmek için nesnel kriter.
kaynak: https://www.hikvision.com/en/newsroom/blog/understanding-oodpcvs--the-new-image-detail-classification-frame1/

## IEC 62676-5:2018 ve 62676-5-1:2024 (kamera görüntü kalitesi + çevresel testler) (standart) [standartlar]
Kamera görüntü kalitesi parametrelerinin (minimum aydınlanma, dinamik aralık, IR menzili, distorsiyon) standart ölçüm yöntemleri; 2024 parçası çevresel (soğuk/kar dahil) test yöntemlerini ekler. EN 50132'nin yerini alan seri.
kaynak: https://webstore.iec.ch/en/publication/62442

## UNE 199141-1:2013 (İspanya — plaka okuyucular fonksiyonel şartnamesi) (standart) [standartlar]
Trafik yönetimi ekipmanında plaka okuyucular için Avrupa'daki tek ulusal fonksiyonel şartname standardı örneği; metni ücretli, ancak şartname başlıkları RFP/kabul dokümanı iskeleti olarak kullanılabilir.
kaynak: https://en-standard.eu/une-199141-1-2013-equipamiento-para-la-gestion-del-trafico-vision-artificial-lectores-de-matriculas-parte-1-especificaciones-funcionales

## ISO/TS 5206-1 / APDS (Alliance for Parking Data Standards) (standart) [standartlar]
ISO'nun 2022'de kabul ettiği küresel otopark veri modeli (yer/oturum/hak/olay kavramları); bariyer-otopark yazılımı entegrasyon alanlarımızı buna uyumlu adlandırmak gelecekteki entegrasyonları ucuzlatır.
kaynak: https://allianceforparkingdatastandards.org/

## BPA/IPC Private Parking Sector Single Code of Practice (UK) (standart) [standartlar]
Özel otoparkta ANPR kullanım kuralları: ekipman 'amaca uygun ve bakımlı' olmalı, saatler senkron olmalı, ANPR kullanımı tabelayla duyurulmalı. KVKK aydınlatma yaklaşımımızla birebir örtüşen sektör pratiği.
kaynak: https://www.britishparking.co.uk/write/Documents/AOS/NEW%20Redesigned%20Documents/sectorsingleCodeofPractice.pdf

## UFPR-ALPR benchmark (Laroca vd. 2018) (acik-kaynak) [standartlar]
4.500 kare / 150 araç×30 kare, 1080p; metrik = tüm karakterler doğru (end-to-end). En kritik ders: karakter-pozisyonu çoğunluk oylaması tek kareyi %64,89→%78,33'e, SSIG'de %85,45→%93,53'e çıkarıyor — V2+voting mimarimizin literatür kanıtı. Ticari sistemler (OpenALPR/Sighthound) aynı sette %70 altında kaldı.
kaynak: https://arxiv.org/pdf/1802.09567

## CCPD (Chinese City Parking Dataset) (acik-kaynak) [standartlar]
280k+ görüntülük otopark plaka veri seti; değerlendirme kuralı: tespit TP için IoU>0.7, tanıma doğru sayılması için IoU>0.6 VE tüm karakterler doğru. Kendi iç benchmark kurallarımız için referans protokol.
kaynak: https://arxiv.org/pdf/2011.14936

## OpenALPR (açık kaynak + ticari benchmark sayfası) (acik-kaynak) [standartlar]
CPU-only FPS referansları (i7-7700K: 1080p ~52 FPS; Xeon E5-2666v3: ~12,7 FPS) zayıf ofis PC'miz için gerçekçi performans zarfı verir; ticari sürüm metrikleri (ilk tahminde %99,02, top-10'da %100) 'aday listesi' raporlamanın örneği.
kaynak: https://cdn.openalpr.com/benchmarks.html

## Plate Recognizer (ticari ALPR) (ticari) [standartlar]
Şeffaf sınır-testi metodolojisi örneği: açı protraktörle ölçülür (70°'ye kadar doğru, 75°'de hata), rotasyon 53°'ye kadar, plaka piksel genişliği kritik. Ürünümüz için 'çalışma zarfı' dokümantasyonunun nasıl yazılacağının şablonu.
kaynak: https://platerecognizer.com/alpr-results/

## Survision (ticari LPR — kar/kış saha deneyimi) (ticari) [standartlar]
Erzurum için en alakalı saha verisi: İzlanda kışında çift kamerayla %99 genel başarı ama %15,9 tek-plaka kaçırma; kar riskleri (plakada birikme, parlama, lens buzlanması) ve önlemler (IR, denoising, ısıtmalı lens, 120-150 px plaka genişliği, trajectory verification).
kaynak: https://survisiongroup.com/post-license-plate-recognition-in-snow:-tackling-winter-challenges

## Hikvision ANPR kameraları (DS-TCG405-E, iDS-2CD7A46G0/P) (ticari) [standartlar]
İkame ettiğimiz ürün: beyan tanıma ≥%98,5 / yakalama ≥%99 (@≤60 km/h, önerilen kurulum); karakter genişliği 100-200 px @2MP, karakter yüksekliği 20-30 px kurulum şartı. A/B kabul testinin kıyas tarafı.
kaynak: https://www.hikvision.com/content/dam/hikvision/products/S000000001/S000000177/S000000180/S000000181/OFR000261/M000052450/Data_Sheet/DS-TCG405-E_Datasheet_2021.pdf

## GMWEB ANPR Parking / AVUTEC (otopark entegratörleri) (ticari) [standartlar]
Otopark bariyeri gecikme bütçesi referansı: uçtan uca <500 ms (LPR <100 ms + DB <50 ms + röle <150 ms); AVUTEC metrik yazısı precision/recall/F1 ve N vs N-1 tuzaklarını tanımlar — gecikme ve doğruluk KPI'larımızın sektör karşılığı.
kaynak: https://gmweb.pl/en/anpr-parking-system

## KVKK Kurul Kararı 2023/924 (otopark plaka verisi) (tr-yerli) [standartlar]
Türkiye'de otopark ANPR'ının hukuki çerçevesi: plaka kişisel veridir, aydınlatma yükümlülüğü ihlaline 75.000 TL ceza kesildi, salt plakayla tek adımlı sorguya ikinci doğrulama faktörü şartı getirildi. Belediye otoparkı işletmecisi olarak doğrudan uygulanacak emsal.
kaynak: https://www.kvkk.gov.tr/Icerik/7763/2023-924

## fast-alpr + fast-plate-ocr (ankandrew) (acik-kaynak) [kosullar]
ONNX Runtime ile CPU'da gerçek-zamanlı çalışmak için tasarlanmış hafif tespit+OCR modelleri; bizim C#/.NET + ONNX Runtime CPU yığınımıza doğrudan uyarlanabilir model mimarisi ve eğitim tarifleri sunuyor.
kaynak: https://github.com/ankandrew/fast-alpr

## MF-LPR² (çok-kare plaka restorasyonu) (acik-kaynak) [kosullar]
Optik akışla hizalanmış ardışık düşük-kalite kareleri birleştirerek karakter doğruluğunu 74.70%→86.44% (+11.74 puan) yükseltiyor; bizim voting mimarimizin bir üst seviyesi olarak 'okunamadı' kuyruğunda ikinci-şans modülü fikri veriyor.
kaynak: https://www.sciencedirect.com/science/article/abs/pii/S1077314225000840

## Laroca et al. YOLO tabanlı ALPR (UFPR-ALPR) (acik-kaynak) [kosullar]
Karakter-pozisyon bazlı çoğunluk oylamasının (temporal redundancy) videoda doğruluğu ciddi artırdığını kanıtlayan referans akademik sistem (SSIG %93.53, ticari OpenALPR %93.03'ü geçiyor); veri augmentasyonu (ters plaka, çevrilmiş karakter) hileleri bizim eğitim hattımıza uygulanabilir.
kaynak: https://arxiv.org/abs/1802.09567

## OpenALPR (acik-kaynak) [kosullar]
Klasik açık kaynak referans hattı (tespit+segmentasyon+OCR); akademik kıyaslamalarda modern derin öğrenme sistemlerinin gerisinde kalıyor — bize 'klasik hat yetmez, uçtan uca CNN gerekli' dersini veriyor.
kaynak: https://github.com/openalpr/openalpr

## HyperLPR (acik-kaynak) [kosullar]
JIST bozulma çalışmasında parlaklık ve sis distorsiyonlarına karşı en tutarlı dayanıklılığı gösteren açık kaynak motor; dayanıklılığın motor mimarisine/eğitim verisine bağlı olduğunun kanıtı.
kaynak: https://library.imaging.org/jist/articles/68/6/060401

## Plate Recognizer (ticari SDK) (ticari) [kosullar]
En somut kurulum sayılarını yayınlayan ticari motor: plaka ≥100 px (min 80), açı ≤45°, park senaryosu enstantane 1/300-1/600, <30 mph için 4-5 eFPS, kar kapaması için dürüst sınır ('insan okuyamıyorsa biz de okuyamayız'); kabul kriterlerimizi kalibre etmek için birebir referans.
kaynak: https://platerecognizer.com/camera-setup-for-best-anpr/

## Survision (kış/kar LPR) (ticari) [kosullar]
Kış koşullarına özel yazılım katmanı: denoising, kontrast iyileştirme, çok-kare trajectory doğrulaması; İzlanda saha testi %99 genel performans ama %4.5 don/hasar kaynaklı okunamama — Erzurum kışı için gerçekçi başarı tavanı ve manuel fallback gerekçesi.
kaynak: https://survisiongroup.com/post-license-plate-recognition-in-snow:-tackling-winter-challenges

## Adaptive Recognition Einar (erişim kontrol ANPR kamerası) (ticari) [kosullar]
Bariyer senaryosunun ticari mihenk taşı: ideal koşulda %99.9 iddiası, ≤80 km/h, triggerless/yazılım/donanım tetikleme, HTTP/FTP olay+snapshot çıktısı, 'frame parity flashing' (farklı pozlamalı karelerden en iyisini seçme) — bizim kare-kalite-skoru yaklaşımımızın donanımlı karşılığı.
kaynak: https://adaptiverecognition.com/products/einar-anpr-camera/

## Hikvision ANPR (DarkFighter serisi) — ikame edilen sistem (ticari) [kosullar]
İkame ettiğimiz yerleşik ANPR'ın dayandığı donanım hileleri: 140 dB WDR, DarkFighter düşük-ışık sensörü, akıllı IR şiddet kontrolü, plaka-bölgesi pozlama. Bunların hiçbirine yazılımdan müdahale edemeyeceğimiz için kurulumda kameranın web arayüzünden tek seferlik ayar (IR %40-60, min shutter, gain limiti) ön koşulumuzdur.
kaynak: https://www.hikvision.com/us-en/products/network-products/network-cameras/performance-series/ds-2cd3646g2t-izs-y-/

## UK NASPLE (Ulusal ANPR Standartları) (standart) [kosullar]
Ölçülebilir kabul kriteri kaynağı: statik ANPR %98 yakalama / %95 okuma; CCTV-entegre sistemde %85/%95; ayrıca veri saklama ve denetim standartları KVKK gereksinimlerimize şablon oluyor.
kaynak: https://assets.publishing.service.gov.uk/media/69c25599d588c92c483e4c1f/NASPLE.v3.4.pdf

## Axis License Plate Capture white paper (standart) [kosullar]
En sağlam mühendislik tablosu: EU plakası min 74 px (pratik 100-150), enstantane-hız-açı matrisi (30 km/h @10° → ≤9.7 ms), toplam açı <30°, LPR'de WDR'ı KAPAT (hareket artefaktı), max gain 9-21 dB, 1/500 shutter'da IR menzili %50 düşer, IR-pass filtre LED farı bloklar — kurulum kılavuzumuzun omurgası.
kaynak: https://whitepapers.axis.com/en-us/license-plate-capture

## placa.ai (gece LPR analizi) (ticari) [kosullar]
Gece başarısızlıklarının %80+'ının IR aşırı pozlamadan kaynaklandığını ve YAZILIMLA DÜZELTİLEMEYECEĞİNİ net söyleyen kaynak; somut reçete: IR manuel %40-60 (3-6 m), min shutter 1/500-1/2000 hıza göre, kamera 10-15° yatay ofset. Dürüst sınır analizimizin temel dayanağı.
kaynak: https://www.placa.ai/ip-camera-missing-license-plates-at-night/

## Türk plaka tanıma çalışmaları (Kılıç & Aydın IEEE + Bigailab-trvlp-20k) (tr-yerli) [kosullar]
Fırat Üniversitesi güvenlik kameralarından gerçek TR plaka veri seti; TR'ye özgü zorluklar (çoklu plaka tipi, değişken boy, standart-dışı font) belgelenmiş; Bigailab-trvlp-20k 20 bin görüntülük TR plaka tespit benchmark'ı — yerel test seti oluşturmadan önce incelenmeli.
kaynak: https://ieeexplore.ieee.org/document/8620744/

## Hava-durumu-adaptif CNN (MDPI Sensors 2024) (acik-kaynak) [kosullar]
Kar/sis/yağmur augmentasyonlu eğitimin (28.7k→125.9k görüntü) F1'i 91.69%→94.36%'ya çıkardığını gösteriyor; mimari değiştirmeden salt eğitim-verisi çeşitlendirmesiyle kötü-hava dayanıklılığı kazanılabileceğinin kanıtı — CPU bütçemizde model büyütmeden iyileşme yolu.
kaynak: https://pmc.ncbi.nlm.nih.gov/articles/PMC11644901/

## Plaka süper-çözünürlük benchmark'ları (LCDNet+MVCP, D_GAN_ESR) (acik-kaynak) [kosullar]
Düşük çözünürlükte SR+5-kare füzyonu tanımayı %1.7→%44.7'ye, deblur+SR OCR'ı %12→%62'ye çıkarıyor; ama gerçek-zamanlı CPU'da pahalı — sadece asenkron 'ikinci şans' kuyruğu için aday teknoloji.
kaynak: https://arxiv.org/html/2505.06393v1

## Plate Recognizer Stream + ParkPow (ticari) [entegrasyon-operasyon]
Webhook JSON şemasının fiili standardı: plate, score, dscore, candidates, box, camera_id, timestamp/timestamp_local, plaka+araç kırpık görüntüleri; 'heartbeat' ayrı olay tipi (sağlık izleme dersi); CSV/JSONL yerel log yedeği; ParkPow tarafında rol tabanlı operatör ekranı (Viewer/Operator/Executive), etiketleme (Employee/Visitor) ve CSV import/export. Bizim webhook tasarımımız için doğrudan şablon.
kaynak: https://guides.platerecognizer.com/docs/stream/results/

## Rekor Scout (OpenALPR) (ticari) [entegrasyon-operasyon]
alpr_group deseni: aynı aracın çok karesini tek olayda gruplar (best_plate_number, best_confidence 0-100, candidates, epoch_start/end, uuids) — ANCAK grup sonucu 10 sn'ye kadar gecikebilir, dokümanda 'gerçek zamanlı uygulamalara değil gözetime uygun' denir. Ders: bariyer için anlık tek-okuma olayı ayrı yayınlanmalı; çok-kare oylaması bizim V2+voting yaklaşımımızı doğruluyor.
kaynak: https://docs.rekor.ai/scout/application-integration/json-group-results

## Hikvision ISAPI ANPR (ikame edilen arayüz) (ticari) [entegrasyon-operasyon]
İki olay alım modeli: GET /ISAPI/Event/notification/alertStream (multipart/mixed pull) ve HttpHostNotification (push; resim tipi all/licensePlatePicture/detectionPicture). EventNotificationAlert XML alanları: licensePlate, confidenceLevel, direction, plateType, plateColor. Beyaz/kara liste: GET/PUT /ISAPI/Traffic/channels/<ID>/licensePlateAuditData. Bariyer ya platformdan ya kameradan tetiklenir. Bu şemayı taklit edersek mevcut otopark yazılımları bizi 'Hikvision gibi' görür.
kaynak: https://tpp.hikvision.com/wiki/isapi/anpr/GUID-328FC5AD-38EA-4409-9E75-996279BF2A46.html

## ZatPark (Unity5) (ticari) [entegrasyon-operasyon]
Fuzzy matching dersi: plakanın near-miss varyantlarını otomatik üretip saklar, 1-2 karakter farklı okumayı eşleştirir; operatöre araç marka/model/renk bilgisiyle aday listesi sunarak doğru plakayı seçtirir. Giriş-çıkış eşleştirme ve operatör düzeltme ekranımızın modeli.
kaynak: https://unity5.zatpark.com/anprfindoutmore

## WPS Paperless Parking (ticari) [entegrasyon-operasyon]
Giriş-çıkış eşleştirmede fuzzy matching ile pozitif eşleşme oranının ~%100'e çıkarılabildiğini belirtir; biletsiz (plaka=bilet) otopark akışının referansı.
kaynak: https://www.wpsparking.com/en/parking-concepts/paperless-parking

## Parklio ANPR (ticari) [entegrasyon-operasyon]
Hem online hem offline çalışma ürün özelliği; bariyersiz free-flow modu; rezervasyon/booking senkronu. Çevrimdışı dayanıklılık gereksinimimizin sektör karşılığı.
kaynak: https://parklio.com/en/parking-solutions/anpr

## Perkotek PTS (tr-yerli) [entegrasyon-operasyon]
Türk pazarı beklenti seti: 20 ms tanıma iddiası, çift satırlı/tek satırlı + siyah/mavi/askeri Türk plakaları desteği, 3 tarife sınıfı (otomobil/otobüs-kamyon/diğer), abonelik (yıllık/aylık/haftalık) + borç takibi + SMS uyarı, üye başına araç kotası, saat bazlı yetki, antipassback, manuel kapı aç/kapa komutu, gün/hafta/ay gelir raporları.
kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr

## Ertel Bilişim otopark LPR rehberi (tr-yerli) [entegrasyon-operasyon]
Türkçe somut sayılar: kamera plakaya <15 derece açı, bariyerden 3-5 m, çıkışta min. 60 fps, 0 lux IR'de %95+ doğruluk; görüntü saklama 30 gün (60'a uzatılabilir); AES + HTTPS; KVKK tabelası zorunlu, ses kaydı yasak; rapor seti (günlük/abone/yoğunluk/gelir/alarm) Excel-PDF + otomatik e-posta; okunamayan plakada manuel müdahale.
kaynak: https://www.ertelbilisim.com.tr/otopark-kamera-lpr-raporlama/

## İSPARK (İstanbul BB otopark işletmesi) (tr-yerli) [entegrasyon-operasyon]
Belediye otopark işletmesi emsali: abone plakaya otomatik bariyer açma, doluluk tespiti ve saatlik/günlük/aylık/yıllık raporlama; abonelik ödemesi her ayın ilk 4 günü peşin; yalnız otomobil+motosiklet aboneliği. Erzurum Belediyesi için birebir iş modeli referansı.
kaynak: https://ispark.istanbul/sikca-sorulan-sorular/

## BPA Single Code of Practice (İngiltere) (standart) [entegrasyon-operasyon]
Ölçülebilir operasyon kuralı: ödemeli parkta çıkışta minimum 10 dakika grace period (ceza kesilemez) + park yeri sayısına göre değişen consideration period; ANPR kayıtlarının adil uygulama sürecini desteklemesi şartı. Ücretsiz süre/toleransın parametrik olması gerektiğinin kaynağı.
kaynak: https://www.britishparking.co.uk/write/Documents/AOS/NEW%20Redesigned%20Documents/sectorsingleCodeofPractice.pdf

## APDS - Alliance for Parking Data Standards (standart) [entegrasyon-operasyon]
Otopark verisi için ISO'ca kabul edilmiş uluslararası veri modeli: Place, Right, Rate, Quote, Occupancy, Session, Observation alanları. Olay/oturum tablolarımızı bu kavramlarla eşlenebilir tasarlamak gelecekteki entegrasyonları kolaylaştırır.
kaynak: https://allianceforparkingdatastandards.org/

## KVKK Kurul Kararı 2023/924 (standart) [entegrasyon-operasyon]
Otopark plaka tanıma emsal kararı: aydınlatma yükümlülüğü ihlali + sadece plaka girerek borç sorgulanabilmesi (tek kademeli doğrulama, yetersiz veri güvenliği) nedeniyle 75.000 TL idari para cezası. Belediye işletmesi için doğrudan uygulanabilir uyum gereksinimi.
kaynak: https://www.kvkk.gov.tr/Icerik/7763/2023-924

## Microsoft Windows Service Guidelines (standart) [entegrasyon-operasyon]
Kritik servisler için SCM recovery önerisi: 1. hata → 1 dk sonra restart, 2. hata → 2 dk sonra restart, hata sayacı 300 sn'de sıfırlanır. ANPR servisimizin Windows servis kurulumunda uygulanacak somut değerler.
kaynak: https://learn.microsoft.com/en-us/windows/win32/rstmgr/guidelines-for-services

## Magnetic AutoControl / bariyer donanımları (ticari) [entegrasyon-operasyon]
Bariyer kolu fiziksel açılış süreleri: hızlı modellerde 1.3 sn, tipik aralık 1.5-6 sn, hız konfigüre edilebilir. Toplam gecikme bütçesinde yazılımın payını görece rahatlatan veri: tanıma+karar <1 sn yeterli, darboğaz mekaniktir.
kaynak: https://magneticgateopeners.com/store/resources/resources-hub/parking-pro-barrier-arm-opener.html

## Plate Recognizer Stream (ticari) [zayif-cpu]
GPU'suz CPU-ANPR'in referans ticari urunu: Passmark tabanli boyutlandirma (tek kamera ~2.100-3.300), kamera basina 480-530 MB RAM, eFPS/sample kavrami (<30 mph icin 4-5 eFPS), AVX sarti, H.264 onerisi; RPi4/Jetson Nano gibi cok zayif cihazlarda bile tek kamera calistirmasi bizim 'zayif ofis PC yeter' tezini kanitliyor.
kaynak: https://guides.platerecognizer.com/docs/stream/onpremise/system-requirements

## Rekor Scout (OpenALPR) (ticari) [zayif-cpu]
Thread=cekirdek modeli (500 MB/thread, lineer fps olcegi), islemci-fps tablolari (i7-6700: 17,3 fps@720p / 11,7 fps@1080p), arac hizina gore fps onerileri (dusuk hiz 5-10 fps) ve Windows'ta %16-22 fps cezasi — kapasite planlamamizin sayisal iskeleti.
kaynak: https://docs.rekor.ai/scout/agent/benchmarking

## ultimateALPR SDK (Doubango) (ticari) [zayif-cpu]
CPU'da en hizli ANPR iddiasi (Xeon'da 237 fps, RPi4'te 7-12 fps); 'pozitif orani' benchmark metodolojisi plakasiz karede erken cikisin ~1,7x fps kazandirdigini gosteriyor — olay-tetikli/erken-cikis boru hatti tasarimimizi dogrulayan en somut veri.
kaynak: https://www.doubango.org/SDKs/anpr/docs/Benchmark.html

## Frigate NVR (acik-kaynak) [zayif-cpu]
Zayif donanimda kademeli isleme pratiginin referansi: hareket algilama ilk filtre -> hareket bolgeleri gruplanir -> obje tespiti yalniz o kare bolgelerinde calisir; detect akisinin dusuk cozunurluklu substream'den alinmasi pratigi de buradan.
kaynak: https://docs.frigate.video/configuration/motion_detection/

## Eocortex LPR modulu (ticari) [zayif-cpu]
Somut esikler: i3-10100+ AVX sarti, min 1280x720 kaynak, karakter yuksekligi >=15 px, plaka min 120x20 px, 2,5/5/10 fps'lik uc isleme modu (hiz kademesi yaklasimi kopyalanabilir), 30 km/h icin 1/500 s enstantane.
kaynak: https://doc.eocortex.com/analytics/lpr/requirements.htm

## Milestone XProtect LPR (ticari) [zayif-cpu]
Kurumsal VMS eklentisi olarak serit basina 4 fps isleme onerisi ve Intel 4. nesil+ CPU sarti — dusuk eFPS'in kurumsal urunlerde de norm oldugunun kaniti.
kaynak: https://doc.milestonesys.com/latest/en-US/add-ons/add-on_lpr/lpr_xprlprsysreq.htm

## Hikvision ANPR kameralar (DS-TCG405-E, iDS-TCM403) (ticari) [zayif-cpu]
Ikame hedefimizin baz cizgisi: >=%98,5 tanima / >=%99 yakalama; kendi kurulum sartnamesi plaka genisligi 130-300 px (optimum 150-200 px) ister — ayni kosullari kendi kabul testimize aynen tasimaliyiz.
kaynak: https://www.hikvision.com/en/products/parking-management/vehicle-access-control-management/anpr-cameras/ds-tcg405-e/

## Adaptive Recognition (Vidar) (ticari) [zayif-cpu]
'Megapiksel miti' analizi: karakter cizgi kalinligi >=2 px kurali, serit basina min 700 px (ideal 1440 px); yuksek cozunurlugun OCR gecikmesi ve kaynak israfi yarattigi uyarisi — zayif CPU'da 720p substream kararimizi destekler.
kaynak: https://adaptiverecognition.com/blog/traffic-transportation/busting-anpr-megapixel-myth/

## OpenVINO (acik-kaynak) [zayif-cpu]
Intel CPU'da en hizli inference runtime'i (YOLOv8n'de ONNX Runtime'a gore %20-40 hizli) ama kritik risk: 2026.0 surumunden itibaren AVX2 zorunlu, SSE destegi kaldiriliyor — eski belediye PC'sinde surum kilitleme/dogrulama gerekir.
kaynak: https://docs.openvino.ai/systemrequirements

## ONNX Runtime INT8 kuantizasyon dokumani (standart) [zayif-cpu]
INT8 karar rehberimiz: VNNI'de 6x'e kadar kazanc; yalniz-AVX2'de U8S8 saturasyon dogruluk riski (reduce_range/U8U8 cozumu); 'eski donanimda int8 icin gerekli komutlar yok denecek kadar az' resmi uyarisi — AVX'siz/eski CPU'da INT8 korlemesine kullanilmamali.
kaynak: https://onnxruntime.ai/docs/performance/model-optimizations/quantization.html

## Ultralytics YOLO export benchmark'lari (acik-kaynak) [zayif-cpu]
CPU'da format karsilastirmasinin sayisal kaynagi: YOLOv8n i7-13700H'de PyTorch 104,6 ms -> ONNX 28,0 ms -> OpenVINO 23,5 ms; dogruluk (mAP) tum formatlarda sabit — export optimizasyonunun bedava hiz kazanci oldugunu gosterir.
kaynak: https://docs.ultralytics.com/integrations/openvino

## Perkotek / Santralmarket PTS (TR entegrator pratigi) (tr-yerli) [zayif-cpu]
Turkiye sahasindaki fiili donanim merdiveni: 1 kamera=i3, 2-4 kamera=i5, 4-8 kamera=i7, >=8 GB RAM; IR bullet kamera ile 10-12 m okuma mesafesi, %95+ dogruluk iddiasi — yerel musteri beklentisi ve donanim normu.
kaynak: https://santralmarket.net.tr/plaka-tanima-sistemi-nedir/

## AVUTEC Gatekeeper (ticari) [zayif-cpu]
Tam-edge olay bazli is akisi ornegi: 20-40 fps cift sensor, NPU'da plaka bulma+OCR+ulke cikarimi ayni zaman diliminde; standalone modda liste esleme ve role tetigi tek haneli ms — bariyer gecikme hedefimizin (<500 ms) ne kadar asagi cekilebildigini gosterir.
kaynak: https://www.parking.net/parking-news/avutec/anpr-in-milliseconds-efficient-workflow-behind-our-technology

## EN 62676-4 (CCTV uygulama standardi) (standart) [zayif-cpu]
Plaka 'identify' gorevi icin 250 px/m standart esigi — kamera yerlesim kabul testinde vendor-bagimsiz olculebilir referans.
kaynak: https://cctvplanner.io/blog/license-plate-camera-distance-and-focal-length-guide

## Hikvision ISAPI (EventNotificationAlert / httpHosts push) (standart) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
Emüle edilebilir tek pratik yüzey: kamera, PUT /ISAPI/Event/notification/httpHosts/1 ile tanımlı adrese multipart/form-data POST atar (anpr.xml + licensePlatePicture.jpg + detectionPicture.jpg). Tam alan listesi ve iki farklı namespace (isapi.org / std-cgi.com) resmi kılavuzlardan çıkarıldı. Push auth opsiyonel (MD5digest/none).
kaynak: https://www.hikvisioneurope.com/eu/portal/portal/Technology%20Partner%20Program/02-Solutioins%20of%20Hikvision%20product%20integration/Fast%20guide%20for%20ANPR%20of%20TCG%20camera%20via%20ISAPI.pdf

## Hikvision HCNetSDK (Device Network SDK) (ticari) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
Arming modu: platform kameranın TCP 8000 portuna özel ikili protokolle bağlanır (NET_DVR_Login_V40 → SetDVRMessageCallBack_V50 → SetupAlarmChan_V50; COMM_ITS_PLATE_RESULT / NET_ITS_PLATE_RESULT: sLicense, byPlateType, byEntireBelieve 0-10, byCountry). Otopark yazılımı bu yolu kullanıyorsa XML emülasyonu imkansız — projenin en büyük riski.
kaynak: https://open.hikvision.com/hardware/definitions/NET_DVR_SetDVRMessageCallBack_V50.html

## Hikvision DS-TCG227-A/AIR (giriş-çıkış ANPR kamerası) (ticari) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
İkame çıtası: yakalama >%98, LPR >%98, yön >%96, hatalı yakalama <%2 (montaj 1.6-2 m, mesafe 4-6 m, açı <30°, karakter 25-30 px). Beyaz liste kamerada tutulabilir (listType 0-4, VCL API) ve bariyer PUT /ISAPI/ITC/Entrance/barrierGateCtrl ile röle üzerinden ISAPI'den açılabilir — B planı için kritik.
kaynak: https://www.hikvision.com/content/dam/hikvision/products/S000000001/S000013642/S000000180/S000000181/OFR000261/M000008428/Data_Sheet/DS-TCG227-A-AIR_Datasheet_20181025.pdf

## node-red-contrib-hikvision-ultimate (acik-kaynak) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
Gerçek dünyada Hikvision ANPR olayını tüketen referans: plateNumber, captureTime, country, laneNo, direction, matchingResult ve base64 resim alanlarını çıkarır; digest auth ve otomatik yeniden bağlanma uygular. Bizim emülatörün üretmesi gereken alan alt kümesi için canlı kanıt.
kaynak: https://github.com/Supergiovane/node-red-contrib-hikvision-ultimate

## uchkunr/hikvision-best-practices (acik-kaynak) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
Saha tuzakları: >5 dk saat sapması digest'i bozar (NTP şart), ~5 hatalı auth denemesi 30 dk hesap kilidi, alertStream multipart/mixed long-poll vs HTTP push seçimi, multipart parça sıralamasının önemi. Operasyon gereksinimlerimizin kaynağı.
kaynak: https://github.com/uchkunr/hikvision-best-practices

## fuqiangZ/hikvision-isapi-go (acik-kaynak) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
EventNotificationAlert/alertStream XML ayrıştırma desenleri (xmlquery/XPath, nil-kontrolleri, aynı ağaçtan çoklu alan çıkarma); C# tarafında XDocument ile aynı desenlerin uygulanabilirliğini gösterir.
kaynak: https://deepwiki.com/fuqiangZ/hikvision-isapi-go/6.2-xml-processing-with-xmlquery

## HikCentral Professional (ticari) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
Hikvision'un kendi giriş-çıkış/otopark platformu; ANPR kameralarla ISAPI+SDK karışık entegre olur. Mevcut otopark yazılımı HikCentral türeviyse entegrasyon yüzeyi büyük olasılıkla SDK'dır — saha keşfinde yazılımın adı/sürümü bu yüzden ilk sorulacak bilgidir.
kaynak: https://www.hikvision.com/tr/products/software/HikCentral-Professional-series/HikCentral-Professional-2-0/

## Hursoft Otopark Plaka Tanıma (tr-yerli) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
Türkiye pazarı referansı: 5-7 ms tanıma süresi pazarlar, abone (beyaz liste) eşleşmesinde otomatik bariyer açma akışı sunar; yerli otopark yazılımlarının plaka→abone→bariyer karar zincirinin tipik örneği.
kaynak: https://www.hursoft.net/urun/otopark-arac-plaka-tanima-fiyatlari

## ccontavalli/hicknotify (acik-kaynak) [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)]
Hikvision olay akışını (alertStream) izleyip aksiyon alan hafif Python servisi; uzun süreli bağlantı kopması/yeniden bağlanma yönetiminin basit ve okunabilir örneği.
kaynak: https://github.com/ccontavalli/hicknotify

## Axis 'Latency in live network video surveillance' beyaz kitabı (ticari) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
Tek güvenilir katman-katman gecikme dökümü: kamera tarafı <50 ms (yakalama 1/fps + ~1 ms encode), LAN birkaç ms, alıcı play-out tamponu EN BÜYÜK kalem (saniyelere kadar). H.264 decoder'ın en az 1 kare tamponladığını, baseline profilin en hızlı çözüldüğünü ve timestamp-overlay ölçüm yöntemini (hata ≤1 kare) belgeler. Bizim gecikme bütçemizin iskeleti buradan alınmalı.
kaynak: https://www.axis.com/dam/public/9d/e4/5d/latency-in-live-network-video-surveillance-en-US-190945.pdf

## Frigate (ffmpeg preset optimizasyonu, issue #5459) (acik-kaynak) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
Bizimkine en yakın gerçek vaka: NVR yazılımında ffmpeg alım hattına -threads 1, -fflags nobuffer, -muxdelay 0, düşük probesize/analyzeduration eklenince ölçülmüş 149 ms gecikme kazancı + daha az CPU. Çok thread'li decode'un (13 thread) zayıf makinede hem gecikme hem CPU israfı olduğu dersi doğrudan bize uygulanır.
kaynak: https://github.com/blakeblackshear/frigate/issues/5459

## Qengineering RTSP-with-OpenCV (acik-kaynak) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
OpenCV/ffmpeg RTSP alımında 'ihmal edilebilir gecikme' deseni: kaçırılan kareleri saptayıp atma + GetLatestFrame ile daima en güncel kareyi işleme. İşleme süresi kare aralığını aşarsa gecikmenin biriktiği temel problemi ve kablolu Ethernet zorunluluğunu belgeler. C# tarafında aynı desen (okuma thread'i + son-kare) uygulanmalı.
kaynak: https://github.com/Qengineering/RTSP-with-OpenCV

## OpenIPC (timestamps-and-time-sync wiki) (acik-kaynak) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
Programatik kare-yaşı ölçümünün referansı: RTCP Sender Report'taki NTP/RTP çifti karenin YAKALAMA anını taşır; ffmpeg/GStreamer alıcıları bunu okuyabilir. Yerel NTP ile ±3-5 ms, genel NTP ile ±10-30 ms doğruluk. Canlı kare-yaşı izleme gereksinimimizin teknik temeli.
kaynak: https://github.com/OpenIPC/wiki/blob/master/en/timestamps-and-time-sync.md

## Plate Recognizer Stream (ticari) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
Ticari ANPR'ın gecikme parametreleri: otopark bariyeri için max_prediction_delay 1-2 sn'ye düşürülmeli (varsayılan 6 sn'ye kadar bekler), otoparkta efektif 4-5 fps işleme yeterli, sample=2-3 ile kare örnekleme. Karar-penceresi/oylama süresi gereksinimimizin sayısal dayanağı.
kaynak: https://guides.platerecognizer.com/docs/stream/configuration/

## OpenALPR / Rekor Scout agent (ticari) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
GStreamer alım hattında latency=1000 drop-on-latency=1 ve do_retransmission=false kullanır — yani ticari ANPR bile jitter tamponunu sınırlar ve geciken kareleri DÜŞÜRÜR; ayrıca kamera fps'i ile I-frame aralığını eşitlemeyi önerir. 'Geç kareyi atmak okumamaktan iyidir' ilkesinin ürünleşmiş kanıtı.
kaynak: https://help.rekor.ai/how-do-i-configure-my-camera-streams

## Hikvision H.264+/H.265+ akıllı codec (ticari) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
Bizim kameranın varsayılanı olabilecek tuzak: arka-plan-referanslı çok uzun GOP kullanır, ffmpeg dahil üçüncü parti decoder'larla uyumsuzdur (I-frame hataları). Kurulumda mutlaka düz H.264'e çekilmeli, I-frame aralığı varsayılan 50'den fps'e (25) indirilmelidir.
kaynak: https://github.com/blakeblackshear/frigate/discussions/13369

## RidgeRun Jetson glass-to-glass + Vay LED/fotodiyot ölçüm yöntemi (acik-kaynak) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
Fiili gecikme ölçüm metodolojisi: ms kronometre/slow-motion kare-kare sayma (basit, hata ~1 kare) ve LED+fotodiyot (µs hassasiyet, dağılım grafiği üretir; Vay açık kaynak). Donanım-hızlandırmalı referans sistemlerde uçtan uca 43-120 ms ölçülmüş — yazılım decode'lu zayıf PC'de bunun katbekat üstü beklenmeli, bu yüzden ölçüm şart.
kaynak: https://developer.ridgerun.com/wiki/index.php/Jetson_glass_to_glass_latency

## WINK RTSP Optimization Guide (ticari) [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)]
GOP'u 1-2 sn ile sınırlama (örn. 15 fps'te GOP=15), TCP tercih (UDP'de kayıp paket = bozuk kare), Hikvision/Axis için somut örnek konfigürasyonlar. Bozuk karenin ANPR'da yanlış okuma riski taşıdığı düşünülürse TCP kararımızı destekler.
kaynak: https://www.wink.co/documentation/RTSP-Cellular-Optimization-Guide

## Hikvision ANPR (7xxx/H8 serisi, Entrance ANPR) (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
İkame ettiğimiz referans davranış: sarı şerit çizgileriyle algılama alanı, mavi 'yakalama çizgisi' (araç çizgiyi geçince snapshot), şerit başına AYNI ANDA YALNIZCA 1 plaka yakalama kuralı; bariyer girişi için 1 şerit (yavaş kapılarda en çok 2) önerilir. Bizim servisimiz en az bu şerit-disiplinini sağlamalı ki bariyer yazılımı davranış farkı görmesin.
kaynak: https://www.hikvision.com/content/dam/hikvision/au/firmware/ids-2cd7a46g0-p-izhs(y)/7xxx-ANPR-Installation-&-Configuration-GuidanceNew.pdf

## Frigate LPR (acik-kaynak) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
CPU'da çalışan en yakın açık kaynak emsal: araç-önce tespit + plakayı araca bağlama, 'en büyük görünen plakalı' kareyi seçme (Frigate+), min_area=1000 px², recognition_threshold=0.9, format regex + replace_rules + min_plate_length filtreleri. LPR'ın zone'a kısıtlanamaması (kamera seviyesinde çalışması) bilinen eksik — bizim tasarımda tetik bölgesi kısıtı baştan konmalı.
kaynak: https://docs.frigate.video/configuration/license_plate_recognition/

## AccessALPR (acik-kaynak) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Erişim kontrolü için yazılmış açık kaynak ANPR: sabit anchor noktasından 416x416 ROI kırpma (zayıf donanım dostu), SORT izleyici ile çoklu araç ayrımı, kare-bazlı çoğunluk oylaması, ağırlıklı Levenshtein (eşik 0.2) ile bilinen-plaka eşleştirme. Track-bazlı oylama fikrinin çalışan kanıtı.
kaynak: https://github.com/glefundes/AccessALPR

## Plate Recognizer Stream (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Parametre seti birebir kopyalanabilir: Detection Zone PNG maskesi (karşı şerit/arka plan dışlama), memory_decay=300 sn (plaka-bazlı dedup), max_prediction_delay=6 sn (karar gecikme sınırı), plates_per_vehicle=1, detection_mode=vehicle (plakasız araç tespiti — tailgating sayımı için), sample=2 (kare atlama). Kamera kurulum rehberi: plaka ~100 px genişlik, maks 45° açı, maks 35 m.
kaynak: https://guides.platerecognizer.com/docs/stream/configuration/

## Survision LPR (entegrasyon API) (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Olay modeli emsali: free-run modunda araç başına msg/anpr/new → decision → end yaşam döngüsü (presence izleme), triggerOn/triggerOff + timeout, reliabilityThreshold (güven aşılınca erken karar), endPlateTimeout_ms≈500 ms (plaka ömrü uzatma — geç tetik yarış durumu çözümü), multiPlate bayrağı (varsayılan tekil plaka).
kaynak: https://fr.survisiongroup.com/documentations/integration_doc.html

## Axis License Plate Verifier (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Bariyer senaryosu için ölçülebilir eşikler: tek satırlı plaka ≥130 px genişlik (çift satır ≥70 px), maks 30° montaj açısı, 2-7 m erişim kontrolü mesafesi; alan başına yön oku ve in/out/any filtresi; New/Update/Lost olay tipleri; bariyer için dahili I/O / röle / kapı kontrolcüsü üçlüsü.
kaynak: https://help.axis.com/en-us/axis-license-plate-verifier

## Milestone XProtect LPR (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Plaka çözünürlük standardı: en az 2,7 px/çizgi-kalınlığı; AB plakası (52 cm) için min 170 px genişlik, hareketli araç+interlace'te 280 px. Bizim 'karar için min plaka piksel genişliği' eşiğinin üst bandını tanımlar.
kaynak: https://doc.milestonesys.com/2021r2/en-US/add-ons/add-on_lpr/lpr_platewidthrecommendat.htm

## Adaptive Recognition Vidar (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
'Virtual loop' kavramının tanımı: tek şeride dijital olarak çizilen, indüksiyon halkası gibi anlık tetikleyen bölge; alternatif 'Plate Finder' tetikleyicisi (plaka görülünce araç var say). %100 araç algılama + %99.7 ANPR doğruluk iddiası; kamera başına en çok 2 şerit.
kaynak: https://adaptiverecognition.com/products/vidar-anpr-camera/

## intuVision Vehicle Tailgating (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Tailgating tanımının video-analitik hali: tek erişim olayında (kart/açılış) birden çok araç geçişi VEYA kullanıcı tanımlı süreden kısa aralıkla iki araç; araç sınıflandırmasıyla (otomobil/kamyon/motosiklet) uzun araçların iki araç sanılması engelleniyor; HTTP tetik/IO ile erişim kontrolüne bağlanıyor; kamera min ~5 m (16 ft) yükseklik, tepeden bakış.
kaynak: https://intuvisiontech.com/solutions/tailgating-vehicle

## gateGUARDIAN (ANPR International) (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Tailgating kanıt yönetimi emsali: bariyer şeritlerini izleyen çift kamera, ihlal anında önceden tanımlı süreli HD video klibi ağ depolamaya kaydeder ve sorumlu aracı ANPR ile teşhis eder — 'tailgating olayında klip + ikinci plaka kaydı' gereksinimimizin kaynağı.
kaynak: http://www.anpr-international.com/tailgating-detection/

## Nedap ANPR Lumo (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Şerit başına tek kamera yaklaşımı: 2-10 m okuma mesafesi, 3,5 m'ye kadar şerit genişliği kapsama (yani kamera tek şeride adanır); izin verilen yön aralığı dışı hareket için 'Wrong Direction' alarmı — yön filtresi gereksinimimizin ticari emsali.
kaynak: https://www.nedapidentification.com/products/anpr/anpr-lumo/

## OpenALPR / Rekor Scout Agent (ticari) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Plaka gruplama (dedup) parametre ailesi: plate_groups_time_delta_ms (benzer plaka için bekleme, görülünce sıfırlanır), plate_groups_min_plates_to_group, plate_groups_max_plates_to_group (araç karede kalsa bile gönder), plate_groups_min_confidence, plate_groups_span_cameras — bizim oylama penceresi/dedup tasarımının parametre şablonu.
kaynak: https://help.rekor.ai/what-are-the-rekor-scout-agent-configuration-properties

## Bariyer + indüksiyon loop endüstri pratiği (Parking Boxx / BD Loops) (standart) [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)]
Donanım tarafı tailgating standardı: kol ilk araç loop'u terk eder etmez hızla iner (yüksek hızlı operatörler <1 sn çevrim), kademeli (staggered) çift loop tek uzun araç ile peş peşe iki aracı ayırt eder, ters loop kapanan kolu araç üstüne indirmez. Yazılımımızın 'bariyer-açık penceresi' varsayımları bu davranışa göre kurulmalı.
kaynak: https://www.parkingboxx.com/the-best-parking-barrier-gates-how-do-they-work

## Hikvision Intelligent ANPR Camera (DS-TCG4xx serisi, firmware V5.4.4) (ticari) [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları]
İkame ettiğimiz sistemin kendisi ve aynı zamanda hazır rollback mekanizmamız: 'By Platform / By Camera' kontrol modları, 'Enable Barrier Gate Control Offline' (platform kopunca bariyeri açık tut veya kamera allowlist'iyle kontrol et), SD karta yazılan yerel allowlist (yazım 15 dk sürebilir), yapılandırılabilir Relay Out Time, bariyer limit sinyali okuma, Wiegand 26/34/72 çıkışı, 'Keep Barrier Open for Following Vehicle'. Kameranın rölesi ISAPI /ISAPI/System/IO/outputs/<ID>/trigger ile PC'den de tetiklenebilir.
kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000178982/Intelligent-ANPR-Camera_User-Manual_V5.4.4_20250923.pdf

## Magnetic MHTM MicroDrive (MGC/MGC Pro kontrol üniteli bariyer) (ticari) [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları]
Kapanma mantığının endüstri referansı: kol altı loop daima 'monitor + closing loop', araç loop'tan çıkmadan bariyer kapanmaz; 'plausibility check' güvenlik cihazı görmezse bariyeri hizmet dışı bırakır (açılışta 3, işletmede 10 açılış); 'Power failure' menüsüyle kesinti davranışı ayarlanır; ölü noktada alet deliğiyle manuel kurtarma; safety loop geometrisi (±500 mm binek, ≥2,5 m kamyon, opening-safety loop ≤1 m, 70-500 µH) sayısal olarak tanımlı.
kaynak: https://magneticgateopeners.com/store/pdfs/MHTM_Manual.pdf

## Proconel BL100B Barrier Logic Loop Detector (ticari) [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları]
Loop dedektör + bariyer lojiği tek kutuda: roll-back koruması (kapanırken araç geri kayarsa kol tekrar kalkar), reverse-out auto-close (2/20 sn), memory input ile çoklu araç geçişi, Beam OFF Delay 2 sn (yanlış kapanmayı önler), arıza durumunda loop fault 'detect' olarak okunur (fail-safe: bariyer açık kalır), -40..+70°C çalışma aralığı — Erzurum kışına uygun.
kaynak: https://www.proconel.com/wp-content/uploads/2019/04/BL100BDatasheetV03.pdf

## ANSI/UL 325 standardı (DASMA TDS-353 teknik veri sayfası) (standart) [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları]
Bariyer fiziksel güvenliğinin sayısal çerçevesi: engelde 2 sn içinde ters yön + min 2 inç geri hareket, ikinci ardışık engelde sadece dur, engel sonrası timer-to-close reset'e kadar devre dışı, her sıkışma bölgesi/yön için en az 2 bağımsız koruma (Type A inherent + B1 fotosel / B2 kenar), TÜM harici sensörler monitörlü ve sahada bypass edilmesi yasak. Sınıf I-IV operatör sınıflandırması.
kaynak: https://www.dasma.com/wp-content/uploads/pubs/TechDataSheets/OperatorElectronics/TDS353.pdf

## Z Sistem / Türk bariyer entegratörleri (Ardox, NIF vb.) (tr-yerli) [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları]
Türkiye otopark pratiği: emniyet fotoseli + loop dedektör kol altında araç varken 'kapat' basılsa bile kolu hareket ettirmez; sensörler arızalansa bile 'akıllı kol' temasta hemen kalkar; açılma-kapanma süreleri 1-6 sn ayarlanabilir; kumanda/buton/kart okuyucu/OGS-HGS entegrasyonları yaygın. Saha keşfinde mevcut bariyer markasının bu özellik setine sahip olup olmadığı kontrol edilmeli.
kaynak: https://zsistem.net/h_bariyer.php

## BEA sensörleri (radar/lazer tabanlı loop-fotosel ikamesi) (ticari) [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları]
Kazı gerektirmeden endüktif loop ve fotoseli ikame eden sanal loop sensörleri; sadece araca tepki verip yayaları ayırt eder, bariyer kapanırken istenmeyen teması azaltır. Mevcut zeminde loop kesimi yapılamayan (kışın asfalt çalışması imkansız) Erzurum lokasyonları için alternatif.
kaynak: https://eu.beasensors.com/en/segment/industrial-door-gate-sensors/barriers/

## Windows sc failure + BIOS AC Power Restore + donanım watchdog zinciri (standart) [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları]
İnsansız toparlanma üçlüsü: (1) BIOS 'Restore on AC Power Loss=Power On' + bcdedit bootstatuspolicy ignoreshutdownfailures ile kesinti sonrası başsız açılış, (2) 'sc failure ... actions= restart/5000/restart/5000/reboot/30000 reset= 86400' ile servis çökmesinde kademeli kurtarma, (3) anakart watchdog timer ile OS donmasında otomatik reset. UPS yazılımı tek başına kapalı PC'yi açamaz — UPS çıkış güç döngüsü + BIOS birlikte test edilmeli.
kaynak: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc742019(v=ws.11)

## fast-plate-ocr (ankandrew) (acik-kaynak) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Seçilen OCR stack'i. Çift satır desteği belgelenmemiş; issue #39 (Mart 2025) iki satırlı plakada karakterlerin karışık/yanlış sırada çıktığını raporluyor ve yanıtsız. Model zoo: cct-s/xs-v2-global (doğruluk yayınlanmamış); legacy global-vit-v2 %93,3 (65+ ülke, 85k+ plaka). Resmi fine-tune workflow'u (max_plate_slots, alphabet) TR çift satır verisiyle yeniden eğitime imkan veriyor.
kaynak: https://github.com/ankandrew/fast-plate-ocr

## fast-alpr (ankandrew) (acik-kaynak) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Uçtan uca stack (open-image-models tespit + fast-plate-ocr OCR). Multiline/motosiklet hiç geçmiyor; nicel doğruluk iddiası yok — çift satır kabiliyeti bizim testimizle kanıtlanmak zorunda.
kaynak: https://github.com/ankandrew/fast-alpr

## OpenALPR (acik-kaynak) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Çift satır için multiline=1 + ayrı config (char_analysis parametreleri) ve ayrı ülke modeli (eu,eu2 aynı instance'ta) gerektiriyor; tek satır config'i çift satırda çalışmıyor çünkü karakter/plaka yükseklik oranı varsayımı bozuluyor. Ders: çift satır 'bedava' gelmez, ayrı yapılandırma+eğitim verisi ister.
kaynak: https://github.com/openalpr/openalpr/issues/597

## LPTR-AFLNet (arXiv 2507.16362) (acik-kaynak) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Çift satır plaka için kanıtlanmış ön işleme deseni: üst ve alt karakter bölgeleri ayrı perspektif dönüşümle rektifiye edilip yatay birleştirilerek tek satır formatında OCR'a verilir (12 parametreli köşe regresyonu). Bizim satır-böl/birleştir ön işlememizin literatür dayanağı.
kaynak: https://arxiv.org/html/2507.16362v2

## Plate Recognizer (ticari) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
İki satırlı plaka desteğini açıkça belirtiyor, araç tipi tespitinde 'motorcycle' sınıfı var; 'insan okuyabiliyorsa motor da okumalı' kuralı ve ~100 px plaka genişliği kamera rehberi bizim kabul kriterlerimize şablon.
kaynak: https://platerecognizer.com/lpr-camera-resolution-guide/

## Hikvision ANPR (ikame edilen) (ticari) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Motosiklet plakası dahil >%98 doğruluk, araç tipi/renk/yön tespiti, 'Entrance & Exit' sahne modları iddia ediyor. İkame yazılımın referans çizgisi; motosiklet/çift satırda geride kalınırsa yazılı kapsam sınırı şart.
kaynak: https://www.anprcams.com/product/hikvision-anpr-camera/

## Carmen ANPR (Adaptive Recognition) (ticari) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
36.000+ plaka tipi ve motosiklet sınıflandırması; blog'u gelişmiş ALPR'ın plaka şekline değil karakter dizilimi+sözdizimine dayandığını vurguluyor — TR regex doğrulama gereksinimimizin ticari karşılığı.
kaynak: https://adaptiverecognition.com/blog/traffic-transportation/challenges-for-anpr-unusual-plates-around-the-world/

## Karayolları Trafik Yönetmeliği Madde 54 (standart) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Motosiklet/motorlu bisiklet: SADECE 1 arka plaka (15x24, arka çamurluk üzerine, yerden arkada 30-125 cm); yük motosikleti ve otomobil vb.: ön+arka 2 plaka (ön 11x52 tek satır, arka 11x52 veya 21x32 kare iki satır). Ön kameranın motosikleti okuyamamasının hukuki temeli.
kaynak: https://www.ileritrafik.com/blog/icerik/trafik-yonetmeligi-2-bolum-4-kisim-4-bolum

## Medway Council / UK ANPR otopark pratiği (ticari) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Saha gerçeği: ön plakasız motosiklet ANPR otoparkına girişte okunamıyor; çözümler ya ayrılmış motosiklet alanı + ücret muafiyeti (Medway) ya girişte arka plaka okuma ya da interkom/manuel bilet. Belediyenin politika kararı gerektirir.
kaynak: https://www.medway.gov.uk/info/200159/parking/1790/automatic_number_plate_recognition_anpr

## TÜİK Motorlu Kara Taşıtları + Erzurum verisi (standart) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
TR filosunda motosiklet payı %20,8 (Tem 2025, ~6,8M/32,6M); yeni kayıtların %43,5'i motosiklet (Nis 2025). Erzurum: ~9.522 motosiklet / ~156 bin araç (≈%6) ama yıllık +%26 artış — motosiklet senaryosu Erzurum'da bugün küçük ama hızla büyüyen ve mevsimsel bir yük.
kaynak: https://data.tuik.gov.tr/Bulten/Index?p=Motorlu-Kara-Tasitlari-Nisan-2025-54040

## EDI Inductive Loop Detector Basics (standart) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Motosiklet büyük loopta ~%0,012 endüktans değişimi yaratır; algılanması için hassasiyet +1 kademe veya küçük loop gerekir. Bariyer kolunun motosiklete çarpmaması yazılımdan bağımsız donanım güvenlik şartı.
kaynak: https://www.editraffic.com/wp-content/uploads/2023/07/Inductive-Loop-Detector-Basics.pdf

## Eocortex / Dipolnet LPR piksel gereksinimleri (ticari) [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış]
Tek satır plaka için min 120x20 px, karakter yüksekliği ≥15 px, çizgi ≥2 px, açı ≤45°; çift satırda karakterler plaka yüksekliğinin <%50'si olduğundan crop yükseklik bütçesi iki katına çıkarılmalı.
kaynak: https://doc.eocortex.com/analytics/lpr/requirements.htm

## Plate Recognizer Stream (ticari) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
Bizim servisimizin birebir kapsam referansı (motor katmanı): RTSP işleme, regions/text_formats ile TR şablonu zorlama, sample=2 kare örnekleme, memory_decay=300sn dedupe, max_prediction_delay=6sn, webhook retry (3 deneme + 2dk'da bir cache tekrarı), heartbeat=30sn, max_reconnection_delay=600sn. Dashboard/tarife/alarm bilinçli olarak KAPSAM DIŞI bırakılmış — aynı sınırı biz de çizmeliyiz.
kaynak: https://guides.platerecognizer.com/docs/stream/configuration/

## ParkPow (ticari) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
PMS katmanı emsali: dashboard, ziyaret takibi, uyarılar (email/Teams/Slack/SMS), araç CRM, rol yönetimi, arama denetim logları ve 'Save visit correction' API'si. Stream'den webhook alır; kamera ID sabitliği kuralı buradan. Bizim webhook'umuzun besleyeceği tarafın yetenek listesi.
kaynak: https://platerecognizer.com/parkpow/ ; https://app.parkpow.com/documentation/

## Plate Recognizer Gate Opener (ticari) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
İnce karar katmanı deseni: Stream webhook'unu alır, yerel accessList.csv'ye bakar, GPIO pinini LOW→HIGH pulse ile bariyeri tetikler; whitelist'i ParkPow'dan ~20 sn'de bir poll ile senkronlar. 'Karar bizde mi PMS'te mi' sorusunun cevabını üç bileşene ayırarak verir — bariyer açma mantığını okuma motoruna gömmez.
kaynak: https://guides.platerecognizer.com/docs/other-apps/gate-opener/

## Hikvision ISAPI ANPR (EventNotificationAlert) (standart) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
İkame edeceğimiz arayüzün kendisi: kamera Alarm Server'a multipart/form-data içinde anpr.xml POST eder (alanlar: licensePlate, confidenceLevel 0-100, direction, dateTime, plateType, plateColor; resim tipi detectionUploadPicturesType ile seçilir). Beyaz/kara liste /ISAPI/ITC/Entrance/VCL ucundan, bariyer PUT /ISAPI/Parking/channels/1/barrierGate ile kontrol edilir. Uyumluluk modu bu sözleşmeyi taklit etmeli.
kaynak: https://forums.raspberrypi.com/viewtopic.php?t=365170 ; https://ipcamtalk.com/threads/how-to-open-control-barrier-using-hikvision-camera.62002/

## Rekor Scout (OpenALPR) (ticari) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
Agent (uçta okuma) + merkezi web server ayrımı; kamera bazlı agent adlandırma, aranabilir resimli okuma panosu, 60 gün veri saklama, sınırsız kullanıcı. Düşük güven düzeltme kuyruğu YOK — bu boşluk bizim kalibrasyon/inceleme UI'ımızın fark yaratacağı yer.
kaynak: https://www.openalpr.com/software/scout ; https://docs.rekor.ai/scout/web-server/configuring-agents

## Perkotek PTS (tr-yerli) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
Türk PMS'inin özellik tabanı — bizim kapsam DIŞI listemizin kaynağı: zaman bazlı tarife, yıllık/aylık/haftalık abonelik + SMS + borç takibi, kara liste uyarısı, vardiya/gelir/geçiş raporları, doluluk oranı, operatör ekranı (canlı görüntü, deforme plaka için manuel giriş, plaka düzeltme, kapı seçerek manuel tetik, acil durum tüm bariyerleri aç), STRIKE MX4 panel ile IP tabanlı bariyer tetikleme, '20 ms tanıma' iddiası.
kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr

## Plate Recognizer Webhook Middleware ailesi (ticari) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
Motor ile hedef sistem arasına giren 11 middleware (Zatpark, NX/Wave VMS, DOT yön filtresi, Front-Rear eşleştirme, Strip Plate gizlilik): 'iletme, iş kuralına göre filtreleme (ör. yön), dönüştürme, zenginleştirme' sorumlulukları ayrı bileşende. Yön filtresi ve PMS adaptörlerini çekirdeğe gömmeyip adaptör olarak yazma dersi.
kaynak: https://guides.platerecognizer.com/docs/stream/integrations/middleware/

## Avutec Gatekeeper + Park-R (ticari) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
Sorumluluk ayrımının bir başka doğrulaması: ANPR kamera 'plakayı okur ve yönü belirler', PMS (Park-R) 'kayıtlı plakaları/rezervasyonları kontrol eder ve erişim kararını verir', bariyer komutu PMS'ten gider. Okuma ile karar ayrımı sektör geneli.
kaynak: https://avutec.com/api-integration-for-anpr-based-parking-access/

## naveenrobo/Hikvision-ANPR-SDK (acik-kaynak) [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?]
Hikvision ANPR kameralardan plaka olayı çeken açık kaynak SDK; Alarm Server push'unun (multipart anpr.xml) topluluk tarafından nasıl ayrıştırıldığının çalışan örneği — uyumluluk modumuzu test etmek için karşı-uç olarak kullanılabilir.
kaynak: https://github.com/naveenrobo/Hikvision-ANPR-SDK
