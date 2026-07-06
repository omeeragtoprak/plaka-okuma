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
    /// OCR öncesi plaka kırpıntısını eğiklikten kurtar (deskew). Açılı plakalarda yardımcı olabilir
    /// ama açılı veride ÖLÇÜLMELİ; clean plakada gereksiz döndürme zarar verebilir. Varsayılan KAPALI.
    /// </summary>
    public bool DeskewPlates { get; set; } = false;

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
