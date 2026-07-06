using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Anpr.Core;
using Anpr.Core.Confirmation;
using SkiaSharp;

// OtoparkAnpr ölçüm/doğrulama düzeneği.
//   label    <datasetDir> <camera> [--ocr f] [--det f] [--out dir] [--fps N]
//   evaluate <datasetDir> <labels.json> [--ocr f] [--det f] [--fps N] [--minconf X]
//                                       [--minocr X] [--minconfirm N] [--label tag]
// Kareler dosya adına göre sıralı; sentetik zaman = index / fps.

string modelsDir = "/Users/omer/Desktop/plakaokuma/OtoparkAnpr/models";

if (args.Length < 2) { Usage(); return; }
string mode = args[0];
var opt = ParseArgs(args);

switch (mode)
{
    case "label": RunLabel(args[1], Arg(args, 2) ?? "giris", opt); break;
    case "evaluate": RunEvaluate(args[1], args.Length > 2 ? args[2] : "labels.json", opt); break;
    default: Usage(); break;
}
return;

// ---------------------------------------------------------------- LABEL
void RunLabel(string datasetDir, string camera, BenchOptions o)
{
    string camDir = Path.Combine(datasetDir, camera);
    var frames = LoadFrames(camDir, o.Fps);
    if (frames.Count == 0) { Console.WriteLine($"Kare yok: {camDir}"); return; }
    Console.WriteLine($"[{camera}] {frames.Count} kare yükleniyor, model çalışıyor...");

    using var engine = MakeEngine(o);
    string outDir = o.OutDir ?? Path.Combine(datasetDir, "bench_out", camera);
    Directory.CreateDirectory(outDir);

    // Her kareyi işle; aktif (geçerli okuma olan) ardışık kareleri "appearance"a grupla.
    var appearances = new List<Appearance>();
    Appearance? cur = null;
    double gapSec = 2.0;

    foreach (var f in frames)
    {
        var readings = SafeRecognize(engine, f.Path);
        var best = readings.Where(r => r.IsValidTr).OrderByDescending(r => r.Confidence).FirstOrDefault();
        if (best == null)
        {
            if (cur != null && f.Sec - cur.EndSec > gapSec) { appearances.Add(cur); cur = null; }
            continue;
        }
        if (cur == null || f.Sec - cur.EndSec > gapSec)
        {
            if (cur != null) appearances.Add(cur);
            cur = new Appearance { Camera = camera, StartFrame = f.Index, StartSec = f.Sec };
        }
        cur.EndFrame = f.Index;
        cur.EndSec = f.Sec;
        cur.Count++;
        if (best.Confidence > cur.BestConf)
        {
            cur.BestConf = best.Confidence;
            cur.ModelGuess = best.Text;
            cur.BestFramePath = f.Path;
            cur.BestBox = best.Box;
            cur.BestOcr = best.OcrConfidence;
        }
    }
    if (cur != null) appearances.Add(cur);

    // Çok kısa (1 kare) gürültüyü ele; kalanları kaydet + kırpıntı kompoziti üret.
    int idx = 0;
    var scaffold = new List<object>();
    foreach (var a in appearances)
    {
        idx++;
        a.Id = $"{camera}-{idx:000}";
        SaveCrop(a, Path.Combine(outDir, $"{a.Id}_{a.ModelGuess.Replace(' ', '_')}_ocr{(int)(a.BestOcr * 100)}.png"));
        scaffold.Add(new
        {
            id = a.Id, camera, startFrame = a.StartFrame, endFrame = a.EndFrame,
            startSec = Math.Round(a.StartSec, 2), endSec = Math.Round(a.EndSec, 2),
            frames = a.Count, modelGuess = a.ModelGuess, ocr = Math.Round(a.BestOcr, 2),
            truePlate = "", // <- elle doldur: gerçek plaka, "?" okunamıyor, "" araç değil
        });
    }

    string scaffoldPath = Path.Combine(outDir, "labels.scaffold.json");
    File.WriteAllText(scaffoldPath, JsonSerializer.Serialize(scaffold, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"[{camera}] {appearances.Count} araç görünümü -> {outDir}");
    Console.WriteLine($"  kırpıntılar + {scaffoldPath} (truePlate alanlarını doldurun)");
}

// ---------------------------------------------------------------- EVALUATE
void RunEvaluate(string datasetDir, string labelsPath, BenchOptions o)
{
    if (!File.Exists(labelsPath)) { Console.WriteLine($"labels yok: {labelsPath}"); return; }
    var labels = JsonSerializer.Deserialize<List<Label>>(File.ReadAllText(labelsPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    var cars = labels.Where(l => !string.IsNullOrWhiteSpace(l.TruePlate) && l.TruePlate != "?" )
                     .ToList();
    Console.WriteLine($"=== EVALUATE [{o.Tag}] ocr={Path.GetFileName(o.Ocr)} det={Path.GetFileName(o.Det)} " +
                      $"fps={o.Fps} minconf={o.MinConf} minocr={o.MinOcr} confirm={o.MinConfirm} ===");
    Console.WriteLine($"Etiketli gerçek araç: {cars.Count} (toplam görünüm {labels.Count})");

    using var engine = MakeEngine(o);
    double totalMs = 0; int framesProcessed = 0;
    var emitted = new List<(string camera, double sec, PlateReading r)>();

    foreach (var camera in cars.Select(c => c.Camera).Distinct())
    {
        var frames = LoadFrames(Path.Combine(datasetDir, camera), o.Fps);
        var confirmer = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(o.SettleSec),
            MinConfirmations = o.MinConfirm,
            CharacterVoting = o.CharVote,
        });
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        foreach (var f in frames)
        {
            var sw = Stopwatch.StartNew();
            var readings = SafeRecognize(engine, f.Path);
            sw.Stop(); totalMs += sw.Elapsed.TotalMilliseconds; framesProcessed++;

            var now = epoch.AddSeconds(f.Sec);
            foreach (var r in readings)
            {
                if (!r.IsValidTr) continue;
                if (r.Confidence < o.MinConf) continue;
                if (r.OcrConfidence < o.MinOcr) continue;
                confirmer.Observe(r, now);
            }
            foreach (var best in confirmer.DrainReady(now))
                emitted.Add((camera, f.Sec, best));
        }
        // kalanları boşalt
        foreach (var best in confirmer.DrainReady(epoch.AddSeconds(frames[^1].Sec + 100)))
            emitted.Add((camera, frames[^1].Sec, best));
    }

    // Eşleştir: her emit'i, aynı kamerada zaman penceresi içeren etiketli araca bağla.
    int correct = 0, wrong = 0, falsePos = 0;
    double charAccSum = 0;
    var matchedCar = new HashSet<string>();
    foreach (var (camera, sec, r) in emitted)
    {
        var car = cars.FirstOrDefault(c => c.Camera == camera && sec >= c.StartSec - 3 && sec <= c.EndSec + 3);
        if (car == null) { falsePos++; continue; }
        string got = Norm(r.Text), exp = Norm(car.TruePlate);
        if (got == exp) { correct++; charAccSum += 1; matchedCar.Add(car.Id); }
        else { wrong++; charAccSum += CharSim(got, exp); }
    }
    int missed = cars.Count(c => !matchedCar.Contains(c.Id));

    double plateAcc = cars.Count > 0 ? (double)matchedCar.Count / cars.Count : 0;
    double charAcc = (correct + wrong) > 0 ? charAccSum / (correct + wrong) : 0;
    Console.WriteLine($"  Plaka tam-eşleşme: {matchedCar.Count}/{cars.Count} = {plateAcc:P1}");
    Console.WriteLine($"  Doğru emit={correct} | yanlış emit={wrong} | yanlış-pozitif={falsePos} | kaçan araç={missed}");
    Console.WriteLine($"  Karakter benzerliği (emitler): {charAcc:P1}");
    Console.WriteLine($"  Ortalama gecikme: {(framesProcessed > 0 ? totalMs / framesProcessed : 0):F1} ms/kare ({framesProcessed} kare)");
}

// ---------------------------------------------------------------- helpers
AnprEngine MakeEngine(BenchOptions o) => AnprEngine.Create(new AnprOptions
{
    DetectorModelPath = o.Det,
    OcrModelPath = o.Ocr,
    OcrConfigPath = o.OcrCfg,
    OnnxIntraOpThreads = 2,
    MinConfidence = 0.25f,
    ApplyTurkishValidation = true,
    DeskewPlates = o.Deskew,
    MinPlateWidthRatio = o.MinPlateW,
});

static IReadOnlyList<PlateReading> SafeRecognize(AnprEngine e, string path)
{
    try { return e.Recognize(File.ReadAllBytes(path)); }
    catch { return Array.Empty<PlateReading>(); }
}

static List<Frame> LoadFrames(string dir, double fps)
{
    if (!Directory.Exists(dir)) return new();
    var files = Directory.GetFiles(dir, "*.jpg").OrderBy(x => x, StringComparer.Ordinal).ToList();
    var list = new List<Frame>(files.Count);
    for (int i = 0; i < files.Count; i++)
        list.Add(new Frame { Index = i, Path = files[i], Sec = i / (fps > 0 ? fps : 3) });
    return list;
}

static void SaveCrop(Appearance a, string outPath)
{
    if (a.BestFramePath == null) return;
    using var bmp = SKBitmap.Decode(a.BestFramePath);
    if (bmp == null) return;
    var b = a.BestBox;
    int px = (int)(b.Width * 0.25), py = (int)(b.Height * 0.6);
    int x = Math.Max(0, b.X - px), y = Math.Max(0, b.Y - py);
    int right = Math.Min(bmp.Width, b.X + b.Width + px), bottom = Math.Min(bmp.Height, b.Y + b.Height + py);
    if (right - x < 4 || bottom - y < 4) return;
    using var sub = new SKBitmap();
    if (!bmp.ExtractSubset(sub, new SKRectI(x, y, right, bottom))) return;
    float scale = Math.Max(3f, 420f / Math.Max(1, sub.Width));
    int sw = (int)(sub.Width * scale), sh = (int)(sub.Height * scale);
    using var big = sub.Resize(new SKImageInfo(sw, sh), SKFilterQuality.High) ?? sub;
    using var canvas = new SKBitmap(Math.Max(sw, 560), sh + 44);
    using (var c = new SKCanvas(canvas))
    {
        c.Clear(SKColors.White);
        c.DrawBitmap(big, 0, 0);
        using var paint = new SKPaint { Color = SKColors.Black, TextSize = 26, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) };
        c.DrawText($"{a.Id}  model: {a.ModelGuess}  ocr={a.BestOcr:P0}  kare={a.Count}", 6, sh + 30, paint);
    }
    using var img = SKImage.FromBitmap(canvas);
    using var data = img.Encode(SKEncodedImageFormat.Png, 95);
    File.WriteAllBytes(outPath, data.ToArray());
}

static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

static double CharSim(string a, string b)
{
    if (a.Length == 0 && b.Length == 0) return 1;
    int d = Levenshtein(a, b);
    return 1.0 - (double)d / Math.Max(a.Length, b.Length);
}

static int Levenshtein(string a, string b)
{
    var dp = new int[a.Length + 1, b.Length + 1];
    for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
    for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
    for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
            dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                                dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
    return dp[a.Length, b.Length];
}

BenchOptions ParseArgs(string[] a)
{
    string ocr = ArgVal(a, "--ocr") ?? "cct_xs_v1_global.onnx";
    string cfg = ocr.Replace(".onnx", "_plate_config.yaml");
    return new BenchOptions
    {
        Det = Path.Combine(modelsDir, ArgVal(a, "--det") ?? "yolo-v9-t-384-license-plates-end2end.onnx"),
        Ocr = Path.Combine(modelsDir, ocr),
        OcrCfg = Path.Combine(modelsDir, cfg),
        Fps = double.Parse(ArgVal(a, "--fps") ?? "3", CultureInfo.InvariantCulture),
        MinConf = float.Parse(ArgVal(a, "--minconf") ?? "0", CultureInfo.InvariantCulture),
        MinOcr = float.Parse(ArgVal(a, "--minocr") ?? "0", CultureInfo.InvariantCulture),
        MinConfirm = int.Parse(ArgVal(a, "--minconfirm") ?? "2"),
        SettleSec = double.Parse(ArgVal(a, "--settle") ?? "3", CultureInfo.InvariantCulture),
        CharVote = (ArgVal(a, "--charvote") ?? "true") != "false",
        Deskew = (ArgVal(a, "--deskew") ?? "false") == "true",
        MinPlateW = float.Parse(ArgVal(a, "--minwidth") ?? "0", CultureInfo.InvariantCulture),
        OutDir = ArgVal(a, "--out"),
        Tag = ArgVal(a, "--label") ?? "cfg",
    };
}

static string? Arg(string[] a, int i) => i < a.Length && !a[i].StartsWith("--") ? a[i] : null;
static string? ArgVal(string[] a, string key)
{
    int i = Array.IndexOf(a, key);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

void Usage()
{
    Console.WriteLine("Kullanım:");
    Console.WriteLine("  label    <datasetDir> <camera> [--ocr f] [--det f] [--out dir] [--fps N]");
    Console.WriteLine("  evaluate <datasetDir> <labels.json> [--ocr f] [--det f] [--fps N] [--minocr X] [--minconfirm N] [--label tag]");
}

sealed class BenchOptions
{
    public string Det = "", Ocr = "", OcrCfg = "";
    public double Fps = 3, SettleSec = 3;
    public float MinConf, MinOcr;
    public int MinConfirm = 2;
    public bool CharVote = true;
    public bool Deskew = false;
    public float MinPlateW = 0;
    public string? OutDir;
    public string Tag = "cfg";
}
sealed class Frame { public int Index; public string Path = ""; public double Sec; }
sealed class Appearance
{
    public string Camera = "", Id = "", ModelGuess = "";
    public int StartFrame, EndFrame, Count;
    public double StartSec, EndSec, BestConf, BestOcr;
    public string? BestFramePath;
    public PlateBox BestBox;
}
sealed class Label
{
    public string Id { get; set; } = "";
    public string Camera { get; set; } = "";
    public double StartSec { get; set; }
    public double EndSec { get; set; }
    public string TruePlate { get; set; } = "";
}
