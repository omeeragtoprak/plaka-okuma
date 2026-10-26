# Anpr.Core

CPU-only **Türk plaka tanıma çekirdeği**: tespit (ONNX/YOLO) → kırpma → kalite/geometri kapıları → OCR
(CCT) → TR gramer-kısıtlı çözümleme → çok-kareli oylama. GPU gerektirmez.

Bu paket **motor** katmanıdır (tek kare / tek kırpıntı üzerinde çalışır). Kamera yönetimi, RTSP,
çok-kameralı orkestrasyon ve olay yayını için **[Anpr.Sdk](https://www.nuget.org/packages/Anpr.Sdk)**
paketini kullanın — otopark yazılımına gömülmek için tasarlanan yüzey odur.

```csharp
using var engine = AnprEngine.Create(new AnprOptions {
    DetectorModelPath = "models/yolo-v9-t-384-license-plates-end2end.onnx",
    OcrModelPath      = "models/cct_xs_v2_erz.onnx",
    OcrConfigPath     = "models/cct_xs_v2_erz_plate_config.yaml",
});
foreach (var r in engine.Recognize(File.ReadAllBytes("kare.jpg")))
    Console.WriteLine($"{r.Text} (ocr {r.OcrConfidence:P0}, geçerli TR: {r.IsValidTr})");
```

Ayrıntı ve entegrasyon: proje deposundaki `docs/entegrasyon-kilavuzu.md`.
