using Microsoft.ML.OnnxRuntime;

namespace Anpr.Core;

/// <summary>
/// Anpr.Core motorunun yapılandırması. Zayıf/CPU-only makineler için varsayılanlar konservatif tutuldu.
/// </summary>
public sealed class AnprOptions
{
    /// <summary>YOLOv9-t end2end ONNX dedektör model yolu.</summary>
    public string DetectorModelPath { get; set; } = "models/yolo-v9-t-384-license-plates-end2end.onnx";

    /// <summary>fast-plate-ocr CCT ONNX model yolu.</summary>
    public string OcrModelPath { get; set; } = "models/cct_xs_v1_global.onnx";

    /// <summary>OCR modeline ait yaml config yolu (alfabe, giriş boyutu vb.).</summary>
    public string OcrConfigPath { get; set; } = "models/cct_xs_v1_global_plate_config.yaml";

    /// <summary>ONNX Runtime IntraOp thread sayısı. Zayıf CPU'yu boğmamak için 2.</summary>
    public int OnnxIntraOpThreads { get; set; } = 2;

    /// <summary>
    /// ONNX thread'leri iş beklerken dönerek (spin) mi beklesin? Spin = kare-başı en düşük gecikme ama
    /// BOŞTA da CPU yakar. Uyarlanabilir-fps ile çoğu zaman boşta olan zayıf PC'lerde false önerilir
    /// (boşta CPU ~0'a iner; kare-başı gecikme birkaç ms artabilir). Ölçerek karar verin.
    /// </summary>
    public bool OnnxAllowSpinning { get; set; } = true;

    /// <summary>
    /// OCR'ın çalıştırılacağı minimum dedektör güven skoru. Bunun altındaki tespitlere OCR uygulanmaz.
    /// </summary>
    public float MinConfidence { get; set; } = 0.5f;

    /// <summary>Dedektörün ham skor eşiği (bunun altındaki tespitler hiç dikkate alınmaz).</summary>
    public float DetectorScoreThreshold { get; set; } = 0.25f;

    /// <summary>Türkiye plaka doğrulama/normalizasyonu uygulansın mı.</summary>
    public bool ApplyTurkishValidation { get; set; } = true;

    /// <summary>
    /// Plaka kırpılırken kutunun her kenarına eklenecek pay oranı (0.06 = %6).
    /// Sıkı kutuların plaka kenarlarını kesmesini önler, OCR doğruluğunu artırır.
    /// </summary>
    public float CropPaddingRatio { get; set; } = 0.06f;

    /// <summary>
    /// Tetik bölgesi: OCR yalnızca plaka kutusu genişliği, kare genişliğinin bu oranından büyükse
    /// çalıştırılır (plaka yakın/büyük = araç bariyerde). 0 = kapalı. ~0.03 uzak/küçük plakaları eler;
    /// hem CPU tasarrufu hem doğruluk (kötü uzak kareler işlenmez).
    /// </summary>
    public float MinPlateWidthRatio { get; set; } = 0.0f;

    /// <summary>
    /// İlgi alanı (ROI) çokgeni, 0-1000 normalize "x1,y1;x2,y2;..." (Hikvision şerit koordinat ölçeği).
    /// Tespit kutusunun merkezi çokgen dışındaysa OKUNMAZ → kapsam kameraya hizalanır + CPU tasarrufu.
    /// Boş = kapalı (tüm kare okunur). Kamera başına ayrı verilmeli.
    /// </summary>
    public string RoiPolygon { get; set; } = "";

    /// <summary>
    /// OCR öncesi plaka kırpıntısını eğiklikten kurtar (deskew). Açılı plakalarda yardımcı olabilir
    /// ama açılı veride ÖLÇÜLMELİ; clean plakada gereksiz döndürme zarar verebilir. Varsayılan KAPALI.
    /// </summary>
    public bool DeskewPlates { get; set; } = false;

    /// <summary>
    /// ROI-kırpımlı tespit: ROI tanımlıysa dedektöre tam kare yerine ROI'nin sınırlayıcı dikdörtgeni
    /// (+%5 pay) verilir → letterbox gri-dolgu israfı geri kazanılır, plakalar dedektör girdisinde büyür
    /// (mevcut çıkış ROI'sinde ×1.35). ROI kare boyutunun ≥%90'ıysa kazanç yok → otomatik tam-kare yolu.
    /// Gözlem katmanı için tam kare FullFrameEveryN karede bir ayrıca taranır.
    /// </summary>
    public bool RoiCropDetector { get; set; } = true;

    /// <summary>ROI-kırpım modunda tam-kare gözlem taraması sıklığı (her N karede 1). Min 1.</summary>
    public int FullFrameEveryN { get; set; } = 3;

    /// <summary>
    /// Kalite kapısı (İkinci-Şans Şeridi Aşama 1): açıkken, bulanıklık varyansı QualityBlurMin
    /// altındaki YA DA karanlık/patlamış piksel oranı eşiği aşan kırpıntılar OCR'a girmez
    /// (oylamayı kirletmez). Eşikler eşik-taramasıyla seçilir; ölçüm kapısını geçmeden AÇILMAZ.
    /// </summary>
    public bool CropQualityGate { get; set; } = false;

    /// <summary>Bulanıklık alt eşiği (gri Laplacian varyansı; altı = bulanık, atılır). 0 = devre dışı.</summary>
    public float QualityBlurMin { get; set; } = 0f;

    /// <summary>Karanlık piksel (gri&lt;30) oranı üst eşiği; üstü atılır. 1 = devre dışı.</summary>
    public float QualityDarkMaxPct { get; set; } = 1f;

    /// <summary>Patlamış piksel (gri&gt;225) oranı üst eşiği; üstü atılır (IR patlaması). 1 = devre dışı.</summary>
    public float QualityBrightMaxPct { get; set; } = 1f;

    /// <summary>
    /// İkinci-Şans Şeridi Aşama 2 — WPOD rektifikasyonu: onaylı ama düşük güvenli izlerde, izin en iyi
    /// kırpıntıları perspektif-düzeltilip OCR'lanır ve oylamaya EK GÖZLEM olarak katılır.
    /// ÖLÇÜM (offline, dağılım-toplamı): açılı 61→67/86, temiz 341→359/416 — her dilimde kazanç.
    /// </summary>
    public bool EnhanceRectify { get; set; } = false;

    /// <summary>Zenginleştirme tetiği: iz uzlaşı OCR güveni bu eşiğin ALTINDAysa çalışır (ölçülen: 0.98).</summary>
    public float EnhanceTriggerOcr { get; set; } = 0.98f;

    /// <summary>İz başına zenginleştirme süre bütçesi (ms); aşımda kalan adımlar atlanır.</summary>
    public int EnhanceBudgetMs { get; set; } = 150;

    /// <summary>WPOD rektifikasyon modeli yolu (yoksa/yüklenemezse aşama kendini kapatır).</summary>
    public string WpodModelPath { get; set; } = "models/wpod_lpd.onnx";

    /// <summary>
    /// Gözlem OCR bütçesi: ROI DIŞI tespitlerden kare başına en fazla bu kadarına (en büyükten başlayarak)
    /// OCR uygulanır ve okumalar InZone=false etiketiyle döner. Kameranın kapsam-dışı (cadde/uzak)
    /// okumalarıyla panelde eşleşebilmek için; bariyer kararına girmez. 0 = kapalı (eski davranış).
    /// ROI tanımsızken etkisizdir (her tespit zaten bölge içi sayılır).
    /// </summary>
    public int ObservationMaxPerFrame { get; set; } = 0;

    /// <summary>
    /// CPU için ONNX Runtime SessionOptions üretir:
    /// CPU EP (varsayılan), IntraOp=OnnxIntraOpThreads, InterOp=1, sequential, ORT_ENABLE_ALL.
    /// Hiçbir yerde CUDA/GPU sağlayıcısı eklenmez.
    /// </summary>
    public SessionOptions CreateSessionOptions()
    {
        var so = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = Math.Max(1, OnnxIntraOpThreads),
            InterOpNumThreads = 1,
        };
        // Boşta CPU tasarrufu: spin kapalıyken thread'ler işi kuyruktan bekler (boşta ~0 CPU).
        so.AddSessionConfigEntry("session.intra_op.allow_spinning", OnnxAllowSpinning ? "1" : "0");
        return so;
    }
}
