namespace Anpr.Service.Configuration;

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

    public string DetectorModelPath { get; set; } = "models/yolo-v9-t-384-license-plates-end2end.onnx";
    public string OcrModelPath { get; set; } = "models/cct_xs_v1_global.onnx";
    public string OcrConfigPath { get; set; } = "models/cct_xs_v1_global_plate_config.yaml";

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
    /// Onaya ulaşmayan araç, son görülmesinden bu kadar saniye sonra yine de yayınlanır
    /// (tek karede geçen aracı kaçırmamak için).
    /// </summary>
    public int SettleWindowSeconds { get; set; } = 3;

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

    /// <summary>Uzamsal iz birleştirme IoU eşiği (aynı fiziksel plaka, ağır varyantlar tek ize). 0 = kapalı.</summary>
    public float SpatialMergeIou { get; set; } = 0.35f;

    /// <summary>SKIP_CHAR seviyesi: hayalet son hane bastırma (TrConstrainedDecoder). 0 = kapalı.
    /// ÖLÇÜM (diag4): 0.5 bir birebiri kaybettirdi (agresif); 0.3 birebiri koruyup mekanizmayı açık tutuyor.</summary>
    public float SkipCharLevel { get; set; } = 0.3f;

    /// <summary>Uzlaşı emitinde taşınacak aday sayısı (top-K). 0 = kapalı.</summary>
    public int CandidateCount { get; set; } = 5;

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

    /// <summary>evidence/ klasöründe tutulacak azami dosya sayısı (aşınca en eskiler silinir).</summary>
    public int EvidenceMaxFiles { get; set; } = 3000;

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

    /// <summary>HTTP dinleme adresi. 0.0.0.0 = tüm arayüzler (LAN'daki web app erişebilsin).</summary>
    public string HttpBindAddress { get; set; } = "0.0.0.0";

    /// <summary>/events/recent için bellekte tutulacak maksimum olay sayısı.</summary>
    public int RecentEventsCapacity { get; set; } = 200;

    /// <summary>Opsiyonel webhook: doluysa her olay bu URL'e POST edilir.</summary>
    public string? WebhookUrl { get; set; }

    public FfmpegOptions Ffmpeg { get; set; } = new();

    public List<CameraOptions> Cameras { get; set; } = new();
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
