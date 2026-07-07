# Otopark Plaka Okuma Yazılımı — Profesyonel Gereksinim Mühendisliği Dokümanı

Tarih: 2026-07-07 · Sürüm: 1.0 · Sahip: OtoparkAnpr
Yöntem: 13 açılı derin web araştırması (7 ana açı + tamlık kritiği + 6 kapanış turu; 424 kaynak
taraması, ~40 birincil doküman/datasheet okundu) × 4+ günlük kendi saha ölçümlerimiz.
Amaç: **Hikvision kamera-içi ANPR bağımlılığının, otopark PC'sinde çalışan kendi exe'mizle tam ikamesi.**

**Katı kurallar (her gereksinim bunlara göre süzüldü):**
1. **Türkiye plakaları** (il 01-81 + 1-3 harf [Ç,Ğ,İ,Ö,Ş,Ü,Q,W,X yok] + 2-5 rakam)
2. **Zayıf, GPU'suz otopark PC'si** (CPU-only; .NET exe; bulut yok, internet bağımsız)
3. Kameraya **yalnız salt-okuma** erişim; hiçbir kamera ayarı biz değiştirmeyiz (gereken ayarlar
   "kurulum ön-koşulu" olarak dokümante edilir, kararı işletmeci verir)
4. Her koşul: otopark açık/kapalı, kamera filtresi siyah-beyaz IR/normal, tüm hava/ışık koşulları

---

## 1. Yönetici Özeti

Araştırmanın en önemli üç bulgusu:

**(a) Mimarimiz sektörle örtüşüyor.** İstisnasız her ciddi ürün (Rekor Scout, Plate Recognizer
Stream, Frigate, Survision, ultimateALPR) aynı çekirdek desene dayanıyor: *çok-kare oylama + TR
format doğrulama + ROI/sanal loop + plaka-bazlı dedup + webhook olayı*. Bunların hepsi bizde var ve
ölçülmüş durumda. CPU-only da kanıtlanmış bir ürün mimarisi: Plate Recognizer Stream 2 kamerayı
Passmark ~2440'lık i3'te işliyor; bizim 27.6 ms/kare Mac ölçümümüz sektör bandının (60-110 ms/kare)
iyi tarafında.

**(b) Eksikler "motor"da değil, motor ile saha arasındaki dikişlerde.** Okuma doğruluğu tarafında
kapatılacak az şey kaldı; asıl eksik sınıflar: olay sözleşmesi (kanıt görüntüsü, aday listesi, UUID,
retry'lı webhook), hedef-araç seçimi/kuyruk (tailgating), sağlık izleme/alarm, KVKK (saklama+erişim),
ve arıza modları (rollback). Bunlar bölüm 4'te gereksinime döküldü.

**(c) İki kritik varsayım hâlâ DOĞRULANMAMIŞ** ve tüm entegrasyon planını taşıyor:
mevcut otopark yazılımının kamerayla GERÇEKTE hangi protokolle konuştuğu (HCNetSDK arming ise XML
emülasyonu hiç çalışmaz!) ve hedef PC'de uçtan uca cam→olay gecikmesi. İkisi de salt-okuma yöntemle
Faz-0'da doğrulanabilir (bölüm 6).

## 2. Sorumluluk Matrisi (kapsam sınırı — kontrolsüz büyümeye karşı)

Sektör işlevleri üç katmana ayırıyor (Plate Recognizer Stream/Gate-Opener/ParkPow üçlüsü, Avutec,
Perkotek aynı bölünme). Bizim proje **1. katman**dır:

| Katman | İşlev | Kimde? |
|---|---|---|
| **1. Okuma motoru (BİZ)** | RTSP alımı, tespit+OCR, çok-kare oylama, TR doğrulama, ROI, dedup, güven skoru, olay üretimi+kanıt görüntüsü, webhook, sağlık/heartbeat, kalibrasyon UI (panel), düşük-güven kuyruğu | **OtoparkAnpr** |
| 2. Karar/geçiş katmanı | beyaz liste eşleşmesi → bariyer AÇ tetiği; offline liste cache | Otopark yazılımı (veya ileride ince "gate" modülü; Gate-Opener deseni) |
| 3. Otopark yönetimi (PMS) | tarife/ücret, abonelik+borç+SMS, kara liste, vardiya/gelir/doluluk raporları, kasa-operatör ekranı, manuel bariyer butonu | Mevcut otopark yazılımı — **kapsam DIŞI**, webhook ile beslenir |

Sektör güvenlik kuralı (Magnetic/UL 325): **bariyer KAPATMA kararını hiçbir ANPR yazılımı vermez** —
kapatma kol-altı emniyet loop'unun işidir. Biz yalnız "plaka okundu" olayı (ve en fazla AÇ tetiği) veririz.

## 3. Sayısal Hedefler (sektör çıtaları → bizim kabul kriterlerimiz)

| Metrik | Sektör referansı | Bizim kabul kriterimiz |
|---|---|---|
| Yakalama (capture) | NASPLE sabit sistem %98; Hikvision datasheet ≥%99 | Gündüz kapı-kapsamlı **≥%98** (bugünkü ölçüm: 22/22) |
| Okuma (read, yakalananın) | NASPLE %95 (TÜM karakterler; N-1 gevşetmesi YOK); Hikvision ≥%98.5 | Gündüz **≥%95**, hedef %98+; gece ayrı raporlanır |
| İkame (cutover) kriteri | gölge çalışmada kamera ile eşleşme ≥%98 | **≥1 hafta panel gölge çalışması, kapı-trafiğinde eşleşme ≥%98** |
| Uçtan uca gecikme | cam→olay medyan ≤800 ms, p95 ≤1.5 s; bariyer toplamı ≤2 s (2 s üstü kuyruk yaratır) | hedef PC'de ÖLÇÜLECEK (Faz-0/1); yazılım-içi karar penceresi ≤2 s |
| CPU/RAM | kamera başına ≤1 çekirdek + ~500 MB (Stream/Scout normu); ort CPU <%50 | 2 kamera + panel: ort **<%50**, RSS **<1 GB** (bugün: %13, 150-300 MB ✅) |
| Plaka piksel genişliği | ≥130 px öneri (100-170 bandı); karakter yüksekliği ≥15-20 px | kurulum doğrulamasında panelden ölçülür; `MinPlateWidthRatio` saha ayarı |
| Kesintisizlik | 7/24 insansız; RTSP kopmada üstel geri çekilme (tavan 600 s) | 24h+ 0-restart kanıtlı ✅; hedef PC'de ilk hafta izlenir |
| Ölçüm raporu | capture × read AYRI + uçtan uca; N/N-1/N-2 kırılımı; neden taksonomisi | `camera_compare` + koşul tablosu (mevcut) + neden etiketi eklenecek |

Kabul testi metodolojisi (Home Office "ANPR Performance Assessment" uyarlaması): **en az 250 ardışık
araç**, bağımsız ground-truth (video/insan gözü — kamera %100 değil, gece kanıtladık), gündüz + gece
+ (Erzurum için) KAR ayrı kümeler, "insan gözüyle okunamayan plaka payda dışı ama ayrı sayaçta",
aynı geçişin çoklu okumaları tek sayılır, tek karakter hatası = yanlış okuma. Yıllık tekrar + yılda
bir 20 bilinen-plaka kontrollü geçiş (2+ kaçırma → düzeltici işlem).

## 4. Gereksinim Seti × Mevcut Durumumuz

Durum işaretleri: ✅ karşılanıyor (ölçülmüş) · 🟡 kısmen var · ❌ eksik · 📋 saha/işletmeci kararı
(yazılım dışı ön-koşul). Öncelik: P1 (ikame için şart) · P2 (profesyonel ürün için şart) · P3 (iyileştirme).

### 4.1 Doğruluk

| # | Gereksinim | Durum | Öncelik |
|---|---|---|---|
| D1 | Çok-kare oylama: olay ≥2 tutarlı okuma, karakter-pozisyon bazlı çoğunluk (literatür: tek kare %64.9→oylamayla %78.3, UFPR) | ✅ PlateConfirmer (V2+voting %87.5→%100 ölçüldü) | — |
| D2 | TR format doğrulama + pozisyona göre O→0/I→1 düzeltme; format dışı olaya dönüşmez, ayrı loglanır | ✅ TurkishPlateNormalizer + kısıtlı çözümleme | — |
| D3 | Olay başına toplam + karakter-bazlı güven + **top-N aday listesi** (whitelist toleransı için) | 🟡 güvenler var; aday listesi dışa verilmiyor | P2 |
| D4 | Tespit ve tanıma güven eşikleri ayrı ayrı yapılandırılabilir | ✅ EmitMinConfidence / EmitMinOcrConfidence | — |
| D5 | Yakalama ve okuma AYRI metrik; günlük rapor; format-geçerli yanlış okuma <%1 | 🟡 ölçüm harness'ta var; günlük otomatik rapor yok | P2 |
| D6 | Çift satır plaka (21x32 kamyon arka, 15x24 motosiklet): CCT stack'te DOĞRULANMAMIŞ (fast-plate-ocr issue #39: karakterler karışıyor). Önden okuyan şeritlerde ön plaka daima tek satır 11x52 → mevcut geometri düşük risk; arkadan okuma eklenirse ≥100 örnekli kabul testi + satır-bölme veya fine-tune şart | 📋 kapsam sınırı yazılacak | P3 |
| D7 | Resmi (siyah), polis (mavi), diplomatik, geçici (sarı) plakalar en azından "istisna" olarak sınıflanıp operatöre düşer | ❌ (normalizer sivil desene göre) | P3 |

### 4.2 Tetikleme ve hedef-araç seçimi (kuyruk problemi)

| # | Gereksinim | Durum | Öncelik |
|---|---|---|---|
| T1 | Poligon tetik bölgesi (sanal loop); karar YALNIZ bölge içinde üretilir | ✅ RoiPolygon + panel çizimi (canlı: gürültü 0'a indi) | — |
| T2 | Plaka-bazlı dedup: aynı plaka pencere içinde tek olay; FARKLI plaka ANINDA yeni olay (global cooldown yasak) | ✅ DedupWindow plaka bazlı | — |
| T3 | Karar penceresi sınırlı (bariyer için 1-2 s; birikimli mod sadece kayıt için) | 🟡 SettleWindow=3 s; bariyer profili ayrı değil | P2 |
| T4 | ROI'de birden fazla FARKLI plaka → deterministik hedef seçimi: (1) en büyük plaka-px, (2) eşitlikte en alttaki; seçim meta verisi olaya yazılır | ❌ | P1 |
| T5 | Hafif iz sürücü (IoU/centroid): oylama YALNIZ aynı track'in kareleri üzerinde; iki aracın kareleri asla birleşmez | 🟡 metin-benzerliği track'i var (varyant-seti); uzamsal track yok | P2 |
| T6 | Yön filtresi (in/out/any): giriş kamerası yalnız yaklaşan araç için olay üretir; ters yön ayrı loglanır | ❌ | P2 |
| T7 | Min plaka piksel genişliği eşiği (öneri ≥130 px, 100-170 bandı); altındakiler "uzak araç" | 🟡 MinPlateWidthRatio mekanizması var (şu an 0; ROI aynı işi görüyor) | P2 |
| T8 | "Araç var ama plaka kararı yok" → X sn içinde PLAKASIZ_ARAC/okunamadı olayı (interkom/manuel akışı tetikler); tailgating sayımı için plakasız araç tespiti | ❌ (plaka-yalnız çalışıyoruz) | P2 |
| T9 | Tailgating: bariyer-açık penceresinde 2+ farklı araç → ihlal olayı + kanıt kare dizisi | ❌ (karar katmanıyla birlikte tasarlanır) | P3 |

### 4.3 Olay sözleşmesi ve entegrasyon

| # | Gereksinim | Durum | Öncelik |
|---|---|---|---|
| E1 | **Entegrasyon yüzeyi keşfi**: otopark yazılımı ↔ kamera trafiği 48 saat izlenir (port-mirror/Wireshark) + `GET /ISAPI/Event/notification/httpHosts` (salt-okuma) + web arayüzü ekran görüntüleri → mod yazılı belgelenir. PC→kamera:8000 kalıcı TCP = **HCNetSDK arming (XML emülasyonu ÇALIŞMAZ → karar kapısı)**; kamera→PC multipart POST = ISAPI push (emüle edilebilir) | ❌ **EN KRİTİK BİLİNMEYEN** | **P1** |
| E2 | Webhook olayı: plate, ISO8601 ts (+03:00), camera_id, direction, confidence, candidates, bbox, plaka+sahne JPEG; tekil event_id (UUID); versiyonlu sözleşme | 🟡 WebhookUrl + JSON var; UUID/candidates/görüntü yok | P1 |
| E3 | Kayıt webhook'unda diskli retry (3 deneme + 2 dk'da bir tekrar; hiçbir olay kaybolmaz); **bariyer webhook'unda retry KAPALI** (gecikmiş tekrar = istenmeyen açılış) | ❌ retry/kuyruk yok (events.jsonl yerel yedek ✅) | P1 |
| E4 | Hikvision uyumluluk modu (E1 "ISAPI push" derse): multipart/form-data içinde anpr.xml (EventNotificationAlert v2.0, sahada yakalanan namespace birebir) + licensePlatePicture.jpg + detectionPicture.jpg; replay kabul testi (curl ile gerçek payload) | ❌ (E1'e bağlı) | P1 |
| E5 | Heartbeat/sağlık webhook'u (30 sn: kamera durumu, işlenen kare, kuyruk) | ❌ (/health çekme-modeli var) | P2 |
| E6 | NTP senkron (kamera+PC aynı kaynak; digest auth <5 dk sapma şartı; olaylar YAKALAMA zamanıyla loglanır) | 📋 saha kurulumu (kamera saati +61 s sapması ölçüldü!) | P1 |
| E7 | Manuel bariyer aç yolu korunur: `PUT /ISAPI/.../barrierGateCtrl` kamera rölesi üzerinden (kablolama değişmez) — YAZMA işlemi, yalnız işletmeci onayı + geçiş fazında | 📋 karar kapısı | P2 |
| E8 | Kamera ID sabitliği (PMS geçmişi kopmasın) | ✅ giris/cikis sabit | — |

### 4.4 Koşullar (ürünün "her koşulda" matrisi → bölüm 5)

| # | Gereksinim | Durum | Öncelik |
|---|---|---|---|
| K1 | Gece/IR (siyah-beyaz) profili: eşikler gece için ayrı ayarlanabilir; tek-kanal gri OCR tam performans | 🟡 gri çalışıyor (gece %84 crop-exact); ayrı gece profili yok | P2 |
| K2 | Kare kalite skoru (bulanıklık/pozlama) → kötü kare OCR'a girmez; araç ROI'deyken 0.5-2 s yeniden deneme | 🟡 oylama fiilen retry; açık kalite skoru yok | P3 |
| K3 | Kar/sis ön-işleme (denoising+kontrast) — ölçümle kanıtlanırsa (deskew dersi: ölçmeden açma!) | ❌ kış verisi yok | P2 (kış öncesi) |
| K4 | "Okunamaz plaka" (IR patlaması, kar örtüsü) ayrı olay tipi + operatör fallback akışı; İzlanda referansı: %4.5 okunamaz → **manuel istisna kapasitesi tasarımda** | ❌ | P2 |
| K5 | Kurulum ön-koşulları (KAMERA AYARI — biz DOKUNMAYIZ, işletmeciye yazılı liste): IR şiddeti %40-60, enstantane ≥1/500, AGC/BLC sınırlama, H.264+ KAPALI, GOP=fps (≈1 s; varsayılan 50=2 s kör pencere), substream düz H.264 | 📋 dokümante edilecek | P1 (doküman) |
| K6 | Süper-çözünürlük/füzyon YALNIZ "okunamadı" kuyruğunda ikinci şans (gerçek zamanlı hatta değil) | ❌ (plan dosyasında Phase B olarak mevcut) | P3 |
| K7 | Erzurum yerel koşul test setleri: kar, gece, alçak güneş, kirli plaka — koşul dilimi başına ayrı doğruluk raporu | 🟡 gece/sabah/gündüz var; **KAR YOK (kış öncesi kritik)** | P1 (mevsimsel) |

### 4.5 Performans (zayıf CPU)

| # | Gereksinim | Durum | Öncelik |
|---|---|---|---|
| P1a | Efektif 4-5 fps yeterli (araç <40 km/h; 3 bağımsız kaynak); alım hattı tam fps tüketir, ANPR örnekler; işlenen kare yaşı >200 ms ise atla | ✅ Fps=5/IdleFps=2 + en-güncel-kare deseni | — |
| P1b | ms/kare hedef donanımda ≤300 ms (referans bandı 60-110 ms); Mac: 27.6 ms ✅; **hedef PC ölçümü YOK** | ❌ Faz-1 | **P1** |
| P1c | AVX/AVX2 CPUID kontrolü kurulumda; AVX'siz CPU'da kontrollü uyarı ("illegal instruction" bilinen çökme sınıfı; OpenVINO 2026+ AVX2 şart) | ❌ | P1 |
| P1d | INT8 kararı hedef CPU'ya göre: VNNI'de 6x; yalnız-AVX2'de U8U8+reduce_range gerekir; AVX'sizde kazanç yok olabilir — A/B + doğruluk regresyonu şart (ConvInteger C#'ta yok — ölçüldü) | ✅ bilgi/plan (Faz-1'de A/B) | P2 |
| P1e | 720p H.264 substream değerlendirmesi (~%48 fps kazancı; plaka ≥130 px şartı sağlanıyorsa) | ❌ denenmedi (mainstream'deyiz) | P2 |
| P1f | Düşük gecikme RTSP bayrakları (-fflags nobuffer, -flags low_delay, -max_delay 0, reorder_queue_size 1, threads=1) A/B ile | 🟡 tcp+kısmi; tam set A/B yapılmadı | P2 |
| P1g | Kare yaşı sürekli izlenir; 1 s eşiği 5 s aşılırsa otomatik stream reset + olay | 🟡 IsConnected yaş-bazlı (watchdog düzeltmesi); metrik dışa verilmiyor | P2 |
| P1h | Windows'ta Ubuntu'ya göre %16-22 düşük fps ile kapasite planı | 📋 Faz-1 hesabı | P2 |

### 4.6 İzleme, operasyon, dayanıklılık

| # | Gereksinim | Durum | Öncelik |
|---|---|---|---|
| O1 | Sağlık skoru + telemetri: eFPS, atlanan kare, ms/kare p50/p95, CPU/RAM; günlük okuma sayısı baseline'ı — düşüşte alarm (odak/açı/kir/IR arızası işareti) | 🟡 /health + panel rozetleri; skor/baseline/alarm yok | P2 |
| O2 | Kaçırma/yanlış okuma NEDEN taksonomisi (kamera-dışı, kar/çamur, IR farkı, font...) test formlarında | 🟡 elle yapıyoruz; şablonlaştırılacak | P3 |
| O3 | Windows servisi: Automatic (Delayed Start) + `sc failure` (5 sn restart ×2, 3.'de reboot, 24 h reset) | 📋 hedef PC kurulum listesi | P1 (doküman) |
| O4 | PC ayağa kalkma zinciri: BIOS "Restore on AC Power Loss=On" + bootstatuspolicy + UPS çıkış güç-döngüsü + (varsa) donanım watchdog — "fişi çek" tatbikatıyla test | 📋 kurulum listesi | P1 (doküman) |
| O5 | **Rollback (hazır mekanizma!)**: kameranın yerleşik ANPR'ı + röle kablosu SÖKÜLMEZ; Hikvision "Barrier Gate Control Offline/By Camera" modu = PC çökünce kameraya otomatik devir; geçiş öncesi "PC fişini çek" tatbikatı; Control Mode değişimi ≤5 dk'da geri alınabilir | 📋 geçiş planına yazıldı | P1 |
| O6 | Düşük-güven inceleme kuyruğu: eşik altı okuma otomatik olay üretmez, operatöre resim+aday listesiyle düşer; düzeltmeler etiketli veri olarak fine-tune'a döner | ❌ (veri fabrikamıza doğal ek) | P2 |
| O7 | Kalibrasyon UI (teknisyen): canlı önizleme + ROI çizimi + son okumalar + kamera sağlığı | ✅ **panel bunu karşılıyor** (sektörde ayrı ürün!) | — |
| O8 | Acil durum/yangın: bariyerler koşulsuz açılır — bariyer kontrol kartı seviyesi, YAZILIM DIŞI; dokümante edilir | 📋 | P2 (doküman) |
| O9 | Motosiklet: TR'de ön plaka YOK (Madde 54) → önden okuyan kapı FİZİKSEL olarak okuyamaz (kamera da okuyamaz — parite korunur). Belediye politika kararı: ayrı alan/muafiyet VEYA arkadan okuyan ek kamera + buton/interkom fallback | 📋 politika kararı | P2 |

### 4.7 Güvenlik ve KVKK

| # | Gereksinim | Durum | Öncelik |
|---|---|---|---|
| G1 | Plaka = kişisel veri (Kurul 2023/924; ihlal emsali 75.000 TL): girişe katmanlı aydınlatma tabelası; hukuki dayanak m.5/2(c/e/f), açık rıza değil | 📋 belediye/işletmeci | P1 (doküman) |
| G2 | Saklama süresi sınırlı + OTOMATİK imha (kamu pratiği 30-45 gün; NASPLE yerel 90 gün) — events.jsonl + kanıt görüntüleri + kırpıntılar dahil | ❌ süresiz birikiyor | P1 |
| G3 | Panel/API kimlik doğrulama + rol (izleyici/operatör/yönetici) + işlem denetim logu (düzeltme/arama dahil) | ❌ panel açık (LAN'da) | P2 |
| G4 | Tamamen yerel işleme; TLS; plaka verisi ücretsiz bulut servislerine gönderilmez | ✅ mimari gereği yerel; TLS hedef PC'de | — |
| G5 | Açık kaynak lisans hijyeni: MIT (fast-plate-ocr, ONNX Runtime, SkiaSharp) ✅; AGPL/SSPL kod alınmaz (yalnız desen referansı) | ✅ mevcut yığın MIT/Apache | — |
| G6 | Plakayla tek-faktörlü dış sorgu ekranı açılmaz (ikinci doğrulama şart) | 📋 PMS tasarım notu | P3 |

## 5. Koşul Matrisi × Ölçülmüş Durumumuz

| Koşul | Kamera (referans) | Biz (ölçülmüş) | Kalan boşluk + kapatma yolu |
|---|---|---|---|
| Gündüz, otopark açık | %100 kabul | **ROI+fps5 sonrası: yakalama 22/22, 30 dk pencerede 13/13 birebir** | — (izlemede) |
| Akşam/alacakaranlık | %100 kabul | %89 (8/9 canlı) | veri birikimi; K2 kalite skoru |
| Gece + IR siyah-beyaz filtre | kamera DA hata yapıyor (15+ UNKNOWN, kendi-çelişkileri ölçüldü) | %84 crop-exact (fine-tune ile %75→%84) | gece kırpıntısı birikimi → re-tune; K1 gece profili; sektör bulgusu: gece kaybının %80'i IR aşırı pozlama = KAMERA AYARI (K5 ön-koşul listesi, işletmeci kararı) |
| Sabah alçak güneş | %71 (bizimle BERABERE — ölçüldü) | %71 | optik sınır, model-bağımsız; K5 + veri |
| Kar / kış (Erzurum!) | bilinmiyor | **HİÇ ÖLÇÜLMEDİ** | İzlanda referansı: IR+ayarla %99 mümkün AMA %4.5 fiziksel okunamaz → K4 manuel istisna + kış verisi hasadı (P1, kar yağmadan hazırlık) |
| Yağmur/sis | bilinmiyor | ölçülmedi | sonbahar hasadı + K3 |
| Otopark kapalı (gece boş) | UNKNOWN'lar logluyor | 15 dk boş-sahne testi: 0 yanlış emit ✅ | — |
| Kirli/eğik/hasarlı plaka | ikisi de kaçırıyor (25ACV952 her oturumda) | aynı | fiziksel plaka sorunu; O6 düşük-güven kuyruğu + D3 aday listesi |
| Hızlı araç | %100 kabul | fps5 + ROI sonrası 5/5 ✅ | — |
| Kuyruk (2+ araç ROI'de) | şerit başına 1 plaka kuralı | **belirsiz — T4/T5 yok** | P1/P2 hedef seçimi işi |

## 6. Doğrulanmamış Kritik Varsayımlar → Faz-0 (hepsi SALT-OKUMA)

1. **Entegrasyon yüzeyi (E1)**: Otopark yazılımı kameradan olayı nasıl alıyor? HCNetSDK arming (TCP
   8000) ise XML emülasyonu çalışmaz → karar kapısı: (a) otopark yazılımı üreticisinden HTTP alıcı
   dokümanı, (b) kamera rölesi yolu, (c) DB/API beslemesi. Yöntem: 48 h trafik izleme + httpHosts
   GET + ekran görüntüleri. **Bu doğrulanmadan uyumluluk-modu geliştirmesine BAŞLANMAZ.**
2. **Gecikme bütçesi**: cam→olay medyan/p95 hedef PC'de ölçülmedi (Mac'te bile formel ölçülmedi).
   Yöntem: kamera OSD zaman damgası vs olay zamanı (≥100 örnek); hedef: medyan ≤800 ms, p95 ≤1.5 s.
3. **Hedef PC hızı** (gap-d): ms/kare, AVX var mı, INT8/OpenVINO A/B — Faz-1 pilotu.
4. **Beyaz listenin fiziksel yeri**: kamerada mı (By Camera) PMS'te mi (By Platform)? "Matching
   Result" alanını PMS tüketiyor mu? (tüketiyorsa ikame yazılım liste eşleştirmesini kendisi doldurmalı).
5. **Çift satır** yalnız arkadan-okuma senaryosunda gündeme gelir (D6) — mevcut iki kapı da önden okuyor.

## 7. Genişletilmiş Yol Haritası

Mevcut yol haritasının (gap-analizi §5) üzerine, araştırmanın eklediği fazlar:

- **Faz 0 — Keşif (salt-okuma, kural ihlali yok; 1-2 gün):** E1 entegrasyon yüzeyi tespiti +
  gecikme ölçüm düzeneği + beyaz liste envanteri (GET) + kamera mevcut ayar envanteri (GET; H264+/GOP
  durumu — değiştirmeden, sadece rapor). Çıktı: "Entegrasyon Keşif Raporu" + karar kapısı.
- **Faz 1 — Hedef-PC pilotu (mevcut plan + eklemeler):** ms/kare + AVX/CPUID kontrolü + INT8/OpenVINO
  A/B + 720p substream A/B + düşük-gecikme ffmpeg bayrakları A/B + Windows servis kurulum listesi (O3/O4).
- **Faz 2 — Olay sözleşmesi v1 (bizim tarafın en büyük geliştirmesi):** E2 (UUID, candidates, kanıt
  JPEG'leri, disk kotalı görüntü deposu) + E3 (retry'lı kayıt webhook'u / retry'siz bariyer kanalı)
  + E5 heartbeat + G2 otomatik imha. Ardından E1 sonucuna göre E4 Hikvision uyumluluk modu VEYA
  üretici API entegrasyonu.
- **Faz 3 — Hedef seçimi ve kuyruk:** T4 (en büyük+en alttaki kuralı) + T5 (IoU track) + T6 (yön) +
  T8 (PLAKASIZ_ARAC) + T7 saha px eşiği. Panel gölge çalışmasıyla A/B doğrulanır.
- **Faz 4 — KVKK + operasyon:** G2/G3 (saklama+auth+audit) + O1 sağlık skoru/alarmlar + O6 düşük-güven
  kuyruğu (veri fabrikasına bağlanır) + G1 tabela (belediye).
- **Faz 5 — Kabul ve geçiş:** 250-araç NASPLE-uyarlı kabul testi (gündüz+gece[+kar]) + ≥1 hafta panel
  gölge çalışması (eşleşme ≥%98) + O5 rollback tatbikatı ("PC fişini çek") → cutover.
- **Sürekli:** koşul-tetikli re-fine-tune (mevcut veri fabrikası) — **kış verisi en yüksek öncelik**;
  yıllık 250-okuma değerlendirmesi + 20 bilinen-plaka spot testi.

## 8. Kapsam DIŞI (bilinçli — kapsam şişmesine karşı)

Tarife/ücret/abonelik/borç/SMS/raporlama/kasa ekranı (PMS'in işi — webhook ile beslenir) · bariyer
KAPATMA kararı (asla yazılımda; emniyet loop'u) · bariyer fiziksel güvenliği UL 325 gereksinimleri
(bariyer kontrol kartı + loop donanımı; kurulum dokümanına referans olarak eklendi) · motosiklet ön
plaka okuma (fiziksel imkânsız — politika kararı) · gerçek-zamanlı süper çözünürlük (yalnız
ikinci-şans kuyruğu, K6) · buluta veri gönderme.

## 9. Ana Kaynaklar

Rekor Scout agent config/benchmark (docs.rekor.ai) · Plate Recognizer Stream/Gate-Opener/ParkPow
kılavuzları (guides.platerecognizer.com) · Frigate LPR (docs.frigate.video) · ultimateALPR (Doubango)
· NASPLE v3.4 + Home Office ANPR Performance Assessment v2.0 · IEC 62676-4/-5 · Hikvision ISAPI ANPR
kılavuzları (tpp.hikvision.com; indirilen PDF'ler: anpr_tcg_isapi.pdf, anpr_7series.pdf,
isapi_listening.pdf) · DS-TCG227/TCG406 datasheet'leri · Survision entegrasyon API'si · Magnetic MHTM
kılavuzu + UL 325/DASMA TDS-353 · Uludağ Üniv. PTS şartnamesi + Perkotek/Traffix/ISSD (TR pazarı) ·
KVKK Kurul kararı 2023/924 · UFPR-ALPR/CCPD benchmark literatürü · MF-LPR² (2025) · Karayolları
Trafik Yönetmeliği (plaka desenleri, Madde 54).

Ayrıntılı gereksinim havuzu (278 madde, kaynak URL'leriyle) ve 13 araştırma özeti:
`docs/research/2026-07-07-anpr-arastirma-ham-bulgular/` altında saklanır.
