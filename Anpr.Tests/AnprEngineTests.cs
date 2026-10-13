using Anpr.Core;

namespace Anpr.Tests;

/// <summary>
/// Uçtan uca entegrasyon: gerçek ONNX modelleriyle örnek araç görseli üzerinde tespit + OCR.
/// Modeller ve sample_car.jpg, csproj tarafından test çıktı dizinine kopyalanır.
/// </summary>
public class AnprEngineTests : IDisposable
{
    private static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    private readonly AnprEngine _engine = AnprEngine.Create(new AnprOptions
    {
        DetectorModelPath = Path.Combine(ModelsDir, "yolo-v9-t-384-license-plates-end2end.onnx"),
        OcrModelPath = Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"),
        OcrConfigPath = Path.Combine(ModelsDir, "cct_xs_v1_global_plate_config.yaml"),
        OnnxIntraOpThreads = 2,
        MinConfidence = 0.25f,
    });

    [Fact]
    public void Detects_and_reads_sample_plate()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ModelsDir, "sample_car.jpg"));
        var readings = _engine.Recognize(bytes);

        Assert.NotEmpty(readings);

        var top = readings[0];
        // AlprNet README'sindeki beklenen plaka: "UD 1234"
        Assert.Equal("UD1234", top.RawText);
        Assert.True(top.DetectorConfidence > 0.5f, $"det conf düşük: {top.DetectorConfidence}");
        Assert.True(top.OcrConfidence > 0.5f, $"ocr conf düşük: {top.OcrConfidence}");

        // "UD" geçerli bir TR il kodu değil -> TR doğrulaması reddetmeli (çöp bariyere gitmez).
        Assert.False(top.IsValidTr);
    }

    [Fact]
    public void Ocr_config_is_loaded_from_yaml()
    {
        var cfg = _engine.OcrConfig;
        Assert.Equal(9, cfg.MaxPlateSlots);
        Assert.Equal(37, cfg.VocabularySize);
        Assert.Equal(128, cfg.ImgWidth);
        Assert.Equal(64, cfg.ImgHeight);
    }

    public void Dispose() => _engine.Dispose();
}
