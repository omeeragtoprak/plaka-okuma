using Anpr.Core;
using Anpr.Core.Detection;
using Anpr.Core.Onnx;

namespace Anpr.Tests;

/// <summary>
/// ONNX oturum paylaşımı: aynı model+ayar tek oturum; sonuçlar paylaşımsız yolla AYNI; eşzamanlı kullanımda da aynı.
/// Bu testler bellek kazancının doğruluğa bedel ödetmediğinin kanıtıdır — paylaşım "aynı sonucu daha az bellekle"
/// vermiyorsa kapatılmalıdır.
/// </summary>
[Collection("OnnxSessionPool")]   // havuz süreç genelidir; sayım yapan testler paralel koşmamalı
public class OnnxSessionPoolTests
{
    private static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");
    private static string Det => Path.Combine(ModelsDir, "yolo-v9-t-384-license-plates-end2end.onnx");

    private static AnprOptions Options(bool share, int threads = 2) => new()
    {
        DetectorModelPath = Det,
        OcrModelPath = Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"),
        OcrConfigPath = Path.Combine(ModelsDir, "cct_xs_v1_global_plate_config.yaml"),
        OnnxIntraOpThreads = threads,
        MinConfidence = 0.25f,
        ShareOnnxSessions = share,
    };

    private static string Sig(IReadOnlyList<PlateReading> rs)
        => string.Join(";", rs.Select(r => $"{r.RawText}|{r.Box.X},{r.Box.Y},{r.Box.Width},{r.Box.Height}|{r.DetectorConfidence:R}|{r.OcrConfidence:R}"));

    [Fact]
    public void Same_model_and_settings_share_one_session_and_different_settings_do_not()
    {
        var o = Options(share: true);
        using var a = new LicensePlateDetector(Det, o.CreateSessionOptions(), 0.25f, o.SessionShareKey);
        using var b = new LicensePlateDetector(Det, o.CreateSessionOptions(), 0.25f, o.SessionShareKey);
        Assert.Same(a.Session, b.Session);

        var o1 = Options(share: true, threads: 1);          // farklı iş parçacığı ayarı → AYRI oturum
        using var c = new LicensePlateDetector(Det, o1.CreateSessionOptions(), 0.25f, o1.SessionShareKey);
        Assert.NotSame(a.Session, c.Session);

        var off = Options(share: false);                    // paylaşım kapalı → anahtar yok → AYRI oturum
        Assert.Null(off.SessionShareKey);
        using var d = new LicensePlateDetector(Det, off.CreateSessionOptions(), 0.25f, off.SessionShareKey);
        Assert.NotSame(a.Session, d.Session);
    }

    [Fact]
    public void Shared_session_survives_until_the_last_user_disposes()
    {
        var o = Options(share: true, threads: 3);           // bu teste özel anahtar (başka test aynı oturumu tutmasın)
        var a = new LicensePlateDetector(Det, o.CreateSessionOptions(), 0.25f, o.SessionShareKey);
        var b = new LicensePlateDetector(Det, o.CreateSessionOptions(), 0.25f, o.SessionShareKey);
        var session = a.Session;
        Assert.Equal(2, OnnxSessionPool.RefCount(session));

        using var img = SkiaSharp.SKBitmap.Decode(Path.Combine(ModelsDir, "sample_car.jpg"));
        int n = b.Detect(img).Count;
        Assert.True(n > 0);
        a.Dispose();                                        // biri bıraktı: oturum AÇIK kalmalı
        Assert.Equal(1, OnnxSessionPool.RefCount(session));
        Assert.Equal(n, b.Detect(img).Count);               // diğeri çalışmaya devam eder
        a.Dispose();                                        // çift Dispose sayacı BOZMAMALI (yoksa b'nin oturumu kapanırdı)
        Assert.Equal(1, OnnxSessionPool.RefCount(session));
        Assert.Equal(n, b.Detect(img).Count);
        b.Dispose();                                        // son kullanıcı → oturum havuzdan çıkar ve kapanır
        Assert.Equal(0, OnnxSessionPool.RefCount(session));
    }

    [Fact]
    public void Shared_and_unshared_engines_give_identical_readings()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ModelsDir, "sample_car.jpg"));
        using var solo = AnprEngine.Create(Options(share: false));
        using var s1 = AnprEngine.Create(Options(share: true));
        using var s2 = AnprEngine.Create(Options(share: true));
        string want = Sig(solo.Recognize(bytes));
        Assert.Contains("UD1234", want);
        Assert.Equal(want, Sig(s1.Recognize(bytes)));
        Assert.Equal(want, Sig(s2.Recognize(bytes)));
    }

    [Fact]
    public async Task Two_engines_sharing_sessions_agree_under_concurrent_use()
    {
        // İki kamera = iki iş parçacığı, tek oturum. Her çağrı tek-iş-parçacıklı sonuçla AYNI olmalı.
        var bytes = File.ReadAllBytes(Path.Combine(ModelsDir, "sample_car.jpg"));
        using var e1 = AnprEngine.Create(Options(share: true));
        using var e2 = AnprEngine.Create(Options(share: true));
        string want = Sig(e1.Recognize(bytes));

        using var start = new ManualResetEventSlim(false);
        string[] Run(AnprEngine e)
        {
            start.Wait();
            var got = new string[25];
            for (int i = 0; i < got.Length; i++) got[i] = Sig(e.Recognize(bytes));
            return got;
        }
        var t1 = Task.Run(() => Run(e1));
        var t2 = Task.Run(() => Run(e2));
        start.Set();
        var all = (await t1).Concat(await t2).ToArray();
        Assert.All(all, s => Assert.Equal(want, s));
    }
}
