namespace Anpr.Service.Configuration;

/// <summary>appsettings.json "Anpr" bölümünün karşılığı.</summary>
public sealed class AnprServiceOptions
{
    public const string SectionName = "Anpr";

    /// <summary>
    /// Aktif (araç varken) saniyede işlenecek kare sayısı. ffmpeg de bu hızda kare üretir.
    /// Daha yüksek fps = araç başına daha çok örnek = daha iyi karakter oylaması. Zayıf CPU'da 3-4 makul.
    /// </summary>
    public double Fps { get; set; } = 4;

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

    /// <summary>OCR öncesi plaka deskew (eğiklik düzeltme). Açılı veride ÖLÇÜLMELİ. Varsayılan kapalı.</summary>
    public bool DeskewPlates { get; set; } = false;

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
