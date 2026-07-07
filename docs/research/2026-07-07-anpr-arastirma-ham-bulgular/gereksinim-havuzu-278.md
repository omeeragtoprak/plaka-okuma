# Gereksinim Adayları (kategoriye göre)

## dogruluk (35)
- [acik-kaynak] Sistem tek kare okumaya değil çok-kare gruplamaya dayanmalı: aynı araca ait ardışık okumalar ~4500 ms'lik pencerede tek olayda birleştirilmeli, olay için en az 2 tutarlı okuma ve ≥%80 güven aranmalı, olay en geç 7500 ms içinde yayınlanmalı.
  ~ Rekor Scout agent varsayılanları (plate_groups_time_delta_ms=4500, min_plates_to_group=2, min_confidence=80.0, max_delay_to_send=7500) sektör standardı çok-kare konsensüs desenini tanımlıyor; tek kare okuması bariyer içi
  ~ kaynak: https://docs.rekor.ai/scout/agent/configuration/agent-properties
- [acik-kaynak] Okunan her plaka TR format regex'i ile doğrulanmalı (il kodu 01-81 + 1-3 harf + 2-5 rakam); formata uymayan okuma olaya dönüşmeden elenmeli ve ayrı loglanmalı.
  ~ Frigate format regex + geçersizleri otomatik atma, Plate Recognizer region_config=strict ve text_formats, OpenALPR bölgesel pattern matching — üç üründe de format şablonu birinci sınıf özellik.
  ~ kaynak: https://docs.frigate.video/configuration/license_plate_recognition/
- [acik-kaynak] OCR karakter karışıklıklarını (O→0, I→1, S→5 vb.) düzelten sıralı, konfigüre edilebilir regex değiştirme kuralları (replace_rules) uygulanmalı; TR plakada harf/rakam pozisyonları belli olduğundan pozisyona göre düzeltme yapılmalı.
  ~ Frigate LPR'da replace_rules (backreference destekli sıralı regex) tam bu amaçla var; TR formatının katı yapısı bu düzeltmeyi deterministik yapar.
  ~ kaynak: https://docs.frigate.video/configuration/license_plate_recognition/
- [acik-kaynak] Her okuma için plaka metniyle birlikte toplam güven skoru, karakter bazlı güven ve top-N aday listesi üretilmeli; bariyer kararı için minimum güven eşiği (örn. tanıma ≥0.9, tespit ≥0.7) ayrı ayrı ayarlanabilmeli.
  ~ OpenALPR top-N aday + güven, ultimateALPR karakter bazlı güven, Frigate detection_threshold=0.7 / recognition_threshold=0.9 varsayılanları; whitelist eşleşmesinde aday listesi kurtarıcı olur.
  ~ kaynak: https://github.com/openalpr/openalpr ; https://docs.frigate.video/configuration/license_plate_recognition/
- [acik-kaynak] Gündüz iyi koşullarda hedef doğruluk ilk tahminde ≥%99, format-geçerli yanlış okuma (bariyeri yanlış araca açma) oranı <%1 olmalı; salt klasik OCR (Tesseract) yaklaşımı kabul edilmemeli.
  ~ Rekor Scout benchmark: modern DL motor ilk tahminde %99.02 doğru; aynı testte Tesseract tabanlı açık kaynak OpenALPR %42.16 — derin öğrenme tabanlı tespit+OCR zorunlu.
  ~ kaynak: https://docs.rekor.ai/scout/agent/benchmarking
- [ticari-otopark] Sistem yakalama oranı (araç tespit) ve okuma oranını (tam plaka doğru) ayrı metrik olarak ölçmeli; hedef: yakalama ≥%99, tam okuma ≥%98.5 (önerilen kurulum/aydınlatma koşullarında).
  ~ Endüstri iki metriği ayırır ve koşula bağlar: Hikvision DS-TCG406-E yakalama ≥%99 / tanıma ≥%98.5; Dahua ITC413 yakalama ≥%99 / AB plaka tanıma ≥%98. Tek bir 'doğruluk' yüzdesi yanıltıcıdır.
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000078840/DS-TCG406-E_Datasheet_20251106.pdf
- [ticari-otopark] Doğruluk raporu N / N-1 / N-2 seviyeleriyle (0/1/2 karakter hatası toleransı) kırılımlı verilmeli; her okuma 0-100 güven puanı ve karakter bazlı güvenilirlik dizisi taşımalı.
  ~ Survision doğruluğun fuzzy seviyelerle ölçülmesini standart kabul eder ve Decision mesajında 0-100 reliability + karakter başına güvenilirlik dizisi döner; eşik ayarı buna dayanır.
  ~ kaynak: https://survisiongroup.com/post-lets-be-accurate-about-lpr-accuracy
- [ticari-otopark] Okumalar Türkiye plaka şablonuyla (il kodu 01-81 + 1-3 harf + 2-5 rakam) doğrulanmalı; şablona uymayan sonuçlar düşük güvene indirilmeli veya reddedilmeli.
  ~ Milestone XProtect ülke modülü (200+ modül) mantığı: plaka yapısı bilgisi yanlış okumaları süzer. Hikvision ve Dahua da Türkiye'yi ülke modülü olarak destekler (Hikvision'da 'TURKEY DP' modeli mevcut).
  ~ kaynak: https://doc.milestonesys.com/latest/en-US/add-ons/add-on_lpr/lpr_lprcountrymodules.htm
- [ticari-otopark] Çok kare oylaması (multi-frame LPR) zorunlu: aynı geçişte en az 3-5 karenin sonuçları birleştirilip tek karar üretilmeli; kirli/karlı plaka toleransı hedeflenmeli.
  ~ Hikvision 'multi-frame LPR, improving LPR rate' ve 'dirty license plate recognition' özelliklerini ayrı satış maddesi yapar; bizim V2+voting yaklaşımımızın ticari doğrulamasıdır.
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000078840/DS-TCG406-E_Datasheet_20251106.pdf
- [tr-pazari] Türkiye standardındaki plakaları en az %95-96 doğru okuma başarısıyla tanımalı (ticari ürün iddiaları %98-99,8 bandında).
  ~ Uludağ Üniv. ihale şartnamesi md.1.1 'en az %95 ve üstü', Buhur şartnamesi 'en az %96'; Traffix %99,8, Noron %99,9 iddia ediyor — kamu kabul eşiği %95-96.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [tr-pazari] Tek satır (11x52 cm) ve çift satır/kare (21x32 cm kamyon-otobüs arka, 15x24 cm motosiklet) plakaların tümünü okumalı.
  ~ Şartname md.1.10 'kare ve dikdörtgen ve her boyuttaki plakalar'; Perkotek çift satır desteğini ürün özelliği olarak veriyor; Karayolları Trafik Yönetmeliği boyutları tanımlıyor.
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr
- [standartlar] Sabit kurulumda sistem, insan gözüyle görülebilir plakaların en az %98'ini yakalamalı (capture) ve yakalananların en az %95'ini tüm karakterleriyle doğru okumalı (read); CCTV/genel amaçlı kamera görüntüsüyle çalışan modda yakalama eşiği en az %85 kabul edilebilir.
  ~ İngiltere NASPLE v3.4 resmi eşikleri: statik sistemler için %98 capture / %95 read, CCTV-entegre sistemler için %85 capture / %95 read. Bizim yazılım Hikvision video akışından çalıştığı için %85-98 bandı hedef aralıktır.
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-standards-for-policing-and-law-enforcement-accessible-version
- [standartlar] Doğru okuma tanımı: plakanın TÜM karakterleri doğru olmalı; S/5, O/0, B/8 gibi tek karakter karışması dahi 'yanlış okuma' sayılmalı, kabul testinde N-1 (bir karakter hata payı) gevşetmesi kullanılmamalı.
  ~ Home Office kılavuzu madde 10: 'All characters must be accurately read. Where similar characters, such as S and 5, are mistakenly interpreted a misread must be recorded.' Avutec de N vs N-1 farkının satıcı raporlarını şi
  ~ kaynak: https://assets.publishing.service.gov.uk/government/uploads/system/uploads/attachment_data/file/909024/ANPR_-_Evaluation_Approved_Version_2.0.pdf
- [standartlar] Aynı geçişin ardışık karelerinde karakter-pozisyonu bazlı çoğunluk oylaması (temporal redundancy / majority vote) uygulanmalı; literatür kazancı: tek kare %64,89 → oylama ile %78,33 (UFPR-ALPR), %85,45 → %93,53 (SSIG).
  ~ UFPR-ALPR makalesi (Laroca vd. 2018) çoklu kare oylamasının tanıma oranını 8-13 puan artırdığını gösteriyor; mevcut V2+voting mimarimizin bilimsel dayanağı.
  ~ kaynak: https://arxiv.org/pdf/1802.09567
- [standartlar] Hikvision ikamesi kabulü: aynı giriş/çıkışta eşzamanlı A/B karşılaştırmasında yazılımımız, Hikvision yerleşik ANPR'ın veri sayfası değerlerine (tanıma ≥ %98,5, yakalama ≥ %99, araç hızı ≤ 60 km/h koşulunda) eşit veya üstün performans göstermeli.
  ~ İkame edilen ürünün (DS-TCG405-E: recognition ≥98.5%, capture ≥99%; DS-TCG205-B: capture >99.5% @≤60 km/h) beyan değerleri fiili kabul çıtasıdır; otopark girişinde hız zaten düşüktür.
  ~ kaynak: https://www.hikvision.com/content/dam/hikvision/products/S000000001/S000000177/S000000180/S000000181/OFR000261/M000052450/Data_Sheet/DS-TCG405-E_Datas
- [standartlar] Akademik-uyumlu iç benchmark: kendi test setimizde tespit için IoU > 0.7, uçtan uca tanıma için 'IoU > 0.6 VE tüm karakterler doğru' kuralı uygulanmalı; sonuçlar UFPR-ALPR/CCPD literatür değerleriyle (%78-99 bandı) karşılaştırılarak raporlanmalı.
  ~ CCPD değerlendirme protokolü (280k+ görüntü) ve UFPR-ALPR protokolü akademide fiili standarttır; motor değişikliklerinde regresyonu ölçülebilir kılar.
  ~ kaynak: https://arxiv.org/pdf/1802.09567
- [kosullar] Sistem, görünür ve okunabilir plakaların en az %98'ini yakalamalı (capture) ve yakalanan plakaların en az %95'ini doğru okumalıdır (read).
  ~ İngiltere ulusal ANPR standardı (NASPLE) statik ANPR için 98% yakalama / 95% doğru okuma şartı koyar; endüstride tek kamera parking sistemleri için de %95-98 norm (Dahua, Selea, Milesight).
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-standards-for-policing-and-law-enforcement-accessible-version
- [kosullar] ANPR için optimize edilmemiş genel CCTV/IP kamera beslemesiyle çalışırken bile en az %85 yakalama / %95 okuma alt sınırı hedeflenmelidir.
  ~ NASPLE, CCTV-entegre (ANPR-optimize olmayan) sistemler için %85 yakalama / %95 okuma tanımlar; Hikvision'ın RTSP beslemesini kullanan salt-yazılım sistemimiz bu sınıfa yakındır.
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-standards-for-policing-and-law-enforcement-accessible-version
- [entegrasyon-operasyon] Plaka okuma doğruluğu iyi koşulda >=%95 (sektör saha normu %95-98) olmalı; kabul testleri yağmur, kar, düşük ışık, kirli plaka senaryolarında ayrıca ölçülmeli ve raporlanmalı.
  ~ Autopay 'yağmur, kar, düşük ışıkta performans sorulmalı; küçük tanıma hataları yanlış ücretlendirme/uygulama davalarına yol açar' der; sektör yayınlanan oranların ideal koşul olduğunu, kar/yağmurda düştüğünü teyit eder. 
  ~ kaynak: https://solutions.autopay.io/latest-news/what-to-consider-when-choosing-an-anpr-based-parking-management-system
- [entegrasyon-operasyon] Gece/IR modunda (siyah-beyaz görüntü, 0 lux) >=%95 okuma hedeflenmeli; kamera plakaya <15 derece açıyla ve bariyerden 3-5 m mesafede konumlanmalı.
  ~ Türk LPR rehberi 0 lux'ta %95+ IR doğruluğunu, <15 derece montaj açısını, 3-5 m bariyer mesafesini ve çıkışta min. 60 fps'yi somut kriter olarak verir; sistem kameranın gündüz renkli / gece IR siyah-beyaz filtre geçişler
  ~ kaynak: https://www.ertelbilisim.com.tr/otopark-kamera-lpr-raporlama/
- [entegrasyon-operasyon] Confidence eşiği yapılandırılabilir olmalı: eşik üstü okuma otomatik bariyer açar, eşik altı operatöre düşer; eşleştirmede candidates listesi ve çok-kare oylaması kullanılmalı.
  ~ Plate Recognizer (score/dscore) ve Hikvision (confidenceLevel) tüm olaylarda güven skoru taşır; Rekor aynı aracın karelerini gruplayıp en iyi okumayı seçer — zayıf CPU'da çok-kare oylaması doğruluğu telafi eder.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/results/
- [zayif-cpu] Dogruluk kabul cizgisi ikame edilen Hikvision ANPR'in resmi speki olmali: plaka tanima >= %98,5 ve arac yakalama >= %99 (coklu kare oylamasi ile saglanabilir); bariyer kararlarinda bu esigin alti regresyon sayilir.
  ~ Hikvision DS-TCG405-E vb. otopark ANPR kameralari >=98,5% tanima / >=99% yakalama deklare eder; kamerayi ikame eden yerel yazilim ayni cizgiyi tutturmazsa proje amacina ulasamaz.
  ~ kaynak: https://www.hikvision.com/en/products/parking-management/vehicle-access-control-management/anpr-cameras/ds-tcg405-e/
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Yerel CPU ANPR, ikame ettiği kameranın veri sayfası çıtasının altına düşmemeli: önerilen kurulumda yakalama oranı >%98, plaka okuma doğruluğu >%98, yön tanıma >%96, hatalı yakalama <%2; kabul ölçümü gerçek sahada gündüz/gece/kar ayrı ayrı raporlanmalı.
  ~ DS-TCG227-A/AIR veri sayfası yerleşik ANPR için bu sayıları taahhüt eder; 'kamerayı ikame' iddiası ancak aynı metriklerle kanıtlanabilir.
  ~ kaynak: https://www.hikvision.com/content/dam/hikvision/products/S000000001/S000013642/S000000180/S000000181/OFR000261/M000008428/Data_Sheet/DS-TCG227-A-AIR_D
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Tetik bölgesinde aynı anda birden fazla FARKLI plaka görüldüğünde hedef araç şu deterministik kuralla seçilmeli: (1) plaka piksel genişliği en büyük olan, (2) eşitlikte plaka kutusunun alt kenarı kadraja en yakın (en alttaki) olan; seçim gerekçesi (genişlik px, konum, track id) olay kaydına yazılmalı.
  ~ Frigate+ 'en büyük görünen plakalı' araç karesini otomatik seçer; perspektif gereği bariyerdeki araç her zaman en büyük ve en alttaki plakaya sahiptir. Yanlış seçim bariyeri arkadaki araca açmak demektir.
  ~ kaynak: https://docs.frigate.video/plus/ ; https://docs.frigate.video/configuration/license_plate_recognition/
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Bariyer kararı için konfigüre edilebilir minimum plaka piksel genişliği eşiği olmalı (başlangıç değeri: tek satırlı plaka ≥130 px; kabul edilebilir aralık 100-170 px); eşik altındaki plakalar 'uzak araç' sayılıp karar dışı tutulmalı fakat izleme listesine alınmalı.
  ~ Axis LPV tek satırlı plakada ≥130 px, çift satırda ≥70 px şart koşar; Milestone AB plakası için 170 px (2,7 px/stroke) önerir; Plate Recognizer ~100 px önerir (benchmark min 30 px). Arkadaki kuyruk araçlarının plakaları 
  ~ kaynak: https://help.axis.com/en-us/axis-license-plate-verifier ; https://doc.milestonesys.com/2021r2/en-US/add-ons/add-on_lpr/lpr_platewidthrecommendat.htm ;
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Şerit başına aynı anda yalnızca 1 aktif araç kararı üretilmeli (Hikvision davranış paritesi); birden çok plakayı eşzamanlı raporlama (multiPlate) bayrağı bulunmalı ve varsayılanı KAPALI olmalı.
  ~ İkame ettiğimiz Hikvision ANPR 'şerit başına aynı anda yalnız 1 plaka yakalanır' kuralıyla çalışır; Survision'da multiPlate, Plate Recognizer'da plates_per_vehicle=1 aynı amaçla varsayılan tekil çalışır. Bariyer yazılımı
  ~ kaynak: https://www.hikvision.com/content/dam/hikvision/au/firmware/ids-2cd7a46g0-p-izhs(y)/7xxx-ANPR-Installation-&-Configuration-GuidanceNew.pdf ; https://f
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Her araç için hafif bir izleyici (IoU/centroid, SORT benzeri) ile new → decision → end yaşam döngüsü tutulmalı; çok-kare oylama SADECE aynı track'in kareleri üzerinde yapılmalı, iki farklı aracın kareleri hiçbir koşulda tek oylamada birleşmemeli.
  ~ Survision free-run modu msg/anpr/new-decision-end yaşam döngüsü yayınlar; AccessALPR SORT tracker + kare-bazlı çoğunluk oylaması kullanır. Mevcut V2+voting hattımız track ayrımı olmadan kuyrukta iki plakanın karelerini k
  ~ kaynak: https://fr.survisiongroup.com/documentations/integration_doc.html ; https://github.com/glefundes/AccessALPR
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Plaka-araç ilişkilendirmesi yapılmalı: plaka kutusu ancak bir araç kutusunun İÇİNDEyse o araca atanmalı; araç tespiti yapılamıyorsa plakalar arası IoU/mesafe ile track sürekliliği korunmalı.
  ~ Frigate normal LPR modunda önce araç tespit edilip plaka araca bağlanır; çoklu araç sahnesinde plakayı yanlış araca yazmak, arkadaki abonenin girişini öndeki misafir araca saydırır (ücretlendirme hatası).
  ~ kaynak: https://docs.frigate.video/configuration/license_plate_recognition/
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Yön filtresi olmalı: giriş kamerası yalnızca yaklaşan (in) yönlü track'ler için giriş olayı üretmeli; ters yönde hareket eden araç (çıkan/geri manevra) 'wrong-direction' olayı olarak ayrıca loglanmalı; yön ataması Axis'teki gibi in/out/any seçenekli olmalı.
  ~ Axis LPV alan başına yön oku ve in/out/any filtresi sunar; Nedap ANPR Lumo izin verilen yön aralığı dışındaki hareketi 'Wrong Direction' alarmı yapar. Karşı şeritten çıkan aracın kadraja girip giriş kaydı üretmesini enge
  ~ kaynak: https://help.axis.com/en-us/axis-license-plate-verifier ; https://www.nedapidentification.com/products/anpr/anpr-lumo/
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Türk plaka format doğrulaması hedef seçiminde ön filtre olmalı: '^(0[1-9]|[1-7][0-9]|8[01])\s?[A-Z]{1,3}\s?\d{2,5}$' desenine uymayan okuma karar adayı olamaz (Frigate format/replace_rules deseni); ayrıca min_plate_length benzeri kısa/eksik okuma filtresi uygulanmalı.
  ~ Frigate format regex'i ve replace_rules OCR hatalarını karar öncesi normalleştirip geçersiz plakaları atar; çoklu-plaka sahnesinde kısmi okunan arka plaka (örn. son 3 hane) format filtresine takılıp yanlış hedef olmaktan
  ~ kaynak: https://docs.frigate.video/configuration/license_plate_recognition/
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] fast-plate-ocr/CCT global modelinin çift satır (21x32 kamyon arka, 15x24 motosiklet) plakada çalıştığı varsayılMAmalı; üretim öncesi en az 100 gerçek TR çift satır plaka görüntüsünde ≥%95 tam-plaka doğruluğu kabul testi şart koşulmalı.
  ~ fast-plate-ocr issue #39 (17 Mart 2025): iki satırlı plakalarda karakterler 'mixed together and not in the correct order' ve maintainer yanıtı/çözüm yok; dokümantasyonda multiline desteği hiç belgelenmemiş.
  ~ kaynak: https://github.com/ankandrew/fast-plate-ocr/issues/39
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Çift satır plakalar için iki mühendislik yolundan biri seçilip pilotta doğrulanmalı: (a) satır bölme + yatay birleştirme ön işlemesi (üst/alt satır ayrı rektifiye edilip tek satır olarak OCR'a verilir) veya (b) TR çift satır verisiyle CCT modelin fine-tune edilmesi (resmi fine_tune_workflow.ipynb, max_plate_slots ve TR alfabesiyle).
  ~ LPTR-AFLNet makalesi çift satırda üst/alt bölgeleri ayrı perspektif düzeltip yatay birleştirme yöntemini tanımlar; OpenALPR da multiline=1 + ayrı config/eğitim verisi ister — global tek model varsayımı literatürde destek
  ~ kaynak: https://arxiv.org/html/2507.16362v2
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Çift satır birleştirme sırası deterministik olmalı: üst satır (il kodu + harf grubu) + alt satır (rakamlar) sırayla birleştirilip TR plaka regex'i (il 01-81, 1-3 harf, 2-5 rakam) ile doğrulanmalı; regex'e uymayan sonuç 'düşük güven' olarak işaretlenmeli.
  ~ TR format kombinasyonları kesin (99 X 9999 ... 99 XXX 999); issue #39'daki 'yanlış sıra' hatası sözdizimi doğrulamasıyla yakalanabilir — Adaptive Recognition da gelişmiş ALPR'ın karakter dizilimi+sözdizimi kontrolü yaptı
  ~ kaynak: https://tr.wikipedia.org/wiki/T%C3%BCrkiye_ta%C5%9F%C4%B1t_plaka_kodu
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Güven skoru eşik altındaki okumalar otomatik olay üretmeden düşük-güven inceleme kuyruğuna düşmeli; operatör düzeltmesi tek ekranda yapılıp hem yerel kayda hem PMS'e (ParkPow 'Save visit correction' benzeri bir uçla) geri yazılabilmeli.
  ~ ParkPow API'sinde visit correction ucu mevcut; Perkotek 'plaka düzeltme' ve 'deforme plaka için manuel giriş' sunuyor — düzeltme akışı sektör standardı ve modelin sahada iyileştirilmesi için veri kaynağıdır.
  ~ kaynak: https://app.parkpow.com/documentation/ ; https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Bölgesel plaka şablonu zorlaması yapılandırılabilir olmalı: TR formatı (2 rakam il 01-81 + 1-3 harf + 2-5 rakam) regex/text_formats ile motorun sonuç süzgecine eklenebilmeli.
  ~ Stream 'regions', 'region_config=strict' ve 'text_formats' (regex) parametreleriyle bölgesel şablon zorlamayı motor katmanının işi olarak tanımlar; TR şablonu yanlış okumaların önemli kısmını eler.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/

## entegrasyon (41)
- [acik-kaynak] Olaylar bariyer/otopark yazılımına HTTP POST webhook ile gönderilmeli; hedef ulaşılamazsa en az 3 anlık yeniden deneme, ardından diske kalıcı kuyruklama ve 2 dakikada bir tekrar gönderim yapılmalı — hiçbir olay kaybolmamalı.
  ~ Plate Recognizer webhook: 3 retry + disk cache + 2 dk aralıkla tekrar; Rekor Scout tüm sonuçları önce yerel beanstalkd kuyruğuna yazıyor. Store-and-forward otopark entegrasyonunun temel dayanıklılık deseni.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/ ; https://help.rekor.ai/how-do-i-receive-data-to-my-custom-application-from-rekor-scout-
- [acik-kaynak] Olay yükü asgari şu alanları içermeli: plaka metni, güven skoru, top-N adaylar, kamera ID, bbox, olaydaki kare sayısı, DST'yi doğru işleyen zaman damgası ve opsiyonel yön bilgisi; özel kimlik doğrulama header'ları ve istek timeout'u (varsayılan 30 sn) yapılandırılabilmeli.
  ~ Plate Recognizer webhook şeması (custom headers, 30 sn timeout, timezone/DST desteği) ve Rekor plate group JSON'u bu alan setini standartlaştırmış.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [acik-kaynak] Abone/beyaz liste desteği: bilinen plakalar tam metin veya regex ile tanımlanabilmeli, 1 karakterlik OCR toleransıyla (match_distance=1) eşleşme yapılabilmeli ve eşleşen plaka olayı etiketli (sub_label benzeri) yayınlanmalı.
  ~ Frigate known plates + match_distance özelliği; abonelik/personel araçlarına otomatik kapı açma otoparkın çekirdek senaryosu.
  ~ kaynak: https://docs.frigate.video/configuration/license_plate_recognition/
- [ticari-otopark] Plaka olayları JSON gövdeli HTTP POST (webhook) ile anlık itilmeli; olay içeriği en az: plaka, ülke, güven puanı, yön (ön/arka), zaman damgası (Unix epoch ms), kanıt JPEG (yapılandırılabilir kırpma) olmalı.
  ~ Fiili standart: Survision HTTP Push (JSON), PlateRecognizer Stream webhook_targets, Hikvision ISAPI olay bildirimi. Bariyer/otopark yazılımına olay verme hedefimizin birebir karşılığı.
  ~ kaynak: https://fr.survisiongroup.com/documentations/integration_doc.html
- [ticari-otopark] Bariyer entegrasyonu iki yolla sunulmalı: (a) kuru kontak röle çıkışı (endüstri normu 2 kanal, ~2A 30VDC; açma darbe süresi yapılandırılabilir), (b) mevcut geçiş kontrol paneline Wiegand 26 (SHA-1) / 34 / 64 bit kimlik çıkışı.
  ~ Nedap Lumo (2 röle + Wiegand 26 SHA1/64/custom + OSDP v2), Hikvision (2 röle + CardID26/SHA-1 26/Hik34/NEWG72), Dahua (2 röle 2A 30VDC + Wiegand SHA-1 26bit), Survision (röle 220VDC 2A + Wiegand) — hepsi bu ikiliyi sunar
  ~ kaynak: https://resources.nedapidentification.com/web/ANPR/Documentation/Datasheets/Datasheet_ANPR_LUMO_EN.pdf
- [ticari-otopark] Bariyersiz (free-flow) genişleme yolu düşünülmeli: giriş ve çıkış okumaları eşleştirilerek park süresi/doluluk hesaplanabilmeli; eşleşmeyen kayıtlar ihlal listesine düşmeli.
  ~ Genetec AutoVu Free-Flow bariyersiz otoparkta giriş/çıkış plakalarını izin/ödeme listeleriyle karşılaştırıp gerçek zamanlı ihlal ve doluluk panosu üretir; belediye otoparkları için doğal ikinci faz.
  ~ kaynak: https://www.genetec.com/products/unified-security/autovu/parking-management
- [ticari-otopark] Webhook → yerel beyaz liste → röle mimarisi referans alınmalı: ANPR motoru okumayı webhook ile Gate Opener benzeri hafif servise iletir, servis yerel CSV/DB beyaz listesine bakar ve eşleşmede röle pinini tetikler.
  ~ PlateRecognizer resmi Gate Opener uygulaması tam bu akışı uygular (Stream webhook_targets=gate-opener → accessList.csv → RTE rölesi); bizim C#/ONNX servisimiz için birebir kopyalanabilir desen.
  ~ kaynak: https://guides.platerecognizer.com/docs/other-apps/gate-opener/
- [tr-pazari] Bariyer aç-kapa çevrimi 1-1,5 sn ayarlanabilir olmalı ve tetik cihazı en az 2 bariyer çıkışı + 2 loop girişi sunmalı (USB veya IP/röle).
  ~ Şartname md.5.11 (1 sn min, 1-1,5 sn ayar) ve md.4.1-4.3 (USB tetik cihazı, 2 bariyer, 2 loop); Hikvision ikamesi için aynı röle davranışı gerekli.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [tr-pazari] Ücretli otopark modülü: sınırsız tarife tanımı, araç sınıfına göre ücret, abone yönetimi (ücretsiz abonelik aktive edilememeli), antipassback ve vardiya sonu PDF raporunun e-posta ile gönderimi desteklenmeli.
  ~ Şartname md.1.30, 1.33, 1.34, 7.13; Perkotek ücretli otopark sürümü aynı özellik setini (tarife, abonelik, borç takibi, SMS) pazar standardı yapmış.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [tr-pazari] Geçiş olayları LED pano/müşteri göstergesi ve fiş/termal yazıcıya bilgi gönderebilecek açık bir olay arayüzü (API/DLL) üzerinden dışarı verilmelidir.
  ~ Şartname md.1.2 (PTS çekirdeği DLL olarak uygulama geliştiricilere açık), md.1.17-1.18 (LED pano, fiş yazıcı) — bizim servis de bariyer/otopark yazılımına olay verecek, aynı model.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [standartlar] Her olay kaydı asgari şu alanları içermeli: plaka metni, güven skoru, zaman damgası, kamera/şerit kimliği, plaka kırpık görüntüsü (referans: 120×60 px, ≤3KB JPEG) ve isteğe bağlı genel sahne görüntüsü (≤25KB JPEG).
  ~ İngiltere NAS Teknik Şartnamesi v3.6'nın olay kaydı alanları; bariyer/otopark yazılımına kanıt görüntüsüyle olay iletmek itiraz yönetimi için şarttır.
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-service-technical-specifications-accessible-version
- [standartlar] Sistem saati NTP ile senkronize olmalı (referans: en az 10 dakikada bir, stratum 3 doğruluk); olay zaman damgaları bariyer/otopark yazılımının saatiyle tutarlı olmalı.
  ~ NAS Teknik Şartnamesi zaman senkronu şartı; ücretlendirme ve kanıt bütünlüğü zaman damgasına dayanır.
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-service-technical-specifications-accessible-version
- [standartlar] Otopark entegrasyon veri alanları ISO/TS 5206-1 (APDS) kavram modeliyle uyumlu adlandırılmalı (yer, oturum, hak/right, olay), böylece ileride farklı otopark yönetim yazılımlarına geçiş kolaylaşır.
  ~ ISO, Mart 2022'de APDS otopark veri şartnamesini ISO/TS 5206-1 olarak kabul etti; B2B veri alışverişinin ortak dili.
  ~ kaynak: https://allianceforparkingdatastandards.org/
- [kosullar] Bariyer/otopark yazılımına entegrasyon Hikvision ANPR olay modelini taklit etmeli: plaka metni + güven skoru + zaman damgası + kanıt görüntüsü (snapshot) içeren HTTP/REST push olayı; görüntü FTP/dosya yolu ile de sunulabilmelidir.
  ~ İkame edilen Hikvision ve rakip Einar access-control kamerası HTTP/HTTPS/FTP upload + snapshot'lı event management sunar; bariyer yazılımları bu sözleşmeyi bekler.
  ~ kaynak: https://adaptiverecognition.com/products/einar-anpr-camera/
- [kosullar] Whitelist eşleştirme tamamen yerel çalışmalı (bulut bağımlılığı sıfır) ve internet kesintisinde bariyer açma işlevi kesintisiz sürmelidir.
  ~ Bizim bulut-yok kısıtımız; Parklio ve benzeri sistemler yerel whitelist ile <1 sn açma sağlıyor — ağ gecikmesi/kesintisi tasarım dışı bırakılmalı.
  ~ kaynak: https://parklio.com/en/parking-solutions/anpr
- [entegrasyon-operasyon] Olay payload'ı asgari şu alanları içermeli: plate, timestamp (ISO 8601, UTC + yerel), camera_id, direction, confidence (0-1), candidates (alternatif okumalar + skorları), bounding box, plaka ve araç kırpık görüntüleri (dosya yolu/URL/base64).
  ~ Plate Recognizer Stream webhook şeması (plate, score, dscore, box, candidates, region, camera_id, timestamp, timestamp_local) ve Rekor (best_plate_number, best_confidence, candidates, epoch_start/end, uuids) fiili sektör
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/results/
- [entegrasyon-operasyon] Olaylar webhook (HTTP POST) ile iletilmeli, başarısız gönderim için yeniden deneme kuyruğu bulunmalı; paralel olarak yerel CSV/JSONL log dosyası daima yazılmalı.
  ~ Plate Recognizer Stream hem webhook hem CSV/JSONL yerel çıktı üretir; yerel log, ağ sorunlarında denetim izini korur.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/results/
- [entegrasyon-operasyon] Hikvision ikamesi olarak servis, EventNotificationAlert XML formatını (eventType=ANPR, licensePlate, confidenceLevel, direction, plateType, plateColor + resim) HTTP host notification push modeliyle üretebilmeli; böylece mevcut otopark yazılımları değişiklik yapmadan bağlanır.
  ~ Hikvision ANPR entegrasyonunun iki fiili alım modeli vardır: GET /ISAPI/Event/notification/alertStream (multipart/mixed pull) ve HttpHostNotification (push, detectionUploadPicturesType=all/licensePlatePicture/detectionPi
  ~ kaynak: https://tpp.hikvision.com/wiki/isapi/anpr/GUID-328FC5AD-38EA-4409-9E75-996279BF2A46.html ve https://github.com/uchkunr/hikvision-best-practices
- [entegrasyon-operasyon] Beyaz liste / kara liste / abone listesi dış yazılımdan API ile yüklenebilir-güncellenebilir olmalı (Hikvision'daki GET/PUT /ISAPI/Traffic/channels/<ID>/licensePlateAuditData muadili) ve yerelde cache'lenmeli.
  ~ Hikvision kameralar beyaz/kara listeyi bu endpoint ile alır; ikame ürünün aynı yeteneği sunması gerekir. Yerel cache çevrimdışı bariyer kararı için şarttır.
  ~ kaynak: https://tpp.hikvision.com/wiki/isapi/anpr/GUID-328FC5AD-38EA-4409-9E75-996279BF2A46.html
- [zayif-cpu] Plaka okuma -> bariyer/otopark yazilimina olay iletimi uctan uca < 500 ms hedeflenmeli; 2 saniye ustu gecikme arac kuyrugu olusturdugu icin kabul edilemez ust sinirdir.
  ~ Modern ANPR gecis sistemleri arac gelisi->bariyer akisini <500 ms'de tamamliyor; sektor kaynaklari 2 sn gecikmenin kuyruk/verim kaybi yarattigini belirtir. AVUTEC edge kamerada liste esleme+role tetigi tek haneli ms.
  ~ kaynak: https://caymil.com/blog/parking-access-control-explained-for-facility-operators ; https://www.parking.net/parking-news/avutec/anpr-in-milliseconds-eff
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Devreye almadan önce otopark yazılımı ile kamera arasındaki trafik en az 48 saat (giriş-çıkış yoğun saatler dahil) port-mirror/Wireshark ile kaydedilip entegrasyon modu yazılı olarak belgelenmeli: PC→kamera TCP 8000 kalıcı bağlantı = HCNetSDK arming; kamera→PC multipart HTTP POST = ISAPI httpHosts push; kamera→PC özel ikili çerçeve = SDK alarm-host listening; PC→kamera periyodik POST /ISAPI/Traffic/.../vehicleDetect/plates = polling.
  ~ Hikvision 7-series kılavuzu dört ayrı entegrasyon yolu tanımlar ve port 8000 SDK'nın özel ikili komut kanalıdır; mod bilinmeden emülatör tasarımı kumar olur. Sahada ilk gün patlayacak tek nokta budur.
  ~ kaynak: https://www.hikvisioneurope.com/eu/portal/portal/Technology%20Partner%20Program/02-Solutioins%20of%20Hikvision%20product%20integration/ANPR%20Camera%2
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Paket yakalamaya ek olarak kamera konfigürasyonundan mod teyidi yapılmalı: GET /ISAPI/Event/notification/httpHosts (digest) çıktısı, web arayüzü TCP/IP sekmesindeki 'Alarm Host Address/Port' ve 'ANPR IP/Port' alanları, Entrance&Exit sayfasındaki 'Upload via SDK / Upload to Alarm Host' kutucukları ekran görüntüsüyle arşivlenmeli.
  ~ TCG hızlı kılavuzu ve listening kılavuzu bu alanların hangi push yolunun aktif olduğunu doğrudan gösterdiğini belgeliyor; Wireshark bulgusunu çapraz doğrular.
  ~ kaynak: https://www.hikvisioneurope.com/eu/portal/portal/Technology%20Partner%20Program/02-Solutioins%20of%20Hikvision%20product%20integration/Fast%20guide%20
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] ISAPI push modu doğrulanırsa emülatör byte-uyumlu format üretmeli: POST http://ip:port/url, Content-Type: multipart/form-data;boundary=...; ilk parça Content-Disposition: form-data; name="anpr.xml";filename="anpr.xml" (text/xml, EventNotificationAlert version=2.0), ardından licensePlatePicture.jpg ve detectionPicture.jpg parçaları (firmware'e göre image/pjpeg VEYA image/jpeg); Connection: Keep-Alive desteklenmeli.
  ~ İki resmi kılavuzdaki gerçek POST örnekleri bu yapıyı birebir gösteriyor; alıcı yazılım parça adlarına (anpr.xml) göre ayrıştırma yapabilir, sapma sessiz reddedilmeye yol açar.
  ~ kaynak: Fast guide for ANPR of TCG camera via ISAPI (s.11-13) + ANPR Camera Integration Solution for 7 series (4.3)
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] XML namespace ve şema sürümü konfigüre edilebilir olmalı: eski firmware xmlns="http://www.std-cgi.com/ver20/XMLSchema", yenisi xmlns="http://www.isapi.org/ver20/XMLSchema" gönderir; emülatör sahada yakalanan GERÇEK örnekteki namespace'i birebir kopyalamalı.
  ~ Hikvision'un kendi iki resmi dokümanı farklı namespace kullanıyor; katı XML parser'lı bir otopark yazılımı yanlış namespace'te olayı sessizce düşürür.
  ~ kaynak: TCG fast guide (isapi.org) ile 7-series integration solution (std-cgi.com) arasındaki fark
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] SDK arming modu tespit edilirse XML emülasyonunun ÇALIŞMAYACAĞI kabul edilip karar kapısı işletilmeli: (a) otopark yazılımı üreticisinden HTTP push alıcısı/API dokümanı yazılı istenmeli, (b) bariyer kamera rölesi üzerinden PUT /ISAPI/ITC/Entrance/barrierGateCtrl (digest; barrietGateNum 1-2, barrietGateOper=on/off/stop/locked) ile doğrudan tetiklenebilirlik test edilmeli, (c) yazılımın DB/API seviyesinden beslenmesi fizibilite raporuna girmeli — üç yoldan biri seçilmeden geliştirmeye başlanmamalı.
  ~ HCNetSDK port 8000 üzerinde belgesiz özel ikili protokoldür (HCNetSDK.dll içinde gömülü); sunucu tarafını emüle etmek pratikte imkansızdır. barrierGateCtrl ISAPI komutu resmi kılavuzda bariyer kontrolü için belgelenmişti
  ~ kaynak: Fast guide TCG (barrierGateCtrl tablosu) + https://skylightcyber.com/2023/09/14/neighbourhood-watch-hikvision-intercom-eavesdropping/
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Hikvision kamerada H.264+ / H.265+ (akıllı codec) KAPATILMALI ve substream düz H.264 baseline/main profil olarak ayarlanmalı; kurulum dokümanında bu ayar zorunlu ön-koşul olarak listelenmelidir.
  ~ H.264+ Hikvision'a özel arka-plan-referanslı çok uzun GOP kullanır; üçüncü parti decoder'lar (ffmpeg/Frigate/SecuritySpy) I-frame hatası verir veya hiç çözemez. Axis dokümanı da baseline profilin decode'unun en hızlı old
  ~ kaynak: https://github.com/blakeblackshear/frigate/discussions/13369
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Kamera I-frame aralığı (GOP) kare hızına eşit (≈1 sn, örn. 25 fps'te 25) ayarlanmalı; 2 sn'yi (Hikvision varsayılanı 50 @25 fps) aşan GOP kabul edilmemeli.
  ~ Decoder bağlantı/yeniden bağlantıda ilk I-frame'i beklemek zorundadır: GOP 2 sn ise en kötü 2 sn kör pencere oluşur. WINK kılavuzu GOP'u 1-2 sn ile sınırlamayı, IPCamTalk I-frame=fps yapmayı önerir; kısa GOP paket kaybı 
  ~ kaynak: https://www.wink.co/documentation/RTSP-Cellular-Optimization-Guide
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Taşıma protokolü LAN'da RTSP/TCP (interleaved) olmalı; UDP yalnızca paket kaybı artefaktlarının kabul edilebildiği durumlarda seçilebilir olmalı ve reorder_queue_size=1 ile sıralama tamponu kapatılmalı.
  ~ Axis dokümanı LAN/az-hop topolojide RTP/RTSP/TCP önerir; WINK kılavuzu UDP'de kayıp paketlerin bozuk kareler ürettiğini belirtir. Bozuk kare ANPR'da yanlış okumaya yol açabileceği için TCP'nin küçük gecikme maliyeti terc
  ~ kaynak: https://www.axis.com/dam/public/9d/e4/5d/latency-in-live-network-video-surveillance-en-US-190945.pdf
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Girişsiz çıkış ve çıkışsız ikinci giriş (anti-passback) durumları otomatik işaretlenmeli: çıkışta okunan plakanın açık giriş kaydı yoksa 'eşleşmeyen çıkış — olası tailgating girişi' etiketiyle operatör kuyruğuna düşmeli; ücretlendirme bu kayıtlarda otomatik değil operatör onaylı olmalı.
  ~ ANPR otopark erişim sistemlerinde anti-passback politika yönetimi standart gereksinimdir (Nuveq); kaçırılan giriş/çıkış okumaları haksız faturalamaya yol açar (PCN Beater ANPR hata örnekleri). Tailgating'in muhasebe tara
  ~ kaynak: https://www.nuveq.net/post/how-an-anpr-parking-access-system-works ; https://www.pcnbeater.co.uk/blog/anpr-parking-ticket-errors
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Bariyer açma olayı payload'ı hedef seçim meta verisini içermeli: track id, plaka piksel genişliği, tetik bölgesi adı, yön, kaç aday plaka arasından seçildiği; bariyer/otopark yazılımı bu alanlarla şüpheli olayları (2+ aday) ayrıca değerlendirebilmeli.
  ~ Survision triggerResult ve Axis New/Update/Lost olay modelleri olay-bazlı zengin payload yayınlar; çok adaylı sahnede seçimin denetlenebilirliği, yanlış bariyer açılışlarının kök neden analizi için zorunlu.
  ~ kaynak: https://fr.survisiongroup.com/documentations/integration_doc.html ; https://help.axis.com/en-us/axis-license-plate-verifier
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Bariyer tarafında (donanım/entegrasyon önerisi olarak dokümante edilmeli): kol her araçtan sonra hemen kapanmalı (yüksek hızlı bariyerler <1 sn'de çevrim tamamlar) ve yazılım 'bariyer kapandı' geri bildirimini ya röle/loop sinyalinden ya da konfigüre edilebilir timeout'tan (varsayılan 8-10 sn) türetmeli.
  ~ Endüstri pratiği: ilk araç loop'u terk eder etmez kolun hızla inmesi tailgating penceresini küçültür; kademeli (staggered) loop'lar tek uzun araç ile peş peşe iki aracı ayırt eder. Yazılımımız loop sinyali yoksa pencerey
  ~ kaynak: https://www.parkingboxx.com/the-best-parking-barrier-gates-how-do-they-work ; https://bdloops.com/Upsell_Loop_Systems.html
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] ANPR PC/servis çöktüğünde otomatik geri dönüş (rollback) 1. kademe: Hikvision kamerada 'Enable Barrier Gate Control Offline' aktif edilecek ve 'Barrier Gate Control When Platform Disconnection' seçeneği ya 'bariyeri açık tut' ya da 'kamera kendi allowlist kurallarıyla kontrol etsin' olarak saha politikasına göre ayarlanacak; geçiş insan müdahalesi olmadan gerçekleşecek.
  ~ Hikvision Intelligent ANPR Camera kılavuzu 4.1.2: 'By Platform' modunda platform koptuğunda kamera bariyeri açık tutabilir veya kendi kurallarıyla ('By Camera') kontrol edebilir. Bizim .NET servisimiz 'platform' rolünde 
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000178982/Intelligent-ANPR-Camera_User-Manual_V5.4.4_20250923.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Bariyer durum geri bildirimi: bariyerin açık/kapalı limit sinyali kameranın (veya kontrol kartının) IO'suna bağlanacak ve sisteme okutulacak; 'aç komutu verildi ama kol açılmadı' durumu N sn içinde arıza olayı olarak raporlanacak.
  ~ Hikvision kılavuzu: 'Ensure the opening or closing to limit signal of the barrier gate is connected'; IO function ile 'device will upload barrier gate status information'. Komut-sonuç doğrulaması olmadan arıza tespiti ya
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000178982/Intelligent-ANPR-Camera_User-Manual_V5.4.4_20250923.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Açma sinyali arayüzü: PC/kamera → bariyer tetiği kuru kontak (COM/NO röle) veya ISAPI '/ISAPI/System/IO/outputs/<ID>/trigger' ile verilecek; röle darbe süresi ('Relay Out Time') bariyer kontrol kartının beklediği tetik süresine göre yapılandırılabilir olacak ve bariyerin kendi auto-close'u ile çakışmayacak şekilde saha testinde ayarlanacak.
  ~ Hikvision how-to ve kılavuz: röle çıkışı COM/NO'dan bariyer kartının Open/Trigger girişine 2 damarlı kablo; 'Relay Out Time' alarm çıkış süresini belirler; ISAPI URL ile de tetiklenebilir. İkame senaryosunda PC, kameranı
  ~ kaynak: http://www.hikvisioneurope.com/za/portal/portal/02-Technical%20Materials/03-How%20to%20document/01-Product%20Operate%20Guide/01--IP/01--IPC/01-Functio
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Alternatif yedek entegrasyon yolu olarak kameranın Wiegand 26/34/72 çıkışı değerlendirilecek: plaka-kart no eşlemesiyle mevcut access kontrol paneline veri gönderilebilir; Wiegand 26'da kart no en fazla 8 hane, Wiegand 34'te 10 hane sınırı dikkate alınacak.
  ~ Hikvision kılavuzu 4.2: kamera allowlist'teki plakaya bağlı kart numarasını Wiegand 26/34/72/SHA-1 ile access kontrol sistemine gönderir. Otopark yazılımı Wiegand tabanlıysa ANPR PC'siz de geçiş sürer.
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000178982/Intelligent-ANPR-Camera_User-Manual_V5.4.4_20250923.pdf
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Her şerit için okunan plaka yüzü (ön/arka) tasarım dokümanında sabitlenmeli: ön plakalar daima tek satır 11x52 olduğundan önden okuyan giriş şeridinde çift satır riski yoktur; çift satır desteği yalnızca arkadan okuma yapılan şeritlerde (çıkış veya motosiklet senaryosu) zorunludur.
  ~ TR ölçüleri: ön 11x52 tek satır; 21x32 kare iki satır yalnız ARKA plakada, 15x24 motosiklet plakası arkada — çift satır gereksinimi kamera yerleşimine bağlıdır (KTY Madde 54).
  ~ kaynak: https://www.ileritrafik.com/blog/icerik/trafik-yonetmeligi-2-bolum-4-kisim-4-bolum
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] İkame kabul kriteri: Hikvision yerleşik ANPR motosiklet plakası dahil >%98 doğruluk iddia ettiğinden, ikame yazılımın motosiklet/çift satır senaryolarında kapsam dışı kaldığı durumlar sözleşmede/şartnamede açıkça 'bilinen sınır' olarak yazılmalı ve işletmeci onayı alınmalı.
  ~ İkame edilen ürünün yeteneği referans çizgidir; belgelenmemiş gerileme kabul testinde reddedilir (Hikvision LPR: 'vehicle and motorcycle plates with over 98% accuracy').
  ~ kaynak: https://www.anprcams.com/product/hikvision-anpr-camera/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Servis her okuma için otopark yazılımına HTTP POST webhook göndermeli; payload en az plaka, güven skoru (0-100), yön, kamera ID, ISO8601 zaman damgası, plaka kırpığı ve tam kare JPEG içermeli.
  ~ Hikvision EventNotificationAlert (licensePlate, confidenceLevel, direction, dateTime, plateType, resim) ve Plate Recognizer Stream webhook'u bu alan setini fiili standart yapmış; PMS bu alanlarla karar veriyor.
  ~ kaynak: https://forums.raspberrypi.com/viewtopic.php?t=365170 ; https://guides.platerecognizer.com/docs/stream/configuration/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Hikvision uyumluluk modu sunulmalı: mevcut otopark yazılımı kamera push'u bekliyorsa servis, Alarm Server formatını (multipart/form-data içinde anpr.xml, EventNotificationAlert XML'i) birebir taklit edebilmeli ki PMS tarafında sıfır değişiklik gereksin.
  ~ Hikvision ANPR kameralar olayları 'Network→Advanced Settings→Alarm Server' hedefine multipart anpr.xml olarak POST eder; ikame servis aynı sözleşmeyi konuşursa PMS entegrasyonu kırılmaz.
  ~ kaynak: https://forums.raspberrypi.com/viewtopic.php?t=365170
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Webhook sözleşmesi versiyonlanmalı, her olay tekil event_id taşımalı (idempotent POST) ve hedef, kimlik doğrulamayı Authorization header token'ı ile yapmalı.
  ~ Parking entegrasyon literatürü asgari API sözleşmesini 'idempotent POST, webhook, versiyonlu uçlar' olarak tanımlar; ParkPow entegrasyonu da webhook'ta API token'lı authorization header şart koşar.
  ~ kaynak: https://fleximodo.com/glossary/anpr-integration/ ; https://guides.platerecognizer.com/docs/parkpow/integrations/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Kamera kimliği (camera ID) entegrasyon boyunca sabit kalmalı; görünen ad serbestçe değiştirilebilmeli ancak ID değişimi PMS tarafında yeni kamera olarak algılanacağından engellenmelidir.
  ~ ParkPow entegrasyon kılavuzu kamera display adının değiştirilebileceğini ama kamera ID'sinin sabit kalması gerektiğini açıkça belirtir; ID kayması geçmiş kayıtların kamerayla ilişkisini koparır.
  ~ kaynak: https://guides.platerecognizer.com/docs/parkpow/integrations/

## guvenlik-kvkk (17)
- [acik-kaynak] Tanıma tamamen yerel çalışmalı: internet kesintisinde işlev kaybı ve 'eve telefon' lisans kontrolü olmamalı; KVKK için saklama süresi tanımlı olmalı ve dışa paylaşım gerektiğinde plaka bulanıklaştırma/redaksiyon aracı bulunmalı.
  ~ ultimateALPR 'lisans anahtarı ve internet gerekmez, veri cihazdan çıkmaz' modeli; Plate Recognizer abonelik lisansı ayda birkaç kez internete çıkmak zorunda (perpetual istisna) — belediye ortamı için yerel model şart. Pa
  ~ kaynak: https://github.com/DoubangoTelecom/ultimateALPR-SDK ; https://github.com/parkpow/deep-license-plate-recognition
- [acik-kaynak] Kullanılacak açık kaynak bileşenlerin lisansları ticari/kapalı kullanım için doğrulanmalı: MIT (fast-alpr, fast-plate-ocr, open-image-models, Frigate) güvenli; AGPL-3.0 (OpenALPR) ve SSPL (CodeProject.AI-ALPR) bulaşıcı olduğundan kod alınmamalı, yalnızca desen referansı olarak kullanılmalı.
  ~ OpenALPR AGPL-3.0, CodeProject.AI-ALPR SSPL (LICENSE dosyası doğrulandı), ankandrew ekosistemi MIT; belediyenin dağıtacağı kapalı exe için lisans uyumu ön şart.
  ~ kaynak: https://raw.githubusercontent.com/codeproject/CodeProject.AI-ALPR/main/LICENSE ; https://github.com/openalpr/openalpr
- [ticari-otopark] Plaka kişisel veridir (KVKK): saklama süresi politikası tanımlanmalı, veriler şifreli saklanmalı, erişim rol bazlı olmalı, aktarım TLS 1.2+ ile yapılmalı; ücretsiz/bulut servislere plaka verisi gönderilmemeli.
  ~ Türk pazarında PTS satıcıları KVKK uyumunu zorunlu sayar (plaka=kişisel veri, şifreli saklama); Traffix 'tam KVKK uyumu'nu ayırt edici özellik olarak pazarlar; Survision TLS 1.2/1.3 + rol yönetimi + güçlü parola uygular.
  ~ kaynak: https://nemesisalarmsis.com/plaka-tanima-sistemleri/
- [ticari-otopark] Servis kimlik doğrulamalı yönetim arayüzü sunmalı: kullanıcı/rol yönetimi, güçlü parola zorunluluğu, HTTPS; denetim (audit) logu tutulmalı.
  ~ Survision Nanopak 5: user/role management + strong password enforcement + TLS; Hikvision güvenlik paketi (digest auth, security audit log, TLS 1.2) endüstri beklentisini gösterir.
  ~ kaynak: https://myfiles.survision.fr/NANOPAK5%20TOTEM%20DATASHEET_English.pdf
- [tr-pazari] Plaka KVKK kapsamında kişisel veridir: otopark girişinde katmanlı aydınlatma (tabela + tam metin) yapılmalı; ihlalin emsal cezası 75.000 TL (Kurul kararı 2023/924).
  ~ KVKK Kurulu 01/06/2023 tarih 2023/924 sayılı kararla otopark işletmecisine aydınlatma yükümlülüğü ihlalinden 75.000 TL idari para cezası verdi; plaka işleme hukuka uygun bulundu ama aydınlatma şart.
  ~ kaynak: https://www.kvkk.gov.tr/Icerik/7763/2023-924
- [tr-pazari] Plaka işleme hukuki dayanağı açık rıza değil KVKK m.5/2(c) sözleşmenin ifası, m.5/2(e) hak tesisi/korunması ve m.5/2(f) meşru menfaat olarak kurgulanmalı; açık rıza ve aydınlatma metinleri ayrı tutulmalı.
  ~ İSPARK aydınlatma metni m.5/2(c) ve (f) dayanaklarını kullanıyor; Kurul 2023/924'te borç tahsili için plaka işlemesini m.5/2(e) ile hukuka uygun buldu; Kurul 2026/347 ilke kararı metinlerin ayrılmasını istiyor.
  ~ kaynak: https://ispark.istanbul/mobil-uygulama-kisisel-verilerin-islenmesine-iliskin-aydinlatma-metni/
- [tr-pazari] Plaka/görüntü kayıtları için tanımlı ve sınırlı saklama süresi (kamu pratiği 30-45 gün; ücret/borç kayıtları için yasal zamanaşımı süresi) uygulanmalı ve süre dolunca otomatik silme/imha çalışmalı.
  ~ Çevre ve Şehircilik Bakanlığı kamera kayıtlarını 30 gün, Yargıtay 45 gün saklıyor (aydınlatma metinleri); KVKK amaçla sınırlılık ilkesi gereği süresiz saklama ölçüsüz sayılıyor.
  ~ kaynak: https://www.yargitay.gov.tr/documents/kvkk/kameraaydinlatmametni.pdf
- [tr-pazari] Plaka ile borç/kayıt sorgulama ekranı yapılacaksa tek faktörlü (sadece plaka girerek) sorguya izin verilmemeli; ikinci doğrulama faktörü eklenmelidir.
  ~ KVKK Kurulu 2023/924 kararında sadece plaka yazarak sorgu yapılabilen sistemi güvenlik açığı sayıp çift faktörlü doğrulama veya sistemin kapatılmasını talep etti.
  ~ kaynak: https://www.kvkk.gov.tr/Icerik/7763/2023-924
- [tr-pazari] Kolluk/merkezi sistem paylaşımına hazırlıklı olunmalı: AVM'lerde plaka verisinin kolluk birimleriyle anlık paylaşımı zorunlu; belediye otoparkı için zorunlu olmasa da PolNet-uyumlu dışa aktarma arayüzü tasarımda öngörülmeli.
  ~ Alışveriş Merkezleri Hakkında Yönetmelik değişikliği (RG 30.12.2016) AVM otoparklarında PTS kurulumunu ve kollukla anlık veri paylaşımını zorunlu kıldı; ISSD ürünleri EGM PolNet merkez yazılımıyla tam uyumluluğu referans
  ~ kaynak: https://www.aa.com.tr/tr/turkiye/avmlere-plaka-tanima-sistemi/716865
- [tr-pazari] Yazılım kullanıcı adı/şifre ile korunmalı, rol bazlı yetkilendirme sunmalı ve tüm operatör işlemleri (plaka düzeltme dahil) loglanmalıdır.
  ~ Şartname md.1.23 ve 7.10-7.12 (şifre ile farklı yetki seviyeleri); KVKK veri güvenliği tedbirleri (m.12) erişim kontrolü ve kayıt gerektiriyor.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [standartlar] KVKK uyumu: otopark girişlerine aydınlatma metni/tabelası konulmalı (plaka verisinin kim tarafından, hangi amaçla işlendiği); plaka verisine salt plakayla tek adımlı dış sorgu açılacaksa ikinci doğrulama faktörü eklenmeli; ihlal emsali 75.000 TL idari para cezasıdır (KVKK Kurulu 01/06/2023, 2023/924).
  ~ KVKK'nın otopark işletmecisi hakkındaki 2023/924 sayılı kararı: aydınlatma yükümlülüğü ihlaline ceza + plaka-tabanlı sorguya iki faktör şartı; belediye otoparkı doğrudan muhatap.
  ~ kaynak: https://www.kvkk.gov.tr/Icerik/7763/2023-924
- [standartlar] Veri saklama politikası tanımlı ve otomatik uygulanmalı: olay kayıtları için yerel saklama referansı 90 gün, merkezi arşiv için azami 12 ay (NASPLE değerleri); süre sonunda görüntü ve plaka verisi otomatik silinmeli, saklama gerekçesi yazılı olmalı.
  ~ NASPLE saklama süreleri (yerel 90 gün / ulusal 12 ay / üçüncü taraf önbelleği 7 gün) ile KVKK amaçla sınırlılık ilkesi birlikte ölçülebilir bir silme kuralı verir.
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-standards-for-policing-and-law-enforcement-accessible-version
- [kosullar] Her geçiş kaydı (plaka, zaman, kanıt görüntüsü) tanımlı saklama süresiyle sınırlandırılmalı, erişim yetkilendirilmeli ve loglanmalıdır (KVKK: plaka kişisel veridir).
  ~ Plaka verisi kişiyi belirlenebilir kılar; NASPLE bile polis kullanımında saklama ve erişim standartları tanımlar — belediye otoparkında KVKK gereği amaçla sınırlı saklama ve erişim kontrolü zorunludur.
  ~ kaynak: https://assets.publishing.service.gov.uk/media/69c25599d588c92c483e4c1f/NASPLE.v3.4.pdf
- [entegrasyon-operasyon] KVKK uyumu: plaka kişisel veridir; otopark girişinde aydınlatma metni/tabela zorunlu, veri sorumlusu-işleme amacı-aktarım bilgisi açıkça verilmeli; plaka üzerinden kayıt/borç sorgusu tek kademeli doğrulamayla yapılamamalı.
  ~ KVKK Kurulu 2023/924 kararında otopark işletmecisine aydınlatma ihlali ve 'sadece plaka yazarak borç sorgulama' (yetersiz veri güvenliği) nedeniyle 75.000 TL idari ceza kesmiştir; belediye işletmesi için doğrudan emsal.
  ~ kaynak: https://www.kvkk.gov.tr/Icerik/7763/2023-924
- [entegrasyon-operasyon] Veri saklama ve imha: geçiş görüntüleri en az 30 gün (gerekirse 60 güne uzatılabilir) saklanmalı, saklama süresi dolan kayıtlar otomatik silinmeli; veriler at-rest AES ile şifrelenmeli, iletim HTTPS olmalı; kim-neyi-düzeltti denetim izi tutulmalı.
  ~ Türk LPR rehberi 30 gün (60'a uzatılabilir) kayıt, AES+HTTPS ve KVKK tabelasını somut gereklilik olarak verir; KVKK saklama/imha politikası ve süre sonunda otomatik imha yükümlülüğü getirir.
  ~ kaynak: https://www.ertelbilisim.com.tr/otopark-kamera-lpr-raporlama/
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Push kimlik doğrulaması netleştirilmeli: HttpHostNotification şeması httpAuthenticationMethod={MD5digest, none} + userName/password destekler; mevcut alıcı MD5digest istiyorsa emülatörün HTTP istemcisi RFC 2617 digest (401→Authorization, nonce sayacı) uygulamalı; alıcı none ise sahte olay enjeksiyonuna açık olduğu risk raporuna yazılmalı.
  ~ Resmi ISAPI şeması push auth'u opsiyonel tanımlar; doğrulamasız alıcı, ağdaki herhangi bir cihazın bariyer açtırabilmesi demektir (belediye otoparkı için kabul edilemez).
  ~ kaynak: https://tpp.hikvision.com/Wiki/ISAPI/Access%20Control%20on%20Person/GUID-620FC470-9BC6-4A41-92CD-E3454FD46E78.html
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] KVKK/GDPR uyumu: plaka+resim kişisel veri sayılmalı, saklama süresi yapılandırılabilir ve sınırlı olmalı (emsal: Rekor Scout 60 gün), erişim rol bazlı olmalı (Viewer/Operator/Executive benzeri) ve plaka arama/düzeltme işlemleri denetim loguna yazılmalı.
  ~ Autopay seçim kriterleri GDPR/yerel mevzuat uyumu ve ISO 27001'i şart koşar; ParkPow rol yönetimi ve 'search logs (audit)' sunar; Rekor Scout 60 günlük veri saklama ile emsal oluşturur — belediye verisi için KVKK karşılı
  ~ kaynak: https://solutions.autopay.io/latest-news/what-to-consider-when-choosing-an-anpr-based-parking-management-system ; https://platerecognizer.com/parkpow/

## izleme (22)
- [acik-kaynak] Sistem kendi sağlığını puanlamalı: işlenen/kaçırılan kare oranı ve işleme gecikmesinden bir sağlık skoru üretilmeli, skor %70'in altına düşünce uyarı verilmeli; ayrıca saha ayarı için tespit edilen plaka bölgelerini diske kaydeden debug modu bulunmalı.
  ~ Plate Recognizer Health Score <%70 = 'makine yetersiz, plaka kaçırma riski' eşiği; Frigate debug_save_plates ile ara görüntü kaydı saha tuning'inin standart aracı.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/help/faq/ ; https://docs.frigate.video/configuration/license_plate_recognition/
- [ticari-otopark] Kurulum doğrulama ve sürekli performans izleme aracı olmalı: günlük okuma/yakalama oranı raporu, oran düşünce uyarı; sağlık olayları (disk dolu/hata, ağ kopması, kamera erişilemez, odak bozulması) alarm üretmeli.
  ~ Survision 'Guard' aracı kurulumu doğrulayıp zaman içinde performansı izler; Dahua defocus/kart hatası/ağ kopması/IP çakışması alarmları tanımlar. Kış koşullarında sessiz performans erimesini erken yakalamak için kritik.
  ~ kaynak: https://myfiles.survision.fr/NANOPAK5%20TOTEM%20DATASHEET_English.pdf
- [tr-pazari] Doluluk/kapasite anlık izlenebilmeli ve giriş-çıkış istatistikleri (plaka bazında hasılat dahil) raporlanıp Excel'e aktarılabilmeli.
  ~ Şartname md.1.34/1.36 (Excel içe-dışa aktarım, hasılat raporu); Perkotek ve Hikvision otopark yazılımları doluluk oranı göstermeyi standart özellik yapmış.
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr
- [standartlar] Performans raporunda üç metrik ayrı ayrı verilmeli: capture rate = (log'a düşen plaka / geçen görünür plaka)×100, read rate = (doğru okunan / yakalanan)×100 ve uçtan uca oran = capture × read; tek başına read rate ile başarı beyan edilmemeli.
  ~ Home Office formülleri + sektör eleştirisi: tespit oranı feda edilerek okuma oranı yapay şişirilebilir; uçtan uca oran iki oranın çarpımıdır.
  ~ kaynak: https://www.linkedin.com/pulse/commentary-automatic-number-plate-recognition-claims-jannink
- [standartlar] Sürekli sağlık izleme: cihaz başına günlük okuma sayısı (24 saatlik hafta içi baseline'a göre) izlenmeli; beklenmedik düşüşte otomatik alarm üretilmeli (olası nedenler: odak kayması, kamera açısı oynaması, lens kirlenmesi, IR arızası).
  ~ Home Office Prosedür 4 (performance monitoring) + bölüm 5 bakım listesi; bizim servis Windows'ta sürekli çalışacağı için self-diagnostic sayaç şart.
  ~ kaynak: https://assets.publishing.service.gov.uk/government/uploads/system/uploads/attachment_data/file/909024/ANPR_-_Evaluation_Approved_Version_2.0.pdf
- [standartlar] Her kaçırılan/yanlış okunan plaka için neden taksonomisi kaydedilmeli (araç görüş alanı dışında, çamur/kar, delaminasyon, vida/aksesuar, illegal font, yabancı plaka, IR-görünür görüntü farkı, yansıtıcılık bozulması vb.) ve testlerde bu form doldurulmalı.
  ~ Home Office Appendix A hata nedeni formu; hem iyileştirme önceliklerini belirler hem de 'okunamaz plaka' ile 'yazılım hatası'nı ayırır.
  ~ kaynak: https://assets.publishing.service.gov.uk/government/uploads/system/uploads/attachment_data/file/909024/ANPR_-_Evaluation_Approved_Version_2.0.pdf
- [kosullar] Kamera sağlık izleme: görüntü ortalama parlaklık/keskinlik metrikleri sürekli izlenmeli; lens kar/buz/kir kapanması veya IR arızası şüphesinde 5 dk içinde operatöre alarm üretilmelidir.
  ~ Survision kışın lens buzlanmasını en kritik arıza modu sayıp 7/24 izleme aracı (Survision Guard) satıyor; Erzurum'da lens kapanması sessiz toplu başarısızlığın 1 numaralı nedeni olacaktır.
  ~ kaynak: https://survisiongroup.com/post-license-plate-recognition-in-snow:-tackling-winter-challenges
- [kosullar] Okuma oranı telemetrisi: günlük yakalama oranı, okuma oranı, ortalama güven skoru ve 'okunamadı' oranı loglanmalı; 24 saatlik okuma oranı %90'ın altına düşerse otomatik uyarı verilmelidir.
  ~ NASPLE uyumluluğu periyodik performans denetimiyle ölçülür; Plate Recognizer 'Health Score ≥75%' benzeri sürekli metrik önerir — kış aylarında sessiz bozulmayı erken yakalamak için şart.
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-standards-for-policing-and-law-enforcement-accessible-version ve http
- [entegrasyon-operasyon] Periyodik heartbeat/sağlık olayı yayınlanmalı (ayrı event tipi); kamera/RTSP bağlantı kopması, FPS düşüşü ve disk doluluğu için alarm üretilmeli.
  ~ Plate Recognizer Stream webhook'unda 'heartbeat' ayrı bir olay tipidir (izleme API'siyle aynı istatistikler); otopark yazılımı ANPR'ın canlı olduğunu bilmeden bariyer kararına güvenemez.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/results/
- [entegrasyon-operasyon] Watchdog + otomatik yeniden başlatma: servis Windows SCM recovery ile 1. hatada 1 dk, 2. hatada 2 dk sonra yeniden başlatılmalı, hata sayacı 300 sn'de sıfırlanmalı; donma (heartbeat kesilmesi) durumunda süreç yeniden başlatılmalı, 7/24 insansız çalışma hedeflenmeli.
  ~ Microsoft servis kılavuzu kritik servisler için bu recovery değerlerini önerir; IP watchdog pratiği 'heartbeat kesilirse otomatik restart ile müdahalesiz 7/24 uptime' der.
  ~ kaynak: https://learn.microsoft.com/en-us/windows/win32/rstmgr/guidelines-for-services
- [entegrasyon-operasyon] Yazılım Windows servisi olarak (otomatik/gecikmeli başlatma, oturumdan bağımsız) çalışmalı; güncelleme dağıtımı yapılandırmayı ve yerel kuyruğu korumalı, başarısız güncellemede önceki sürüme dönüş mümkün olmalı.
  ~ Autopay donanım-yazılım ayrımının 'kolay optimizasyon ve güncelleme' sağladığını seçim kriteri sayar; SCM recovery ancak gerçek Windows servisi için çalışır — konsol exe + kullanıcı oturumu kabul edilemez.
  ~ kaynak: https://solutions.autopay.io/latest-news/what-to-consider-when-choosing-an-anpr-based-parking-management-system
- [zayif-cpu] Servis; islenen eFPS, atlanan kare sayisi, kare basina ms (p50/p95), CPU% ve RAM metriklerini surekli yayinlamali; paylasimli ofis PC'sinde ortalama CPU < %50 ve tepe < %80 hedeflenmeli (ANPR thread'leri birakilirsa CPU'yu %100 doyurur).
  ~ Rekor Scout CPU'yu %100'e kadar kullanacak sekilde olceklenir; ofis PC'si baska isler de yaptigi icin fps ornekleme + thread siniri ile headroom birakildigi metriklerle kanitlanmali.
  ~ kaynak: https://docs.rekor.ai/scout/agent/system-requirements
- [zayif-cpu] Kabul testi benchmark'i 'pozitif orani' metodolojisiyle yapilmali: ayni video setinde %0 / %50 / %100 plakali kare senaryolarinda fps ve ms/kare olculmeli; boylece hareket-tetikli mimarinin gercek kazanci belgelenir.
  ~ ultimateALPR resmi benchmark'i her cihazi 0/20/50/70/100% pozitif oranlarinda ayri olcer (orn. RPi4 12,21 -> 7,06 fps); bizim otoparkta karelerin buyuk cogunlugu plakasizdir, dolayisiyla gercekci kapasite ancak bu metodo
  ~ kaynak: https://www.doubango.org/SDKs/anpr/docs/Benchmark.html
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Her olay için benzersiz UUID üretilmeli; aynı olayın tekrar gönderiminde (alıcı 200 dönmezse) aynı UUID korunup activePostCount artırılmalı; gönderim başarısızlığında olaylar diske journal edilip en az 3 deneme/üstel bekleme ile tekrar denenmeli — hiçbir geçiş olayı kaybolmamalı.
  ~ Resmi örnek payload'larda UUID ve activePostCount alanları mevcut; kamera davranışının (retry semantiği) taklidi, alıcı tarafta deduplikasyonun bozulmamasını sağlar.
  ~ kaynak: Fast guide TCG s.12 + 7-series 4.3 örnek XML
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Kesin geçişten önce en az 1 hafta gölge (paralel) çalışma yapılmalı: kamera yerleşik ANPR'ı ile yerel CPU ANPR aynı geçişleri eşzamanlı loglamalı, plaka bazında eşleşme oranı ve fark listesi (kaçan/yanlış okunan) günlük raporlanmalı; cutover kriteri: yerel sistemin kamera referansına göre eşleşme oranı ≥%98.
  ~ Veri sayfası çıtası (%98) ancak aynı trafikte yan yana ölçümle kanıtlanır; gölge mod, ISAPI push'un iki hedefe (mevcut yazılım + bizim kayıt servisi) aynı anda gönderilebilmesi sayesinde ek donanımsız kurulabilir (httpHo
  ~ kaynak: DS-TCG227 datasheet + /ISAPI/Event/notification/httpHosts çoklu host yapısı (Hikvision ISAPI)
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Kabul testi olarak hedef PC'de fiili cam→karar (glass-to-decision) gecikmesi ölçülmeli: ms çözünürlüklü LED/kronometre görüntüsü veya kamera OSD zaman damgası yöntemiyle en az 100 örneklik dağılım (medyan + p95) raporlanmalı; yöntem hatası ≤1 kare aralığı (25 fps'te 40 ms) kabul edilmeli.
  ~ Axis'in timestamp-overlay yöntemi (kamera kendi ekran çıktısını filme alır) 1 kare aralığına kadar hatayla ölçüm verir; Vay/RidgeRun LED+fotodiyot yöntemi µs hassasiyet sağlar ve dağılım grafiği üretir. Tek anlık ölçüm d
  ~ kaynak: https://medium.com/vay-technology/how-to-measure-glass-to-glass-video-latency-13256037e552
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Çalışma anında kare yaşı (frame age) sürekli izlenmeli — tercihen RTCP Sender Report NTP/RTP çiftinden (kamera + PC ortak NTP kaynağına senkron, LAN'da ±3-5 ms hassasiyet) — ve kare yaşı 1 sn eşiğini 5 sn boyunca aşarsa stream otomatik reset edilip olay loglanmalı.
  ~ IPVM'de tampon birikmesiyle 2 ayda 10 sn'ye büyüyen NVR gecikmesi vakası raporlandı; gecikme statik değil zamanla büyüyen bir arızadır. OpenIPC wiki RTCP SR ile kare-başına yakalama zamanının programatik alınabildiğini v
  ~ kaynak: https://github.com/OpenIPC/wiki/blob/master/en/timestamps-and-time-sync.md
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Tetik bölgesinde X sn (varsayılan 10 sn) boyunca plaka kararı üretilemeyen bir araç bekliyorsa 'okunamadı/araç bekliyor' olayı üretilmeli ki operatör/interkom devreye girebilsin; bu olay tailgating penceresini de kapatmalı (bariyer manuel açıldıysa geçen araçlar sayılmaya devam etmeli).
  ~ Otopark ANPR çözümlerinde okunamayan plaka için manuel giriş/interkom/operatör override standart fallback'tir (Parklio/Park+ karşılaştırmaları); Survision presence yaşam döngüsü (new→end) bekleyen aracın varlığını zaten 
  ~ kaynak: https://parklio.com/en/blog/top-10-parking-license-plate-recognition-anpr-solutions-for-2025-compare-before-you-invest ; https://fr.survisiongroup.com
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Sağlık izleme ve alarm: ANPR servisi kamera RTSP/ISAPI bağlantısı, tanıma boru hattı ve bariyer IO için heartbeat üretecek; kamera offline, servis down veya bariyer durum sinyali tutarsız olduğunda operatöre en geç 5 dakika içinde bildirim (SMS/e-posta/panel) gidecek ve tüm arıza/kurtarma olayları zaman damgalı loglanacak.
  ~ SLA/uptime izleme pratiği (PRTG, IBM SLA metrikleri): uptime ve gerçek zamanlı sapma alarmı temel bileşendir; ANPR ürünleri de gate açma/alarm tetikleme olaylarını gerçek zamanlı bildirir. Sessiz arıza (kimse fark etmede
  ~ kaynak: https://www.paessler.com/monitoring/network/sla-monitoring
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Devreye almadan önce 1-2 haftalık pilotta şerit bazında günlük motosiklet geçişi ve çift satır (kare arka) plaka geçiş oranı sayılmalı; kabul eşiği ve fallback kapasitesi bu ölçüme göre belirlenip rapor edilmeli.
  ~ Hacim yerel ve mevsimsel: TR filosunda motosiklet %20,8 (Tem 2025, 6,8M adet) ama Erzurum'da ~9.522 motosiklet/~156 bin araç (≈%6) ve yıllık +%26 artış; kışın düşük yazın pik — genel istatistik saha yerine geçemez.
  ~ kaynak: https://www.haberler.com/ekonomi/erzurum-da-arac-sayisi-156-bini-asti-motosiklette-yuzde-26-artis-19971989-haberi/
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Okunamayan/plakasız her geçiş görüntüsüyle arşivlenmeli ve günlük 'okunamadı' raporu (adet, saat, olası araç tipi) üretilmeli; motosiklet ve çift satır kaynaklı hatalar ayrı etiketlenmeli.
  ~ Plate Recognizer pratiği: okunamayan plakalar manuel inceleme kuyruğuna alınır; ikame edilen Hikvision'a göre gerileme olup olmadığı ancak bu logla kanıtlanabilir.
  ~ kaynak: https://parkingpaymentguide.com/technology/lpr-based-parking-payments/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Servis periyodik heartbeat/sağlık webhook'u göndermeli (varsayılan 30 sn: kamera bağlantı durumu, işlenen kare, kuyruk uzunluğu) ve RTSP koptuğunda artan aralıklı otomatik yeniden bağlanmalı (tavan 600 sn).
  ~ Stream'in heartbeat (30 sn) ve max_reconnection_delay (600 sn) parametreleri motor katmanının sağlık bildirimi sorumluluğunu tanımlar; karlı Erzurum kışında kamera/ağ kopmaları olağandır ve sessiz ölüm kabul edilemez.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/

## kosullar (40)
- [acik-kaynak] Gece/IR (siyah-beyaz) modunda ayrı profil olmalı: tanıma eşiği gece için düşürülebilmeli, 0-10 kademeli görüntü iyileştirme (kontrast/parlaklık) uygulanabilmeli ve iyileştirme CPU'da <10 ms sürmeli.
  ~ Frigate gece için recognition_threshold düşürme + enhancement 0-10 ayarı öneriyor; ultimateALPR IENV gece iyileştirmesi 720p'de ~2 ms/500 fps ile bunun CPU'da ucuz yapılabildiğini kanıtlıyor. Erzurum kışı + IR filtre geç
  ~ kaynak: https://www.doubango.org/SDKs/anpr/docs/Features.html ; https://docs.frigate.video/configuration/license_plate_recognition/
- [acik-kaynak] Kamera konumlandırma ölçülebilir doğrulanmalı: plaka görüntüde hedef ~100 px genişlikte (mutlak alt sınır ~30 px, Frigate karşılığı min_area ≥1000 px²), yatay ve dikey açı <45°; kurulum sırasında bu değerleri ölçen bir kalibrasyon/önizleme aracı sunulmalı.
  ~ Plate Recognizer kamera kılavuzu (100 px tasarım hedefi, 30 px alt sınır, <45° açı) ve Frigate min_area=1000 px² varsayılanı; 'gözle okunamayan plakayı motor da okuyamaz' ilkesi tüm ürünlerde ortak.
  ~ kaynak: https://platerecognizer.com/lpr-camera-resolution-guide/ ; https://platerecognizer.com/camera-setup-for-best-anpr/
- [acik-kaynak] Düşük güvenli/okunamayan durum (karlı-kirli plaka) birinci sınıf sonuç olmalı: sistem 'bilinmeyen/düşük güven' olayı üretip kanıt görüntüsüyle operatöre manuel açma akışı sunmalı; bu olaylar sonradan model iyileştirme için etiketlenebilmeli.
  ~ Plate Recognizer'ın temel varsayımı plakanın gözle seçilebilmesi; kar/çamurla bu bozulduğunda otoparkın kilitlenmemesi için insan-fallback şart. Frigate'in unknown plakayı recognized_license_plate alanında yine de yayınl
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/help/faq/ ; https://docs.frigate.video/configuration/license_plate_recognition/
- [ticari-otopark] Gece 0 lux'te 850 nm IR aydınlatma ile çalışılmalı; IR menzili araç-kamera mesafesiyle eşleşmeli (10 m mesafe için ≥10 m IR; tele lens için 20 m) ve siyah-beyaz IR görüntüde okuma tam performansta olmalı.
  ~ Hikvision/Dahua ANPR aydınlatma menzilini 10 m (2.8-12mm) / 20 m (8-32mm) verir; Genetec SharpV 0 lux için darbeli LED (940/850/740/590 nm) ve monokrom global shutter ALPR sensörü kullanır; PlateRecognizer IR mesafe eşle
  ~ kaynak: https://platerecognizer.com/camera-setup-for-best-anpr/
- [ticari-otopark] Görüntüde plaka genişliği AB tipi plaka için ≥170 px (duran araç) / ≥280 px (hareketli, interlaced) veya ≥2.7 px/karakter-çizgi-kalınlığı olmalı; motorumuz için pratik alt sınır 80-100 px genişlik.
  ~ Milestone XProtect LPR resmi gereksinimi: 2.7 px/stroke, AB plakası min 170 px. PlateRecognizer 'en az 80 px, ideali 100 px' der (motor 30 px'e kadar okuyabilir ama güvenilmez).
  ~ kaynak: https://doc.milestonesys.com/2021r2/en-US/add-ons/add-on_lpr/lpr_platewidthrecommendat.htm
- [ticari-otopark] Kamera açısı dikey ve yatayda ≤45° tutulmalı; yazılım 53° (dikey) / 70° (yatay) sapmaya kadar bozulmadan okuyabilmeli.
  ~ PlateRecognizer kurulum kılavuzu: kurulumda 45° sınır, motor 53°/70°'ye kadar destekler, 75° üstü okunamaz. Quercus 'zor açılarda okuma' için motorlu lens kullanır.
  ~ kaynak: https://platerecognizer.com/camera-setup-for-best-anpr/
- [ticari-otopark] Otopark girişinde enstantane 1/300-1/600 s aralığında sabitlenmeli; AGC, DNR, autofocus ve BLC kapatılmalı; hedef araç hızı ≤30 km/h için 1/600 yeterli.
  ~ PlateRecognizer: duran araç 1/300, <50 km/h 1/600; AGC/DNR/BLC plaka üstünde yapay bozulma yaratır. Otopark senaryosunda yüksek hız gerekmez (Dahua erişim modeli 80 km/h ile sınırlı).
  ~ kaynak: https://platerecognizer.com/camera-setup-for-best-anpr/
- [ticari-otopark] Sistem kar ve parlak güneş için ayrı görüntü profili (pozlama/kazanç seti) desteklemeli; WDR ≥140 dB kamera tercih edilmeli; Erzurum kışı için donanım -30°C'de çalışabilmeli.
  ~ Milestone dokümantasyonu 'kar ve çok parlak güneş özel kamera yapılandırması gerektirir' der; Hikvision/Dahua 140 dB WDR ve giriş-çıkışa özel pozlama algoritması kullanır; çalışma aralıkları Dahua -30..+65°C, Hikvision -
  ~ kaynak: https://www.mobotix.com/sites/default/files/2022-04/2022-R1_Milestone_XProtect_LPR_Administrator_Manual_en_20220407.pdf
- [ticari-otopark] Kar fırtınası/okumama senaryosu için mimari yedek: mümkünse ön+arka (giriş yönü + çıkış yönü) iki görüş açısı veya ikinci deneme mantığı; okunamayan geçişler operatöre elle düzeltme kuyruğuna düşmeli.
  ~ SKIDATA kayak merkezi otoparklarında 'LPR twin cameras for front and rear imaging' ile donma ve kar fırtınasında plakayı yakalar; tek açı yetersiz kaldığında ikinci açı kurtarır.
  ~ kaynak: https://www.skidata.com/en-us/skidata-blog/double-your-guests-convenience-with-parking-management-for-ski-resorts
- [tr-pazari] Gece-gündüz ve farklı iklim koşullarında (kar, yağmur, sis) aynı okuma başarısını korumalı; gündüz renkli / gece siyah-beyaz (IR/ICR) görüntülerin her ikisinde de çalışmalı.
  ~ Şartname md.1.11 ve md.1.5 açık şart; sektör yazıları kar/yağışta oranın düştüğünü, IR aydınlatma+WDR ile telafi edildiğini belirtiyor — Erzurum kışı için birincil risk.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [tr-pazari] Kamera kurulumu: plaka-kamera açısı 30 derecenin altında, 850 nm IR aydınlatma, WDR, min. 2 MP çözünürlük, 30 m'ye kadar gece görüş; dış ünite -10..+60 °C ve ısıtıcılı/fanlı muhafaza (Erzurum için daha düşük sıcaklık toleransı seçilmeli).
  ~ Buhur şartnamesi (4MP, 30 m IR), Uludağ şartnamesi md.2.15/2.18 (-10..+60 °C, ısıtıcılı muhafaza, 850 nm 6xPOWER LED spot), HDS/sektör kaynakları <30° açı kuralını veriyor; kar yansıması ve -30 °C Erzurum kışı için ısıtı
  ~ kaynak: https://www.buhur.com.tr/plaka-tanima-sistemleri-teknik-sartnamesi/
- [standartlar] Kabul testi asgari gündüz + gece (IR/monokrom mod) koşullarında ayrı ayrı koşulmalı; imkan varsa alacakaranlık (şafak/gün batımı), kapalı hava ve KAR yağışı/karla kaplı plaka senaryoları da ayrı test kümeleri olarak eklenmeli.
  ~ Home Office 4.1.1 minimum gündüz+gece ister, dusk/dawn ve bulutlu koşulları şiddetle önerir; Erzurum kışı için kar zorunlu ek senaryodur (JIST çalışmasında kar bozulmasında ALPR doğruluğu sıfıra yaklaşabiliyor).
  ~ kaynak: https://library.imaging.org/jist/articles/68/6/060401
- [standartlar] Görüntüde plaka çözünürlüğü asgari eşiği doğrulanmalı: plaka genişliği ~120-150 px (veya karakter genişliği 100-200 px @2MP, karakter yüksekliği ≥20-30 px); saha kurulumunda IEC 62676-4 piksel yoğunluğu 250 px/m (2025 revizyonu: zorlu ışık/hareket için 500 px/m) sağlanmalı ve kurulum aracı bu değeri ölçüp raporlamalı.
  ~ IEC 62676-4:2014 DORI 'Identification'=250 px/m plaka okuma eşiğidir; IEC 62676-4:2025 (OODPCVS) bunu 500 px/m'ye ('Validate') yükseltti; Hikvision ve Survision pratik piksel önerileri aynı bandı doğrular.
  ~ kaynak: https://www.hikvision.com/en/newsroom/blog/understanding-oodpcvs--the-new-image-detail-classification-frame1/
- [standartlar] Kamera-plaka geometrisi sınır testleri: yatay bakış açısı 30°'yi aşan kurulumlarda performans ayrıca ölçülmeli (referans: iyi motorlar ~70°'ye kadar okuyup 75°'de düşer, görüntü rotasyonu ~53°'ye kadar tolere edilir); motorumuzun açı/rotasyon zarfı ölçülüp dokümante edilmeli.
  ~ Plate Recognizer'ın açı iskeleti (protraktörle ölçülmüş 70°/75° sınırı, 53° rotasyon) ürünler için ölçülebilir zarf tanımlama örneğidir; kurulum kılavuzumuza sınır koymak için gerekli.
  ~ kaynak: https://platerecognizer.com/alpr-results/
- [standartlar] Kar/kış dayanımı önlemleri: IR aydınlatmadan yararlanma, görüntü denoising/kontrast iyileştirme, karla kısmen kapalı plakalar için eğitim/test örnekleri ve mercek karlanması için ısıtmalı muhafaza + periyodik lens temizliği planı; kış modunda ayrı doğruluk ölçümü yapılmalı (referans saha testi: İzlanda'da çift kamerayla %99, ancak kayıtların %15,9'unda en az bir plaka kaçtı).
  ~ Survision kış raporu kar için somut riskler (plakada kar birikimi, parlama, lens buzlanması) ve önlemler verir; Home Office kılavuzu da kış tuzlamasının lensi kirlettiğini ve temizlik sıklığının artırılmasını söyler.
  ~ kaynak: https://survisiongroup.com/post-license-plate-recognition-in-snow:-tackling-winter-challenges
- [standartlar] Test kapsam kuralı: 'insan gözüyle görülebilirlik' kamera bakış noktasından ve o anki hava koşulları dahil değerlendirilmeli; insan gözünün kameradan okuyamadığı plakalar (tam kar örtüsü vb.) payda dışı bırakılmalı ama ayrı sayaçta raporlanmalı.
  ~ NASPLE'nin 'Visible to the Human eye' tanımı: kameranın gördüğü noktadan insan okuyabiliyorsa sistem de okumalı; okunamaz plakaları paydadan çıkarmak adil ölçümün ön şartı.
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-standards-for-policing-and-law-enforcement-accessible-version
- [standartlar] Kamera görüntü kalitesi doğrulaması IEC 62676-5 (2018) / 62676-5-1 (2024) ölçüm kavramlarına göre yapılmalı: minimum aydınlanma, dinamik aralık, IR aydınlatma menzili ve çevresel koşullarda görüntü kalitesi; Hikvision akışını devraldığımız için kurulumda bu parametreler sahada kontrol listesine girmeli.
  ~ IEC 62676-5 kamera görüntü kalitesi performansının standart ölçüm yöntemlerini, 62676-5-1:2024 ise çevresel test yöntemlerini tanımlar (EN 50132'nin yerini EN 62676 serisi aldı).
  ~ kaynak: https://webstore.iec.ch/en/publication/62442
- [kosullar] Karakter-pozisyon bazlı çoğunluk oylaması (multi-frame voting) ile aynı araca ait ardışık kareler birleştirilmeli; tek kareye asla güvenilmemelidir.
  ~ Video akışında temporal çoğunluk oylaması göreli doğruluğu %40 artırıyor (Laroca et al.); optik-akış tabanlı çok-kare restorasyon (MF-LPR²) karakter doğruluğunu 74.70%'ten 86.44%'e (+11.74 puan) çıkarıyor. Mevcut V2+voti
  ~ kaynak: https://arxiv.org/abs/1802.09567 ve https://www.sciencedirect.com/science/article/abs/pii/S1077314225000840
- [kosullar] Gece/IR (siyah-beyaz) modunda çalışma zorunludur; iyi ayarlanmış kamera ile gece uçtan-uca doğruluk hedefi ≥%90-94 olmalı, ayarlanmamış gece modunda %50-70'e düşüş kabul edilen risk olarak belgelenmelidir.
  ~ placa.ai: gündüz %95-99 okuyan standart IP kamera, auto-IR gece modunda %50-70'e düşüyor; akademik VLM çalışması 889 gece görüntüsünde %94.60 uçtan-uca doğruluk gösteriyor — yani gece %90+ ulaşılabilir ama kamera ayarına
  ~ kaynak: https://www.placa.ai/ip-camera-missing-license-plates-at-night/ ve https://arxiv.org/pdf/2510.10533
- [kosullar] Kurulumda tek seferlik kamera yapılandırması yapılmalı: IR şiddeti manuel %40-60 (3-6 m bariyer mesafesi için), minimum enstantane 1/500 (bariyer hızı) - 1/1000, gain 9-21 dB aralığında sınırlandırılmalı; bu bir yazılım-dışı ÖN KOŞUL olarak dokümante edilmelidir.
  ~ Gece başarısızlıklarının %80+'ı IR aşırı pozlamasından (retroreflektif plaka bembeyaz) kaynaklanıyor ve YAZILIMLA DÜZELTİLEMEZ; Axis: yansıtıcı plakalar için max gain 9-21 dB; placa.ai: IR %40-60 + min shutter 1/500 gate
  ~ kaynak: https://www.placa.ai/ip-camera-missing-license-plates-at-night/ ve https://whitepapers.axis.com/en-us/license-plate-capture
- [kosullar] Sistem 'okunamaz plaka' durumlarını (IR patlaması=bembeyaz dikdörtgen, kar örtüsü, aşırı bulanıklık) tespit edip 60 sn içinde ayrı bir olay tipi olarak raporlamalı; bariyer için manuel açma/interkom fallback akışı tanımlı olmalıdır.
  ~ Plate Recognizer: 'plaka insan gözüyle okunamıyorsa sistem de okuyamaz' — karla tamamen kaplı plaka hiçbir yazılımla çözülemez; Survision İzlanda kış testinde plakaların %4.5'i don/hasar nedeniyle okunamadı. Erzurum kışı
  ~ kaynak: https://platerecognizer.com/will-my-license-plate-recognition-system-work-in-snow/ ve https://survisiongroup.com/post-license-plate-recognition-in-sno
- [kosullar] Kar yağışı ve sis için ön-işleme hattı bulunmalı: kar tanesi/gürültü giderme (denoising) + kontrast iyileştirme (ör. CLAHE) + kare-arası tutarlılık (trajectory) doğrulaması.
  ~ Survision kış çözümünde yazılım katmanı tam olarak bunları kullanıyor (image denoising, contrast enhancement, trajectory verification) ve İzlanda'da %99 genel performans raporluyor; akademik testte kar/don distorsiyonu b
  ~ kaynak: https://survisiongroup.com/post-license-plate-recognition-in-snow:-tackling-winter-challenges ve https://library.imaging.org/jist/articles/68/6/060401
- [kosullar] Her kare için kalite skoru (bulanıklık ölçümü + pozlama histogramı) hesaplanmalı; skoru eşik altındaki kareler OCR'a sokulmadan elenmeli ve araç ROI'de kaldığı sürece 0.5-2 sn'lik yeniden deneme (retry) penceresi işletilmelidir.
  ~ Adaptive Recognition 'frame parity flashing' ile farklı pozlamalı karelerden en iyisini seçiyor; kameraya müdahale edemeyen salt-yazılım sistemde eşdeğeri kare seçimidir. Ubicept: far kaynaklı değişken aydınlatma tek kar
  ~ kaynak: https://adaptiverecognition.com/blog/traffic-transportation/illumination-for-anpr/ ve https://www.ubicept.com/blog/license-plate-recognition-in-low-li
- [kosullar] Plaka görüntüdeki genişliği ≥100 piksel olacak şekilde kamera FOV/zoom kurulumu doğrulanmalı; 74-80 px mutlak alt sınırın altındaki plakalar 'düşük çözünürlük' olarak işaretlenmelidir.
  ~ Plate Recognizer: ~100 px genişlik önerisi, 80 px alt sınır; Axis: Avrupa plakası için tam kontrastta minimum 74 px, pratik 100-150 px. Bu sağlanmazsa hiçbir OCR motoru güvenilir çalışmaz.
  ~ kaynak: https://platerecognizer.com/camera-setup-for-best-anpr/ ve https://whitepapers.axis.com/en-us/license-plate-capture
- [kosullar] Eğik/açılı plakalar için perspektif düzeltme (rektifikasyon) uygulanmalı; dikey/yatay ≤45° açıya kadar okuma desteklenmeli, toplam kamera-yol açısının ≤30° olduğu kurulum tercih edilmelidir.
  ~ Plate Recognizer 45° (dikeyde 53°, yatayda 70°'ye kadar destek) sınırı verir, 75°+ okunamaz; Axis toplam açı <30° önerir. Kar yığını nedeniyle şeride açılı yaklaşan araçlar Erzurum'da yaygın senaryodur.
  ~ kaynak: https://platerecognizer.com/camera-setup-for-best-anpr/ ve https://whitepapers.axis.com/en-us/license-plate-capture
- [kosullar] Otopark hızlarında (≤30 km/h) hareket bulanıklığı bütçesi: pozlama süresi ≤9.7 ms (10° açı) olan kamera ayarı yeterlidir; yazılım Laplacian-varyans türü bulanıklık ölçümüyle bulanık kareleri atmalıdır.
  ~ Axis tablosu: 30 km/h + 10° açıda max 9.7 ms pozlama; Plate Recognizer: park senaryosunda 1/300-1/600 enstantane yeterli. Bariyer önünde yavaşlayan araçta bulanıklık düşük risk, ama durmadan geçen araç için kare eleme şa
  ~ kaynak: https://whitepapers.axis.com/en-us/license-plate-capture ve https://platerecognizer.com/camera-setup-for-best-anpr/
- [kosullar] Düşük çözünürlüklü/bulanık plaka kırpıntıları için opsiyonel süper-çözünürlük + çok-görüntü füzyon modülü değerlendirilmeli, ancak gerçek-zamanlı yol üzerinde değil, yalnızca 'okunamadı' kuyruğunda ikinci-şans olarak çalıştırılmalıdır.
  ~ SR + 5-kare füzyonu tanıma oranını %1.7'den %44.7'ye, deblur+SR OCR'ı %12'den %62'ye çıkarabiliyor; ancak GPU'suz zayıf CPU'da gerçek zamanlı maliyeti yüksek — asenkron ikinci-şans hattı doğru mimari.
  ~ kaynak: https://arxiv.org/html/2505.06393v1 ve https://pmc.ncbi.nlm.nih.gov/articles/PMC8605205/
- [kosullar] Far parlaması ve gölge/parlama geçişlerinde sistem aynı araca ait sonraki kareleri otomatik denemeli; B/W IR modunda renk bilgisi olmadığı kabul edilip OCR hattı tek-kanal (gri) görüntüyle tam performans vermelidir.
  ~ Axis: IR-pass filtre LED farlarını bloklar (donanım avantajı), halojen geçer; Adaptive Recognition: IR monokromatiktir, renk tanıma imkansızdır ama plaka okuma için sorun değildir; inex: IR kameralar far körlemesine daha
  ~ kaynak: https://whitepapers.axis.com/en-us/license-plate-capture ve https://adaptiverecognition.com/blog/traffic-transportation/illumination-for-anpr/
- [entegrasyon-operasyon] Çevrimdışı mod store-and-forward deseniyle çalışmalı: otopark sunucusu/ağ yokken olaylar yerel disk kuyruğunda (crash'e dayanıklı, atomik yazım) birikmeli, bağlantı dönünce sırayla senkronlanmalı; kuyruk üst limiti ve dolunca uygulanacak politika tanımlı olmalı.
  ~ Endüstriyel store-and-forward sistemleri (yerel cache + yapılandırılabilir aralıkla yeniden deneme + buffer limiti + disk dolana kadar büyüme riski) bu deseni tanımlar; Parklio ANPR hem online hem offline çalışmayı ürün 
  ~ kaynak: https://parklio.com/en/parking-solutions/anpr
- [entegrasyon-operasyon] Çevrimdışıyken bile bariyer kararı yerelde verilebilmeli: beyaz liste/abone cache'i üzerinden geçiş izni, olaylar sonradan senkron; sunucu bağımlılığı tek hata noktası olmamalı.
  ~ Kameraların yerleşik ANPR'ı bu yüzden kamera-tarafı beyaz liste tutar (Hikvision licensePlateAuditData); ikame yazılım aynı yerel-karar yeteneğini korumalı ki ağ kesintisi kapıyı kilitlemesin.
  ~ kaynak: https://tpp.hikvision.com/wiki/isapi/anpr/GUID-328FC5AD-38EA-4409-9E75-996279BF2A46.html
- [zayif-cpu] Okuma hattinda plaka goruntu genisligi >= 130 px (optimum 150-200 px, ust sinir 300 px), karakter yuksekligi >= 15 px, karakter cizgi kalinligi >= 2 px olmali; EN 62676-4 'identify' esigi 250 px/m.
  ~ Hikvision kendi ANPR kurulum sartnamesi 130-300 px (optimum 150-200) ister — ikame edecegimiz urunun kendi kosulu. Eocortex 15 px karakter, Adaptive Recognition 2 px cizgi kurali; kabul testinde kamera konumu bu esiklerl
  ~ kaynak: https://www.clevelandsecuritycameras.com/post/hikvision-anpr-alpr-camera-installation-setup-guide ; https://doc.eocortex.com/analytics/lpr/requirement
- [zayif-cpu] Gece/IR modunda enstantane >= 1/250 s, 30 km/h arac icin 1/500 s olmali; kar/gece kosulunda IR aydinlatma ve siyah-beyaz IR filtre modu desteklenmeli (model her iki modda test edilmeli).
  ~ Plate Recognizer kamera kilavuzu (gece IR 1/250-1/500 s) ve Eocortex (30 km/h -> 1/500 s) hareket bulaniklidini onlemek icin somut enstantane degerleri veriyor; Erzurum kis kosulu icin kritik.
  ~ kaynak: https://platerecognizer.com/lpr-camera-resolution-guide/ ve https://doc.eocortex.com/analytics/lpr/requirements.htm
- [zayif-cpu] Kamera kaynagi otopark kapisi icin 1080p @ 15 fps yeterli olmali (25-30 fps yalniz ~55 km/h ustu hizlar icin gerekir); ANPR icin cozunurlugu artirmak yerine FOV'u daraltarak plaka-piksel esigi saglanmali (megapiksel artisi OCR gecikmesini buyutur).
  ~ Plate Recognizer kamera kilavuzu: kapi/otopark ~15 fps, >35 mph icin 25-30 fps; Adaptive Recognition 'megapiksel miti' yazisi yuksek cozunurlugun bant genisligi ve OCR gecikmesi maliyetini vurgular (serit basina 700 px m
  ~ kaynak: https://platerecognizer.com/lpr-camera-resolution-guide/ ; https://adaptiverecognition.com/blog/traffic-transportation/busting-anpr-megapixel-myth/
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Kamera montaj geometrisi yerleşik ANPR referans koşullarına göre denetlenmeli: giriş-çıkış modunda montaj yüksekliği 1.6-2 m, araca mesafe 4-6 m, yatay açı <30°, plaka karakter yüksekliği görüntüde 25-30 piksel, tek şerit; ikame ANPR'ın ROI/çözünürlük ayarları bu geometriye göre kalibre edilmeli.
  ~ 7-series kılavuzu doğruluk taahhütlerini bu kurulum şartlarına bağlar; aynı görüntü geometrisi bizim CPU modelimizin de girdi kalitesini belirler.
  ~ kaynak: ANPR Camera Integration Solution for 7 series (2.1 Installation Tips)
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Emülatörün ürettiği olay resimleri kameranın gece davranışıyla uyumlu olmalı: IR modunda licensePlatePicture monokromdur ve XML'de plateColor=other/unknown, color=unknown değerleri normaldir; alıcının bu 'unknown' değerlerini hatasız işlediği hem gündüz hem gece yakalanan örneklerle doğrulanmalı.
  ~ Resmi örnek payload'larda dahi plateColor/color=unknown gönderiliyor; Erzurum'da kış gecesi IR/siyah-beyaz mod baskın çalışma koşuludur.
  ~ kaynak: Fast guide TCG örnek XML + 7-series 4.3 örnek XML
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Kış güvenlik sensörü stratejisi: birincil güvenlik cihazı endüktif loop olacak (kar/far parlaması/gölgeden etkilenmez); fotosel kullanılacaksa kar yansıması ve lens buzlanmasına karşı 2 sn beam-off gecikmesi ayarlanacak ve -40°C çalışma dayanımlı model seçilecek; loop'lar donma-çözülme döngüsüne karşı yılda bir megger/indüktans testiyle kontrol edilecek.
  ~ Loop dedektörler parlama, gölge, kar ve far yansımasından etkilenmez; ancak donma-çözülme loop'u yuvasından çıkarabilir ve gevşek tel yanlış algılara yol açar. Fotoseller kar yansımasıyla gündüz tetiklenir, lens üzerinde
  ~ kaynak: https://alphagateanddoor.com/access-control-systems/vehicle-detection-systems/inductive-loop-detection-the-pros-and-cons/
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Soğuk iklim donanımı: bariyer gövdesine termostat kontrollü kartuş ısıtıcı takılacak (redüktör yağının koyulaşıp dişlilerin sıkışmasını önlemek için) ve seçilen loop dedektör en az -40°C çalışma sıcaklığına dayanacak (örn. BL100B: -40..+70°C).
  ~ CAME bariyer aksesuarları 'cartridge heaters with thermostat' sunar; kılavuzlar ısıtıcının gearbox yağı donmasını ve motor sıkışmasını önlediğini belirtir. BL100B datasheet çalışma aralığını -40..+70°C verir. Erzurum'da 
  ~ kaynak: https://www.came.com/global/en/products/automatic-barriers/
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Sistem, öne bakan giriş kamerasının motosikletleri okuyamayacağını mimari kısıt olarak belgelemeli: TR mevzuatına göre motosiklette yalnız arka plaka (15x24 cm) vardır, ön plaka yoktur (yük motosikleti hariç).
  ~ Karayolları Trafik Yönetmeliği Madde 54: motosiklet/motorlu bisiklete arka çamurluk üzerine sadece 1 plaka takılır; yazılımsal çözümü yoktur.
  ~ kaynak: https://www.ileritrafik.com/blog/icerik/trafik-yonetmeligi-2-bolum-4-kisim-4-bolum
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Çift satır kabul testleri kameranın siyah-beyaz IR gece modunda ve karlı/kirli plaka koşullarında tekrarlanmalı; gece çift satır doğruluğu gündüzden ayrı raporlanmalı.
  ~ Bizim saha koşulu (Erzurum kışı, IR filtreli kamera) + eocortex/Axis LPR rehberleri kontrast ve aydınlatmanın okuma başarısını belirlediğini söylüyor; tek toplam doğruluk rakamı gece regresyonunu gizler.
  ~ kaynak: https://doc.eocortex.com/analytics/lpr/requirements.htm
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Yön filtresi olmalı: yalnızca kameraya yaklaşan (veya senaryoya göre uzaklaşan) araçlar olay üretmeli; yön bilgisi payload'da taşınmalı.
  ~ Hikvision ANPR olayı 'direction' alanı taşır; Plate Recognizer DOT middleware'i tam da 'seyahat yönüne göre filtreleme' için ayrı bir bileşen olarak sunulur — giriş kamerasının çıkan aracı okuyup bariyeri tetiklemesi eng
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/integrations/middleware/ ; https://forums.raspberrypi.com/viewtopic.php?t=365170

## operasyon (48)
- [acik-kaynak] RTSP bağlantı yönetimi dayanıklı olmalı: kopmada 2 sn'den başlayan üstel geri çekilme ile otomatik yeniden bağlanma (üst sınır 600 sn, yapılandırılabilir) ve bağlantı durumu olayı üretilmeli.
  ~ Plate Recognizer Stream'in kamera yeniden bağlanma varsayılanları (2 sn başlangıç, max_reconnection_delay=600); Hikvision RTSP akışında kış koşullarında kopmalar kaçınılmaz.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [acik-kaynak] Görüntüdeki sabit yazılar (kamera zaman damgası, OSD) ve alakasız bölgeler ROI/maske ile analiz dışı bırakılabilmeli (siyah/beyaz PNG maske veya koordinat listesi).
  ~ Frigate timestamp'ın plaka sanılmasına karşı motion mask öneriyor; Plate Recognizer detection zones PNG maskesi sunuyor. Kamera OSD'si yanlış okuma üretebiliyor.
  ~ kaynak: https://docs.frigate.video/configuration/license_plate_recognition/ ; https://guides.platerecognizer.com/docs/stream/configuration/
- [ticari-otopark] Beyaz liste / kara liste / yoksay listesi en az 100.000 kayıt kapasiteli ve zaman bazlı kurallı (gün/saat aralığı) olmalı; listeler REST API ile uzaktan eklenip silinebilmeli.
  ~ Nedap Lumo 100.000 kayıtlı zaman bazlı erişim listeleri + REST API ile liste yönetimi; Hikvision 100.000, Dahua 110.000 allowlist/blocklist. Abone/kayıtlı araç yönetimi otopark ANPR'ın çekirdek özelliği.
  ~ kaynak: https://resources.nedapidentification.com/web/ANPR/Documentation/Datasheets/Datasheet_ANPR_LUMO_EN.pdf
- [ticari-otopark] Beyaz liste eşleştirmesi yapılandırılabilir Levenshtein toleransıyla (örn. 1 karakter farka izin) yapılabilmeli; 'herkese aç' (openForAll) ve 'sadece listeye aç' modları bulunmalı.
  ~ Survision standalone modu matchTolerance (Levenshtein eşiği) ve openForAll parametrelerini tanımlar; tek karakter OCR hatasında aboneyi kapıda bırakmamak için gerekli.
  ~ kaynak: https://fr.survisiongroup.com/documentations/integration_doc.html
- [ticari-otopark] Çevrimdışı davranış: sunucu/ağ/otopark yazılımı erişilemezken sistem yerel beyaz listeyle bariyer açmaya devam etmeli; kesinti sırasındaki olaylar yerelde tamponlanıp (en az ~2000 olay veya yerel disk) bağlantı gelince otomatik senkronlanmalı.
  ~ Hikvision 'offline control of barrier gate under allowlist mode' + ANR (auto network replenishment) + 1 TB TF kart; Survision ~2100 mesaj (~700 araç) tamponlar; HUB Parking JMS'te iş mantığı sahada kalır, internet kesils
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000078840/DS-TCG406-E_Datasheet_20251106.pdf
- [ticari-otopark] Bariyer durumu (kapalı/açık/uzaktan açıldı) izlenip loglanmalı; peş peşe araç geçişinde otomatik 'normally-open' moda geçiş ve takip eden araç için bariyeri açık tutma senaryoları desteklenmeli.
  ~ Hikvision DS-TCG406-E bariyer durum bildirimi, kamera/platform/karma kontrol modu, 'keep boom pole open for following vehicle' ve yoğun geçişte otomatik normally-open özelliklerini sunar; kuyruk ve tailgating yönetimi iç
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000078840/DS-TCG406-E_Datasheet_20251106.pdf
- [ticari-otopark] Okunamayan/düşük güvenli geçişler için insanlı akış: olay 'plakasız/okunamadı' etiketiyle kanıt fotoğrafıyla kaydedilmeli ve operatöre elle düzeltme/manuel açma imkânı sunulmalı.
  ~ Dahua 'recognition of unlicensed vehicles' (plakasız araç algılama) sunar; HUB Parking kayıp bilet/uyuşmazlıkta plakadan giriş zamanı bulma akışını tanımlar. %100 okuma hiçbir üründe garanti edilmez, insan yedeği zorunlu
  ~ kaynak: https://www.hubparking.com/features/license-plate-recognition-lpr/
- [tr-pazari] Hatalı okunan plaka operatör tarafından düzeltilebilmeli ve kayıtlar en az 2 karakterle kısmi plaka sorgusuyla aranabilmeli.
  ~ Şartname md.1.24 (operatör düzeltmesi) ve md.1.22 (2 karakterle sorgu) — %100 doğruluk olmadığının kamu şartnamesince kabulü; düzeltme akışı zorunlu.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [tr-pazari] Kara liste/yasaklı araç geçiş denemesinde sesli+görsel alarm üretmeli ve araç fotoğrafıyla birlikte tanımlı e-posta adreslerine bildirim gönderebilmeli.
  ~ Şartname md.1.27 ve 1.38; Perkotek de yasaklı araç uyarısı + bariyer tetiklememe davranışını standart özellik olarak sunuyor.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [tr-pazari] Acil durum modu bulunmalı: tek tuşla seçili/tüm bariyerler açık bırakılabilmeli ve yangın otomasyonundan gelen sinyalle bariyerler otomatik açılmalı.
  ~ Şartname md.1.40 (acil durum butonu, araç geçse bile açık kalma) ve md.7.14 (yangın alarmıyla otopark tahliyesi) — kamu otoparkı güvenlik şartı.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [tr-pazari] Ağ/sunucu kesintisinde uçta (yerelde) kayıt ve çalışmaya devam edip bağlantı gelince verileri merkeze aktarmalı (offline tampon).
  ~ ISSD POINTR 'connection failure durumunda memory backup + FTP aktarım' özelliğini standartlaştırmış; şartnamedeki KVM/KDM cihazları da online/offline çalışma şartı taşıyor.
  ~ kaynak: https://www.issd.com.tr/en/number-plate-recognition-system/
- [tr-pazari] Lisanslama/kurulum PC değişiminde üreticiye bağımlılık yaratmamalı; kamu ihalelerinde '%100 yerli üretim yazılım' şartı arandığından in-house geliştirme bu şartı doğrudan karşılar.
  ~ Şartname md.1.6 (elektronik anahtar, PC değişiminde yeniden lisans yok) ve md.1.25 ('%100 yerli üretim') — belediyenin kendi C#/.NET çözümü her iki maddeyi de karşılıyor.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [standartlar] Kabul testi örneklemi en az 250 ardışık araç olmalı (düşük trafikli sahada alternatif: 2 saatte geçen tüm araçlar, daha düşük istatistiksel güçle); değerlendirme bağımsız ground-truth video üzerinden İNSAN gözüyle yapılmalı, bilgisayarla değil.
  ~ Home Office Prosedür 1: 250 ardışık araç tam popülasyonu makul güvenle temsil eder; ground-truth insan tarafından okunmalı ki yazılım varsayımları test sonucuna sızmasın.
  ~ kaynak: https://assets.publishing.service.gov.uk/government/uploads/system/uploads/attachment_data/file/909024/ANPR_-_Evaluation_Approved_Version_2.0.pdf
- [standartlar] Kurulum sonrası ilk kabul ölçümü zorunlu; işletmede yıllık tam ground-truth değerlendirmesi (min 250 ardışık okuma, gündüz+gece), kanıt görüntüsü saklamayan modda ise 6 ayda bir değerlendirme yapılmalı.
  ~ NASPLE performans doğrulama periyotları: yıllık 250-okumalı değerlendirme, görüntüsüz sistemlerde 6 aylık aralık.
  ~ kaynak: https://www.gov.uk/government/publications/national-anpr-standards/national-anpr-standards-for-policing-and-law-enforcement-accessible-version
- [standartlar] Hafif periyodik spot test: yılda en az bir kez bilinen plakalı araçlarla tercihen 20 (asgari 10) kontrollü geçiş; 20 geçişte 2+ yakalanamama VEYA 3+ yanlış okuma (10 geçişte 2+) tespit edilirse düzeltici işlem başlatılıp test tekrarlanmalı.
  ~ Home Office Prosedür 3'ün sayısal düzeltici-işlem eşikleri; tam ground-truth testinden çok daha ucuz bir erken uyarı mekanizması.
  ~ kaynak: https://assets.publishing.service.gov.uk/government/uploads/system/uploads/attachment_data/file/909024/ANPR_-_Evaluation_Approved_Version_2.0.pdf
- [entegrasyon-operasyon] Giriş-çıkış eşleştirmesi fuzzy matching desteklemeli: 1-2 karakter farklı okumalar eşleşebilmeli, near-miss varyantları otomatik üretilmeli; hedef eşleşme oranı ~%100.
  ~ ZatPark fuzzy matching'i 'plaka 1-2 karakter farklı okunsa bile tanıma' olarak tanımlar; WPS fuzzy matching ile pozitif eşleşmenin %100'e yaklaştırılabildiğini belirtir. OCR hatası çıkışta faturalamayı bozmamalı.
  ~ kaynak: https://unity5.zatpark.com/anprfindoutmore ve https://www.wpsparking.com/en/parking-concepts/paperless-parking
- [entegrasyon-operasyon] Düşük güvenli (confidence eşiği altı) okumalar bariyeri otomatik açmamalı; operatör onay/düzeltme kuyruğuna düşmeli, operatöre görüntü + aday plaka listesi (mümkünse araç rengi/tipi ile) sunulmalı.
  ~ ZatPark fuzzy match ekranı operatöre araç marka/model/renk bilgisiyle sistem üretimi aday listesinden doğru plakayı seçtirir; ParkPow rol tabanlı (Viewer/Operator/Executive) kullanıcı yönetimi sunar.
  ~ kaynak: https://unity5.zatpark.com/anprfindoutmore ve https://parkpow.com/features/
- [entegrasyon-operasyon] Okunamayan/hatalı plaka için fallback zinciri tanımlı olmalı: manuel plaka girişi, interkom/operatör çağrısı, yönetici override ile bariyer açma; elektrik kesintisinde manuel açma prosedürü.
  ~ ANPR entegrasyon rehberleri kirli/hasarlı plaka ve kötü ışıkta manuel giriş, yedek kart okuyucu ve admin override'ı standart fallback olarak sayar; Türk şartnameleri acil durum manuel açma protokolü ister.
  ~ kaynak: https://ronspotflexwork.com/blog/anpr-parking-integration/
- [entegrasyon-operasyon] Ücretsiz süre (grace period) parametrik olmalı: girişten itibaren ücretsiz ilk X dakika (Türkiye pratiği ~15 dk) ve ödemeli senaryoda çıkış için en az 10 dakika tolerans.
  ~ BPA/IPC Code of Practice ödeme sonrası çıkışta minimum 10 dk grace period'u zorunlu kılar (ayrıca park yeri sayısına göre değişen consideration period); Türk otopark yazılımları 'ilk X dakika ücretsiz' modelini standart 
  ~ kaynak: https://www.britishparking.co.uk/write/Documents/AOS/NEW%20Redesigned%20Documents/sectorsingleCodeofPractice.pdf
- [entegrasyon-operasyon] Anti-passback desteklenmeli: giriş kaydı olmadan çıkış / çift giriş engellenebilmeli; hard (geçişi reddet), soft (izin ver + olayı raporla) ve timed (aynı yönde tekrar için süre şartı) modları yapılandırılabilir olmalı.
  ~ Erişim kontrol standartlarında hard/soft/timed anti-passback ayrımı yerleşiktir; Perkotek PTS 'antipassback ile ardışık giriş/çıkış engelleme' özelliğini yerli üründe sunar.
  ~ kaynak: https://www.getkisi.com/guides/anti-passback ve https://www.silvaconsultants.com/anti-passback-in-access-control
- [entegrasyon-operasyon] Abonelik yönetimi: yıllık/aylık/haftalık abonelik, abone plakaya otomatik bariyer, borç takibi, süresi dolan aboneye bildirim (SMS/e-posta), araç kotası ve zaman bazlı yetki (saat aralığı) desteklenmeli.
  ~ Perkotek PTS bu özellik setini (borç takibi, SMS uyarı, üye başına araç kotası, saat bazlı yetki) yerli pazar standardı olarak tanımlar; İSPARK abone plakaya otomatik bariyer açar.
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr
- [entegrasyon-operasyon] Ücretlendirme entegrasyonu için en az 3 tarife sınıfı (otomobil / otobüs-kamyon / diğer) ve giriş kaydı bulunamayan çıkışlar için operatör onaylı çözüm akışı bulunmalı.
  ~ Perkotek üç tarife modelini standart sunar; kayıp eşleşme (giriş kaydı yok) durumu fuzzy matching ile çözülemezse operatör kararı gerektirir.
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr
- [zayif-cpu] Kurulumda CPUID/komut seti dogrulamasi yapilmali (AVX/AVX2 var mi?) ve exe AVX'siz CPU'da kontrollu uyari vermeli: ticari ANPR'lar AVX sart kosar (Plate Recognizer: Intel 3. nesil+ AVX; Eocortex: i3-10100+ AVX), OpenVINO 2026.0+ AVX2'yi zorunlu kilip SSE destegini birakiyor; AVX'siz CPU'da 'illegal instruction' cokmesi bilinen hata sinifidir.
  ~ Belediye ofis PC'leri eski olabilir; runtime/native kutuphane secimi (ONNX Runtime resmi paketleri calisir ama yavas, OpenVINO yeni surumleri hic acilmaz) kurulum oncesi otomatik kontrole baglanmali.
  ~ kaynak: https://docs.openvino.ai/systemrequirements ; https://guides.platerecognizer.com/docs/stream/onpremise/system-requirements ; https://doc.eocortex.com/
- [zayif-cpu] Coklu kamera olcekleme kurali: her ANPR analiz thread'i 1 tam cekirdek tuketir ve okuma/sn cekirdek sayisiyla lineer artar; planlama tablosu 1 kamera=i3, 2-4 kamera=i5, 4-8 kamera=i7 ve >= 8 GB RAM (TR entegrator pratigi) olarak dogrulanmali.
  ~ Rekor Scout mimarisi (thread=cekirdek, lineer olcek) ile Turk PTS entegratorlerinin sahada kullandigi i3/i5/i7 merdiveni birbirini doguruluyor; ileride ikinci giris kapisi eklenirse butce ongorusu hazir olur.
  ~ kaynak: https://docs.rekor.ai/scout/agent/system-requirements ; https://santralmarket.net.tr/plaka-tanima-sistemi-nedir/
- [zayif-cpu] Windows hedefleniyorsa kapasite plani Ubuntu benchmark'larindan %16-22 dusuk fps ile yapilmali (ayni donanimda Windows cezasi).
  ~ Rekor Scout resmi dokumantasyonu Windows kurulumlarinda fps'in %16-22 azaltilarak hesaplanmasini soyler; belediye PC'si buyuk olasilikla Windows oldugu icin dogrudan bizim kabul esigimizi etkiler.
  ~ kaynak: https://docs.rekor.ai/scout/agent/system-requirements
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Kameraya yapılan TÜM ISAPI çağrıları (httpHosts okuma, VCL liste, barrierGateCtrl) digest auth ile yapılmalı; PC-kamera saat sapması NTP ile <5 dakika tutulmalı (aksi halde digest reddedilir) ve başarısız kimlik denemesi ~5'te durdurulmalı (30 dakikalık hesap kilidi tetiklenmemeli, üstel geri çekilme uygulanmalı).
  ~ ISAPI kılavuzu tüm isteklerde RFC 2617 Digest zorunlu kılar; saha entegrasyon notları >5 dk saat sapmasının digest'i bozduğunu ve ~5 hatalı denemenin 30 dk kilit getirdiğini raporluyor — kışın elektrik kesintisi sonrası 
  ~ kaynak: Fast guide TCG (Authorization bölümü) + https://github.com/uchkunr/hikvision-best-practices
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Kameranın yerleşik ANPR push'u kapatılırken (httpHosts kaydı silinir/değiştirilirken) otopark yazılımının kamera-çevrimdışı alarmı üretmediği ve RTSP/ping/deviceInfo gibi ayrı canlılık kanallarının bozulmadığı test edilmeli; kamera fiziksel olarak yerinde ve erişilebilir kalmalı.
  ~ Platformlar cihaz sağlığını olay kanalından bağımsız izleyebilir (alertStream heartbeat, deviceInfo polling); yalnız olay kaynağını değiştirip kamerayı söküp atmak yanlış alarm/kilitlenme riski taşır (türetilmiş gereksin
  ~ kaynak: ANPR Camera Integration Solution for 7 series (4.1.2 alertStream heartbeat) + https://github.com/uchkunr/hikvision-best-practices
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] RTSP bağlantı kopmasında otomatik yeniden bağlanma + ilk çözülebilir kare (I-frame bekleme dahil) toplamı ≤3 sn olmalı; bu 'kör pencere' süresi test raporunda ölçülmeli (GOP 1 sn ayarıyla I-frame bekleme payı en kötü 1 sn).
  ~ Bariyer uygulamasında stream kopması aracın okunamamasına yol açar; decoder yeniden bağlantıda bir sonraki I-frame'e kadar kare üretemez, bu süre GOP uzunluğuyla sınırlıdır. Kış/kar koşullu dış saha ağında kopma olağandı
  ~ kaynak: https://securitycamcenter.com/i-frame-interval-security-camera-systems/
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Kamera ve ANPR PC'si aynı NTP kaynağına senkronlanmalı (LAN'da yerel NTP; hedef sapma ≤10 ms) ve tüm ANPR olayları kare yakalama zaman damgasıyla (varış zamanıyla değil) loglanmalı.
  ~ Kare yaşı izleme ve gecikme ölçümü RTCP SR NTP karşılaştırmasına dayanır; OpenIPC ölçümleri yerel NTP ile ±3-5 ms, genel internet NTP ile ±10-30 ms sapma gösterir. Bariyer olay kayıtlarının hukuki/operasyonel doğruluğu d
  ~ kaynak: https://github.com/OpenIPC/wiki/blob/master/en/timestamps-and-time-sync.md
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Tailgating tespiti: bariyer-açık penceresi (aç komutu → kapandı bilgisi/timeout) içinde tetik bölgesinden 2 veya daha fazla FARKLI araç/plaka geçerse 'kuyruk ihlali (tailgating)' olayı üretilmeli; ikinci aracın plakası (okunabildiyse) ve olay anının kare dizisi/klibi kanıt olarak saklanmalı.
  ~ intuVision 'tek kart geçişinde iki araç' veya 'kullanıcı tanımlı süreden kısa aralıkla iki araç' kuralını uygular; gateGUARDIAN tailgating olayında önceden tanımlı süreli HD klibi ağ depolamaya kaydedip sorumlu aracı ANP
  ~ kaynak: https://intuvisiontech.com/solutions/tailgating-vehicle ; http://www.anpr-international.com/tailgating-detection/
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Tailgating'de ikinci aracın plakası okunamasa bile araç-geçti sayımı yapılmalı: plaka-yalnız mod yetmez, tetik bölgesinde plakasız araç tespiti (Plate Recognizer detection_mode=vehicle eşdeğeri) desteklenmeli ve 'plakasız/okunamayan geçiş' kaydı düşülmeli.
  ~ Tailgating'de arkadaki araç öndekine çok yaklaştığında öndeki araç plakayı fiziksel olarak kapatır (Which? ANPR hata analizi); Plate Recognizer detection_mode=vehicle plakasız araçları da algılar. Sayım olmadan tek açılı
  ~ kaynak: https://www.which.co.uk/news/article/the-big-anpr-glitch-and-why-license-plate-cameras-get-it-wrong-aS15f8c42pmv ; https://guides.platerecognizer.com/
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] UL 325 uyumu: bariyer engel algıladığında 2 saniye içinde ters yöne dönecek ve en az 2 inç (5 cm) geri hareket edecek; ikinci ardışık engelde yalnızca duracak; engel nedeniyle durma/terslemeden sonra otomatik kapanma zamanlayıcısı resetlenene kadar devre dışı kalacak.
  ~ DASMA TDS-353 (ANSI/UL 325): 'reversing must begin within 2 seconds... travel a minimum of 2 inches... after any obstruction stop the timer-to-close is disabled until reset'. Araç/yaya üstüne kol inmesini önleyen temel s
  ~ kaynak: https://www.dasma.com/wp-content/uploads/pubs/TechDataSheets/OperatorElectronics/TDS353.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Her sıkışma bölgesi ve her hareket yönü için en az 2 bağımsız koruma bulunacak (Type A inherent tork/temas algılama + Type B1 fotosel veya B2 kenar sensörü veya emniyet loop'u) ve tüm harici güvenlik sensörleri monitörlü olacak: sensör yoksa/arızalıysa bariyer otomatik modda çalışmayacak.
  ~ UL 325: 'ALL external means of protecting against entrapment must be monitored... should any device not be present or a fault condition occur' operatör çalışmaz. Magnetic MHTM 'plausibility check': açılışta 3, işletmede 
  ~ kaynak: https://www.dasma.com/wp-content/uploads/pubs/TechDataSheets/OperatorElectronics/TDS353.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Emniyet loop'u geometrisi: loop kol izdüşümünü tüm uzunluk boyunca kapsayacak, binek araç için kolun önünde ve arkasında en az 500 mm taşacak; açma loop'u ile emniyet loop'u arası en fazla 1,0 m olacak; toplam loop indüktansı 70-500 µH aralığında kalacak.
  ~ Magnetic MHTM kılavuzu sayfa 45-56: safety loop tehlike alanını tamamen örtmeli, binek için ±500 mm, kamyon için ≥2,5 m; opening-safety loop mesafesi ≤1 m; Ltotal 70-500 µH. Saha keşfinde loop kesim planının kabul kriter
  ~ kaynak: https://magneticgateopeners.com/store/pdfs/MHTM_Manual.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Fotosel/loop tamamen arızalansa dahi son savunma hattı olarak kol temas algılamalı ('akıllı kol' / inherent Type A) olacak: kol bir cisme değdiğinde derhal yukarı kalkacak.
  ~ Türk entegratör dokümantasyonu (Z Sistem): 'emniyet fotoseli ve loop dedektörü arızalansa bile akıllı kol özelliği ile kol altındaki bir cisme temas ettiğinde cismi algılayıp hemen kalkar'. UL 325 Type A inherent koruma 
  ~ kaynak: https://zsistem.net/h_bariyer.php
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Elektrik kesintisinde bariyer davranışı konfigüre edilebilir olacak ve belediye otoparkı için politika 'personelsiz saatlerde fail-open (kol yukarı/serbest itilebilir)' olarak yazılacak; kol ölü noktada (tam açık/tam kapalı) kilitlenirse alet deliği ile manuel kurtarma prosedürü tanımlanıp personele eğitim verilecek.
  ~ Magnetic MGC kontrol ünitesi 'Power failure' menüsünde kesinti davranışı ayarlanır; kılavuz 12.5: kesintide kol ölü noktadaysa üst/alt alet deliğine bastırarak manuel açma/kapama yapılır. Gate-A-Mation: fail-safe bariyer
  ~ kaynak: https://magneticgateopeners.com/store/pdfs/MHTM_Manual.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Yangın/acil durum girişi bağlanacak: itfaiye anahtarı veya yangın alarmı kontağı aktive olduğunda bariyer koşulsuz açılacak ve açık kilitlenecek.
  ~ Magnetic MHTM 'Connecting emergency opening contacts: fire-fighter switch, emergency opening contacts' bölümü; IFC 503 itfaiye erişim yolları için geçersiz kılma gerektirir. Kamu otoparkında acil tahliye/itfaiye erişimi 
  ~ kaynak: https://barriergatesystems.com/operations/manual-override-procedures-emergencies/
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Rollback prosedürü dokümante ve TEST edilmiş olacak: kameranın Control Mode'unu 'By Platform'dan 'By Camera'ya çevirme tek konfigürasyon adımıyla ≤5 dakikada yapılabilecek; devreye alma öncesi 'PC fişini çek' tatbikatıyla bariyerin kamera moduna düştüğü fiilen doğrulanacak.
  ~ Hikvision kılavuzu iki modu da destekler (By Platform / By Camera); mod değişimi web arayüzünden yapılır. Hikvision yerleşik ANPR'ını ikame eden projede geri dönüş yolu tatbikatla kanıtlanmazsa kağıt üzerinde kalır.
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000178982/Intelligent-ANPR-Camera_User-Manual_V5.4.4_20250923.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] PC otomatik ayağa kalkma zinciri: BIOS'ta 'Restore on AC Power Loss = Power On' ayarlanacak, 'bcdedit /set {default} bootstatuspolicy ignoreshutdownfailures' uygulanacak (kesinti sonrası kurtarma ekranında takılmayı önler) ve zincir gerçek elektrik kesintisi testiyle doğrulanacak: fiş çekilip takıldığında PC klavye/ekran olmadan masaüstüne ve ANPR servisine kadar kendiliğinden dönecek.
  ~ Intel/ASUS BIOS dokümanları 'Restore on AC Power Loss' ayarını tanımlar; Microsoft Q&A ve saha raporları başsız kiosk PC'lerin kesinti sonrası recovery ekranında takıldığını, bootstatuspolicy ile çözüldüğünü doğrular. Ge
  ~ kaynak: https://learn.microsoft.com/en-us/answers/questions/4154157/auto-restart-normally-without-recovery-after-power
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] UPS entegrasyonu: UPS, pil tükenmeden OS'a temiz kapanma komutu verecek ve şebeke dönünce ÇIKIŞINI güç döngüsü yaparak (output power cycle) BIOS'u tetikleyip PC'yi yeniden başlatacak; yalnızca UPS yazılımı PC'yi açamayacağından BIOS ayarı ile birlikte uçtan uca test edilecek.
  ~ Schneider/APC topluluk çözümü: 'UPS software cannot turn a computer on once it is off — set BIOS to Always On, configure UPS software to shut down the OS on battery; when AC is restored the UPS powers back up and the sys
  ~ kaynak: https://community.se.com/t5/APC-UPS-for-Home-and-Office-Forum/Need-a-Solution-to-Turn-PC-on-after-Power-Restore/td-p/288618
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] ANPR Windows servisi 'Automatic (Delayed Start)' olacak ve kurtarma politikası 'sc failure AnprSvc reset= 86400 actions= restart/5000/restart/5000/reboot/30000' düzeninde ayarlanacak: 1. ve 2. çökmede 5 sn içinde servis yeniden başlatılır, 3. çökmede 30 sn sonra makine reboot edilir, sayaç 24 saatte sıfırlanır.
  ~ Microsoft 'sc failure' dokümantasyonu bu aksiyonları (restart/run/reboot + bekleme ms) tanımlar; örnek desen literatürde aynen geçer. Servis çökmesinin bariyer hattını dakikalarca kilitlemesini önler.
  ~ kaynak: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc742019(v=ws.11)
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] OS tamamen donarsa kurtarma için donanım watchdog kullanılacak: anakart/endüstriyel PC watchdog timer'ı etkinleştirilecek, ANPR servisi periyodik besleyecek (kick) ve besleme kesilirse watchdog makineyi otomatik resetleyecek; watchdog yoksa harici zamanlı güç rölesi alternatifi değerlendirilecek.
  ~ Watchdog timer literatürü (Wikipedia, DFI, PUSR): insansız/uzak sistemlerde yazılım kilitlenmesi veya geçici donanım hatasında timeout sinyali otomatik reboot başlatır; teknisyen sevkiyatını ve uzun duruşu önler. Zayıf o
  ~ kaynak: https://en.wikipedia.org/wiki/Watchdog_timer
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Motosiklet ve okunamayan araçlar için fiziksel fallback (buton/interkom/manuel bilet veya görevli onayıyla bariyer açma) şartname gereği zorunlu tutulmalı; yazılım bu fallback'i olay API'sinde 'manuel giriş' olarak desteklemeli.
  ~ UK ANPR otoparklarında ön plakasız motosikletler girişte okunamıyor; interkom/manuel bilet endüstri standardı fallback (NCP, Ronspot, parkingpaymentguide).
  ~ kaynak: https://forums.moneysavingexpert.com/discussion/5700225/do-car-park-anpr-camers-detect-motorbikes
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Belediye, motosikletler için politika kararı almalı: ya (a) ayrılmış motosiklet alanı + ücret muafiyeti (ANPR kapsamı dışı) ya da (b) arkadan okuyan ek kamera ile motosiklet takibi; yazılım kabulü bu karara bağlanmalı.
  ~ Medway Council örneği: 'Motorcycles do not need to pay the parking charge if using a motorcycle bay' — motosikleti ANPR dışına almak yaygın ve en ucuz çözüm; bazı UK otoparkları ise girişte arka plakayı okuyarak çözüyor.
  ~ kaynak: https://www.medway.gov.uk/info/200159/parking/1790/automatic_number_plate_recognition_anpr
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Bariyer varlık algılama donanımı motosikleti güvenilir algılamalı: indüksiyon loop hassasiyeti bir kademe artırılmalı veya küçük loop/radar kullanılmalı; bariyer kolu üzerinde araç varken inmemeli (yazılımdan bağımsız güvenlik şartı).
  ~ EDI teknik dokümanı: motosiklet büyük loopta yalnızca ~%0,012 endüktans değişimi yaratır ve algılanmayabilir — plaka okunamayan motosiklet bariyer altında kalırsa kol çarpma riski doğar.
  ~ kaynak: https://www.editraffic.com/wp-content/uploads/2023/07/Inductive-Loop-Detector-Basics.pdf
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Kapsam DIŞI (PMS'te kalacak) işlevler yazılı olarak sabitlenmeli: tarife/ücret hesabı, yıllık-aylık-haftalık abonelik, borç takibi ve SMS bildirimi, kara liste tanımı ve alarmı, vardiya/gelir/geçiş raporları, doluluk panosu, kasa ekranı — ANPR servisi bunları yalnızca webhook ile besler.
  ~ Türk otopark yazılımlarında (Perkotek PTS) bu işlevlerin tamamı zaten standart; ParkPow da aynı katmanda konumlanır. İkame projesinin bu işlevleri kopyalaması hem MVP'yi şişirir hem mevcut yazılımla çakışır.
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr ; https://platerecognizer.com/parkpow/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Operatör (kasa) ekranı PMS sorumluluğudur; asgari beklenen set referans olarak: canlı görüntü + son okuma kartı (plaka, resim, saat, kamera adı), deforme/okunamayan plaka için manuel giriş-çıkış, plaka düzeltme, kapı seçerek manuel bariyer aç/kapat tetiği ve acil durumda tüm bariyerleri açma butonu.
  ~ Perkotek PTS operatör ekranında bu işlevlerin tümü mevcut; mevcut PMS'te eksikse ancak o zaman bizim tarafta minimal bir operatör görünümü gündeme gelmeli (saha keşfiyle doğrulanmalı).
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] ANPR servisinin kendi teknisyen/kalibrasyon UI'ı olmalı: canlı kamera önizlemesi üzerinde LPR alanı (ROI) çizimi, tetikleme tipi seçimi, bölge/format ayarı ve son N okumanın resimli listesi.
  ~ Hikvision kamerada bu ayarlar web arayüzünde yapılır ('LPR alanı çiz', tetik tipi seç); kamerayı ikame eden servis bu kurulum yüzeyini de ikame etmek zorundadır — aksi halde saha kurulumu yapılamaz.
  ~ kaynak: https://www.hikvisioneurope.com/eu/portal/portal/Technology%20Partner%20Program/02-Solutioins%20of%20Hikvision%20product%20integration/Fast%20guide%20

## performans (30)
- [acik-kaynak] Zayıf CPU'da hedef işleme kapasitesi: otopark giriş hızı (<40 km/s) için kamera başına 5-10 fps analiz yeterli kabul edilmeli; boru hattı (tespit+OCR) kare başına CPU'da ortalama ≤100-200 ms olmalı ve fps yetişmediğinde kareler atlanmalı (sample rate).
  ~ Rekor benchmark kılavuzu düşük hız için 5-10 fps öneriyor (i7-6700 CPU 720p'de 17.3 fps işleyebiliyor); Plate Recognizer Snapshot 50-100 ms çıkarım ve Stream sample=2 varsayılanıyla kare atlama deseni.
  ~ kaynak: https://docs.rekor.ai/scout/agent/benchmarking ; https://platerecognizer.com/snapshot/
- [acik-kaynak] CPU çıkarımı hızlandırılmış çalıştırılmalı (ONNX Runtime/OpenVINO, AVX2): hızlandırma açıkken en az 2-3 kat kazanç doğrulanmalı; hedef PC'de AVX2 desteği kurulum ön şartı olarak kontrol edilmeli.
  ~ ultimateALPR i7-4790K: OpenVINO kapalı 18 fps → açık 41.7 fps (%100 pozitif senaryo); Frigate LPR için AVX+AVX2 ve 4GB RAM'i açık ön şart ilan ediyor.
  ~ kaynak: https://www.doubango.org/SDKs/anpr/docs/Benchmark.html ; https://docs.frigate.video/configuration/license_plate_recognition/
- [acik-kaynak] Hareket algılama + kare tamponu olmalı: analiz yalnızca hareket varken yapılmalı, CPU yetişemezse kareler tamponlanıp (örn. 60 karelik buffer) hareket bitince işlenmeli; böylece zayıf CPU hiçbir geçişi tamamen kaçırmamalı.
  ~ Rekor Scout motion_detection=1 + camera_buffer_size=60 deseni: 10 karelik geçişte CPU anlık 3 kare işleyebilse bile buffer sayesinde 10'u da analiz ediliyor.
  ~ kaynak: https://docs.rekor.ai/scout/agent/configuration/agent-properties ; https://help.rekor.ai/what-are-the-rekor-scout-agent-configuration-properties
- [ticari-otopark] Tanıma gecikmesi tek kare için ≤100 ms (CPU'da), araç göründükten bariyer açma kararına kadar uçtan uca <1 s hedeflenmeli.
  ~ Survision 20 ms'de okuma iddia eder; PlateRecognizer Snapshot CPU'da 50-100 ms çıkarım verir; Genetec 'ultra low reading latency' satar. Otopark akışında 1 s üstü gecikme kuyruk yaratır.
  ~ kaynak: https://survisiongroup.com/post-lpr-technology
- [ticari-otopark] Zayıf GPU'suz PC fizibilitesi: 1-2 kamera için Passmark ~2500 puanlı CPU (örn. i3-4100M) + 1 GB RAM sınıfı donanım yeterli olmalı; kare örnekleme (her 2.-3. kare, eFPS=kameraFPS/sample) ile CPU yükü ayarlanabilir olmalı; AVX destekli CPU şartı belgelenmeli.
  ~ PlateRecognizer Stream resmi donanım kılavuzu: 2 kamera+MMC için i3-4100M (Passmark 2440) yeterli, orta seviye CPU 5-10 kamera işler, Intel 3. nesil+ AVX zorunlu, varsayılan sample=2. CPU-only hedefimizin ticari kanıtı.
  ~ kaynak: https://platerecognizer.com/help/what-is-the-recommended-hardware-for-stream/
- [tr-pazari] Plaka tanıma süresi araç durur/yavaşlarken saniye altı olmalı (Perkotek 20 ms tanıma iddia ediyor); abone sorgusu milisaniyeler mertebesinde tamamlanıp bariyer operatörsüz açılmalı.
  ~ Perkotek '20 milisaniyede tanımlama'; İSPARK/belediye pratiği 'milisaniyeler mertebesinde abone sorgusu + otomatik bariyer'; şartname md.1.32 operatörsüz açma şartı.
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr
- [tr-pazari] Kamera üreticisinden bağımsız çalışmalı (ONVIF/RTSP ile herhangi bir IP kameradan görüntü alabilmeli) ve en az 1280x720 çözünürlükte gerçek zamanlı (25 fps PAL) işleme yapabilmeli.
  ~ Şartname md.1.9 'tek üreticiye ait kamera ile çalışmamalı', md.1.12-1.13 (25 fps, 1280x720) ve kamera md.2.13 ONVIF şartı — Hikvision'a kilitlenmeme bizim ikame senaryomuzun ta kendisi.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [standartlar] Bariyer tetikleme gecikmesi: karenin alınmasından bariyer rölesi/olay çıkışına kadar uçtan uca süre hedef < 500 ms (bileşen bütçesi: LPR < 100 ms, liste sorgusu < 50 ms, röle < 150 ms); zayıf CPU'lu PC'de kabul üst sınırı ≤ 2 s ve 95. persentilde ölçülüp raporlanmalı (NASPLE polis bağlamında bile okuma→alarm 4-6 s üst sınırını %95 oranında şart koşar).
  ~ GMWEB otopark ANPR referans mimarisi <500 ms uçtan uca değer verir; Survision 20 ms tanıma, tek haneli ms röle bildirir; NASPLE 95. persentil yaklaşımı ölçüm yöntemini tanımlar.
  ~ kaynak: https://gmweb.pl/en/anpr-parking-system
- [standartlar] CPU-only performans zarfı: hedef donanımda (GPU'suz ofis PC) sürekli çalışmada kare işleme hızı ve ortalama/tepe CPU kullanımı ölçülüp beyan edilmeli; referans: OpenALPR i7-7700K CPU'da 1080p ~52 FPS, Xeon E5-2666v3'te ~12,7 FPS — zayıf CPU'da gerçekçi bütçe geçiş başına birkaç kare analizidir.
  ~ OpenALPR CPU benchmark tablosu GPU'suz gerçekçi FPS aralığını gösterir; kabul testinde 'FPS + CPU% + gecikme' üçlüsü birlikte raporlanmalı.
  ~ kaynak: https://cdn.openalpr.com/benchmarks.html
- [kosullar] Video akışı otopark senaryosu için en az 4-5 efektif FPS hızında işlenmeli (zayıf CPU'da bile); araç ROI'ye girdiğinde 0.2 sn'lik pencerede en az 1 kare yakalanabilmelidir.
  ~ Plate Recognizer: <30 mph için 4-5 eFPS yeterli; Axis 5 fps analizde 0.2 sn tespit penceresi hesabı verir. GPU'suz ofis PC kısıtımızda bu hedef ONNX CPU modellerle ulaşılabilir (fast-alpr/fast-plate-ocr).
  ~ kaynak: https://platerecognizer.com/camera-setup-for-best-anpr/ ve https://github.com/ankandrew/fast-alpr
- [kosullar] Plaka görünür olmasından bariyer 'aç' olayının yayınlanmasına kadar uçtan uca gecikme <1 saniye olmalıdır.
  ~ Parklio: whitelist'li araçta bariyer 1 saniyeden kısa sürede açılıyor; Dahua ANPR tespiti ~100 ms'de tamamlıyor — sürücü deneyimi ve kuyruk oluşmaması için endüstri normu.
  ~ kaynak: https://parklio.com/en/parking-solutions/anpr
- [entegrasyon-operasyon] Plaka tanıma + olay iletimi toplam gecikmesi 1 saniyenin altında olmalı (tanıma hedefi 100 ms mertebesi); bariyer kolunun fiziksel açılışı için ek 1.3-6 sn bütçelenmeli.
  ~ Sektörde tanıma 20-100 ms (Perkotek 20 ms iddia eder), üst sınıf sistemlerde uçtan uca uyarı gecikmesi <1 sn; bariyer donanımı 1.3 sn (hızlı model) - 6 sn arasında açılır. Yazılımsal sistemlerde >3 sn'ye çıkan tanıma kuy
  ~ kaynak: https://www.macrosafegates.com/article/complete-guide-to-license-plate-recognition-system-i00054i1.html ve https://magneticgateopeners.com/store/resou
- [zayif-cpu] Tek kamera icin hedef PC'nin Passmark cok-cekirdek puani en az ~2.100-3.300 olmali; her ek kamera benzer butce ekler (2 kamera dusuk trafik: 2.440 / i3-4100M sinifi yeterli).
  ~ Plate Recognizer Stream resmi boyutlandirmasi: tek kamera 2.096-3.306 Passmark, 20 kamera+MMC icin 24.000 (i7-11700K). GPU'suz calisma resmi olarak destekleniyor; RPi4 bile 1 kamerayi tasiyor.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/onpremise/system-requirements
- [zayif-cpu] Servis RAM tuketimi kamera basina <= 500-600 MB (1080p) olmali; tek kameralik kurulumda toplam servis RAM'i < 1,5-2 GB kabul esigi.
  ~ Plate Recognizer Stream: 480-530 MB/kamera (1080p); Rekor Scout: analiz thread'i basina ~500 MB ve sonradan artmaz. Olculebilir RAM butcesi iki bagimsiz ticari urunle uyumlu.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/onpremise/system-requirements ve https://docs.rekor.ai/scout/agent/system-requirements
- [zayif-cpu] Islenen kare basina uctan uca ANPR suresi zayif CPU'da <= 300 ms hedeflenmeli; referans olcumler: CPU'da 60-110 ms/kare (Xeon E3-1240v3: 60 ms, i7-8550U: 70 ms, Xeon E5-2680v2: 110 ms), i7-6700 1080p'de 11,7 fps (~85 ms/kare).
  ~ Plate Recognizer 'ALPR without GPU' olcumleri ve Rekor Scout islemci benchmark tablosu; zayif ofis PC'sinde 300 ms/kare 4-5 eFPS hedefiyle tutarli ve ulasilabilir.
  ~ kaynak: https://platerecognizer.com/alpr-without-gpu/ ve https://docs.rekor.ai/scout/agent/benchmarking
- [zayif-cpu] Karar akisi 1080p ana akis yerine 720p H.264 substream uzerinden islenmeli (plaka >=130 px sartini sagliyorsa): ayni CPU'da 720p, 1080p'ye gore ~%48 fazla fps verir (i7-6700: 17,3 vs 11,7 fps); H.265 decode CPU maliyetini artirir, H.264 tercih edilmeli.
  ~ Rekor benchmark tablosu cozunurluk-fps iliskisini, Plate Recognizer dokumani H.264 onerisini acikca veriyor; RTSP decode maliyeti zayif CPU'da inference kadar onemli.
  ~ kaynak: https://docs.rekor.ai/scout/agent/benchmarking ve https://guides.platerecognizer.com/docs/stream/onpremise/system-requirements
- [zayif-cpu] INT8 kuantizasyon karari hedef CPU komut setine gore verilmeli: VNNI'li CPU'da 6x'e kadar hizlanma; yalniz-AVX2 makinede U8S8 formati VPMADDUBSW saturasyonu ile DOGRULUK HATASI uretir (U8U8 tercih + reduce_range zorunlu); AVX'siz eski donanimda INT8 hic kazanc saglamayabilir — A/B benchmark + dogruluk regresyon testi sart.
  ~ ONNX Runtime resmi dokumani: 'Old hardware has none or few of the instructions needed to perform efficient inference in int8' ve AVX2/AVX512 (VNNI'siz) icin reduce_range onerisi; korlemesine INT8'e gecmek zayif PC'de hem
  ~ kaynak: https://onnxruntime.ai/docs/performance/model-optimizations/quantization.html
- [zayif-cpu] Runtime secimi hedef donanimda A/B olcumu ile yapilmali: Intel CPU'da OpenVINO, ONNX Runtime CPU EP'ye gore %20-40 hizlanma verebilir (YOLOv8n: 28,02 -> 23,53 ms i7-13700H; 39,86 -> 11,34 ms Xeon) ama bazi modellerde OpenVINO EP'nin daha yavas ciktigi raporlanmistir.
  ~ Ultralytics resmi benchmark'lari OpenVINO avantajini, onnxruntime issue #15027 ise ters ornegi gosteriyor; 'her zaman daha hizli' varsayimi yerine olcum sartnameye yazilmali.
  ~ kaynak: https://docs.ultralytics.com/integrations/openvino ; https://github.com/microsoft/onnxruntime/issues/15027
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Araç tetik çizgisini geçtiği andan otopark yazılımına POST tamamlanana kadar uçtan uca gecikme hedefi ölçülüp raporlanmalı: hedef ≤1 sn (RTSP çözme + CPU çıkarım + push dahil), bariyer toplam tepkisi ≤2 sn; ticari sistemler tanıma süresini 5-7 ms mertebesinde pazarladığından gecikme farkı sahada sürücü davranışıyla test edilmeli.
  ~ Yerleşik ANPR kamerada işlem anlıktır; ikame zincir GPU'suz PC'de ek gecikme ekler. Hursoft gibi yerli ürünler 5-7 ms tanıma iddia eder — kullanıcı algısı için üst sınır belirlemek şarttır (≤1 sn/≤2 sn değerleri türetilm
  ~ kaynak: https://www.hursoft.net/urun/otopark-arac-plaka-tanima-fiyatlari
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Uçtan uca gecikme bütçesi katmanlı tanımlanmalı: kamera tarafı ≤50 ms (25 fps'te 40 ms yakalama + ~1 ms encode), LAN ≤5 ms, alım+decode+çıkarım+karar kalan bütçe; hedef PC'de ölçülmüş cam→ANPR-olayı gecikmesi medyan ≤800 ms, p95 ≤1500 ms olmalı.
  ~ Axis beyaz kitabı kamera tarafını <50 ms, LAN'ı birkaç ms ölçüyor; IPVM VMS canlı izlemede ~0.5 sn gecikmeyi 'yaygın' sayıyor. Yani <1 sn hedefi ancak alıcı tamponu disiplinliyse gerçekçi — bütçe çıkarım süresine değil h
  ~ kaynak: https://www.axis.com/dam/public/9d/e4/5d/latency-in-live-network-video-surveillance-en-US-190945.pdf
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] RTSP alım hattı düşük gecikme bayraklarıyla açılmalı: -rtsp_transport tcp, -fflags nobuffer+flush_packets, -flags low_delay, -probesize ≤100k, -analyzeduration ≤1 sn, -max_delay 0, -reorder_queue_size 1; bu ayar seti kabul testinde varsayılan ayarlarla A/B karşılaştırılarak doğrulanmalı.
  ~ PhenixRTS/mediamtx dokümanları bu bayrakları düşük gecikme standardı sayar; Frigate #5459'da aynı optimizasyon ölçülmüş 149 ms gecikme azalması + daha düşük CPU sağladı. Varsayılan ffmpeg probesize/analyzeduration değerl
  ~ kaynak: https://github.com/blakeblackshear/frigate/issues/5459
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Decode tek thread ile yapılmalı (ffmpeg threads=1) ve kare başına decode süresi hedef PC'de ölçülüp kamera kare aralığının (25 fps'te 40 ms) altında kaldığı kanıtlanmalı; aşarsa substream çözünürlüğü/fps düşürülmeli.
  ~ Frigate ölçümünde çok thread'li decode (13 thread) hem gecikme hem CPU açısından tek thread'den kötüydü; frame-threading decode gecikmesine kare başına ek tampon koyar. Zayıf GPU'suz ofis PC'sinde decode kare aralığını a
  ~ kaynak: https://github.com/blakeblackshear/frigate/issues/5459
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Kareler kameranın tam fps'inde kesintisiz tüketilmeli ve ANPR'a daima yalnızca EN GÜNCEL kare verilmeli (ayrı okuma thread'i + son-kare deseni veya grab()/retrieve() ile eski kareleri atma); işlenen karenin yaşı 200 ms'yi aşarsa kare atlanmalı.
  ~ OpenCV VideoCapture iç tamponu tüketilmeyen kareleri biriktirir ve gecikme saniyelere çıkar (OpenCV forum vakaları); Qengineering RTSP-with-OpenCV projesi 'ihmal edilebilir gecikme' için kaçırılan kareleri atıp GetLatest
  ~ kaynak: https://github.com/Qengineering/RTSP-with-OpenCV
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Yazılımın kendi jitter/play-out tamponu en fazla 1-2 kare (≤80 ms) olmalı; VLC benzeri oynatıcı tamponlarıyla (varsayılan network-caching 1000 ms) yapılan gecikme gözlemleri kabul ölçütü olarak KULLANILMAMALI.
  ~ Axis beyaz kitabı alıcı tamponunun uçtan uca gecikmenin EN BÜYÜK kalemi olduğunu (birkaç saniyeye kadar) belirtir; VLC 50 ms cache ayarında bile ~850 ms fiili gecikme ölçülmüştür. Yani doğrulama kendi hattımız üzerinde y
  ~ kaynak: https://forum.videolan.org/viewtopic.php?t=148355
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] ANPR işleme hızı otopark senaryosunda efektif 4-5 fps olarak hedeflenmeli (her kare değil, örnekleme ile); alım hattı ise tam kamera fps'ini tüketmeye devam etmelidir.
  ~ Plate Recognizer otopark (yavaş araç) senaryosu için 4-5 eFPS önerir; zayıf CPU'da her kareyi işlemek decode+çıkarım bütçesini aşar, ama örnekleme alım hattından bağımsız olmalıdır yoksa tampon birikir.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Substream kare hızı gecikme hesabına katılmalı: fps yükseldikçe kare-başına tampon gecikmesi düşer (25 fps→40 ms, 30 fps→33 ms, 15 fps→67 ms); zayıf CPU'da 15 fps altına inilecekse eklenen yakalama gecikmesi bütçede gösterilmeli.
  ~ Axis: her tampon en az 1 kare bekletir, dolayısıyla kare aralığı doğrudan gecikme kalemidir; düşük fps CPU'yu rahatlatır ama hem yakalama gecikmesini hem plaka görünür-kare sayısını azaltır — bu ödünleşim sayısallaştırıl
  ~ kaynak: https://www.axis.com/dam/public/9d/e4/5d/latency-in-live-network-video-surveillance-en-US-190945.pdf
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Karar gecikmesi sınırlandırılmalı: araç tetik bölgesine girdikten sonra en geç 2 sn içinde (konfigüre edilebilir; Plate Recognizer varsayılanı 6 sn) plaka kararı verilmeli; güven eşiği (örn. oylama kararlılığı) erken aşılırsa beklemeden karar gönderilmeli (Survision reliabilityThreshold deseni).
  ~ Plate Recognizer max_prediction_delay=6 sn varsayılanıyla doğruluk/hız dengesi kurar; Survision reliabilityThreshold aşıldığında sonucu anında yollar. Bariyer önünde bekleyen sürücü için toplam gecikme (tanıma+bariyer) i
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/ ; https://fr.survisiongroup.com/documentations/integration_doc.html
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Zayıf CPU hedefi için hedef-seçim hattı ROI kırpma ile çalışmalı: tam kare yerine bariyer tetik bölgesini kapsayan sabit kırpma (AccessALPR'daki anchor + 416x416 deseni) üzerinde tespit koşulmalı; kuyruk yoğun anlarda kare atlama (Plate Recognizer sample=2 deseni) uygulanmalı.
  ~ AccessALPR sabit kameralarda anchor noktasından 416x416 kare keserek gerçek zamanlı çalışıyor; Plate Recognizer sample=2 (her 2. kare) ile CPU yükünü düşürüyor. ROI kırpma aynı zamanda uzak/karşı şerit plakalarını fiziks
  ~ kaynak: https://github.com/glefundes/AccessALPR ; https://guides.platerecognizer.com/docs/stream/configuration/
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] Kamera/crop piksel bütçesi: plaka genişliği ≥100-150 px, karakter yüksekliği ≥15 px, çizgi kalınlığı ≥2 px, bakış açısı ≤45° sağlanmalı; çift satır plakalarda karakterler plaka yüksekliğinin <%50'si olduğundan çift satır crop yüksekliği tek satır asgarisinin (20 px) en az 2 katı (≥40 px) hedeflenmeli.
  ~ Plate Recognizer LPR çözünürlük rehberi ~100 px plaka genişliği tasarım hedefi; eocortex tek satır için min 120x20 px, dipolnet min 15 px karakter yüksekliği/2 px çizgi; OpenALPR çift satırda karakter/yükseklik oranının 
  ~ kaynak: https://platerecognizer.com/lpr-camera-resolution-guide/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Okumadan webhook olay teslimine uçtan uca gecikme bariyer senaryosunda 2 sn'yi aşmamalı; motor içi olay biriktirme gecikmesi (Stream max_prediction_delay eşdeğeri) yapılandırılabilir olmalı ve varsayılan 6 sn'den aşağı çekilmeli.
  ~ Stream varsayılanı sonuç göndermeden önce 6 sn'ye kadar bekleyebilir (daha iyi konsensüs için); bariyer uygulamasında araç beklerken bu kabul edilemez, Türk PMS pazarlaması 20 ms 'tanıma' iddiasıyla hız beklentisini yüks
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/ ; https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr

## tetikleme (26)
- [acik-kaynak] Otopark/bariyer modunda plaka ilk görüldükten sonra olay üretme gecikmesi yapılandırılabilir olmalı ve varsayılan ≤3 saniye olmalı (kötü görüşte 6 sn'ye çıkarılabilir, alt sınır 0.1 sn).
  ~ Plate Recognizer Stream max_prediction_delay parametresi: varsayılan 6 sn, otopark girişi için 3 sn önerisi; 0.1 sn mümkün ama doğruluk kaybı nedeniyle önerilmiyor.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [acik-kaynak] Aynı plakanın yeniden bildirimini engelleyen ayarlanabilir bir bekleme süresi (memory_decay benzeri) olmalı; varsayılan 300 sn, kapıda bekleyen/geri manevra yapan araç için kamera bazında kısaltılabilmeli.
  ~ Plate Recognizer Stream memory_decay=300 sn varsayılanı otopark senaryosu için tanımlanmış dedup mekanizması; bariyere mükerrer aç komutu gitmesini önler.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [ticari-otopark] Tetikleme modları: birincil sanal loop / video tetikleme (ROI'de araç algılama), opsiyonel donanım tetik girişi (loop/fotosel, optokuplör 15-30V) ve yazılım tetiği (API üzerinden triggerOn/triggerOff) desteklenmeli.
  ~ Endüstri üçlüsü: Hikvision 'video detection, I/O coil, radar' tetikleme; Survision free-running (tetiksiz, 60 fps sürekli tarama) + yazılım tetiği + donanım tetiği; Genetec SharpV yerleşik 'virtual loop' analitiği.
  ~ kaynak: https://myfiles.survision.fr/NANOPAK5%20TOTEM%20DATASHEET_English.pdf
- [ticari-otopark] Aynı araç için tekrar eden okumalar tekilleştirilmeli: araç sahneden çıkana kadar tek karar (waitForEnd) veya periyodik yeniden yayın (reemitDecision) seçilebilir olmalı; araç giriş (New) ve çıkış (End) olayları ayrı üretilmeli.
  ~ Survision CDK/SSWS protokolü New/Decision/End olay modelini ve waitForEnd/reemitDecision yapılandırmasını tanımlar; bariyer senaryosunda çifte açma komutunu engeller.
  ~ kaynak: https://fr.survisiongroup.com/documentations/integration_doc.html
- [tr-pazari] Araç gelişini harici tetikleyici (loop/radar) olmadan video akışından tespit edebilmeli, ama istenirse LOOP/manyetik dedektör ile entegre de çalışabilmeli.
  ~ Şartname md.1.20 (tetiksiz çalışma) ve md.1.28 (loop entegrasyonu) — ikisi birden isteniyor; bizim CPU çözümünde hareket/araç tespiti bu şartı karşılamalı.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [standartlar] Aynı araç geçişinden üretilen çoklu okumalar tek olaya indirgenmeli (tekilleştirme); test sayımında bir geçişin çoklu kayıtlarından herhangi biri yanlışsa o geçiş 'yanlış okuma' sayılmalı.
  ~ Home Office Prosedür 2 madde 6'nın açık kuralı; bariyer senaryosunda çift tetiklemeyi de önler.
  ~ kaynak: https://assets.publishing.service.gov.uk/government/uploads/system/uploads/attachment_data/file/909024/ANPR_-_Evaluation_Approved_Version_2.0.pdf
- [kosullar] Aynı araç için ardışık okumalar tekilleştirilmeli (dedup): plaka + zaman penceresi (ör. 5-10 sn) bazında tek 'araç geçti' olayı üretilmelidir.
  ~ Çok-kare işlemenin doğal sonucu çoklu okumadır; Laroca temporal redundancy yaklaşımı ve tüm ticari access-control kameraları (Einar) olay bazlı tekil çıktı verir — bariyer yazılımı çift tetiklenmemelidir.
  ~ kaynak: https://arxiv.org/abs/1802.09567 ve https://adaptiverecognition.com/products/einar-anpr-camera/
- [entegrasyon-operasyon] Servis, bariyer kararı için gruplanmamış ANLIK tek-okuma olayı yayınlamalı; gruplanmış/geç sonuç (10 sn'ye kadar gecikebilen Rekor alpr_group tarzı) yalnız kayıt/rapor için kullanılmalı.
  ~ Rekor Scout dokümanı grup sonuçlarının araç geçtikten 10 sn sonrasına kadar gecikebileceğini ve gerçek zamanlı uygulamalara uygun olmadığını açıkça belirtir; bariyer gerçek zamanlı karar ister.
  ~ kaynak: https://docs.rekor.ai/scout/application-integration/json-group-results
- [entegrasyon-operasyon] Aynı araç için tekrar-tetikleme bastırma (dedup penceresi, örn. plaka+yön bazlı 10-30 sn) ve araç yönü (giriş/çıkış) tespiti zorunlu; bariyerin aynı geçişte ikinci kez açılması engellenmeli.
  ~ Avutec/Park-R entegrasyonu yön tespitiyle bariyerin tekrarlı aktivasyonunu önlemeyi temel akış olarak tanımlar; Rekor da aynı plakanın karelerini tek olayda toplar.
  ~ kaynak: https://avutec.com/api-integration-for-anpr-based-parking-access/
- [entegrasyon-operasyon] Bariyer tetikleme hem yazılım kararıyla (webhook/API üzerinden otopark yazılımı) hem de doğrudan servis çıkışıyla (röle/IO veya kameranın IO'su) yapılabilmeli; giriş ve çıkış yönleri ayrı kanal/karar olarak ele alınmalı.
  ~ Hikvision entegrasyon rehberi bariyer kontrolünün iki yolunu tanımlar: platform üzerinden veya kameranın kendisi üzerinden; ikame yazılım her iki modeli de sunmalı ki farklı otopark yazılımlarıyla uyum sağlansın.
  ~ kaynak: https://tpp.hikvision.com/wiki/isapi/anpr/GUID-328FC5AD-38EA-4409-9E75-996279BF2A46.html
- [zayif-cpu] Surekli tum kareleri isleme yerine hareket-tetikli isleme zorunlu: hareket algilama ilk filtre olmali, tespit yalnizca hareket bolgelerinde calismali; plakasiz/hareketsiz karede erken cikis fps'i ~1,7x artirir (RPi4: %0 pozitif 12,2 fps vs %100 pozitif 7,1 fps).
  ~ Frigate'in kademeli boru hatti (motion box -> region -> detection) zayif CPU NVR'larinin standart pratigi; ultimateALPR benchmark'i pozitif orani dustukce fps'in belirgin arttigini sayisal gosteriyor.
  ~ kaynak: https://docs.frigate.video/configuration/motion_detection/ ve https://www.doubango.org/SDKs/anpr/docs/Benchmark.html
- [zayif-cpu] Otopark bariyer senaryosunda (arac hizi < 40-48 km/h) islenen etkin kare hizi 4-5 eFPS yeterli olmali; kamera 15 fps akisindan ornekleme (her 3. kare) ile dusurulmeli.
  ~ Uc bagimsiz kaynak ayni bandi veriyor: Plate Recognizer eFPS onerisi <30 mph icin 4-5, Rekor Scout dusuk hiz icin 5-10 fps, Milestone XProtect LPR 4 fps/serit onerir. CPU butcesinin ana kaldiraci budur.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/onpremise/system-requirements ; https://docs.rekor.ai/scout/agent/system-requirements ; https://doc.mil
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Kesin geçiş öncesi zorunlu kabul testi: canlı geçişten yakalanan gerçek bir EventNotificationAlert multipart payload'u curl ile otopark yazılımına farklı kaynak IP'den yeniden oynatıldığında yazılım plakayı işlemeli (kayıt VE bariyer kararı); alıcının kaynak-IP veya XML ipAddress/macAddress doğrulaması yaptığı tespit edilirse emülatör bu alanları gerçek kameranın değerleriyle doldurmalı.
  ~ Hikvision'un kendi test yöntemi de ham TCP dinleyiciyle payload yakalamaktır (NetAssist); replay testi, emülatörün olaylarının farklı IP'den geleceği gerçeğini cutover'dan önce ucuza doğrular.
  ~ kaynak: https://www.hikvisioneurope.com/eu/portal/portal/Technology%20Partner%20Program/03-How%20to/How%20to%20get%20%20events%20by%20ISAPI%20listening%20mode
- [kapanis:RTSP + decode boru hattı gecikmesi hiç bütçelenmemiş (uçtan uca <1 sn iddiası sadece çıkarım süresine dayanıyor)] Plaka karar penceresi (çoklu kare oylaması dahil) bariyer senaryosunda 1-2 sn ile sınırlandırılmalı; oylamayı bekletip kararı geciktiren birikimli mod yalnızca serbest-akış/istatistik modunda kullanılmalı.
  ~ Plate Recognizer Stream, park bariyeri için max_prediction_delay'in 1-2 sn'ye düşürülmesini önerir (varsayılan 6 sn'ye kadar bekler — bariyer için kabul edilemez). Oylama doğruluğu ile kapı açma gecikmesi arasındaki deng
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Bariyer şeridi için görüntü üzerinde poligon 'tetikleme bölgesi' (sanal loop) tanımlanabilmeli ve plaka KARARI yalnızca plaka/araç kutusu bu bölge içindeyken üretilmeli; bölge dışındaki plakalar loglanabilir ama bariyer olayı tetikleyemez.
  ~ Adaptive Recognition virtual loop'u 'tek şeride çizilen dijital indüksiyon halkası' olarak tanımlar; Hikvision sarı şerit çizgileri + mavi yakalama çizgisi ile aynı işi yapar. Arkadaki/karşı şeritteki araçların bariyeri 
  ~ kaynak: https://adaptiverecognition.com/blog/traffic-transportation/under-magnifier-software-triggers-anpr-cameras/ ; https://www.clevelandsecuritycameras.com
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Karşı (çıkış) şerit ve yol arka planı, Plate Recognizer'ın zone_<camera-id>.png benzeri maske/poligon ile analiz dışı bırakılabilmeli; maske editörü ekran görüntüsü üzerinden ayarlanabilmeli.
  ~ Plate Recognizer Stream, Detection Zone PNG maskesiyle (siyah=maskeli, beyaz=algılama) istenmeyen yön/şeritlerin ve arka plandaki yazıların okunmasını engelliyor; giriş-çıkış şeritleri yan yana olan otoparkımızda karşı ş
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Araç yaşam döngüsü bitiminde 'plaka ömrü uzatma' tamponu uygulanmalı (Survision endPlateTimeout_ms benzeri, varsayılan ~500 ms): araç görüşten çıktıktan hemen sonra gelen geç tetik/olay isteği son plakayı hâlâ döndürebilmeli.
  ~ Survision endPlateTimeout_ms (~500 ms) 'plakanın yaşamının uzatılması' ile geç gelen trigger'ın yeni ayrılan aracı yakalamasını sağlar; bariyer yazılımından gelecek sorgu/tetik ile görüntü işleme arasındaki yarış durumun
  ~ kaynak: https://fr.survisiongroup.com/documentations/integration_doc.html
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] ANPR yazılımı bariyere yalnızca AÇ (open/trigger) darbesi gönderecek; KAPATMA kararı hiçbir koşulda ANPR PC'sinden verilmeyecek, kapatma yalnızca bariyer kontrol kartının kol altı kapanma/emniyet loop'u 'boş' onayıyla yapılacak.
  ~ Magnetic MHTM kılavuzu: kol altındaki loop daima 'monitor + closing loop'tur; araç loop üzerindeyken bariyer açık kalır, ancak araç loop'u terk edince kapanır. Hikvision how-to da kamera rölesini sadece bariyer kartının 
  ~ kaynak: https://magneticgateopeners.com/store/pdfs/MHTM_Manual.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Kapanma loop'u yoksa veya arızalıysa bariyer, ayarlanabilir otomatik kapanma zamanlayıcısıyla (endüstride 2 sn / 20 sn seçenekli; Türk entegratörlerde 1-6 sn) kapanacak ve araç açtırıp geri çıkarsa (reverse-out) süre aşımıyla kol indirilecek.
  ~ Proconel BL100B: 'Time out if vehicle reverses out (Auto Close)' anahtarla 2/20 sn; Z Sistem: açılma-kapanma süreleri 1-6 sn ayarlanabilir. Plaka okundu ama araç girmedi senaryosunda bariyerin açık kalmaması için.
  ~ kaynak: https://www.proconel.com/wp-content/uploads/2019/04/BL100BDatasheetV03.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Roll-back koruması zorunlu: bariyer kapanırken araç kapanma loop'una geri kayarsa (buzlu rampa!) bariyer derhal yeniden kalkacak ve araç loop'tan ileri çıkana kadar açık kalacak.
  ~ Proconel BL100B 'Roll-back protection' özelliği aynen bunu tanımlar. Erzurum kışında buzlu eğimli otopark girişinde araçların geri kayması gerçekçi ve yüksek riskli senaryodur.
  ~ kaynak: https://www.proconel.com/wp-content/uploads/2019/04/BL100BDatasheetV03.pdf
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Ardışık araç (tailgating) senaryosunda bariyer her aracı ayrı sayacak: ya kamera 'Keep Barrier Open for Following Vehicle' modu ya da loop dedektörün memory input özelliğiyle N aracın geçişi bitene kadar kol inmeyecek.
  ~ Hikvision Intelligent ANPR kamera kılavuzu 4.1.2 'Keep Barrier Open for Following Vehicle'; BL100B 'Memory input' birden fazla aracın kapanma loop'undan geçmesine izin verir. Kuyruklu girişte kolun araç arasına inmesini 
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000178982/Intelligent-ANPR-Camera_User-Manual_V5.4.4_20250923.pdf
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] 'Araç algılandı ama plaka bulunamadı' durumu ayrı bir olay tipi (PLAKASIZ_ARAC) olarak, araç algılandıktan sonra en geç 3 sn içinde bariyer/otopark yazılımına gönderilmeli ve görüntüsüyle birlikte loglanmalı.
  ~ Sektör pratiği: LPR okuyamazsa ~3 sn timeout sonrası otomatik bilet/fallback tetiklenir ve plaka manuel inceleme kuyruğuna alınır; motosiklet girişleri ancak böyle yakalanır.
  ~ kaynak: https://parkingpaymentguide.com/technology/lpr-based-parking-payments/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Bariyer/karar webhook hedefi ile kayıt webhook hedefi ayrılmalı: kayıt hedefinde disk önbellekli retry (3 deneme, sonra 2 dakikada bir tekrar), bariyer hedefinde retry ve önbellek KAPALI olmalı.
  ~ Plate Recognizer dokümantasyonu bariyer açma için caching'i açıkça önermez: başarısız isteğin sonradan tekrar iletimi 'istenmeyen bariyer açılışlarına' yol açar; kayıt için ise her ziyaretin kaybolmaması adına caching şa
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Aynı araç görüş alanında kaldığı sürece tek olay üretilmeli (dedupe); yeniden raporlama aralığı kamera başına yapılandırılabilir olmalı (Stream memory_decay varsayılanı 300 sn; bariyer girişi için 10-60 sn önerilir).
  ~ Bariyerde bekleyen aracın her karede tekrar olay üretmesi PMS'te çift ücretlendirme ve bariyerin tekrar tetiklenmesi riski doğurur; Stream bunu memory_decay parametresiyle çözer.
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Beyaz liste → bariyer aç kararı ANPR servisinde DEĞİL, otopark yazılımında (veya ayrı ince gate modülünde) olmalı; bizde tutulacaksa Gate Opener deseni uygulanmalı: yerel liste (CSV/SQLite), PMS'ten periyodik senkron (~20 sn poll) ve ağ kesintisinde son kopya ile offline çalışma.
  ~ Plate Recognizer ekosistemi bu ayrımı net kurar: Stream okur, Gate Opener yerel whitelist'e bakıp GPIO/röleyi tetikler, ParkPow listeyi yönetir; karar mantığını okuma motoruna gömmek sorumlulukları karıştırır.
  ~ kaynak: https://guides.platerecognizer.com/docs/other-apps/gate-opener/
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Operatörün manuel bariyer açma komutu, mevcut Hikvision kameranın rölesi üzerinden ISAPI ile verilebilmeli: PUT /ISAPI/Parking/channels/1/barrierGate (ctrlMode=open) veya /ISAPI/ITC/Entrance/barrierGateCtrl; böylece bariyer kablolaması değişmeden ikame yapılabilir.
  ~ Kameranın ANPR'ı kapatılsa bile röle çıkışı ve ISAPI bariyer kontrol uçları kullanılabilir; bu, ek röle donanımı almadan geçişin en düşük maliyetli yoludur.
  ~ kaynak: https://ipcamtalk.com/threads/how-to-open-control-barrier-using-hikvision-camera.62002/

## veri (19)
- [acik-kaynak] Her olayda kanıt görüntüsü saklanmalı ve webhook'ta seçilebilir olmalı: tam kare JPEG + plaka kırpıntısı (+ opsiyonel araç kırpıntısı); JPEG kalitesi ~85, koordinatlar (bbox) olay verisinde bulunmalı.
  ~ Rekor Scout store_plates_jpeg_quality=85 ile kırpıntı saklıyor; Plate Recognizer webhook'ta orijinal/araç/plaka kırpıntısı seçenekleri sunuyor. İtiraz/ücret anlaşmazlıklarında kanıt zorunlu.
  ~ kaynak: https://docs.rekor.ai/scout/agent/configuration/agent-properties ; https://guides.platerecognizer.com/docs/stream/configuration/
- [acik-kaynak] Görüntü deposu disk kotalı olmalı (örn. 8000 MB) ve kota dolunca FIFO ile otomatik temizlenmeli; olay meta verisi ayrıca JSONL/CSV günlüğüne yazılmalı.
  ~ Rekor Scout store_plates_maxsize_mb=8000 + FIFO temizlik, Plate Recognizer Stream CSV/JSONL çıktı; zayıf ofis PC'sinde disk dolması servisi çökertmesin.
  ~ kaynak: https://docs.rekor.ai/scout/agent/configuration/agent-properties ; https://guides.platerecognizer.com/docs/stream/configuration/
- [acik-kaynak] TR plakaya özel model stratejisi: global OCR modeli (fast-plate-ocr cct-xs/cct-s) TR veri setiyle (Roboflow 2246 görüntü + Kaggle sentetik plakalar + sahadan toplanan kar/gece örnekleri) ince ayarlanmalı; minimum karakter sayısı filtresi TR için ≥7 olmalı.
  ~ Hiçbir açık kaynak proje hazır TR modeli sunmuyor; fast-plate-ocr eğitim/fine-tune CLI'ı ve ONNX export'u ile TR uyarlaması pratik. Frigate min_plate_length benzeri filtre kısmi okumaları eler (TR plaka 7-8 karakter).
  ~ kaynak: https://github.com/ankandrew/fast-plate-ocr ; https://universe.roboflow.com/plakatanima-vnt3k/turkish-number-plates
- [ticari-otopark] Her geçiş için kanıt paketi: sahne görüntüsü + plaka yakın çekim JPEG, üstyazı (plaka, zaman, konum, yön), NTP ile senkron zaman damgası; görüntülere doğrulanabilir filigran/imza eklenebilmeli.
  ~ Hikvision 'scene picture + close-up picture' ve overlay alanları; Dahua videoda/görüntüde doğrulanabilir watermark; Survision JPEG (3 çözünürlük, ayarlanabilir sıkıştırma) + NTP. İtiraz/ücretlendirme uyuşmazlığında kanıt
  ~ kaynak: https://material.dahuasecurity.com/uploads/cpq/prm-os-srv-res/smart/datasheetzipfiles/ITC413-PW4D-Series_datasheet_20230407.pdf
- [tr-pazari] TR plaka gramerini tam kapsamalı: 01-81 il kodu + harf grubu (1-3 harf) + rakam grubu (2-5 rakam) desenleri (99 X 9999/99999, 99 XX 999/9999, 99 XXX 99/999) ve Ç,Ğ,İ,Ö,Ş,Ü harflerinin plakada bulunmadığı daraltılmış alfabe.
  ~ Format doğrulama (regex/sözlük) yanlış okumaları elemenin en ucuz yolu; Karayolları Trafik Yönetmeliği ve Vikipedi format tablosu bu desenleri tanımlıyor.
  ~ kaynak: https://tr.wikipedia.org/wiki/T%C3%BCrkiye_ta%C5%9F%C4%B1t_plaka_kodu
- [tr-pazari] Sivil beyaz plaka dışında resmi (siyah zemin), polis (mavi zemin), diplomatik (yeşil/beyaz), geçici (sarı) ve yabancı plakaları en azından 'okunamadı/istisna' olarak sınıflandırıp operatöre düşürmeli.
  ~ Perkotek 'yabancı, yerli, askeri, siyah ve mavi plakaları eşit tanır' diyerek bunu pazar standardı yapıyor; belediye otoparkına resmi araçlar sık girer.
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr
- [tr-pazari] Her geçişte plaka metni + HD araç fotoğrafı + tarih/saat + kapı/şerit bilgisi veritabanına yazılmalı; çıkışta giriş-çıkış fotoğrafları yan yana gösterilip kalış süresi otomatik hesaplanmalı.
  ~ Şartname md.1.16, 1.21, 1.31; HGS serbest geçiş pratiği de plaka fotoğrafını tahsilat/itiraz kanıtı olarak kullanıyor — fotoğraf kanıt niteliğinde saklanmalı.
  ~ kaynak: https://www.uludag.edu.tr/dosyalar/yapiisleri/irtifak%20hak%20ihale2016/7-%C3%BCcretli%20otopark%20plaka%20tan%C4%B1ma%20sistemi%20%C5%9Fartnamesi.pdf
- [standartlar] Türk plaka sözdizimi son-doğrulaması: il kodu 01-81 aralığında + 1-3 harf grubu + 2-4 rakam deseni regex ile doğrulanmalı; desene uymayan çıktılar düşük güvenli olarak işaretlenip oylamada cezalandırılmalı, ancak yabancı/standart dışı plakalar da olay olarak kaydedilmeli (sessizce atılmamalı).
  ~ Türkiye taşıt plaka sistemi 01-81 il kodu + harf grubu + seri rakamından oluşur; Home Office kılavuzu yabancı/standart dışı plakaların test kapsamına dahil edilmesini şart koşar.
  ~ kaynak: https://tr.wikipedia.org/wiki/T%C3%BCrkiye_ta%C5%9F%C4%B1t_plaka_kodu
- [kosullar] OCR sonucu Türk plaka düzeni regex'iyle son-doğrulanmalı (il kodu 01-81 + 1-3 harf + 2-5 rakam, TR alfabesinde Q/W/X yok); format dışı okumalar reddedilip yeniden denenmelidir.
  ~ Format post-processing yanlış-pozitifleri eler ve oylamanın etkisini artırır; Türk plakalarına özgü zorluklar (farklı boy/tip/font) akademik çalışmalarda ayrıca belgelenmiştir.
  ~ kaynak: https://ieeexplore.ieee.org/document/8620744/
- [kosullar] Kar/gece/parlama/kirli plaka içeren YEREL (Erzurum) test seti toplanmalı ve model bu koşullarla augmente edilmiş veriyle eğitilmeli/değerlendirilmelidir; kabul testi her koşul dilimi için ayrı doğruluk raporlamalıdır.
  ~ Sensors (MDPI) çalışması: hava-durumu augmentasyonu F1'i 91.69%'dan 94.36%'ya çıkardı (mAP50 96.25%); JIST çalışması motorlar arası dayanıklılık farkının çok büyük olduğunu (parlaklıkta %83.3→%28.6 düşüş vs. etkilenmeme)
  ~ kaynak: https://pmc.ncbi.nlm.nih.gov/articles/PMC11644901/ ve https://library.imaging.org/jist/articles/68/6/060401
- [entegrasyon-operasyon] Raporlama: günlük/haftalık/aylık gelir, doluluk (saatlik/günlük/aylık/yıllık), giriş-çıkış istatistikleri, abone ve alarm raporları üretilebilmeli; Excel/PDF dışa aktarma ve otomatik e-posta dağıtımı desteklenmeli.
  ~ Ertel rehberi bu rapor setini ve Excel/PDF + otomatik e-posta dağıtımını standart sayar; İSPARK plaka tanıma sisteminden saatlik/günlük/aylık/yıllık doluluk raporu alır; Perkotek gün/hafta/ay gelir raporlarını üründe sun
  ~ kaynak: https://www.ertelbilisim.com.tr/otopark-kamera-lpr-raporlama/
- [entegrasyon-operasyon] Olay/oturum veri modeli ileride APDS (ISO standardı) kavramlarıyla (Session, Observation, Place, Rate) eşlenebilir şekilde tasarlanmalı.
  ~ APDS Information Model v4.0 otopark verisi paylaşımının ISO'ca kabul edilmiş uluslararası standardıdır (Place/Right/Rate/Quote/Occupancy/Session/Observation alanları); belediye ölçeğinde gelecek entegrasyonları garanti e
  ~ kaynak: https://allianceforparkingdatastandards.org/
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Beyaz listenin fiziksel yeri tespit edilmeli: kamerada 'Control Mode=By Camera' ise liste kamerada (listType 0:Whitelist 1:Blacklist 2:Graylist 3:Yellowlist 4:Otherlist, plaka başına startTime/endTime) tutuluyor ve bariyeri kamera rölesi açıyor demektir; geçişten önce mevcut liste GET/Export ile tam envanterlenmeli (giriş kameralarında /ISAPI/ITC/Entrance/VCL, yol kameralarında GET /ISAPI/Traffic/channels/<ID>/licensePlateAuditData?fileType=xml).
  ~ TCG kılavuzu iki bariyer kontrol modunu (By Camera / By Platform) açıkça ayırır; liste kameradaysa ikame yazılım eşleştirme + bariyer tetiklemeyi de devralmak zorundadır ve mevcut aboneler kaybolmamalıdır.
  ~ kaynak: https://www.hikvisioneurope.com/eu/portal/portal/Technology%20Partner%20Program/02-Solutioins%20of%20Hikvision%20product%20integration/Fast%20guide%20
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Hangi XML alanlarının tüketildiği kanıtlanana kadar emülatör TAM alan kümesi üretmeli: ipAddress, portNo, protocol, macAddress, channelID, dateTime (ISO8601 + saat dilimi ofseti, Erzurum için +03:00), activePostCount, eventType=ANPR, eventState=active, eventDescription, channelName, ANPR{licensePlate, line, direction, confidenceLevel 0-100, plateType, plateColor, plateCharBelieve (karakter başına 0-100), vehicleType, detectDir, vehicleInfo, pictureInfoList{fileName, type, dataType, plateRect X/Y/width/height}}, UUID, picNum ve yeni firmware alanları originalLicensePlate, deviceId, monitoringSiteID.
  ~ Resmi örnek payload'lar bu alanları içeriyor; node-red-contrib-hikvision-ultimate gibi gerçek tüketiciler captureTime/plateNumber/country/laneNo/direction/matchingResult okuyor — hangi alt kümenin kritik olduğu ancak sah
  ~ kaynak: Fast guide TCG + 7-series 4.3 + https://github.com/Supergiovane/node-red-contrib-hikvision-ultimate
- [kapanis:Mevcut otopark yazılımının GERÇEK entegrasyon yüzeyi doğrulanmamış (ISAPI HTTP push mu, HCNetSDK mi?)] Kameranın 'Matching Result' (whitelist/blacklist/other list eşleşme sonucu) alanını yazılımın tüketip tüketmediği doğrulanmalı; tüketiyorsa ikame ANPR liste eşleştirmesini kendisi yapıp aynı semantikte doldurmalı (aksi halde tüm araçlar 'Other List' görünür ve bariyer açılmaz).
  ~ Kamera web arayüzü Real-time LPR Result tablosunda Matching Result kolonu vardır ve node-red entegrasyonu matchingResult alanını çıkarır; kamera-tarafı liste kararının ikamesi unutulursa sistem işlevsiz kalır.
  ~ kaynak: 7-series guide (2.6 Real-time LPR Result) + https://github.com/Supergiovane/node-red-contrib-hikvision-ultimate
- [kapanis:Aynı karede birden fazla araç/plaka varken hedef araç seçimi ve tailgating (kuyruk problemi)] Dedup PLAKA BAZLI olmalı: aynı plaka aynı kamerada memory_decay süresi (varsayılan 30-60 sn, ayarlanabilir; Plate Recognizer varsayılanı 300 sn) içinde ikinci olay üretmemeli; ancak FARKLI bir plaka bu süreden bağımsız ANINDA yeni olay üretebilmeli (global cooldown yasak).
  ~ Plate Recognizer memory_decay parametresi kamera başına aynı aracın yeniden algılanma süresini tanımlar (varsayılan 300 sn, min 0.1 sn). Kuyruk senaryosunda global bir bekleme süresi arkadaki ikinci aboneyi bariyerde bek
  ~ kaynak: https://guides.platerecognizer.com/docs/stream/configuration/
- [kapanis:Bariyer kapanma mantığı, fiziksel güvenlik (araç üstüne kapanmama) ve toplam sistem arıza modları] Rollback 2. kademe: kameranın yerleşik ANPR'ı ve röle-bariyer kablosu fiziksel olarak SÖKÜLMEYECEK; kameranın SD kartındaki yerel allowlist abone plakalarıyla senkron tutulacak (ISAPI veya XLS import ile, en az günde 1 kez) ve allowlist yazımının 15 dakikaya kadar sürebildiği, işlem sırasında kameranın yeniden başlatılmaması gerektiği dikkate alınacak; SD kart sağlık durumu izlenecek.
  ~ Hikvision kılavuzu: bariyer kameranın röle çıkışına bağlı olmalı, storage card takılı ve normal olmalı; 'Wait for 15 minutes to let the added allowlist write into the storage. Do not reboot the device during the process'
  ~ kaynak: https://assets.hikvision.com/prd/normal/all/doc/m000178982/Intelligent-ANPR-Camera_User-Manual_V5.4.4_20250923.pdf
- [kapanis:Motosiklet (önde plaka yok) ve çift satır/kare plakaların seçilen OCR stack'iyle okunabildiği doğrulanmamış] TR çift satır test/eğitim veri seti kurulmalı: en az 100-200 etiketli görüntü (21x32 kamyon/kamyonet/minibüs arka + 15x24 motosiklet), gece IR siyah-beyaz, kar ve gündüz koşullarını kapsamalı.
  ~ fast-plate-ocr maintainer'ı eğitim verisinin sınırlı olduğunu kabul ediyor (issue #14: 'I don't have other plates cropped and annotated, other than Argentinian ones'); fine-tune ve kabul testi bu set olmadan yapılamaz.
  ~ kaynak: https://github.com/ankandrew/fast-plate-ocr
- [kapanis:Kapsam/sorumluluk matrisi ve operatör konsolu: hangi işlev ANPR servisinde, hangisi otopark yazılımında?] Her geçiş olayı resimleriyle birlikte yerel veritabanına yazılmalı ve plaka/zaman aralığı/kamera ile sorgulanabilmeli; PMS'e teslim edilemeyen olaylar disk kuyruğunda bekletilip sırayla teslim edilmeli, disk dolarken en eski gönderilmemişler budanmalı.
  ~ Türk PMS'leri tüm geçişleri resimli kayıt+sorgu ile sunar; Stream'in disk cache davranışı (dolunca en eski unsent kayıtların silinmesi) kuyruk tasarımına emsaldir — kayıt kaybı ancak kontrollü olabilir.
  ~ kaynak: https://www.perkotek.com/plaka-tanima-sistemi-pts-anpr ; https://guides.platerecognizer.com/docs/stream/configuration/
