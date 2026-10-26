using Anpr.Core.Recognition;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace Anpr.Tests;

/// <summary>OCR model/config boyut-uyuşmazlığı korumaları (2026-07-21 Core denetimi bulgusu).</summary>
public class RecognizerRobustnessTests
{
    private static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");
    private static SessionOptions So() => new();

    [Fact]
    public void Matching_v2_model_and_config_constructs_ok()
    {
        // Doğru eşleşme (10 slot × 37 = 370): sorunsuz kurulur.
        using var r = new LicensePlateRecognizer(
            Path.Combine(ModelsDir, "cct_xs_v2_erz.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v2_erz_plate_config.yaml"), So());
        Assert.Equal(10, r.Config.MaxPlateSlots);
    }

    [Fact]
    public void Static_model_with_wrong_slot_config_throws_clear_error()
    {
        // v2 model STATİK [1,10,37]=370; config'i 11 slot (407) yapıp uyuşmazlık üret -> AÇIK hata.
        string cfgSrc = File.ReadAllText(Path.Combine(ModelsDir, "cct_xs_v2_erz_plate_config.yaml"));
        string bad = cfgSrc.Replace("max_plate_slots: 10", "max_plate_slots: 11");
        string tmp = Path.Combine(Path.GetTempPath(), $"bad_cfg_{Guid.NewGuid():N}.yaml");
        File.WriteAllText(tmp, bad);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new LicensePlateRecognizer(Path.Combine(ModelsDir, "cct_xs_v2_erz.onnx"), tmp, So()));
            Assert.Contains("UYUŞMAZ", ex.Message);   // net teşhis mesajı
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Mismatched_v1_model_and_v2_config_throws_clear_error()
    {
        // v1 model [*,9,37]=333 (dinamik batch) + v2 config (10 slot=370): pozitif-boyut çarpımı
        // (9×37=333) doğrulandığından uyuşmazlık YAKALANIR -> ham IndexOutOfRange yerine AÇIK hata.
        var ex = Assert.Throws<InvalidOperationException>(() => new LicensePlateRecognizer(
            Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v2_erz_plate_config.yaml"), So()));
        Assert.Contains("UYUŞMAZ", ex.Message);
    }

    [Fact]
    public void Matching_v1_model_and_config_constructs_ok()
    {
        // v1 model + v1 config (9 slot) tutarlı -> sorunsuz (mevcut kullanım kırılmadı).
        using var r = new LicensePlateRecognizer(
            Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v1_global_plate_config.yaml"), So());
        Assert.Equal(9, r.Config.MaxPlateSlots);
    }
}
