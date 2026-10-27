using Anpr.Core.Imaging;
using SkiaSharp;

namespace Anpr.Tests;

public class PlateRectifierTests
{
    // Beyaz zemin üzerine yatay siyah "metin satırları" çiz (eğiklik = 0).
    private static SKBitmap MakeBars()
    {
        var bmp = new SKBitmap(new SKImageInfo(240, 80, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        using var p = new SKPaint { Color = SKColors.Black };
        c.DrawRect(20, 30, 200, 8, p);  // bir kalın yatay satır
        c.DrawRect(20, 46, 200, 8, p);  // ikinci satır
        return bmp;
    }

    private static SKBitmap RotateBy(SKBitmap src, double deg)
    {
        var dst = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var c = new SKCanvas(dst);
        c.Clear(SKColors.White);
        c.Translate(src.Width / 2f, src.Height / 2f);
        c.RotateDegrees((float)deg);
        c.Translate(-src.Width / 2f, -src.Height / 2f);
        c.DrawBitmap(src, 0, 0);
        return dst;
    }

    [Fact]
    public void Detects_skew_and_deskew_reduces_it()
    {
        using var bars = MakeBars();
        using var skewed = RotateBy(bars, 6.0);

        double estBefore = PlateRectifier.EstimateSkewAngle(skewed);
        Assert.True(Math.Abs(estBefore) >= 3.0, $"eğiklik algılanmadı: {estBefore}");

        using var fixedUp = PlateRectifier.Deskew(skewed);
        double estAfter = PlateRectifier.EstimateSkewAngle(fixedUp);
        Assert.True(Math.Abs(estAfter) < Math.Abs(estBefore), $"deskew eğikliği azaltmadı: {estBefore} -> {estAfter}");
        Assert.True(Math.Abs(estAfter) <= 2.0, $"deskew sonrası hâlâ eğik: {estAfter}");
    }

    [Fact]
    public void Straight_image_is_left_unchanged()
    {
        using var bars = MakeBars();
        double est = PlateRectifier.EstimateSkewAngle(bars);
        Assert.True(Math.Abs(est) <= 1.0, $"düz görüntüde sahte eğiklik: {est}");
        using var result = PlateRectifier.Deskew(bars);
        Assert.True(ReferenceEquals(result, bars)); // |açı|<1 -> aynı bitmap döner
    }
}

public class Rectifier2GeometryTests
{
    [Fact]
    public void Homography_maps_quad_corners_to_target_rect()
    {
        // Eğik dörtgen -> eksen-hizalı hedef; çözülen H her köşeyi ±0.5 px içinde taşımalı.
        float[] sx = { 20, 200, 210, 15 };
        float[] sy = { 30, 10, 90, 100 };
        float[] dx = { 0, 240, 240, 0 };
        float[] dy = { 0, 0, 80, 80 };

        var h = Anpr.Core.Imaging.PlateRectifier2.SolveHomography(sx, sy, dx, dy);
        Assert.NotNull(h);
        for (int i = 0; i < 4; i++)
        {
            float wz = h![6] * sx[i] + h[7] * sy[i] + h[8];
            float u = (h[0] * sx[i] + h[1] * sy[i] + h[2]) / wz;
            float v = (h[3] * sx[i] + h[4] * sy[i] + h[5]) / wz;
            Assert.True(MathF.Abs(u - dx[i]) < 0.5f && MathF.Abs(v - dy[i]) < 0.5f,
                $"köşe {i}: ({u:F2},{v:F2}) beklenen ({dx[i]},{dy[i]})");
        }
    }

    [Fact]
    public void Degenerate_quad_returns_null()
    {
        float[] sx = { 0, 0, 0, 0 };
        float[] sy = { 0, 0, 0, 0 };
        float[] dx = { 0, 240, 240, 0 };
        float[] dy = { 0, 0, 80, 80 };
        Assert.Null(Anpr.Core.Imaging.PlateRectifier2.SolveHomography(sx, sy, dx, dy));
    }
}

/// <summary>
/// 2.2.0: varsayılan rektifikasyon modeli IWPOD-NET (MIT, pakette); eski WPOD-NET (GPL, pakette yok) hâlâ
/// açıkça verilebilir. Tür çıktı kanal sayısından anlaşılır — biri sessizce ötekinin kod yoluna düşmesin.
/// </summary>
public class Rectifier2ModelTests
{
    private static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    [Theory]
    [InlineData(new[] { 1, -1, -1, 7 }, 160)] // IWPOD: 1 olasılık + 6 affine
    [InlineData(new[] { 1, -1, -1, 8 }, 208)] // WPOD: 2 olasılık + 6 affine
    [InlineData(new[] { 1, -1, -1, -1 }, 208)] // bilinmeyen → eski davranış
    public void Input_side_follows_model_kind(int[] dims, int expected)
        => Assert.Equal(expected, PlateRectifier2.InputMinSideFor(dims));

    [Fact]
    public void Shipped_iwpod_and_legacy_wpod_are_told_apart_by_output_channels()
    {
        using var iw = new PlateRectifier2(Path.Combine(ModelsDir, "iwpod_net.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions());
        using var wp = new PlateRectifier2(Path.Combine(ModelsDir, "wpod_lpd.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions());
        Assert.Equal(160, iw.InputMinSide);
        Assert.Equal(208, wp.InputMinSide);
    }

    [Fact]
    public void Iwpod_rectified_sample_plate_reads_in_full()
    {
        // sample_car.jpg = AlprNet örneği (gerçek bir TR plakası DEĞİL, "UD 1234"). Dedektör kutusu → IWPOD
        // dörtgeni → düzeltilmiş görüntü OCR'da TAM okunmalı. Affine kanalları yanlış okunursa (ör. IWPOD'da
        // WPOD'un kanal-2 ofseti) dizin taşar ya da dörtgen anlamsızlaşır.
        // ⓘ Ölçüldü (2026-10-27): aynı kırpıntıda WPOD dörtgeni plakanın solunu KESİYOR ("D 1234" → OCR "1234");
        // IWPOD tamamını alıyor. Bu yüzden test iki modelin dörtgenini birbirine kıyaslamaz, OKUNANI sınar.
        using var decoded = SkiaSharp.SKBitmap.Decode(Path.Combine(ModelsDir, "sample_car.jpg"));
        using var img = decoded.Copy(SkiaSharp.SKColorType.Rgba8888);
        using var det = new Anpr.Core.Detection.LicensePlateDetector(
            Path.Combine(ModelsDir, "yolo-v9-t-384-license-plates-end2end.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions());
        var d = det.Detect(img).OrderByDescending(x => x.Confidence).First();
        using var crop = new SkiaSharp.SKBitmap(d.Width, d.Height);
        Assert.True(img.ExtractSubset(crop, new SkiaSharp.SKRectI(d.X, d.Y, d.X + d.Width, d.Y + d.Height)));

        using var iw = new PlateRectifier2(Path.Combine(ModelsDir, "iwpod_net.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions());
        using var rec = new Anpr.Core.Recognition.LicensePlateRecognizer(
            Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"), Path.Combine(ModelsDir, "cct_xs_v1_global_plate_config.yaml"),
            new Microsoft.ML.OnnxRuntime.SessionOptions());
        var ri = iw.Rectify(crop);
        Assert.NotNull(ri);
        using var rimg = ri!.Value.Image;
        Assert.True(ri.Value.Prob > 0.5f, $"IWPOD olasılığı düşük: {ri.Value.Prob}");
        Assert.True((double)rimg.Width / rimg.Height > 2.5, $"çıktı plaka biçiminde değil: {rimg.Width}x{rimg.Height}");
        Assert.Equal("UD1234", new string(rec.Recognize(rimg).Text.Where(char.IsLetterOrDigit).ToArray()));
    }

    [Fact]
    public void Legacy_wpod_rectifies_exactly_like_the_published_2_1_0_code()
    {
        // Eski WPOD'u açıkça seçen kurulum değişiklikten ÖNCEKİ davranışı almalı. Beklenen değerler nuget.org'daki
        // Anpr.Core 2.1.0 paketiyle (değişiklik öncesi kod) aynı kırpıntıda ölçüldü (2026-10-27): 288x113 · olasılık
        // 0,9998 · eğiklik 0,213° · perspektif 1,0000. Bağımsız incelemenin bulgusu: affine ofsetini 1'e kaydırmak
        // (arka plan olasılığı A00 sanılır) bu test yokken 757 testin hiçbirini düşürmüyordu.
        using var crop = SampleCrop();
        using var wp = new PlateRectifier2(Path.Combine(ModelsDir, "wpod_lpd.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions());
        var r = wp.Rectify(crop);
        Assert.NotNull(r);
        using var img = r!.Value.Image;
        // Aralıklar platformlar arası (CI: x64 Linux · geliştirme: arm64 macOS) ORT kayan nokta farkına pay bırakır:
        // tamsayıya kesilen kenarlar ±1–2 px oynayabilir. Kanal ofseti bozulduğunda dörtgen hiç bulunmuyor
        // (Rectify null — ölçüldü), yani bu pay mutasyonu gizlemez.
        Assert.InRange(img.Width, 285, 288);
        Assert.InRange(img.Height, 110, 116);   // 2.1.0: 113 — IWPOD aynı kırpıntıda 80 verir (ayırt edici)
        Assert.True(r.Value.Prob > 0.999f, $"olasılık {r.Value.Prob}");
        Assert.InRange(r.Value.SkewDeg, 0f, 1f);
        Assert.InRange(r.Value.PerspRatio, 0.98f, 1.02f);
    }

    private static SkiaSharp.SKBitmap SampleCrop()
    {
        using var decoded = SkiaSharp.SKBitmap.Decode(Path.Combine(ModelsDir, "sample_car.jpg"));
        using var img = decoded.Copy(SkiaSharp.SKColorType.Rgba8888);
        using var det = new Anpr.Core.Detection.LicensePlateDetector(
            Path.Combine(ModelsDir, "yolo-v9-t-384-license-plates-end2end.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions());
        var d = det.Detect(img).OrderByDescending(x => x.Confidence).First();
        var crop = new SkiaSharp.SKBitmap(d.Width, d.Height);
        Assert.True(img.ExtractSubset(crop, new SkiaSharp.SKRectI(d.X, d.Y, d.X + d.Width, d.Y + d.Height)));
        return crop;
    }

    [Fact]
    public void Shipped_default_is_the_packaged_mit_model_and_wpod_stays_out_of_the_package()
    {
        Assert.Equal("models/iwpod_net.onnx", new Anpr.Sdk.Configuration.AnprServiceOptions().WpodModelPath);
        Assert.Equal("models/iwpod_net.onnx", new Anpr.Core.AnprOptions().WpodModelPath);

        // Paket bildirimi: IWPOD Pack=true öğe listesinde, WPOD ayrı ve Pack=false (GPL-3.0 → MIT pakete giremez).
        string repo = AppContext.BaseDirectory;
        while (repo.Length > 0 && !File.Exists(Path.Combine(repo, "OtoparkAnpr.sln"))) repo = Path.GetDirectoryName(repo) ?? "";
        var xml = System.Xml.Linq.XDocument.Load(Path.Combine(repo, "Anpr.Sdk", "Anpr.Sdk.csproj"));
        var items = xml.Descendants("None").ToList();
        bool Packs(System.Xml.Linq.XElement e) => (string?)e.Attribute("Pack") == "true";
        Assert.Contains(items, e => Packs(e) && ((string?)e.Attribute("Include") ?? "").Contains("iwpod_net.onnx"));
        Assert.DoesNotContain(items, e => Packs(e) && ((string?)e.Attribute("Include") ?? "").Contains("wpod_lpd.onnx"));
    }
}

/// <summary>
/// Zenginleştirme (rektifikasyon/SR) izin kırpıntılarına muhtaçtır; kırpıntılar eskiden yalnız kanıt/füzyon açıkken
/// saklanıyordu → kanıtı kapatan kurulumda aşama SESSİZCE hiçbir şey yapmıyordu (2026-10-27, 30 dk oynatmada ölçüldü).
/// </summary>
public class EnhancementCropRetentionTests
{
    private static Anpr.Sdk.Configuration.AnprServiceOptions O(bool evidence, bool fusion = false, bool rect = true, bool sr = true)
        => new() { EvidenceCrops = evidence, MultiFrameFusion = fusion, EnhanceRectify = rect, EnhanceSr = sr };

    [Fact]
    public void Evidence_off_still_keeps_small_crops_when_enhancement_can_run()
        => Assert.Equal(Anpr.Sdk.Workers.CameraWorker.CropKeep.CropsOnly,
                        Anpr.Sdk.Workers.CameraWorker.ShouldKeepCrops(O(evidence: false), rectifierAvailable: true, superResolverAvailable: true));

    [Fact]
    public void Evidence_or_fusion_keeps_the_pre_2_2_full_behaviour()
    {
        Assert.Equal(Anpr.Sdk.Workers.CameraWorker.CropKeep.Full, Anpr.Sdk.Workers.CameraWorker.ShouldKeepCrops(O(evidence: true), false, false));
        Assert.Equal(Anpr.Sdk.Workers.CameraWorker.CropKeep.Full, Anpr.Sdk.Workers.CameraWorker.ShouldKeepCrops(O(evidence: false, fusion: true), false, false));
    }

    [Theory]
    [InlineData(false, false, true, true)]   // ikisi de kapalı
    [InlineData(true, true, false, false)]   // açık ama model yok
    [InlineData(true, false, false, true)]   // ÇAPRAZ: açık olanın modeli yok, modeli olan kapalı
    [InlineData(false, true, true, false)]   // ÇAPRAZ (ters)
    public void Nothing_is_kept_when_no_stage_needs_crops(bool rect, bool sr, bool rectAvail, bool srAvail)
        => Assert.Equal(Anpr.Sdk.Workers.CameraWorker.CropKeep.None,
                        Anpr.Sdk.Workers.CameraWorker.ShouldKeepCrops(O(evidence: false, rect: rect, sr: sr), rectAvail, srAvail));

    [Theory]
    [InlineData(true, false, true, false)]   // yalnız rektifikasyon
    [InlineData(false, true, false, true)]   // yalnız süper çözünürlük
    public void Either_stage_alone_is_enough_to_keep_crops(bool rect, bool sr, bool rectAvail, bool srAvail)
        => Assert.Equal(Anpr.Sdk.Workers.CameraWorker.CropKeep.CropsOnly,
                        Anpr.Sdk.Workers.CameraWorker.ShouldKeepCrops(O(evidence: false, rect: rect, sr: sr), rectAvail, srAvail));

    private static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>sample_car.jpg üzerine KURGUSAL iki plaka çizer: aracın plakası yerine TR-geçerli "25 ABC 123"
    /// (scripts/kvkk_fictional_plates.txt'te) ve yol zeminine TR-GEÇERSİZ "AB 1234" (yedek kırpıntı yolu için).
    /// Ölçüldü (2026-10-27): dedektör ikisini de bulur (0,87 · 0,44), OCR ilkini geçerli okur.</summary>
    private static byte[] SyntheticTwoPlateJpeg()
    {
        using var decoded = SkiaSharp.SKBitmap.Decode(Path.Combine(ModelsDir, "sample_car.jpg"));
        using var img = decoded.Copy(SkiaSharp.SKColorType.Rgba8888);
        using (var c = new SkiaSharp.SKCanvas(img))
        using (var font = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, TextSize = 17, IsAntialias = true,
                   Typeface = SkiaSharp.SKTypeface.FromFamilyName("Arial", SkiaSharp.SKFontStyle.Bold) })
        using (var white = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White })
        using (var edge = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 1 })
        {
            void Plate(string text, float cx, float top)
            {
                float w = font.MeasureText(text);
                var r = new SkiaSharp.SKRect(cx - w / 2 - 6, top, cx + w / 2 + 6, top + 26);
                c.DrawRect(r, white); c.DrawRect(r, edge);
                c.DrawText(text, cx - w / 2, top + 19, font);
            }
            Plate("25 ABC 123", 200, 486);
            Plate("AB 1234", 80, 540);
        }
        using var ms = new MemoryStream();
        img.Encode(ms, SkiaSharp.SKEncodedImageFormat.Jpeg, 95);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(true)]    // kanıt KAPALI + düzeltme açık → küçük kırpıntılar tutulur, tam kare/yedek TUTULMAZ
    [InlineData(false)]   // kanıt kapalı + aşamalar kapalı → hiçbir şey tutulmaz (eski Recognize yolu)
    public async Task Worker_keeps_crops_for_enhancement_without_evidence(bool enhance)
    {
        var cam = new Anpr.Sdk.Configuration.CameraOptions { Id = "t", Name = "t", RtspUrl = "file://x" };
        var opts = new Anpr.Sdk.Configuration.AnprServiceOptions
        {
            MotionGate = Anpr.Sdk.Configuration.MotionGateMode.Off, Fps = 5, IdleFps = 5, MinConfidence = 0.25f,
            EvidenceCrops = false, MultiFrameFusion = false, EnhanceRectify = enhance, EnhanceSr = enhance, Cameras = { cam },
        };
        Assert.True(opts.SaveFullFrame);   // varsayılan AÇIK: CropsOnly kare tutsaydı bu testte görünürdü
        var engine = Anpr.Core.AnprEngine.Create(Anpr.Sdk.Workers.AnprCameraSupervisor.ToEngineOptions(opts,
            Path.Combine(ModelsDir, "yolo-v9-t-384-license-plates-end2end.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v1_global_plate_config.yaml"), "", cam));
        await using var worker = new Anpr.Sdk.Workers.CameraWorker(cam, opts, new PlateSizeLimitTests.RepeatingJpegSource(SyntheticTwoPlateJpeg()),
            engine, Array.Empty<Anpr.Sdk.Events.IEventSink>(), new Anpr.Sdk.Events.FrequentPlateRegistry(),
            new Anpr.Sdk.Workers.RecentEmitRegistry(TimeSpan.Zero, TimeSpan.Zero),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        worker.Start(cts.Token);
        while (worker.FramesProcessed < 3 && !cts.IsCancellationRequested) await Task.Delay(50);
        cts.Cancel();
        // Döngünün gerçekten durmasını bekle (listeler döngü iş parçacığına aittir).
        for (long last = -1; last != worker.FramesProcessed; ) { last = worker.FramesProcessed; await Task.Delay(300); }

        Assert.True(worker.FramesProcessed >= 3, "işçi kare işlemedi");
        if (enhance)
        {
            Assert.True(worker.CropBucketCount >= 1, "kanıt kapalıyken zenginleştirme için kırpıntı SAKLANMADI (2.2.0 öncesi hata)");
            Assert.Equal(0, worker.CropBucketFramesHeld);   // 4 MP tam kare tutulmaz (bellek)
            Assert.Equal(0, worker.FallbackCropCount);      // yedek kırpıntı yalnız kanıt içindir
        }
        else
        {
            Assert.Equal(0, worker.CropBucketCount);
            Assert.Equal(0, worker.FallbackCropCount);
        }
    }
}

public class InjectObservationTests
{
    private static Anpr.Core.PlateReading Reading(string text, float conf) => new()
    {
        Text = text, RawText = text.Replace(" ", ""), Confidence = conf,
        DetectorConfidence = conf, OcrConfidence = conf,
        Box = new Anpr.Core.PlateBox(10, 10, 120, 50), IsValidTr = true,
    };

    [Fact]
    public void Injected_observation_updates_consensus_without_double_emit()
    {
        var c = new Anpr.Core.Confirmation.PlateConfirmer(new Anpr.Core.Confirmation.PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 2,
            FuzzyMergeDistance = 1,
            EarlyEmitMinOcr = 0.9f, // düşük güvenli iz bekletilir -> enhance penceresi
        });
        var t = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("25 ABC 123", 0.6f), t);
        c.Observe(Reading("25 ABC 123", 0.6f), t.AddSeconds(0.2));

        var pending = c.PendingLowConf(t.AddSeconds(0.4), 0.98f);
        Assert.Single(pending);

        // Zenginleştirilmiş yüksek güvenli gözlem enjekte edilir
        c.InjectObservation(pending[0].Key, Reading("25 ABC 123", 0.97f));
        c.MarkEnhanced(pending[0].Key);

        Assert.Empty(c.PendingLowConf(t.AddSeconds(0.5), 0.98f)); // tek deneme garantisi

        var ready = c.DrainReady(t.AddSeconds(4)); // settle
        Assert.Single(ready); // TEK emit
        Assert.Equal("25 ABC 123", ready[0].Text);
        Assert.Empty(c.DrainReady(t.AddSeconds(5))); // ikinci emit yok
    }
}
