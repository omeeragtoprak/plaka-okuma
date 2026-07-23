namespace Anpr.Sdk.Configuration;

/// <summary>appsettings.json "Anpr" bölümünün karşılığı.</summary>
public sealed class AnprServiceOptions
{
    public const string SectionName = "Anpr";

    /// <summary>
    /// Aktif (araç varken) saniyede işlenecek kare sayısı. ffmpeg de bu hızda kare üretir.
    /// ÖLÇÜM (2026-07-02, kamera karşılaştırması): fps artırmak recall'i KURTARMIYOR (kaçan araçlar
    /// kaynak-yakalama/OCR limiti, işlem fps'i değil) ama gürültüyü SERT artırıyor (precision 57→28%) ve
    /// zayıf CPU'yu boğuyor. Bariyerde araç 3-8 sn durduğu için 2 fps bile 6-16 kare = yeterli oylama.
    /// → Zayıf ofis PC için varsayılan 2 (güvenli); güçlü PC'de (AVX2/4-çekirdek) ölçerek 3-4'e çıkılabilir.
    /// </summary>
    public double Fps { get; set; } = 2;

    /// <summary>Boştayken (araç yokken) saniyede işlenecek kare. CPU tasarrufu için düşük tutulur.</summary>
    public double IdleFps { get; set; } = 1;

    /// <summary>Bir tespitten sonra bu kadar saniye "aktif" hızda işlenir (araç geçişini tam yakalamak için).</summary>
    public double ActiveHoldSeconds { get; set; } = 4;

    /// <summary>Tetik bölgesi: OCR yalnızca plaka genişliği kare genişliğinin bu oranından büyükse. 0 = kapalı.</summary>
    public float MinPlateWidthRatio { get; set; } = 0.0f;

    /// <summary>Aynı plaka aynı kamerada bu süre içinde tekrar görülürse TEK olay sayılır.</summary>
    public int DedupWindowSeconds { get; set; } = 8;

    /// <summary>OCR'ın çalıştırılacağı minimum dedektör güven skoru.</summary>
    public float MinConfidence { get; set; } = 0.5f;

    /// <summary>ONNX Runtime IntraOp thread sayısı (zayıf CPU için 2).</summary>
    public int OnnxIntraOpThreads { get; set; } = 2;

    /// <summary>
    /// ONNX thread'leri boşta dönerek mi beklesin? Zayıf PC'de false = boşta CPU ~0 (uyarlanabilir fps ile
    /// çoğu zaman boşta); kare-başı gecikme birkaç ms artabilir. Ölçümle karar verin.
    /// </summary>
    public bool OnnxAllowSpinning { get; set; } = true;

    // Varsayılanlar SDK ile gelen modellere işaret eder (paket, çıktı dizinine models/ kopyalar) —
    // PMS tarafında sıfır model-yapılandırmasıyla çalışır. Servis appsettings.json ile ezmeye devam eder.
    public string DetectorModelPath { get; set; } = "models/yolo-v9-t-384-license-plates-end2end.onnx";
    public string OcrModelPath { get; set; } = "models/cct_xs_v2_erz.onnx";
    public string OcrConfigPath { get; set; } = "models/cct_xs_v2_erz_plate_config.yaml";

    /// <summary>Dedektör ham skor eşiği.</summary>
    public float DetectorScoreThreshold { get; set; } = 0.25f;

    /// <summary>Plaka kırpılırken her kenara eklenecek pay oranı.</summary>
    public float CropPaddingRatio { get; set; } = 0.06f;

    /// <summary>TR plaka doğrulaması uygulansın mı.</summary>
    public bool ApplyTurkishValidation { get; set; } = true;

    /// <summary>
    /// Bir olayın yayınlanması için gereken minimum birleşik güven (dedektör*OCR).
    /// TR doğrulaması zaten geçmiş olmalı; bu ek bir güven bariyeridir.
    /// </summary>
    public float EmitMinConfidence { get; set; } = 0.0f;

    /// <summary>
    /// Yayınlamak için gereken minimum OCR güveni. OCR güveni, METNİN doğruluğuyla doğrudan
    /// ilintilidir (doğru okumalar ~%92-100, bulanık/kenar okumalar daha düşük). Bariyere belirsiz
    /// okuma gitmesin diye ham/kötü kareleri eler. Daha katı için 0.85+, her şeyi loglamak için 0.
    /// </summary>
    public float EmitMinOcrConfidence { get; set; } = 0.45f;

    /// <summary>
    /// Çok kareli onay: bir plaka, onay penceresi (DedupWindowSeconds) içinde EN AZ bu kadar
    /// kez okunmadan yayınlanmaz. Tek seferlik OCR yanlışlarını (ör. "25 AFH 11" vs "25 AFH 149")
    /// eler. Yayınlanırken o aracın en yüksek güvenli okuması seçilir. 1 = onay kapalı (her okuma).
    /// Bariyerde araç birkaç saniye kaldığı için 2 güvenli bir varsayılandır.
    /// </summary>
    public int MinConfirmations { get; set; } = 2;

    /// <summary>
    /// Onaya ulaşmayan (tek-kare hızlı) araç, son görülmesinden bu kadar saniye sonra yine de yayınlanır.
    /// YALNIZ 1-kare izleri etkiler (2+ kare zaten onayla ANINDA emit) → hızlı-araç bariyer gecikmesi.
    /// Ölçüm (diag4, 26 araç, iki kamera): settle 1..3s emit sonucunu DEĞİŞTİRMİYOR (saf zamanlama knob'u).
    /// 2 = hızlı araçta 3s yerine 2s bariyer + geç gelen 2. kareye 2s tolerans (konservatif).
    /// </summary>
    public int SettleWindowSeconds { get; set; } = 2;

    /// <summary>
    /// Karakter-bazlı çok-kareli oylama. Açıksa aynı aracın tüm karelerinde slot-bazlı olasılıklar
    /// toplanıp pozisyon-başına en olası karakter seçilir (doğruluğu artırır). Kapalıysa en yüksek
    /// güvenli tüm-plaka seçilir.
    /// </summary>
    public bool CharacterVoting { get; set; } = true;

    /// <summary>
    /// TR gramer-kısıtlı çözümleme: biriken slot dağılımını geçerli Türk plaka yapılarına göre çözer
    /// (8/B, 0/O, 1/I gibi karışıklıkları pozisyona göre çözer). CharacterVoting ile birlikte çalışır.
    /// </summary>
    public bool ConstrainedDecoding { get; set; } = true;

    /// <summary>
    /// Kimlik anahtarında il'i yok say (harf|rakam) → il ilk-hanesi yanlış okunan aynı araç tek olaya
    /// iner, il oylamayla belirlenir. Çift/yanlış-il emitlerini azaltır. false = eski (il|harf).
    /// </summary>
    public bool MergeAcrossProvince { get; set; } = false;

    /// <summary>
    /// Bulanık zamansal kümeleme: >0 ise aktif penceredeki okumalar edit-mesafesi ≤ bu değerle birleşir →
    /// aynı aracın geçişindeki il/rakam/harf yanlış-okuma varyantları TEK olaya çöker (kamera-ANPR davranışı).
    /// diag'da emit 18→8/gürültü 11→1; live1h'te yanlış-emit -%24 (recall -1 araç). Bariyerde recall öncelikli
    /// olduğu için varsayılan 1 (güvenli); daha temiz çıktı için 2, kapatmak için 0.
    /// </summary>
    public int FuzzyMergeDistance { get; set; } = 1;

    /// <summary>
    /// Güven-kapılı erken emit: onaylı izin uzlaşı OCR güveni bu eşiğin altındaysa hemen yayınlama,
    /// daha iyi okuma bekle (en geç SettleWindow'da yayınlanır). Uzaktan kırpık okumayla erken/yanlış
    /// emit'i keser; yüksek güvenli plakaya gecikme eklemez. 0 = kapalı. Ölçümle karar verilir.
    /// </summary>
    public float EarlyEmitMinOcr { get; set; } = 0f;

    /// <summary>OCR öncesi plaka deskew (eğiklik düzeltme). Açılı veride ÖLÇÜLMELİ. Varsayılan kapalı.</summary>
    public bool DeskewPlates { get; set; } = false;

    /// <summary>ROI-kırpımlı tespit: dedektöre ROI sınırlayıcı dikdörtgeni verilir (plaka pikseli büyür). </summary>
    public bool RoiCropDetector { get; set; } = true;

    /// <summary>ROI-kırpım modunda tam-kare gözlem taraması sıklığı (her N karede 1).</summary>
    public int FullFrameEveryN { get; set; } = 3;

    /// <summary>Kalite kapısı: bulanık/aşırı karanlık/patlamış kırpıntı OCR'a girmez (oylamayı kirletmez).
    /// ÖLÇÜM (2026-07-10): blur=600 eşiğiyle diag4 birebir korunup gürültü 11→9; live1h recall sabit.</summary>
    public bool CropQualityGate { get; set; } = true;
    public float QualityBlurMin { get; set; } = 600f;
    public float QualityDarkMaxPct { get; set; } = 0.85f;
    public float QualityBrightMaxPct { get; set; } = 0.80f;

    /// <summary>WPOD rektifikasyonu (İkinci-Şans Aşama 2). ÖLÇÜM: açılı 61→67, temiz 341→359 (oylama-birleşimli).</summary>
    public bool EnhanceRectify { get; set; } = true;
    public float EnhanceTriggerOcr { get; set; } = 0.98f;
    public int EnhanceBudgetMs { get; set; } = 150;
    public string WpodModelPath { get; set; } = "models/wpod_lpd.onnx";

    /// <summary>FSRCNN x2 SR (küçük kırpıntı). ÖLÇÜM: küçük dilim 28→30/41 (+2/−0), 0.8 ms.</summary>
    public bool EnhanceSr { get; set; } = true;
    public int SrMaxWidthPx { get; set; } = 100;
    public string SrModelPath { get; set; } = "models/fsrcnn_x2.onnx";

    /// <summary>Uzamsal iz birleştirme IoU eşiği (aynı fiziksel plaka, ağır varyantlar tek ize). 0 = kapalı.</summary>
    public float SpatialMergeIou { get; set; } = 0.35f;

    /// <summary>SKIP_CHAR seviyesi: hayalet son hane bastırma (TrConstrainedDecoder). 0 = kapalı.
    /// ÖLÇÜM (diag4): 0.5 bir birebiri kaybettirdi (agresif); 0.3 birebiri koruyup mekanizmayı açık tutuyor.</summary>
    public float SkipCharLevel { get; set; } = 0.3f;

    /// <summary>Uzlaşı emitinde taşınacak aday sayısı (top-K). 0 = kapalı.</summary>
    public int CandidateCount { get; set; } = 5;

    /// <summary>
    /// Yalnız YAKLAŞAN aracı yayınla: DirectionTracker plaka GENİŞLİĞİ eğilimini izler; iz belirgin
    /// küçülüyorsa (uzaklaşıyor) roi olayı bastırılır. VARSAYILAN KAPALI: 4 canlı koşuda (2026-07-14)
    /// bu geometride KANITLI ETKİSİZ — plaka ancak araç kapıya gelip DURUNCA (boyut donmuş) net
    /// tespit edildiğinden trend hep ~1.0 çıkıyor; ayırt edici boyut değişimi araç uzaktayken (tespit
    /// güvenilmez) oluyor. Doğru sinyal muhtemelen HAREKET-YOLU (konum yönü) — kameralar dönünce
    /// denenecek. Bu bayrak açılsa da güvenli (0 yanlış-bastırma ölçüldü) ama pratikte iş yapmaz.
    /// </summary>
    public bool RequireApproaching { get; set; } = false;

    /// <summary>Uzaklaşıyor eşiği: son-yarı-max / erken-yarı-max plaka GENİŞLİK oranı bu değerin ALTINDAYSA
    /// "uzaklaşıyor" sayılır. Yarı-başına-max mülayim küçülmeyi yumuşattığı için 0.85 (belirgin küçülmeyi
    /// yakalar, yaklaşan/sabit izlere ~1.0'a güvenli marj bırakır). trend=-1 (kararsız) geçer. Ölçümle ayarla.</summary>
    public float ApproachDepartRatio { get; set; } = 0.85f;

    /// <summary>Yön kararı için gereken asgari genişlik örneği (yarı-başına-max sağlamlığı için ≥4).
    /// 2 fps'te araç ~2 karede emit olduğundan, filtre açıkken emit bu kadar kere görülene dek TUTULUR
    /// (settle'da her hâlükârda yayınlanır) — böylece yön tek/iki kareye değil yeterli kareye dayanır.</summary>
    public int ApproachMinSamples { get; set; } = 4;

    /// <summary>Yön kararı için izin kapsaması gereken asgari süre (sn). Altındaysa karar verilmez (geçer).</summary>
    public double ApproachMinSpanSeconds { get; set; } = 0.6;

    /// <summary>
    /// Bilinen plaka whitelist'i (ör. resmi/protokol araçları). Emit anında, okuma bu listeden bir
    /// plakaya edit-mesafesi ≤ SnapMaxLev yakınsa ona DÜZELTİLİR ("snap"). Siyah resmi plaka gibi
    /// FREKANSLI araçların sistematik yanlış-okumasını düzeltir (offline ölçüm: lev≤1 siyah +27pp,
    /// beyazda 0 yanlış-snap). Boş = etkisiz. Güvenli: küçük, küratörlü liste + lev≤1.
    /// </summary>
    public List<string> KnownPlates { get; set; } = new();

    /// <summary>Snap için azami edit-mesafesi (boşluksuz plaka). 1 = güvenli (0 yanlış-snap ölçüldü);
    /// 2 = daha çok düzeltir (siyah +45pp) ama küçük yanlış-snap riski (~%0.5). 0 = snap kapalı.</summary>
    public int SnapMaxLev { get; set; } = 1;

    /// <summary>Manuel whitelist'e EK olarak, otomatik öğrenilen MÜDAVİM plakalara da snap et. Müdavim
    /// kaydı yanlış-okumalarla kirlenebileceğinden varsayılan KAPALI (canlı doğrulama sonrası açılır).</summary>
    public bool SnapToFrequent { get; set; } = false;

    /// <summary>Güven-kapısı: yalnız OCR güveni bu değerin ALTINDA olan okumayı snap et. Yüksek-güvenli
    /// bir okuma whitelist plakasına yakın olsa bile GERÇEK farklı bir plaka olabilir (yanlış-snap riski).
    /// **0.99 varsayılan (2026-07-23 gate-sweep optimum):** model ≥0.99 okumaları DOĞRU (snap'leme),
    /// sistematik yanlış-okumalar <0.99'da; 0.99→1.0 uçurumu (395-plaka whitelist'te yanlış-snap 1→11).
    /// val+night: 0.99 → +9.3pp, %0.16 yanlış-snap. Küçük manuel whitelist için 1.0 da güvenli olabilir
    /// (az komşu) ama 0.99 her ölçekte güvenli. Daha düşürmek (0.90) yüksek-güvenli okumaları daha korur.</summary>
    public float SnapMaxOcrConfidence { get; set; } = 0.99f;

    /// <summary>
    /// Emit-sonrası varyant bastırma penceresi (sn): yeni emitin plakası, aynı kameranın/kapsamın son
    /// emitlerinden birinin ADAY listesinde geçiyorsa (veya birebir aynıysa) yeni OLAY yayınlanmaz —
    /// aynı geçişin farklı-uzunluk hayalet varyantları tek olaya iner. 0 = kapalı.
    /// </summary>
    public int VariantSuppressSeconds { get; set; } = 30;

    /// <summary>
    /// Gözlem OCR bütçesi: ROI DIŞI tespitlerden kare başına en fazla bu kadarı (en büyükten) okunur ve
    /// Scope="gozlem" olayı olarak yayınlanır (bariyer kararına girmez; panelde kamerayla eşleşir).
    /// 0 = kapalı. ROI tanımsız kamerada etkisizdir.
    /// </summary>
    public int ObservationMaxPerFrame { get; set; } = 2;

    /// <summary>
    /// Çok-kareli füzyon: düşük güvenli uzlaşı emitlerinde, izin en iyi kırpıntıları hizalanıp
    /// ortalanır ve füzyon görüntüsünde OCR bir kez daha çalışır; daha güvenli sonuç kazanır.
    /// Bulanık/düşük çözünürlüklü plakalar için. Araç başına en fazla 1 ekstra OCR. Ölçümle açılır.
    /// </summary>
    public bool MultiFrameFusion { get; set; } = false;

    /// <summary>Füzyonda kullanılacak en iyi kırpıntı sayısı (iz başına saklanan üst sınır).</summary>
    public int FusionMaxFrames { get; set; } = 5;

    /// <summary>Uzlaşı OCR güveni bu eşiğin ALTINDAysa füzyon denenir (yüksek güvenli emitlere dokunulmaz).</summary>
    public float FusionTriggerOcr { get; set; } = 0.85f;

    /// <summary>
    /// Kanıt kırpıntısı: her olayda, izin en yüksek güvenli plaka kırpıntısı JPEG olarak evidence/
    /// klasörüne kaydedilir ve olayda yolu taşınır (panelde küçük resim; ihtilaflarda gözle karar).
    /// Disk FIFO ile sınırlanır (EvidenceMaxFiles).
    /// </summary>
    public bool EvidenceCrops { get; set; } = true;

    /// <summary>evidence/ klasöründe tutulacak azami dosya sayısı (aşınca en eskiler silinir).
    /// NOT: SaveFullFrame açıkken olay başına 2 dosya (plaka + tam kare) yazılır; bu sınır ikisini
    /// birlikte sayar — aynı gün-sayısı kanıt tutmak için gerekiyorsa iki katına çıkarın.</summary>
    public int EvidenceMaxFiles { get; set; } = 6000;

    /// <summary>
    /// Her olayda plaka kırpıntısının YANINDA, en iyi okumanın geldiği TAM KAREyi (aracın da göründüğü
    /// sahne) evidence/..._full.jpg olarak kaydeder ve olayda FramePath ile taşır — otopark yazılımı
    /// "son okunan aracı" görselleyebilsin diye. EvidenceCrops kapalıysa etkisizdir. Disk için kapatılabilir.
    /// </summary>
    public bool SaveFullFrame { get; set; } = true;

    /// <summary>
    /// KVKK saklama süresi: bu günden eski kanıt kırpıntıları otomatik silinir (kamu pratiği 30-45 gün;
    /// bağlayıcı süreyi hukuk birimi belirler). 0 = süre sınırı yok (yalnız adet sınırı çalışır).
    /// </summary>
    public int EvidenceRetentionDays { get; set; } = 30;

    /// <summary>
    /// Loglarda plaka maskeleme (KVKK): true ise "25ABC123" -> "25***123" yazılır. Geliştirme/gölge
    /// çalışma döneminde karşılaştırma için false; canlı işletmede hukuk birimi kararına göre açılır.
    /// </summary>
    public bool MaskPlatesInLogs { get; set; } = false;

    /// <summary>
    /// Operatör-onayı eşiği: uzlaşı OCR güveni bu değerin altındaki olaylar LowConfidence=true
    /// bayrağıyla yayınlanır (otopark yazılımı bariyeri otomatik açmak yerine operatöre düşürebilir).
    /// </summary>
    public float ManualReviewMinOcr { get; set; } = 0.75f;

    /// <summary>Dolu ise açılışta tespit modelinin SHA-256'sı doğrulanır; uyuşmazsa servis BAŞLAMAZ (bozuk/yanlış model koruması).</summary>
    public string? DetectorModelSha256 { get; set; }

    /// <summary>Dolu ise açılışta OCR modelinin SHA-256'sı doğrulanır; uyuşmazsa servis BAŞLAMAZ.</summary>
    public string? OcrModelSha256 { get; set; }

    public int HttpPort { get; set; } = 8080;

    /// <summary>HTTP dinleme adresi. Varsayılan "localhost" (yalnız bu makine — güvenli varsayılan, KVKK).
    /// LAN'dan erişim için açıkça "0.0.0.0" yapın VE PanelToken ayarlayın (aksi halde canlı görüntü/plaka/ROI
    /// tüm ağa kimlik-doğrulamasız açık olur — açılışta uyarılır). Boş "" = loopback.</summary>
    public string HttpBindAddress { get; set; } = "localhost";

    /// <summary>/events/recent için bellekte tutulacak maksimum olay sayısı.</summary>
    public int RecentEventsCapacity { get; set; } = 200;

    /// <summary>Webhook URL — YALNIZ Anpr.Service paneli kullanır (doluysa her roi olayı bu URL'e POST
    /// edilir, disk-kuyruk + retry ile). **DLL/AnprRuntime gömülümünde YOK SAYILIR** — PMS olayları
    /// PlateDetected olayından ya da extraSinks (IEventSink) ile alır. Set edilirse AnprRuntime uyarır.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Webhook doluysa bu aralıkta sağlık kalp-atışı POST edilir (kamera durumu, sayaçlar). 0 = kapalı.</summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>events.jsonl KVKK saklama süresi (gün) — açılışta eski satırlar budanır. 0 = sınırsız.</summary>
    public int EventRetentionDays { get; set; } = 30;

    /// <summary>Dolu ise panel ve API uçları bu belirteci ister (?token= veya X-Anpr-Token başlığı).
    /// Boş = koruma kapalı (geliştirme). Canlı sahada doldurulmalı (KVKK erişim kontrolü).</summary>
    public string? PanelToken { get; set; }

    public FfmpegOptions Ffmpeg { get; set; } = new();

    public List<CameraOptions> Cameras { get; set; } = new();

    /// <summary>Açılışta yapılandırmayı doğrular (M6). Aralık-dışı eşik/fps/port SESSİZCE kabul edilip
    /// bariyerin hiç açılmamasına yol açmasın diye TÜM sorunları toplayıp tek net hata fırlatır.
    /// Konservatif: yalnız açıkça-geçersiz değerler yakalanır (üretim varsayılanları her zaman geçer).</summary>
    public void Validate()
    {
        var errs = new List<string>();
        void Range01(float v, string name) { if (v < 0f || v > 1f) errs.Add($"{name}={v} [0,1] aralığında olmalı (tipik hata: 0.45 yerine 45 → hiç emit olmaz)"); }

        if (Fps <= 0) errs.Add($"Fps={Fps} > 0 olmalı");
        if (IdleFps < 0) errs.Add($"IdleFps={IdleFps} >= 0 olmalı");
        Range01(MinConfidence, nameof(MinConfidence));
        Range01(DetectorScoreThreshold, nameof(DetectorScoreThreshold));
        Range01(EmitMinConfidence, nameof(EmitMinConfidence));
        Range01(EmitMinOcrConfidence, nameof(EmitMinOcrConfidence));
        Range01(SnapMaxOcrConfidence, nameof(SnapMaxOcrConfidence));
        Range01(EarlyEmitMinOcr, nameof(EarlyEmitMinOcr));
        if (MinConfirmations < 1) errs.Add($"MinConfirmations={MinConfirmations} >= 1 olmalı");
        if (SnapMaxLev < 0) errs.Add($"SnapMaxLev={SnapMaxLev} >= 0 olmalı");
        if (CropPaddingRatio < 0) errs.Add($"CropPaddingRatio={CropPaddingRatio} >= 0 olmalı");
        if (HttpPort is < 1 or > 65535) errs.Add($"HttpPort={HttpPort} 1..65535 olmalı");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Cameras.Count; i++)
        {
            var c = Cameras[i];
            string tag = string.IsNullOrWhiteSpace(c.Id) ? $"Cameras[{i}]" : $"kamera '{c.Id}'";
            if (string.IsNullOrWhiteSpace(c.Id)) errs.Add($"{tag}: Id boş olamaz");
            else if (!seenIds.Add(c.Id)) errs.Add($"kamera Id '{c.Id}' yinelenmiş (benzersiz olmalı)");
            if (string.IsNullOrWhiteSpace(c.RtspUrl) && string.IsNullOrWhiteSpace(c.Ip))
                errs.Add($"{tag}: RtspUrl ya da Ip verilmeli (aksi halde bağlanılamaz, sessizce)");
        }

        if (errs.Count > 0)
            throw new InvalidOperationException(
                "ANPR yapılandırması GEÇERSİZ (düzeltilmezse bariyer sessizce hiç açılmayabilir):\n  - "
                + string.Join("\n  - ", errs));
    }
}

public sealed class CameraOptions
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Tam RTSP URL. Boşsa Ip/Username/Password'dan üretilebilir.</summary>
    public string RtspUrl { get; set; } = string.Empty;

    /// <summary>
    /// Bu kameranın ROI çokgeni, 0-1000 normalize "x1,y1;x2,y2;..." (Hikvision şerit koordinat ölçeği —
    /// kameranın LaneConfig noktalarından türetilebilir). Merkezi dışarıda kalan tespit OKUNMAZ.
    /// Boş = kapalı (tüm kare). Ölçmeden etkinleştirme.
    /// </summary>
    public string Roi { get; set; } = string.Empty;
}

public sealed class FfmpegOptions
{
    /// <summary>ffmpeg yürütülebilir yolu (PATH'te ise "ffmpeg").</summary>
    public string Path { get; set; } = "ffmpeg";

    /// <summary>RTSP taşıma: tcp (önerilir) veya udp.</summary>
    public string RtspTransport { get; set; } = "tcp";

    /// <summary>MJPEG kalitesi (-q:v): 2 en iyi .. 31 en kötü.</summary>
    public int JpegQuality { get; set; } = 4;

    /// <summary>0 = doğal çözünürlük; >0 ise kareyi bu genişliğe küçültür (CPU tasarrufu).</summary>
    public int ScaleWidth { get; set; } = 0;

    /// <summary>Bu süre boyunca kare gelmezse bağlantı kopmuş sayılır ve yeniden bağlanılır.</summary>
    public int NoFrameTimeoutSeconds { get; set; } = 12;

    /// <summary>Yeniden bağlanmada üstel beklemenin üst sınırı (sn).</summary>
    public int MaxBackoffSeconds { get; set; } = 30;

    /// <summary>
    /// İleri düzey CPU ayarı için ek ffmpeg GİRİŞ argümanları (ör. "-discard nokey" sadece
    /// anahtar kareleri çözer). Boş bırakılabilir.
    /// </summary>
    public string? ExtraInputArgs { get; set; }
}
