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
    case "fusetest": RunFuseTest(args[1], args.Length > 2 ? args[2] : "labels.json", opt); break;
    case "montage": RunMontage(args[1], args.Length > 2 ? args[2] : "grid"); break;
    case "widthstats": RunWidthStats(args[1], opt); break;
    case "vscamera": RunVsCamera(args[1], args[2], args[3], double.Parse(args[4], CultureInfo.InvariantCulture), opt); break;
    case "emitsim": RunEmitSim(args[1], opt); break;
    case "harvest": RunHarvest(args[1], args[2], opt); break;
    case "qscore": RunQScore(args[1], args.Length > 2 ? args[2] : "qscore.csv"); break;
    case "rectify": RunRectify(args[1], args.Length > 2 ? args[2] : "rectified.png", opt); break;
    case "cropab": RunCropAb(args[1], opt); break;
    case "detect": RunDetect(args[1], args.Length > 2 ? args[2] : "predictions.jsonl", opt); break;
    case "framebench": RunFrameBench(args[1], args.Length > 2 ? int.Parse(args[2]) : 20, opt); break;
    case "membench": RunMemBench(args[1], opt); break;
    case "replayab": RunReplayAb(args[1], opt); break;
    case "gatedump": RunGateDump(args[1], opt); break;
    case "replayrun": RunReplayRun(args[1], args, opt).GetAwaiter().GetResult(); break;
    case "inferbench": RunInferBench(args[1], args, opt); break;
    case "transportbench": RunTransportBench(args[1], args); break;
    default: Usage(); break;
}
return;

// ---------------------------------------------------------- CROP A/B (argmax vs TR gramer-kısıtlı çözümleme)
// Kırpıntı-seviye A/B: aynı OCR slot-dağılımından ham argmax metni ile TrConstrainedDecoder çıktısını
// gerçek-değere karşı karşılaştırır. Faz A'nın (varsayılan AÇIK) net katkısını offline ölçer.
// CSV: image_path,plate_text (CSV'nin bulunduğu klasöre göreli). Örn:
//   dotnet run -c Release --project Anpr.Bench -- cropab dataset/finetune/val.csv --ocr cct_xs_v2_erz.onnx
void RunCropAb(string csvPath, BenchOptions o)
{
    string baseDir = Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? ".";
    var lines = File.ReadAllLines(csvPath);
    if (lines.Length < 2) { Console.WriteLine("boş CSV"); return; }
    var header = lines[0].Split(',');
    int iImg = Array.IndexOf(header, "image_path"), iPlate = Array.IndexOf(header, "plate_text");
    if (iImg < 0 || iPlate < 0) { Console.WriteLine("image_path/plate_text kolonu gerekli"); return; }

    static string Compact(string? s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    // Emit floor (--minocr) üretim davranışını taklit eder: güven eşiği altındaki okuma YAYINLANMAZ
    // (metin = "" boş). Negatif (boş-plaka) örnekte doğru davranış = boş üretmek. Böylece gramer
    // decoder'ın halüsinasyonlarının floor tarafından geçitlenip geçitlenmediği ölçülür.
    float floor = o.MinOcr;
    using var rec = new Anpr.Core.Recognition.LicensePlateRecognizer(o.Ocr, o.OcrCfg, new Microsoft.ML.OnnxRuntime.SessionOptions());
    int tot = 0, negTot = 0, argOk = 0, gramOk = 0, help = 0, hurt = 0, invalidFallback = 0;
    int negHalluc = 0, negHallucGated = 0;   // negatiflerde gramer halüsinasyonu / floor ile geçitlenen
    var helpEx = new List<string>(); var hurtEx = new List<string>();

    foreach (var line in lines.Skip(1))
    {
        var cols = line.Split(',');
        if (cols.Length <= Math.Max(iImg, iPlate)) continue;
        string p = Path.Combine(baseDir, cols[iImg].Trim());
        if (!File.Exists(p)) continue;
        using var bmp = SKBitmap.Decode(p);
        if (bmp == null) continue;

        var res = rec.Recognize(bmp);
        int slots = res.Alphabet.Length > 0 ? res.SlotProbs.Length / res.Alphabet.Length : 0;
        string gt = Compact(cols[iPlate]);
        bool isNeg = gt.Length == 0;

        // argmax yolu: metin res.Text, güven res.MeanConfidence
        string arg = res.MeanConfidence >= floor ? Compact(res.Text) : "";
        // gramer yolu (üretim): dec.Ok ise metin+güven decoder'dan; değilse argmax'a düşer
        var dec = Anpr.Core.TurkishPlates.TrConstrainedDecoder.Decode(res.SlotProbs, res.Alphabet, slots, o.SkipChar);
        float gramConf = dec.Ok ? dec.Confidence : res.MeanConfidence;
        string gramRaw = dec.Ok ? dec.Compact : Compact(res.Text);
        string gram = gramConf >= floor ? gramRaw : "";
        if (!dec.Ok) invalidFallback++;

        tot++;
        if (isNeg)
        {
            negTot++;
            if (dec.Ok && dec.Compact.Length > 0) { negHalluc++; if (gramConf < floor) negHallucGated++; }
        }
        bool a = arg == gt, g = gram == gt;
        if (a) argOk++;
        if (g) gramOk++;
        if (g && !a) { help++; if (helpEx.Count < 15) helpEx.Add($"{gt}: argmax={arg} -> gramer={gram} (conf {gramConf:F2})"); }
        if (a && !g) { hurt++; if (hurtEx.Count < 15) hurtEx.Add($"{(isNeg ? "NEG" : gt)}: argmax={arg} -> gramer={gram} (conf {gramConf:F2})"); }
    }

    Console.WriteLine($"=== CROP A/B [{Path.GetFileName(csvPath)}] ocr={Path.GetFileName(o.Ocr)} skipchar={o.SkipChar} minocr(floor)={floor} ===");
    Console.WriteLine($"  N={tot} (negatif={negTot})  argmax={argOk} ({100.0 * argOk / Math.Max(1, tot):F1}%)  " +
                      $"gramer={gramOk} ({100.0 * gramOk / Math.Max(1, tot):F1}%)  net={gramOk - argOk:+0;-0;0}");
    Console.WriteLine($"  gramer yardım={help}  zarar={hurt}  (geçersiz->argmax fallback: {invalidFallback})");
    Console.WriteLine($"  negatif halüsinasyon: {negHalluc}/{negTot}  (floor {floor} ile geçitlenen: {negHallucGated})");
    if (helpEx.Count > 0) { Console.WriteLine("  --- gramer YARDIM ---"); foreach (var e in helpEx) Console.WriteLine("    " + e); }
    if (hurtEx.Count > 0) { Console.WriteLine("  --- gramer ZARAR ---"); foreach (var e in hurtEx) Console.WriteLine("    " + e); }
}

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
            ConstrainedDecoding = o.Constrained,
            MergeAcrossProvince = o.MergeProvince,
            FuzzyMergeDistance = o.Fuzzy,
            SpatialMergeIou = o.SpatialIou,
            SkipCharLevel = o.SkipChar,
        });
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        foreach (var f in frames)
        {
            var bytes = File.ReadAllBytes(f.Path);
            if (o.Degrade > 0) bytes = Degrade(bytes, o.Degrade); // sentetik bozma (zor set simülasyonu)
            var sw = Stopwatch.StartNew();
            var readings = SafeRecognizeBytes(engine, bytes);
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
            {
                if (best.OcrConfidence < o.EmitFloor) continue; // emit-seviyesi taban (Fix 2)
                emitted.Add((camera, f.Sec, best));
            }
        }
        // kalanları boşalt
        foreach (var best in confirmer.DrainReady(epoch.AddSeconds(frames[^1].Sec + 100)))
        {
            if (best.OcrConfidence < o.EmitFloor) continue;
            emitted.Add((camera, frames[^1].Sec, best));
        }
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
    Console.WriteLine($"  TOPLAM emit={emitted.Count} | Doğru emit={correct} | yanlış emit={wrong} | yanlış-pozitif={falsePos} | kaçan araç={missed}");
    Console.WriteLine($"  Karakter benzerliği (emitler): {charAcc:P1}");
    Console.WriteLine($"  Ortalama gecikme: {(framesProcessed > 0 ? totalMs / framesProcessed : 0):F1} ms/kare ({framesProcessed} kare)");
}

// ---------------------------------------------------------------- FUSETEST
// Her etiketli araç için: kareleri (bozarak) topla -> en iyi TEK kare okuması vs ÇOK-KARELİ FÜZYON okuması.
void RunFuseTest(string datasetDir, string labelsPath, BenchOptions o)
{
    if (!File.Exists(labelsPath)) { Console.WriteLine($"labels yok: {labelsPath}"); return; }
    var labels = JsonSerializer.Deserialize<List<Label>>(File.ReadAllText(labelsPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    var cars = labels.Where(l => !string.IsNullOrWhiteSpace(l.TruePlate) && l.TruePlate != "?").ToList();
    Console.WriteLine($"=== FUSETEST degrade={o.Degrade} fusionMax={o.FusionMax} ocr={Path.GetFileName(o.Ocr)} | {cars.Count} araç ===");

    using var engine = MakeEngine(o);
    int singleOk = 0, fusedOk = 0;

    foreach (var car in cars)
    {
        var frames = LoadFrames(Path.Combine(datasetDir, car.Camera), o.Fps)
            .Where(f => f.Sec >= car.StartSec - 1 && f.Sec <= car.EndSec + 1).ToList();

        var crops = new List<(SKBitmap crop, float conf, string text)>();
        foreach (var f in frames)
        {
            var bytes = File.ReadAllBytes(f.Path);
            if (o.Degrade > 0) bytes = Degrade(bytes, o.Degrade);
            IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> res;
            try { res = engine.RecognizeWithCrops(bytes); } catch { continue; }
            foreach (var (r, c) in res)
            {
                if (r.IsValidTr) crops.Add((c, r.OcrConfidence, r.Text));
                else c.Dispose();
            }
        }

        string exp = Norm(car.TruePlate);
        if (crops.Count == 0) { Console.WriteLine($"{car.Id}: (okuma yok) truth={car.TruePlate}"); continue; }

        var top = crops.OrderByDescending(c => c.conf).ToList();
        string single = top[0].text;
        var fusedReading = engine.RecognizeFused(top.Take(o.FusionMax).Select(c => c.crop).ToList());
        string fused = fusedReading?.Text ?? "";

        bool sOk = Norm(single) == exp, fOk = Norm(fused) == exp;
        if (sOk) singleOk++;
        if (fOk) fusedOk++;
        Console.WriteLine($"{car.Id}: truth={car.TruePlate} | tek={single}[{(sOk ? "OK" : "X")}] füzyon={fused}[{(fOk ? "OK" : "X")}] ({crops.Count} kare)");

        foreach (var c in crops) c.crop.Dispose();
    }

    Console.WriteLine($"--- TEK-kare en iyi: {singleOk}/{cars.Count} | ÇOK-KARELİ FÜZYON: {fusedOk}/{cars.Count} ---");
}

// ---------------------------------------------------------------- RECTIFY (teşhis)
// Tek kırpıntıda WPOD rektifikasyonu: quad/eğiklik yazdırır, warp'ı kaydeder, ham vs rektifiye OCR karşılaştırır.
void RunRectify(string imgPath, string outPath, BenchOptions o)
{
    o.EnhanceRectify = true;
    using var engine = MakeEngine(o);
    using var raw = SkiaSharp.SKBitmap.Decode(imgPath)?.Copy(SkiaSharp.SKColorType.Rgba8888)
        ?? throw new FileNotFoundException(imgPath);
    if (!engine.RectifierAvailable) { Console.WriteLine("WPOD modeli yok/yüklenemedi"); return; }

    var r0 = engine.ReadCrop(raw);
    Console.WriteLine($"HAM      : '{r0?.Text}' ocr={r0?.OcrConfidence:P0} tr={r0?.IsValidTr}");
    var rect = engine.TryRectify(raw);
    if (rect == null) { Console.WriteLine("quad bulunamadı"); return; }
    using var rimg = rect.Value.Image;
    Console.WriteLine($"QUAD     : prob={rect.Value.Prob:F2} skew={rect.Value.SkewDeg:F1}° persp={rect.Value.PerspRatio:F2}");
    var r1 = engine.ReadCrop(rimg);
    Console.WriteLine($"REKTİFİYE: '{r1?.Text}' ocr={r1?.OcrConfidence:P0} tr={r1?.IsValidTr}");
    using var img = SkiaSharp.SKImage.FromBitmap(rimg);
    using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(outPath, data.ToArray());
    Console.WriteLine($"-> {outPath}");
}

// ---------------------------------------------------------------- QSCORE
// Kalite skoru dökümü: CSV'deki (image_path,plate_text) kırpıntıların C# CropQualityScorer
// skorlarını yazar — eşik taraması C# ölçeğiyle yapılsın diye (python/C# tutarlılığı).
void RunQScore(string csvPath, string outPath)
{
    string baseDir = Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? ".";
    using var w = new StreamWriter(outPath);
    w.WriteLine("image_path,blur,dark_pct,bright_pct,mean_gray");
    int n = 0;
    foreach (var line in File.ReadLines(csvPath).Skip(1))
    {
        int comma = line.LastIndexOf(',');
        if (comma <= 0) continue;
        string p = line[..comma];
        if (!Path.IsPathRooted(p)) p = Path.GetFullPath(Path.Combine(baseDir, p));
        if (!File.Exists(p)) continue;
        using var raw = SkiaSharp.SKBitmap.Decode(p);
        if (raw == null) continue;
        using var bmp = raw.ColorType == SkiaSharp.SKColorType.Rgba8888
            ? raw.Copy(SkiaSharp.SKColorType.Rgba8888)
            : raw.Copy(SkiaSharp.SKColorType.Rgba8888);
        var q = Anpr.Core.Imaging.CropQualityScorer.Score(bmp);
        w.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{p},{q.BlurVar:F1},{q.DarkPct:F4},{q.BrightPct:F4},{q.MeanGray:F1}"));
        n++;
    }
    Console.WriteLine($"qscore: {n} kırpıntı -> {outPath}");
}

// ---------------------------------------------------------------- HARVEST
// Fine-tune veri hasadı: her karede geçerli-TR plaka kırpıntılarını kaydeder (kaynak çözünürlükte).
// Dosya adı: s<saniye>_<okunan>_c<güven>.png — saniye, 5fps yakalama varsayımıyla kare-indeksinden.
// Sonrasında scripts/autolabel.py kamera gerçek-değeriyle eşleştirip etiketler.
void RunHarvest(string framesDir, string outDir, BenchOptions o)
{
    const double captureFps = 5.0;
    Directory.CreateDirectory(outDir);
    var frames = Directory.GetFiles(framesDir, "*.jpg").OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (frames.Length == 0) { Console.WriteLine("kare yok"); return; }
    int step = Math.Max(1, (int)Math.Round(captureFps / (o.Fps > 0 ? o.Fps : captureFps)));
    using var engine = MakeEngine(o);
    int saved = 0;
    for (int i = 0; i < frames.Length; i += step)
    {
        double sec = i / captureFps;
        IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> res;
        try { res = engine.RecognizeWithCrops(File.ReadAllBytes(frames[i])); } catch { continue; }
        foreach (var (r, c) in res)
        {
            if (r.IsValidTr && r.OcrConfidence >= o.MinOcr)
            {
                using var img = SKImage.FromBitmap(c);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(outDir, $"s{sec.ToString("F1", CultureInfo.InvariantCulture)}_{Norm(r.Text)}_c{(int)(r.OcrConfidence * 100)}.png"), data.ToArray());
                saved++;
            }
            c.Dispose();
        }
    }
    Console.WriteLine($"harvest: {frames.Length} kare tarandı, {saved} kırpıntı -> {outDir}");
}

// ---------------------------------------------------------------- EMITSIM
// Bir kare dizisini (tek kamera, sabit fps yakalanmış) canlı emisyon pipeline'ından geçirir:
// o.Fps'e alt-örnekle, confirmer ayarlarıyla (MinConfirmations vb.) yayınlanan DISTINCT plakaları verir.
// Emisyon mantığının recall'e etkisini ölçmek için.
void RunEmitSim(string framesDir, BenchOptions o)
{
    const double captureFps = 5.0;
    var frames = Directory.GetFiles(framesDir, "*.jpg").OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (frames.Length == 0) { Console.WriteLine("kare yok"); return; }
    int step = Math.Max(1, (int)Math.Round(captureFps / (o.Fps > 0 ? o.Fps : 1)));

    using var engine = MakeEngine(o);
    var confirmer = new PlateConfirmer(new PlateConfirmerOptions
    {
        DedupWindow = TimeSpan.FromSeconds(o.DedupSec),
        SettleWindow = TimeSpan.FromSeconds(o.SettleSec),
        MinConfirmations = o.MinConfirm,
        CharacterVoting = o.CharVote,
        ConstrainedDecoding = o.Constrained,
        MergeAcrossProvince = o.MergeProvince,
        FuzzyMergeDistance = o.Fuzzy,
        EarlyEmitMinOcr = o.EarlyEmit,
        SpatialMergeIou = o.SpatialIou,
        SkipCharLevel = o.SkipChar,
    });
    var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var emitted = new List<string>();
    var details = new List<string>(); // görsel doğrulama için: hangi saniyede, hangi kutuda
    var lastSeen = new Dictionary<string, double>(); // plaka -> son GÖRÜLME saniyesi (emit anı değil!)
    // KnownPlates snap (pipeline-seviyesi ölçüm): whitelist yüklüyse ve OCR güveni SnapGate altındaysa
    // emit edilen plaka bilinen plakaya lev≤1 düzeltilir (CameraWorker'daki üretim davranışının aynısı).
    var snapList = o.SnapFile != null && File.Exists(o.SnapFile)
        ? (System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(o.SnapFile)) ?? new())
            .Select(Norm).Where(p => p.Length >= 5).Distinct().ToList()
        : new List<string>();
    var snapSet = new HashSet<string>(snapList);
    if (snapList.Count > 0) Console.WriteLine($"[snap AÇIK: {snapList.Count} bilinen plaka, gate<{o.SnapGate}]");
    void Drain(DateTimeOffset now)
    {
        foreach (var b in confirmer.DrainReady(now))
            if (b.OcrConfidence >= o.EmitFloor)
            {
                string pc = Norm(b.Text);
                if (snapList.Count > 0 && b.OcrConfidence < o.SnapGate) pc = Snap(pc, snapSet, snapList);
                emitted.Add(pc);
                double seenSec = lastSeen.TryGetValue(pc, out var s) ? s : (now - epoch).TotalSeconds;
                details.Add($"EMIT sec={seenSec:F1} plate={pc} ocr={b.OcrConfidence:F2} box={b.Box.X},{b.Box.Y},{b.Box.Width},{b.Box.Height}");
            }
    }
    for (int i = 0; i < frames.Length; i += step)
    {
        var now = epoch.AddSeconds(i / captureFps);
        IReadOnlyList<PlateReading> rs;
        try { rs = engine.Recognize(File.ReadAllBytes(frames[i])); } catch { continue; }
        foreach (var r in rs)
            if (r.IsValidTr && r.Confidence >= o.MinConf && r.OcrConfidence >= o.MinOcr)
            {
                if (o.Trace.Length > 0 && Levenshtein(Norm(r.Text), o.Trace) <= 2)
                    Console.WriteLine($"  OBS sec={i / captureFps:F1} text={Norm(r.Text)} ocr={r.OcrConfidence:F2} det={r.DetectorConfidence:F2} box={r.Box.X},{r.Box.Y},{r.Box.Width},{r.Box.Height}");
                confirmer.Observe(r, now);
                lastSeen[Norm(r.Text)] = i / captureFps;
            }
        Drain(now);
    }
    Drain(epoch.AddSeconds(frames.Length / captureFps + 100));
    foreach (var d in details) Console.WriteLine("  " + d);
    var distinct = emitted.Distinct().OrderBy(x => x).ToList();
    Console.WriteLine($"  [fps={o.Fps} minConfirm={o.MinConfirm} minocr={o.MinOcr} emitfloor={o.EmitFloor} merge={o.MergeProvince} fuzzy={o.Fuzzy}] " +
                      $"toplam emit={emitted.Count}, DISTINCT plaka={distinct.Count}");
    Console.WriteLine("    " + string.Join(" · ", distinct));
}

// ---------------------------------------------------------------- VSCAMERA
// Teşhis: kameranın plaka okuduğu HER AN için, o ana denk gelen kendi karemizde OCR yap.
// "Model mi, yakalama mı?" → kamera anlarında bizim OCR doğruysa sorun YAKALAMA (fps/floor); değilse MODEL.
void RunVsCamera(string framesDir, string cameraXml, string t0iso, double fps, BenchOptions o)
{
    long T0 = (long)DateTimeOffset.Parse(t0iso).ToUnixTimeSeconds();
    var frames = Directory.GetFiles(framesDir, "*.jpg").OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (frames.Length == 0) { Console.WriteLine("kare yok"); return; }

    // kamera plakaları: (plate, epoch), T0 sonrası, plaka bazında ilk
    var xml = File.ReadAllText(cameraXml);
    var plates = System.Text.RegularExpressions.Regex.Matches(xml, "<plateNumber>([^<]*)</plateNumber>").Select(m => m.Groups[1].Value).ToList();
    var times = System.Text.RegularExpressions.Regex.Matches(xml, "<captureTime>([^<]*)</captureTime>").Select(m => m.Groups[1].Value).ToList();
    var seen = new HashSet<string>();
    var cam = new List<(string plate, long ep)>();
    for (int i = 0; i < Math.Min(plates.Count, times.Count); i++)
    {
        string pn = Norm(plates[i]); if (pn.Length == 0 || pn == "UNKNOWN") continue;
        long ep = ParseCamEpoch(times[i]); if (ep < T0 - 2) continue;
        if (seen.Add(pn)) cam.Add((pn, ep));
    }

    using var engine = MakeEngine(o);
    int correct = 0, near = 0, wrong = 0, noread = 0;
    var misses = new List<string>();
    foreach (var (plate, ep) in cam)
    {
        int idx = (int)Math.Round((ep - T0) * fps);
        string best = ""; float bestConf = -1;
        for (int k = idx - 3; k <= idx + 3; k++)
        {
            if (k < 0 || k >= frames.Length) continue;
            IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> res;
            try { res = engine.RecognizeWithCrops(File.ReadAllBytes(frames[k])); } catch { continue; }
            foreach (var (r, c) in res) { if (r.IsValidTr && r.OcrConfidence > bestConf) { bestConf = r.OcrConfidence; best = Norm(r.Text); } c.Dispose(); }
        }
        string tag;
        if (best.Length == 0) { noread++; tag = "OKUMA-YOK"; misses.Add($"  {plate,-10} -> (bizim okuma yok)"); }
        else if (best == plate) { correct++; tag = "DOĞRU"; }
        else if (Levenshtein(best, plate) == 1) { near++; misses.Add($"  {plate,-10} -> {best} (1 fark)"); tag = "1-FARK"; }
        else { wrong++; misses.Add($"  {plate,-10} -> {best} (yanlış)"); tag = "YANLIŞ"; }
    }
    int n = cam.Count;
    Console.WriteLine($"=== VSCAMERA: kameranın {n} plakası, her birinin ANINDA bizim OCR (±0.6s, {frames.Length} kare @ {fps}fps) ===");
    Console.WriteLine($"  DOĞRU: {correct}/{n} = {(n > 0 ? 100.0 * correct / n : 0):F0}%  |  1-fark: {near}  |  yanlış: {wrong}  |  okuma-yok: {noread}");
    Console.WriteLine($"  => bizim OCR, kamera-anlarında okuyabildiği: {(n > 0 ? 100.0 * (correct + near) / n : 0):F0}% (doğru+1fark)");
    Console.WriteLine("  --- kaçırdıklarımız/yanlışlar ---");
    foreach (var m in misses.Take(40)) Console.WriteLine(m);
}

static long ParseCamEpoch(string s)
{
    var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})([+-]\d{2})(\d{2})");
    if (!m.Success) return 0;
    int[] g = Enumerable.Range(1, 6).Select(i => int.Parse(m.Groups[i].Value)).ToArray();
    var off = TimeSpan.FromHours(int.Parse(m.Groups[7].Value));
    return new DateTimeOffset(g[0], g[1], g[2], g[3], g[4], g[5], off).ToUnixTimeSeconds();
}

// ---------------------------------------------------------------- WIDTHSTATS
// Geçerli plaka tespitlerinin plaka-genişlik/kare-genişlik oran dağılımı -> MinPlateWidthRatio için.
void RunWidthStats(string datasetDir, BenchOptions o)
{
    using var engine = MakeEngine(o);
    var ratios = new List<double>();
    var lowConf = new List<double>(); // ocr<0.9 tespitlerin oranları (riskli/uzak)
    int frameW = 0;
    foreach (var cam in new[] { "giris", "cikis" })
    {
        var camDir = Path.Combine(datasetDir, cam);
        if (!Directory.Exists(camDir)) continue;
        var files = Directory.GetFiles(camDir, "*.jpg").OrderBy(x => x, StringComparer.Ordinal).Where((_, i) => i % 5 == 0).ToList();
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(f);
            if (frameW == 0) { using var b = SKBitmap.Decode(bytes); frameW = b?.Width ?? 2688; }
            IReadOnlyList<(PlateReading Reading, SKBitmap Crop)> res;
            try { res = engine.RecognizeWithCrops(bytes); } catch { continue; }
            foreach (var (r, c) in res)
            {
                if (r.IsValidTr) { double rt = (double)r.Box.Width / frameW; ratios.Add(rt); if (r.OcrConfidence < 0.9f) lowConf.Add(rt); }
                c.Dispose();
            }
        }
    }
    if (ratios.Count == 0) { Console.WriteLine("tespit yok"); return; }
    ratios.Sort();
    double Pct(List<double> v, double p) => v.Count == 0 ? 0 : v[(int)(p * (v.Count - 1))];
    Console.WriteLine($"=== WIDTHSTATS: {ratios.Count} geçerli plaka tespiti | kare genişliği {frameW}px (her 5. kare) ===");
    Console.WriteLine($"  plaka-genişlik oranı (bbox.W/kare.W):");
    Console.WriteLine($"    min={ratios[0]:F3} p5={Pct(ratios, .05):F3} p10={Pct(ratios, .1):F3} p25={Pct(ratios, .25):F3} medyan={Pct(ratios, .5):F3} p75={Pct(ratios, .75):F3} max={ratios[^1]:F3}");
    Console.WriteLine($"  DÜŞÜK-GÜVEN (<%90) tespitlerin oranı: {lowConf.Count} adet, medyan={Pct(lowConf.OrderBy(x => x).ToList(), .5):F3}");
    Console.WriteLine($"  Öneri: MinPlateWidthRatio, düşük-güvenlilerin çoğunu eleyip yüksek-güvenlileri tutan eşik ~ p10-p25 ({Pct(ratios, .1):F3}-{Pct(ratios, .25):F3}).");
}

// ---------------------------------------------------------------- MONTAGE
// Bir dizindeki kırpıntı PNG'lerini ızgaralara dizer (görsel doğrulama için, sayfa başına 12 plaka).
void RunMontage(string dir, string outPrefix)
{
    var files = Directory.GetFiles(dir, "*.png").OrderBy(x => x, StringComparer.Ordinal).ToList();
    if (files.Count == 0) { Console.WriteLine($"PNG yok: {dir}"); return; }
    int cols = 2, rows = 2, per = cols * rows;
    int cw = 780, ch = 300;
    int pages = (files.Count + per - 1) / per;
    for (int p = 0; p < pages; p++)
    {
        using var canvas = new SKBitmap(new SKImageInfo(cols * cw, rows * ch, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var c = new SKCanvas(canvas))
        {
            c.Clear(SKColors.White);
            for (int i = 0; i < per; i++)
            {
                int idx = p * per + i;
                if (idx >= files.Count) break;
                using var img = SKBitmap.Decode(files[idx]);
                if (img == null) continue;
                int col = i % cols, row = i / cols;
                float scale = Math.Min((float)(cw - 8) / img.Width, (float)(ch - 8) / img.Height);
                int w = (int)(img.Width * scale), h = (int)(img.Height * scale);
                using var r = img.Resize(new SKImageInfo(w, h), SKFilterQuality.Medium) ?? img;
                c.DrawBitmap(r, col * cw + 4, row * ch + 4);
            }
        }
        string outPath = $"{outPrefix}_{p + 1}.png";
        using var oimg = SKImage.FromBitmap(canvas);
        using var data = oimg.Encode(SKEncodedImageFormat.Png, 92);
        File.WriteAllBytes(outPath, data.ToArray());
        Console.WriteLine(outPath);
    }
}

// ---------------------------------------------------------------- HAREKET KAPISI VERİ DÖKÜMÜ (2026-09-30)
// Kayıtlı akışın HER karesi için (a) hareket kapısının gördüğü parlaklık ızgarasını ve (b) motorun o karede
// bulduğu plakaları diske yazar. Kapı politikaları (eşik, bekleme, zorunlu tarama aralığı…) sonra bu döküm
// üzerinde saniyeler içinde BENZETİLİR (scripts/gate_sim.py) — canlıda günlerce "gölge" beklemek yerine.
//   dotnet run -c Release --project Anpr.Bench -- gatedump <kayıt.mkv> --roi "<spec>" --ocr cct_xs_v2_erz.onnx --out <dizin> [--fps 5] [--max N]
// Çıktı: <ad>.grids.u8 (kare × 64×36 bayt) · <ad>.frames.jsonl (kare başına tespitler) · <ad>.meta.json
// ⚠️ frames.jsonl PLAKA METNİ içerir (araçları gruplamak için) → yalnız gitignored dizine yazın (KVKK).
void RunGateDump(string video, BenchOptions opt)
{
    const int Cols = 64, Rows = 36;
    string outDir = opt.OutDir ?? ".";
    Directory.CreateDirectory(outDir);
    string stem = Path.Combine(outDir, Path.GetFileNameWithoutExtension(video));

    var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var a in new[] { "-nostdin", "-loglevel", "error", "-threads", "2", "-i", video, "-an", "-f", "yuv4mpegpipe", "-pix_fmt", "yuv420p",
                              "-vf", $"fps={opt.Fps.ToString(CultureInfo.InvariantCulture)}", "pipe:1" }) psi.ArgumentList.Add(a);
    using var ff = Process.Start(psi)!;
    _ = ff.StandardError.ReadToEndAsync();
    var st = ff.StandardOutput.BaseStream;
    static bool ReadExact(Stream s, byte[] buf, int off, int n)
    {
        while (n > 0) { int r = s.Read(buf, off, n); if (r <= 0) return false; off += r; n -= r; }
        return true;
    }
    var hdr = new List<byte>();
    for (int c; (c = st.ReadByte()) >= 0 && c != '\n';) hdr.Add((byte)c);
    string hline = Encoding.ASCII.GetString(hdr.ToArray());
    int W = int.Parse(hline.Split(' ').First(t => t[0] == 'W')[1..]), H = int.Parse(hline.Split(' ').First(t => t[0] == 'H')[1..]);
    bool full = hline.Contains("XCOLORRANGE=FULL");
    int yuvBytes = Anpr.Core.Imaging.Yuv420FrameView.FrameBytes(W, H);

    // Kapının baktığı dikdörtgen: CameraWorker.GateRoiRect ile AYNI (ROI sınır kutusu, %5 pay).
    var poly = Anpr.Core.Imaging.RoiPolygon.Parse(opt.Roi);
    var rr = poly?.BoundingRectPixels(W, H, 0.05f) ?? (0, 0, W, H);
    int rx = Math.Clamp(rr.Item1, 0, W - 1), ry = Math.Clamp(rr.Item2, 0, H - 1);
    int rw = Math.Clamp(rr.Item3, 2, W - rx), rh = Math.Clamp(rr.Item4, 2, H - ry);

    using var engine = MakeEngine(opt);
    using var grids = File.Create(stem + ".grids.u8");
    using var fw = new StreamWriter(stem + ".frames.jsonl");
    var mk = new byte[6]; var grid = new byte[Cols * Rows];
    int n = 0, max = opt.MaxFrames > 0 ? opt.MaxFrames : int.MaxValue;
    var sw = Stopwatch.StartNew();
    while (n < max)
    {
        if (!ReadExact(st, mk, 0, 6)) break;
        var yuv = new byte[yuvBytes];
        if (!ReadExact(st, yuv, 0, yuvBytes)) break;
        // ızgara: MotionGate.Sample (YUV dalı) ile aynı örnekleme noktaları
        for (int row = 0; row < Rows; row++)
        {
            int y = Math.Min(ry + (int)((row + 0.5) * rh / Rows), H - 1);
            for (int col = 0; col < Cols; col++)
            {
                int x = Math.Min(rx + (int)((col + 0.5) * rw / Cols), W - 1);
                grid[row * Cols + col] = yuv[y * W + x];
            }
        }
        grids.Write(grid);
        var frame = Anpr.Sdk.Frames.VideoFrame.FromYuv420(yuv, W, H, full, DateTimeOffset.UtcNow, n);
        var rs = engine.Recognize(frame.TryGetView()!);
        fw.WriteLine(JsonSerializer.Serialize(new
        {
            i = n,
            dets = rs.Select(r => new
            {
                x = r.Box.X, y = r.Box.Y, w = r.Box.Width, h = r.Box.Height, det = Math.Round(r.DetectorConfidence, 4),
                ocr = Math.Round(r.OcrConfidence, 4), valid = r.IsValidTr, zone = r.InZone, text = r.IsValidTr ? r.Text.Replace(" ", "") : r.RawText,
            }),
        }));
        n++;
        if (n % 500 == 0) Console.WriteLine($"  {n} kare ({sw.Elapsed.TotalSeconds:F0} sn)");
    }
    try { ff.Kill(); } catch { }
    File.WriteAllText(stem + ".meta.json", JsonSerializer.Serialize(new { video = Path.GetFileName(video), width = W, height = H, fps = opt.Fps, frames = n, cols = Cols, rows = Rows, rect = new[] { rx, ry, rw, rh }, limitedRange = !full, roi = opt.Roi }));
    Console.WriteLine($"✅ {n} kare → {stem}.grids.u8 / .frames.jsonl ({sw.Elapsed.TotalSeconds:F0} sn)");
}

// ---------------------------------------------------------------- UÇTAN UCA OYNATMA (2026-09-30)
// Kayıtlı kamera akışını GERÇEK ZAMANLI olarak ÜRÜNÜN KENDİSİNDEN (AnprRuntime) geçirir: ffmpeg → kare kaynağı →
// hareket kapısı → dedektör/OCR → çok-kareli oylama → olay. Çıktı: yayınlanan OLAYLAR + işlemci/bellek özeti.
// Aynı kayıt farklı ayarlarla oynatılıp olay listeleri karşılaştırılır → "bu ayar aynı plakaları yayınlıyor mu,
// kaç işlemci/bellek harcıyor?" sorusunun uçtan uca cevabı. (Gerçek zamanlıdır: 10 dk'lık kayıt 10 dk sürer.)
//   dotnet run -c Release --project Anpr.Bench -- replayrun <kayıt.mkv> --camera giris --config Anpr.Service/appsettings.json
//        --roi "<spec>" --out olaylar.jsonl [--seconds N] [--set Ffmpeg.OutputFormat=Y4m --set MotionGate=On ...]
async Task RunReplayRun(string video, string[] argv, BenchOptions opt)
{
    string cfgPath = ArgVal(argv, "--config") ?? "Anpr.Service/appsettings.json";
    string camId = ArgVal(argv, "--camera") ?? "cam";
    string? outPath = ArgVal(argv, "--out");
    var jo = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
    using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    var anprEl = doc.RootElement.TryGetProperty("Anpr", out var ae) ? ae : doc.RootElement;
    var o = JsonSerializer.Deserialize<Anpr.Sdk.Configuration.AnprServiceOptions>(anprEl.GetRawText(), jo)!;

    // Tek kamera: kayıt dosyası. -re = dosyayı gerçek zamanlı oku (canlı akış gibi).
    o.Cameras = new List<Anpr.Sdk.Configuration.CameraOptions>
    {
        new() { Id = camId, Name = camId, RtspUrl = Path.GetFullPath(video), Roi = opt.Roi },
    };
    // İkinci kamera (isteğe bağlı): iki kamerayı AYNI süreçte koşturmak gerçek kurulumun bellek/işlemci ölçüsüdür
    // (modeller kameralar arasında paylaşılır).
    if (ArgVal(argv, "--video2") is { } video2)
        o.Cameras.Add(new() { Id = ArgVal(argv, "--camera2") ?? "cam2", Name = "cam2", RtspUrl = Path.GetFullPath(video2),
                              Roi = ArgVal(argv, "--roi2") ?? "" });
    o.Ffmpeg.ExtraInputArgs = "-re";
    o.WebhookUrl = null;
    o.EvidenceCrops = (ArgVal(argv, "--evidence") ?? "false") == "true";
    o.SaveFullFrame = o.EvidenceCrops;
    // Model yolları: depo kökündeki models/ (yapılandırmadaki göreli yollar servis dizinine göredir)
    string M(string p) => Path.Combine(modelsDir, Path.GetFileName(p));
    o.DetectorModelPath = M(o.DetectorModelPath); o.OcrModelPath = M(o.OcrModelPath); o.OcrConfigPath = M(o.OcrConfigPath);
    o.WpodModelPath = M(o.WpodModelPath); o.SrModelPath = M(o.SrModelPath);

    // --set Ad=Değer / --set Ffmpeg.Ad=Değer : herhangi bir ayarı ez (A/B için yeni bayrak yazmaya gerek kalmasın)
    var sets = new List<string>();
    for (int i = 0; i < argv.Length - 1; i++) if (argv[i] == "--set") sets.Add(argv[i + 1]);
    foreach (var kv in sets)
    {
        int eq = kv.IndexOf('='); if (eq < 1) throw new ArgumentException($"--set biçimi Ad=Değer olmalı: {kv}");
        string path = kv[..eq], val = kv[(eq + 1)..];
        object target = o; var parts = path.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
            target = target.GetType().GetProperty(parts[i])?.GetValue(target) ?? throw new ArgumentException($"ayar yok: {path}");
        var prop = target.GetType().GetProperty(parts[^1]) ?? throw new ArgumentException($"ayar yok: {path}");
        var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        object v = t.IsEnum ? Enum.Parse(t, val, ignoreCase: true) : Convert.ChangeType(val, t, CultureInfo.InvariantCulture);
        prop.SetValue(target, v);
    }

    double seconds = double.Parse(ArgVal(argv, "--seconds") ?? "0", CultureInfo.InvariantCulture);
    if (seconds <= 0)
    {
        // Paket sayısından süre (Matroska'da süre başlığı olmayabilir): 25 fps varsayımı yerine ffprobe'a sor.
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts_time", "-of", "csv=p=0", video }) psi.ArgumentList.Add(a);
        using var pr = Process.Start(psi)!;
        string last = "0"; string? line;
        while ((line = pr.StandardOutput.ReadLine()) != null) if (line.Length > 0) last = line.Trim().TrimEnd(',');
        pr.WaitForExit();
        seconds = double.Parse(last, CultureInfo.InvariantCulture);
    }

    using var outW = outPath != null ? new StreamWriter(outPath) : null;
    int events = 0; object gate = new();
    var start = DateTimeOffset.UtcNow;
    await using var rt = new Anpr.Sdk.AnprRuntime(o);
    rt.PlateDetected += e =>
    {
        lock (gate)
        {
            events++;
            outW?.WriteLine(JsonSerializer.Serialize(new
            {
                t = Math.Round((e.Timestamp - start).TotalSeconds, 2), cam = e.CameraId, plate = e.PlateCompact, scope = e.Scope,
                ocr = Math.Round(e.OcrConfidence, 4), det = Math.Round(e.DetectorConfidence, 4), low = e.LowConfidence,
                reasons = e.LowConfidenceReasons, snappedFrom = e.SnappedFrom, frames = e.TrackFrames, w = e.Box.Width,
            }));
            outW?.Flush();
        }
    };
    using var me = Process.GetCurrentProcess();
    var cpu0 = me.TotalProcessorTime;
    start = DateTimeOffset.UtcNow;
    await rt.StartAsync();

    // Çocuk ffmpeg'in belleği/işlemci zamanı: tek-pid `ps` (tüm süreç tablosunu gezen komutlar takılabiliyor).
    static (double rssMb, double cpuS) Child(int parent)
    {
        try
        {
            static string Sh(string f, string a) { using var p = Process.Start(new ProcessStartInfo(f, a) { RedirectStandardOutput = true, UseShellExecute = false })!; string s = p.StandardOutput.ReadToEnd(); p.WaitForExit(2000); return s; }
            double rss = 0, cpu = 0;
            foreach (var pid in Sh("pgrep", $"-P {parent}").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = Sh("ps", $"-p {pid.Trim()} -o rss=,time=,comm=").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 3 || !f[2].Contains("ffmpeg")) continue;
                rss += double.Parse(f[0], CultureInfo.InvariantCulture) / 1024.0;
                var tp = f[1].Split(':');
                cpu += tp.Length == 3 ? int.Parse(tp[0]) * 3600 + int.Parse(tp[1]) * 60 + double.Parse(tp[2], CultureInfo.InvariantCulture)
                                      : int.Parse(tp[0]) * 60 + double.Parse(tp[1], CultureInfo.InvariantCulture);
            }
            return (rss, cpu);
        }
        catch { return (0, 0); }
    }
    double peakRss = 0, sumRss = 0, peakFf = 0, ffCpu = 0; int samples = 0;
    while ((DateTimeOffset.UtcNow - start).TotalSeconds < seconds + 2.5)
    {
        await Task.Delay(2000);
        me.Refresh();
        double rss = me.WorkingSet64 / 1048576.0;
        var (frss, fcpu) = Child(me.Id);
        if ((DateTimeOffset.UtcNow - start).TotalSeconds > 20) { sumRss += rss; samples++; }   // ısınma dışı ortalama
        peakRss = Math.Max(peakRss, rss); peakFf = Math.Max(peakFf, frss); ffCpu = Math.Max(ffCpu, fcpu);
    }
    me.Refresh();
    double cpuS = (me.TotalProcessorTime - cpu0).TotalSeconds;
    double wall = (DateTimeOffset.UtcNow - start).TotalSeconds;
    var w = rt.Cameras[0];
    var ws = rt.Cameras.ToList();
    long gEval = ws.Sum(x => x.GateEvaluated), gWould = ws.Sum(x => x.GateWouldSkip), gSkip = ws.Sum(x => x.GateSkipped),
         gMiss = ws.Sum(x => x.GateMissedDetections), gForced = ws.Sum(x => x.GateForced);
    long gMotion = ws.Sum(x => x.GateMotionFrames), gHold = ws.Sum(x => x.GateHoldFrames), gActive = ws.Sum(x => x.GateActiveFrames),
         gForcedHits = ws.Sum(x => x.GateForcedHits);
    long fr = ws.Sum(x => x.FramesReceived), fp = ws.Sum(x => x.FramesProcessed); double p50 = w.FrameMsP50, p95 = w.FrameMsP95; string fmt = w.SourceFormat;
    await rt.StopAsync();

    var summary = new
    {
        video = Path.GetFileName(video), camera = camId, sets, seconds = Math.Round(wall, 1), events,
        framesReceived = fr, framesProcessed = fp, frameMsP50 = p50, frameMsP95 = p95, sourceFormat = fmt,
        gate = o.MotionGate.ToString(), gateEvaluated = gEval, gateWouldSkip = gWould, gateSkipped = gSkip, gateMissed = gMiss, gateForced = gForced,
        gateMotionFrames = gMotion, gateHoldFrames = gHold, gateActiveFrames = gActive, gateForcedHits = gForcedHits,
        cpuPctOneCore = Math.Round(100 * cpuS / wall, 1), ffmpegCpuPctOneCore = Math.Round(100 * ffCpu / wall, 1),
        rssAvgMb = Math.Round(samples > 0 ? sumRss / samples : 0, 0), rssPeakMb = Math.Round(peakRss, 0), ffmpegRssPeakMb = Math.Round(peakFf, 0),
        gen2 = GC.CollectionCount(2), serverGc = System.Runtime.GCSettings.IsServerGC,
    };
    Console.WriteLine(JsonSerializer.Serialize(summary));
}

// ---------------------------------------------------------------- ÇIKARIM MALİYETİ (2026-10-20)
// Dedektörün çıkarım başına İŞLEMCİ zamanı — zayıf PC'de belirleyici olan duvar saati değil budur.
//   dotnet run -c Release --project Anpr.Bench -- inferbench <kare.jpg> [--det f] [--threads N] [--spin true]
//                                                [--arena false] [--mempattern false] [--n 60] [--pace 500]
// --pace ms: çıkarımlar arasında bekleme. Canlıda kare 200–500 ms arayla gelir; işletim sistemi aradaki boşlukta
// çekirdeği yavaşlatır ya da verimlilik çekirdeğine taşır — gerçekçi maliyet ancak aralıklı çalıştırmada görünür.
void RunInferBench(string jpegPath, string[] argv, BenchOptions opt)
{
    int n = int.Parse(ArgVal(argv, "--n") ?? "60", CultureInfo.InvariantCulture);
    int pace = int.Parse(ArgVal(argv, "--pace") ?? "0", CultureInfo.InvariantCulture);
    using var decoded = SKBitmap.Decode(File.ReadAllBytes(jpegPath));
    using var rgba = decoded.Copy(SKColorType.Rgba8888);
    var ao = new AnprOptions
    {
        OnnxIntraOpThreads = opt.Threads, OnnxAllowSpinning = opt.Spin,
        OnnxCpuMemArena = opt.Arena, OnnxMemoryPattern = opt.MemPattern,
    };
    using var det = new Anpr.Core.Detection.LicensePlateDetector(opt.Det, ao.CreateSessionOptions(), 0.25f);
    int found = 0;
    for (int i = 0; i < 5; i++) found = det.Detect(rgba).Count;   // ısınma
    using var me = Process.GetCurrentProcess();
    var walls = new double[n];
    me.Refresh();
    var cpu0 = me.TotalProcessorTime;
    long t0 = Stopwatch.GetTimestamp();
    for (int i = 0; i < n; i++)
    {
        long s = Stopwatch.GetTimestamp();
        det.Detect(rgba);
        walls[i] = Stopwatch.GetElapsedTime(s).TotalMilliseconds;
        if (pace > 0) Thread.Sleep(pace);
    }
    double wallAll = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    me.Refresh();
    double cpuMs = (me.TotalProcessorTime - cpu0).TotalMilliseconds;
    Array.Sort(walls);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        model = Path.GetFileName(opt.Det), input = det.InputSize, threads = opt.Threads, spin = opt.Spin, arena = opt.Arena,
        mempattern = opt.MemPattern, rssMb = Math.Round(me.WorkingSet64 / 1048576.0, 1),
        n, paceMs = pace, detections = found,
        wallMsMin = Math.Round(walls[0], 1), wallMsP50 = Math.Round(walls[n / 2], 1),
        cpuMsPerInference = Math.Round(cpuMs / n, 1),
        processCpuPctOneCore = Math.Round(100.0 * cpuMs / wallAll, 1),
    }));
}

// ---------------------------------------------------------------- KARE TAŞIMA MALİYETİ (2026-10-20)
// ffmpeg → .NET kare taşımanın İŞLEMCİ maliyeti, dedektör olmadan. Üç yol:
//   async : boru (pipe) + Stream.ReadAsync (2.0.0 ürün yolu; macOS/Linux'ta her okuma bir kqueue/epoll turu)
//   sync  : aynı boru, ayrı iş parçacığında bloklayan Stream.Read (2.1.0 ürün yolu)
//   tcp   : 127.0.0.1 üzerinde soket, büyük alma/gönderme tamponu
//   dotnet run -c Release --project Anpr.Bench -- transportbench <kayıt.mkv> --mode async|sync|tcp [--fps 5] [--seconds 60]
// Not: donanım çözme videotoolbox'a sabitlidir (ölçüm makinesi macOS); başka işletim sisteminde değiştirin.
void RunTransportBench(string video, string[] argv)
{
    string mode = ArgVal(argv, "--mode") ?? "async";
    double fps = double.Parse(ArgVal(argv, "--fps") ?? "5", CultureInfo.InvariantCulture);
    double seconds = double.Parse(ArgVal(argv, "--seconds") ?? "60", CultureInfo.InvariantCulture);
    System.Net.Sockets.TcpListener? listener = null;
    string output = "pipe:1";
    if (mode == "tcp")
    {
        listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        output = $"tcp://127.0.0.1:{port}?send_buffer_size=4194304";
    }
    var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = mode != "tcp", RedirectStandardError = true, UseShellExecute = false };
    foreach (var a in new[] { "-nostdin", "-loglevel", "error", "-threads", "2", "-hwaccel", "videotoolbox", "-hwaccel_output_format", "videotoolbox_vld",
                              "-re", "-i", video, "-an", "-f", "yuv4mpegpipe", "-pix_fmt", "yuv420p",
                              "-vf", $"fps={fps.ToString(CultureInfo.InvariantCulture)},hwdownload,format=nv12", output })
        psi.ArgumentList.Add(a);
    using var ff = Process.Start(psi)!;
    _ = ff.StandardError.ReadToEndAsync();
    Stream src;
    System.Net.Sockets.TcpClient? client = null;
    if (mode == "tcp")
    {
        client = listener!.AcceptTcpClient();
        client.ReceiveBufferSize = 4 * 1024 * 1024;
        src = client.GetStream();
    }
    else src = ff.StandardOutput.BaseStream;

    using var me = Process.GetCurrentProcess();
    me.Refresh();
    var cpu0 = me.TotalProcessorTime;
    long t0 = Stopwatch.GetTimestamp();
    long frames = 0, reads = 0;
    var deadline = TimeSpan.FromSeconds(seconds);

    string ReadLine()
    {
        var sb = new StringBuilder();
        int b;
        while ((b = src.ReadByte()) >= 0 && b != '\n') sb.Append((char)b);
        return sb.ToString();
    }
    async Task<bool> ReadExact(byte[] buf, int count, bool sync)
    {
        int off = 0;
        while (off < count)
        {
            int got = sync ? src.Read(buf, off, count - off) : await src.ReadAsync(buf.AsMemory(off, count - off));
            if (got <= 0) return false;
            off += got; reads++;
        }
        return true;
    }
    var header = ReadLine();
    var parts = header.Split(' ');
    int w = int.Parse(parts.First(x => x.StartsWith('W'))[1..]), h = int.Parse(parts.First(x => x.StartsWith('H'))[1..]);
    var frame = new byte[w * h + 2 * ((w + 1) / 2) * ((h + 1) / 2)];
    var marker = new byte[6];
    bool syncRead = mode != "async";
    async Task Loop()
    {
        while (Stopwatch.GetElapsedTime(t0) < deadline)
        {
            if (!await ReadExact(marker, 6, syncRead)) break;
            if (!await ReadExact(frame, frame.Length, syncRead)) break;
            frames++;
        }
    }
    if (syncRead) Task.Factory.StartNew(() => Loop().GetAwaiter().GetResult(), TaskCreationOptions.LongRunning).Wait();
    else Loop().GetAwaiter().GetResult();
    double wall = Stopwatch.GetElapsedTime(t0).TotalSeconds;
    me.Refresh();
    double cpu = (me.TotalProcessorTime - cpu0).TotalMilliseconds;
    double ffCpu = 0;
    try { ff.Refresh(); ffCpu = ff.TotalProcessorTime.TotalMilliseconds; } catch { /* süreç çıkmış olabilir */ }
    try { ff.Kill(); } catch { /* zaten çıkmış */ }
    client?.Dispose(); listener?.Stop();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode, fps, frames, frameMb = Math.Round(frame.Length / 1048576.0, 2), readsPerFrame = frames > 0 ? reads / frames : 0,
        dotnetCpuMsPerFrame = Math.Round(cpu / Math.Max(1, frames), 2), dotnetCpuPctOneCore = Math.Round(100 * cpu / 1000 / wall, 2),
        ffmpegCpuPctOneCore = Math.Round(100 * ffCpu / 1000 / wall, 2), wallS = Math.Round(wall, 1),
    }));
}

// ---------------------------------------------------------------- KARE YOLU A/B (2026-09-30)
// AYNI kayıtlı akış, İKİ yoldan çözülür ve kare kare karşılaştırılır:
//   A = bugünkü yol : ffmpeg → BMP/BGRA → RGBA bitmap → motor
//   B = yeni yol    : ffmpeg → Y4M/YUV 4:2:0 → kopyasız görünüm → motor (tam kare RGB'ye çevrilmez)
// İki ffmpeg aynı dosyayı aynı fps süzgeciyle okur → kareler bire bir eşleşir. Soru: "yeni yol aynı plakaları
// aynı güvenle buluyor mu?" — doğruluk-nötrlüğün ölçüsü budur (etiket gerekmez; A'nın kendisi referanstır).
//   dotnet run -c Release --project Anpr.Bench -- replayab <kayıt.mkv> --ocr cct_xs_v2_erz.onnx --roi "<spec>" [--fps 5] [--max N]
void RunReplayAb(string video, BenchOptions opt)
{
    static Process Ff(string video, double fps, params string[] outArgs)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-nostdin", "-loglevel", "error", "-threads", "2", "-i", video, "-an" }) psi.ArgumentList.Add(a);
        foreach (var a in outArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add($"fps={fps.ToString(CultureInfo.InvariantCulture)}"); psi.ArgumentList.Add("pipe:1");
        var p = Process.Start(psi)!;
        _ = p.StandardError.ReadToEndAsync();
        return p;
    }
    static bool ReadExact(Stream s, byte[] buf, int off, int n)
    {
        while (n > 0) { int r = s.Read(buf, off, n); if (r <= 0) return false; off += r; n -= r; }
        return true;
    }

    using var pa = Ff(video, opt.Fps, "-f", "image2pipe", "-vcodec", "bmp", "-pix_fmt", "bgra");
    using var pb = Ff(video, opt.Fps, "-f", "yuv4mpegpipe", "-pix_fmt", "yuv420p");
    var sa = pa.StandardOutput.BaseStream; var sb = pb.StandardOutput.BaseStream;

    // Y4M başlığı
    var hdr = new List<byte>();
    for (int c; (c = sb.ReadByte()) >= 0 && c != '\n';) hdr.Add((byte)c);
    string hline = Encoding.ASCII.GetString(hdr.ToArray());
    int W = int.Parse(hline.Split(' ').First(t => t[0] == 'W')[1..]), H = int.Parse(hline.Split(' ').First(t => t[0] == 'H')[1..]);
    bool full = hline.Contains("XCOLORRANGE=FULL");
    int yuvBytes = Anpr.Core.Imaging.Yuv420FrameView.FrameBytes(W, H);
    Console.WriteLine($"{Path.GetFileName(video)} · {W}x{H} · fps={opt.Fps} · roi={(string.IsNullOrEmpty(opt.Roi) ? "yok" : "var")} · Y4M: {hline}");
    Console.WriteLine("  A = ffmpeg BGRA → RGBA bitmap → motor (bugünkü yol)");
    Console.WriteLine("  B = ffmpeg YUV → kopyasız görünüm → motor (yeni yol)");
    Console.WriteLine("  C = ffmpeg YUV → BİZİM dönüşümle RGBA bitmap → motor (ara kol: renk dönüşümü ile örneklemeyi ayırır)");

    using var ea = MakeEngine(opt); using var eb = MakeEngine(opt); using var ec = MakeEngine(opt);
    var bh = new byte[14]; var mk = new byte[6];
    var ab = new PairStats("A↔B (bugünkü ↔ yeni)"); var ac = new PairStats("A↔C (yalnız renk dönüşümü farkı)"); var cb = new PairStats("C↔B (yalnız örnekleme farkı)");
    long frames = 0; double msA = 0, msB = 0, msC = 0;
    var sw = new Stopwatch();
    int max = opt.MaxFrames > 0 ? opt.MaxFrames : int.MaxValue;
    StreamWriter? csv = opt.OutDir != null ? new StreamWriter(Path.Combine(opt.OutDir, Path.GetFileNameWithoutExtension(video) + ".pairs.csv")) : null;
    csv?.WriteLine("frame,pair,iou,detA,detB,ocrA,ocrB,validA,validB,lenA,lenB,lev,boxW,inZone");
    while (frames < max)
    {
        if (!ReadExact(sa, bh, 0, 14)) break;
        int size = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bh.AsSpan(2, 4));
        var bmp = new byte[size]; Buffer.BlockCopy(bh, 0, bmp, 0, 14);
        if (!ReadExact(sa, bmp, 14, size - 14)) break;
        if (!ReadExact(sb, mk, 0, 6)) break;
        var yuv = new byte[yuvBytes];
        if (!ReadExact(sb, yuv, 0, yuvBytes)) break;
        frames++;

        var fa = Anpr.Sdk.Frames.VideoFrame.FromBmp(bmp, DateTimeOffset.UtcNow, frames);
        var fb = Anpr.Sdk.Frames.VideoFrame.FromYuv420(yuv, W, H, full, DateTimeOffset.UtcNow, frames);
        sw.Restart();
        IReadOnlyList<PlateReading> ra;
        using (var img = fa.ToRgbaBitmap()) ra = ea.Recognize(img);
        msA += sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var rb = eb.Recognize(fb.TryGetView()!);
        msB += sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        IReadOnlyList<PlateReading> rc;
        using (var img = fb.ToRgbaBitmap()) rc = ec.Recognize(img);
        msC += sw.Elapsed.TotalMilliseconds;

        ab.Add(frames, ra, rb, csv, "AB"); ac.Add(frames, ra, rc, csv, "AC"); cb.Add(frames, rc, rb, csv, "CB");
    }
    csv?.Dispose();
    try { pa.Kill(); } catch { }
    try { pb.Kill(); } catch { }

    Console.WriteLine($"kare {frames} · süre: A {msA / Math.Max(1, frames):F1} · B {msB / Math.Max(1, frames):F1} · C {msC / Math.Max(1, frames):F1} ms/kare");
    ab.Print(frames); ac.Print(frames); cb.Print(frames);
}

// ---------------------------------------------------------------- BELLEK BÜTÇESİ (2026-09-30)
// Kullanıcı hedefi: "PC'de CPU'da RAM'de çok düşük yer kaplayarak". Bu komut kütüphanenin belleğini
// BİLEŞEN BİLEŞEN ölçer: çalışma zamanı → Skia → her ONNX oturumu (yükleme + ısınma) → ikinci kamera →
// kare döngüsü. Her satır o ANDAKİ yerleşik belleği (RSS) ve bir önceki satıra göre farkı basar.
//   dotnet run -c Release --project Anpr.Bench -- membench <kare.jpg> --ocr cct_xs_v2_erz.onnx
//        [--arena true|false] [--mempattern true|false] [--threads N] [--engines N] [--frames N]
// ⚠️ RSS işletim sistemi sayfalarını ölçer: serbest bırakılan bellek hemen düşmeyebilir (ayırıcı önbelleği).
//    Bu yüzden her aşamada hem "anlık" hem "GC.Collect sonrası" değer basılır; karar farklara bakılarak verilir.
void RunMemBench(string jpegPath, BenchOptions opt)
{
    static double Rss() { using var p = Process.GetCurrentProcess(); p.Refresh(); return p.WorkingSet64 / 1048576.0; }
    double last = Rss();
    void Row(string ad)
    {
        double now = Rss();
        var gi = GC.GetGCMemoryInfo();
        Console.WriteLine($"  {ad,-58} RSS {now,7:F1} MB  (Δ {now - last,+7:F1})   yönetilen {GC.GetTotalMemory(false) / 1048576.0,6:F1} MB · GC taahhüt {gi.TotalCommittedBytes / 1048576.0,6:F1} MB");
        last = now;
    }
    static void Settle() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(150); }

    Console.WriteLine($"membench · arena={opt.Arena} · mempattern={opt.MemPattern} · threads={opt.Threads} · engines={opt.Engines} · frames={opt.Frames}" +
                      $" · GC: server={System.Runtime.GCSettings.IsServerGC} concurrent={System.Runtime.GCSettings.LatencyMode}");
    Row("(0) süreç başlangıcı (.NET çalışma zamanı)");

    var jpeg = File.ReadAllBytes(jpegPath);
    SKBitmap rgba = SKBitmap.Decode(jpeg);
    if (rgba.ColorType != SKColorType.Rgba8888)
    {
        var conv = rgba.Copy(SKColorType.Rgba8888); rgba.Dispose(); rgba = conv;
    }
    Row($"(1) Skia yüklendi + {rgba.Width}x{rgba.Height} RGBA bitmap ({rgba.ByteCount / 1048576.0:F0} MB)");

    var ao = new AnprOptions
    {
        DetectorModelPath = opt.Det, OcrModelPath = opt.Ocr, OcrConfigPath = opt.OcrCfg,
        OnnxIntraOpThreads = opt.Threads, OnnxAllowSpinning = opt.Spin,
        OnnxCpuMemArena = opt.Arena, OnnxMemoryPattern = opt.MemPattern,
        MinConfidence = 0.25f, ApplyTurkishValidation = true, RoiPolygon = opt.Roi,
        EnhanceRectify = true, EnhanceSr = true, CropQualityGate = true,
    };

    // Oturumlar TEK TEK: hangisinin ne kadar tuttuğu görünsün.
    var det = new Anpr.Core.Detection.LicensePlateDetector(ao.DetectorModelPath, ao.CreateSessionOptions(), 0.25f);
    Row("(2) dedektör oturumu yüklendi");
    IReadOnlyList<Anpr.Core.Detection.PlateDetection> dets = Array.Empty<Anpr.Core.Detection.PlateDetection>();
    for (int i = 0; i < 5; i++) dets = det.Detect(rgba);
    Row($"(3) dedektör ısındı (5 çıkarım, {dets.Count} tespit)");

    var rec = new Anpr.Core.Recognition.LicensePlateRecognizer(ao.OcrModelPath, ao.OcrConfigPath, ao.CreateSessionOptions());
    Row("(4) OCR oturumu yüklendi");
    SKBitmap crop;
    if (dets.Count > 0)
    {
        var d = dets[0];
        crop = new SKBitmap(d.Width, d.Height);
        rgba.ExtractSubset(crop, new SKRectI(d.X, d.Y, d.X + d.Width, d.Y + d.Height));
    }
    else { crop = new SKBitmap(160, 50); crop.Erase(SKColors.Gray); }
    for (int i = 0; i < 5; i++) rec.Recognize(crop);
    Row("(5) OCR ısındı (5 çıkarım)");

    string mdir = Path.GetDirectoryName(Path.GetFullPath(ao.OcrModelPath))!;
    string wpodPath = Path.Combine(mdir, Path.GetFileName(ao.WpodModelPath)), srPath = Path.Combine(mdir, Path.GetFileName(ao.SrModelPath));
    Anpr.Core.Imaging.PlateRectifier2? wpod = null; Anpr.Core.Imaging.PlateSuperResolver? sr = null;
    if (File.Exists(wpodPath))
    {
        wpod = new Anpr.Core.Imaging.PlateRectifier2(wpodPath, ao.CreateSessionOptions());
        Row("(6) WPOD (rektifikasyon) oturumu yüklendi");
        for (int i = 0; i < 3; i++) { var r = wpod.Rectify(crop); r?.Image.Dispose(); }
        Row("(7) WPOD ısındı (3 çıkarım)");
    }
    if (File.Exists(srPath))
    {
        sr = new Anpr.Core.Imaging.PlateSuperResolver(srPath, ao.CreateSessionOptions());
        Row("(8) FSRCNN (süper çözünürlük) oturumu yüklendi");
        for (int i = 0; i < 3; i++) sr.Upscale(crop)?.Dispose();
        Row("(9) FSRCNN ısındı (3 çıkarım)");
    }
    det.Dispose(); rec.Dispose(); wpod?.Dispose(); sr?.Dispose(); crop.Dispose();
    Settle();
    Row("(10) dört oturum KAPATILDI + GC (geri verilen bellek)");

    // Kamera başına TAM motor (servisin kurduğu gibi) — N kamera
    var engines = new List<AnprEngine>();
    for (int e = 0; e < Math.Max(1, opt.Engines); e++)
    {
        var eng = AnprEngine.Create(ao);
        for (int i = 0; i < 4; i++) eng.Recognize(rgba);
        engines.Add(eng);
        Row($"(11.{e + 1}) motor #{e + 1} kuruldu + ısındı (4 tam-kare tanıma)");
    }

    // Kare döngüsü: servis yolu = 16 MB ham kare (byte[]) → RGBA bitmap → tanıma → bırak
    int w = rgba.Width, h = rgba.Height, stride = w * 4;
    var bmp = new byte[54 + stride * h];
    bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), 54);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), w);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -h);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26), 1);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28), 32);
    using (var bgra = rgba.Copy(SKColorType.Bgra8888))
        System.Runtime.InteropServices.Marshal.Copy(bgra.GetPixels(), bmp, 54, stride * h);
    Row("(12) 16 MB ham kare şablonu hazır");

    var sw = Stopwatch.StartNew();
    using var proc0 = Process.GetCurrentProcess();
    var cpu0 = proc0.TotalProcessorTime;
    double peak = Rss();
    for (int i = 0; i < opt.Frames; i++)
    {
        // Kaynak her kare için YENİ 16 MB dizi üretir (tüketici aldığı için geri dönüşmez) — en kötü durum.
        var buf = GC.AllocateUninitializedArray<byte>(bmp.Length);
        Buffer.BlockCopy(bmp, 0, buf, 0, bmp.Length);
        var frame = Anpr.Sdk.Frames.VideoFrame.FromBmp(buf, DateTimeOffset.UtcNow, i);
        using var img = frame.ToRgbaBitmap();
        engines[i % engines.Count].Recognize(img);
        if ((i & 7) == 7) peak = Math.Max(peak, Rss());
    }
    sw.Stop(); proc0.Refresh();
    Row($"(13) {opt.Frames} kare döngüsü bitti (tepe RSS {peak:F1} MB · {sw.Elapsed.TotalMilliseconds / Math.Max(1, opt.Frames):F1} ms/kare duvar · {(proc0.TotalProcessorTime - cpu0).TotalMilliseconds / Math.Max(1, opt.Frames):F1} ms/kare CPU)");
    Settle();
    Row("(14) GC sonrası (kalıcı taban)");
    foreach (var e in engines) e.Dispose();
    rgba.Dispose();
}

// ---------------------------------------------------------------- helpers
AnprEngine MakeEngine(BenchOptions o) => AnprEngine.Create(new AnprOptions
{
    DetectorModelPath = o.Det,
    OcrModelPath = o.Ocr,
    OcrConfigPath = o.OcrCfg,
    OnnxIntraOpThreads = o.Threads,
    OnnxAllowSpinning = o.Spin,
    OnnxCpuMemArena = o.Arena,
    OnnxMemoryPattern = o.MemPattern,
    MinConfidence = 0.25f,
    ApplyTurkishValidation = true,
    DeskewPlates = o.Deskew,
    MinPlateWidthRatio = o.MinPlateW,
    MinPlateAspectRatio = o.MinAspect,
    CropPaddingRatio = o.Pad,
    RoiPolygon = o.Roi,
    EnhanceRectify = o.EnhanceRectify,
    CropQualityGate = o.QualityBlurMin > 0 || o.QualityDarkMax < 1 || o.QualityBrightMax < 1,
    QualityBlurMin = o.QualityBlurMin,
    QualityDarkMaxPct = o.QualityDarkMax,
    QualityBrightMaxPct = o.QualityBrightMax,
});


// FAZ 0 (2026-08-26): kare başına TÜM tespitleri predictions.jsonl'a döker.
// bench_report.py bunu okur; mAP/precision/recall İNSAN etiketiyle (plates.jsonl) kıyaslanır.
// Not: IsValidTr FİLTRELENMEZ — geçersiz-format okumalar da bir "tespit"tir ve precision'a
// dahil edilmelidir; yalnız kutusu olmayan okumalar atlanır.
void RunDetect(string framesDir, string outPath, BenchOptions o)
{
    var files = Directory.GetFiles(framesDir, "*.jpg").OrderBy(f => f).ToList();
    if (files.Count == 0) { Console.WriteLine($"Kare yok: {framesDir}"); return; }
    Console.WriteLine($"{files.Count} kare, model çalışıyor...");
    using var engine = MakeEngine(o);
    using var w = new StreamWriter(outPath);
    int done = 0;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    foreach (var f in files)
    {
        var readings = o.Via == "jpeg" ? SafeRecognize(engine, f) : RecognizeVia(engine, f, o.Via);
        var dets = readings
            .Where(r => r.Box.Width > 0 && r.Box.Height > 0)
            .Select(r => new
            {
                x = r.Box.X, y = r.Box.Y, w = r.Box.Width, h = r.Box.Height,
                score = Math.Round(r.Confidence, 4),
                ocr = Math.Round(r.OcrConfidence, 4),
                plate = r.IsValidTr ? r.Text.Replace(" ", "") : null,
                raw = r.RawText,
            }).ToList();
        w.WriteLine(JsonSerializer.Serialize(new { frame = Path.GetFileName(f), dets }));
        if (++done % 100 == 0) Console.WriteLine($"  {done}/{files.Count}  ({sw.Elapsed.TotalSeconds:F0}s)");
    }
    Console.WriteLine($"✅ {done} kare -> {outPath}  ({sw.Elapsed.TotalSeconds:F0}s, {sw.Elapsed.TotalMilliseconds/done:F0} ms/kare)");
}

// Etiketli karelerde KARE YOLU kıyası: JPEG → RGBA → (ileri dönüşümle) YUV 4:2:0, sonra
//   "yuvview" : YUV → kopyasız görünüm (dedektör girdisi doğrudan örneklenir) → motor   [yeni yol]
//   "yuvrgba" : YUV → bizim dönüşümle tam RGBA bitmap → motor (Skia örneklemesi)        [ara kol]
// İkisi AYNI YUV pikselinden başlar → fark yalnız örnekleme yöntemidir. (JPEG→YUV gidiş-dönüş kaybı
// ikisinde de vardır; üretimde kaynak zaten YUV olduğu için o kayıp üretimde YOKTUR.)
static IReadOnlyList<PlateReading> RecognizeVia(AnprEngine e, string path, string via)
{
    try
    {
        using var rgba0 = SKBitmap.Decode(path);
        using var rgba = rgba0.ColorType == SKColorType.Rgba8888 ? rgba0.Copy() : rgba0.Copy(SKColorType.Rgba8888);
        int w = rgba.Width & ~1, h = rgba.Height & ~1;          // çift boyut (4:2:0)
        var yuv = new byte[w * h * 3 / 2];
        var px = rgba.GetPixelSpan(); int rb = rgba.RowBytes; int cw = w / 2;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = y * rb + x * 4;
                yuv[y * w + x] = (byte)Math.Clamp((int)Math.Round(16 + 0.256788 * px[o] + 0.504129 * px[o + 1] + 0.097906 * px[o + 2]), 0, 255);
            }
        for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                // 2×2 bloğun ortalama rengi → kroma
                double r = 0, g = 0, b = 0;
                for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { int o = (y + dy) * rb + (x + dx) * 4; r += px[o]; g += px[o + 1]; b += px[o + 2]; }
                r /= 4; g /= 4; b /= 4;
                int ci = (y / 2) * cw + x / 2;
                yuv[w * h + ci] = (byte)Math.Clamp((int)Math.Round(128 - 0.148223 * r - 0.290993 * g + 0.439216 * b), 0, 255);
                yuv[w * h + cw * (h / 2) + ci] = (byte)Math.Clamp((int)Math.Round(128 + 0.439216 * r - 0.367788 * g - 0.071427 * b), 0, 255);
            }
        var frame = Anpr.Sdk.Frames.VideoFrame.FromYuv420(yuv, w, h, false, DateTimeOffset.UtcNow);
        if (via == "yuvview") return e.Recognize(frame.TryGetView()!);
        using var back = frame.ToRgbaBitmap();
        return e.Recognize(back);
    }
    catch { return Array.Empty<PlateReading>(); }
}
static IReadOnlyList<PlateReading> SafeRecognize(AnprEngine e, string path)
{
    try { return e.Recognize(File.ReadAllBytes(path)); }
    catch { return Array.Empty<PlateReading>(); }
}

static IReadOnlyList<PlateReading> SafeRecognizeBytes(AnprEngine e, byte[] bytes)
{
    try { return e.Recognize(bytes); }
    catch { return Array.Empty<PlateReading>(); }
}

// Sentetik bozma: çözünürlük düşür (küçült+büyüt) + düşük-Q JPEG. Zor/uzak/sıkıştırılmış kareyi taklit eder.
static byte[] Degrade(byte[] bytes, int level)
{
    using var bmp = SKBitmap.Decode(bytes);
    if (bmp == null) return bytes;
    float scale = level switch { 1 => 0.6f, 2 => 0.45f, _ => 0.33f };
    int q = level switch { 1 => 40, 2 => 25, _ => 15 };
    int dw = Math.Max(1, (int)(bmp.Width * scale)), dh = Math.Max(1, (int)(bmp.Height * scale));
    using var small = bmp.Resize(new SKImageInfo(dw, dh), SKFilterQuality.Medium) ?? bmp;
    using var back = small.Resize(new SKImageInfo(bmp.Width, bmp.Height), SKFilterQuality.Medium) ?? small;
    using var img = SKImage.FromBitmap(back);
    using var data = img.Encode(SKEncodedImageFormat.Jpeg, q);
    return data.ToArray();
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

// KnownPlates snap (C#-birebir: min lev≤1'deki İLK aday) — pipeline-seviyesi snap ölçümü için.
static string Snap(string c, HashSet<string> set, IReadOnlyList<string> list)
{
    if (set.Contains(c)) return c;
    string best = c; int bl = 2;
    foreach (var w in list) { int d = BLev(c, w, 1); if (d < bl) { bl = d; best = w; } }
    return best;
}
static int BLev(string a, string b, int max)
{
    if (Math.Abs(a.Length - b.Length) > max) return max + 1;
    var dp = new int[b.Length + 1];
    for (int j = 0; j <= b.Length; j++) dp[j] = j;
    for (int i = 1; i <= a.Length; i++)
    {
        int prev = dp[0]; dp[0] = i; int rmin = dp[0];
        for (int j = 1; j <= b.Length; j++)
        {
            int cur = dp[j];
            dp[j] = Math.Min(Math.Min(dp[j] + 1, dp[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur; if (dp[j] < rmin) rmin = dp[j];
        }
        if (rmin > max) return max + 1;
    }
    return dp[b.Length];
}

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
        DedupSec = double.Parse(ArgVal(a, "--dedup") ?? "8", CultureInfo.InvariantCulture),
        EarlyEmit = float.Parse(ArgVal(a, "--earlyemit") ?? "0", CultureInfo.InvariantCulture),
        SpatialIou = float.Parse(ArgVal(a, "--spatialiou") ?? "0", CultureInfo.InvariantCulture),
        SkipChar = float.Parse(ArgVal(a, "--skipchar") ?? "0", CultureInfo.InvariantCulture),
        QualityBlurMin = float.Parse(ArgVal(a, "--qblur") ?? "0", CultureInfo.InvariantCulture),
        QualityDarkMax = float.Parse(ArgVal(a, "--qdark") ?? "1", CultureInfo.InvariantCulture),
        QualityBrightMax = float.Parse(ArgVal(a, "--qbright") ?? "1", CultureInfo.InvariantCulture),
        Trace = (ArgVal(a, "--trace") ?? "").ToUpperInvariant(),
        CharVote = (ArgVal(a, "--charvote") ?? "true") != "false",
        Deskew = (ArgVal(a, "--deskew") ?? "false") == "true",
        Spin = (ArgVal(a, "--spin") ?? "false") == "true",
        Arena = (ArgVal(a, "--arena") ?? "true") != "false",
        MemPattern = (ArgVal(a, "--mempattern") ?? "true") != "false",
        Threads = int.Parse(ArgVal(a, "--threads") ?? "2"),
        Engines = int.Parse(ArgVal(a, "--engines") ?? "2"),
        Frames = int.Parse(ArgVal(a, "--frames") ?? "60"),
        MaxFrames = int.Parse(ArgVal(a, "--max") ?? "0"),
        Via = ArgVal(a, "--via") ?? "jpeg",
        MinPlateW = float.Parse(ArgVal(a, "--minwidth") ?? "0", CultureInfo.InvariantCulture),
        MinAspect = float.Parse(ArgVal(a, "--aspect") ?? "1.0", CultureInfo.InvariantCulture),
        Pad = float.Parse(ArgVal(a, "--pad") ?? "0.06", CultureInfo.InvariantCulture),
        Roi = ArgVal(a, "--roi") ?? "",
        Constrained = (ArgVal(a, "--constrained") ?? "true") != "false",
        MergeProvince = (ArgVal(a, "--mergeprovince") ?? "true") != "false",
        Fuzzy = int.Parse(ArgVal(a, "--fuzzy") ?? "0"),
        EmitFloor = float.Parse(ArgVal(a, "--emitfloor") ?? "0", CultureInfo.InvariantCulture),
        Degrade = int.Parse(ArgVal(a, "--degrade") ?? "0"),
        FusionMax = int.Parse(ArgVal(a, "--fusionmax") ?? "5"),
        OutDir = ArgVal(a, "--out"),
        SnapFile = ArgVal(a, "--snap"),
        SnapGate = float.Parse(ArgVal(a, "--snapgate") ?? "1.01", CultureInfo.InvariantCulture),
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

// ─────────────────────────────────────────────────────────────────────────────
// FAZ 2 MİKRO-ÖLÇÜM: ham kare yolunun kare başına maliyeti nereye gidiyor?
// Bir JPEG kareyi alır, ondan ffmpeg'in üreteceği biçimde bir BMP/BGRA kare kurar ve şunları ayrı ayrı
// zamanlar: (1) eski yol = JPEG çözme, (2) yeni yol = BGRA→RGBA dönüşümü, (3) modelin kendisi (Recognize),
// (4) 16 MB kare tahsisinin GC bedeli. Çıktı ms/kare + GC sayaçları. Kullanım: framebench <kare.jpg> [n]
void RunFrameBench(string jpegPath, int n, BenchOptions opt)
{
    var jpeg = File.ReadAllBytes(jpegPath);
    using var rgba = SkiaSharp.SKBitmap.Decode(jpeg);
    Console.WriteLine($"kare {rgba.Width}x{rgba.Height} · {n} yineleme · ONNX spinning={opt.Spin}");

    // ffmpeg'in BMP'si: 54 bayt başlık + yukarıdan-aşağı BGRA
    int w = rgba.Width, h = rgba.Height, stride = w * 4;
    var bmp = new byte[54 + stride * h];
    bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), 54);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), w);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -h);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26), 1);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28), 32);
    using (var bgra = rgba.Copy(SkiaSharp.SKColorType.Bgra8888))
        System.Runtime.InteropServices.Marshal.Copy(bgra.GetPixels(), bmp, 54, stride * h);
    var frame = Anpr.Sdk.Frames.VideoFrame.FromBmp(bmp, DateTimeOffset.UtcNow);

    // ort = ortalama duvar süresi · min = en düşük (başka süreçlerin gürültüsüne dayanıklı) · cpu = işlemci zamanı/kare
    // (spinning gibi "bekleyerek yakan" maliyetleri duvar süresi göstermez, işlemci zamanı gösterir)
    static (double ms, double minMs, double cpuMs, int gen0, int gen2, double pauseMs) Time(int n, Action a)
    {
        a(); // ısınma
        int g0 = GC.CollectionCount(0), g2 = GC.CollectionCount(2);
        var p0 = GC.GetTotalPauseDuration();
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        var c0 = proc.TotalProcessorTime;
        double min = double.MaxValue;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            long t0 = sw.ElapsedTicks; a();
            min = Math.Min(min, (sw.ElapsedTicks - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }
        sw.Stop();
        proc.Refresh();
        double cpu = (proc.TotalProcessorTime - c0).TotalMilliseconds / n;
        return (sw.Elapsed.TotalMilliseconds / n, min, cpu, GC.CollectionCount(0) - g0, GC.CollectionCount(2) - g2,
                (GC.GetTotalPauseDuration() - p0).TotalMilliseconds);
    }
    void Row(string ad, (double ms, double minMs, double cpuMs, int gen0, int gen2, double pauseMs) r)
        => Console.WriteLine($"  {ad,-46} ort {r.ms,7:F1}  min {r.minMs,7:F1}  cpu {r.cpuMs,7:F1} ms/kare   gen2 {r.gen2,2}  GC durak {r.pauseMs,6:F1} ms");

    Row("(1) ESKİ: JPEG çöz → RGBA (SKBitmap.Decode)", Time(n, () => { using var b = SkiaSharp.SKBitmap.Decode(jpeg); }));
    Row("(2) YENİ: BGRA→RGBA dönüşüm SIMD (ToRgbaBitmap)", Time(n, () => { using var b = frame.ToRgbaBitmap(); }));
    Row("(2b) ilk sürüm: Skia SKPixmap.ReadPixels dönüşümü", Time(n, () =>
    {
        var dst = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul));
        var hnd = System.Runtime.InteropServices.GCHandle.Alloc(bmp, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var src = new SkiaSharp.SKPixmap(new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul), hnd.AddrOfPinnedObject() + 54, stride);
            src.ReadPixels(dst.Info, dst.GetPixels(), dst.RowBytes);
        }
        finally { hnd.Free(); dst.Dispose(); }
    }));
    Row("(3) 16 MB kare tahsisi (LOH) + kopya", Time(n, () => { var x = GC.AllocateUninitializedArray<byte>(bmp.Length); Buffer.BlockCopy(bmp, 0, x, 0, bmp.Length); GC.KeepAlive(x); }));

    var engine = MakeEngine(opt);
    using var img = frame.ToRgbaBitmap();
    Row("(4) MODEL: Recognize(RGBA bitmap), tam kare", Time(Math.Max(3, n / 4), () => engine.Recognize(img)));
    Row("(5) YENİ uçtan uca: dönüşüm + Recognize", Time(Math.Max(3, n / 4), () => { using var b = frame.ToRgbaBitmap(); engine.Recognize(b); }));
    Row("(6) ESKİ uçtan uca: JPEG çöz + Recognize", Time(Math.Max(3, n / 4), () => { using var b = SkiaSharp.SKBitmap.Decode(jpeg); engine.Recognize(b); }));
    Console.WriteLine($"  Vector128 donanım hızlandırma: {System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated}");
}

/// <summary>İki okuma kümesini kare kare eşleştirip (IoU ≥ 0,5) farkları biriktirir. replayab için.</summary>
sealed class PairStats
{
    private readonly string _name;
    public PairStats(string name) => _name = name;
    long _bothEmpty, _identical, _n1, _n2, _matched, _only1, _only2, _only1Valid, _only2Valid, _textSame, _textDiff, _v1, _v2;
    long _hi1Diff, _hiBoth;          // en az bir taraf ocr ≥ 0,85 iken metin farkı · iki taraf da ≥ 0,85 iken metin farkı
    long _zoneFlip;                  // bölge-içi/dışı sınıflaması değişen eşleşme
    double _sumOcrAbs, _maxOcrAbs, _sumOcrSigned, _sumDetAbs, _sumDetSigned, _sumIou;
    readonly List<string> _diffs = new();

    static float Iou(PlateBox a, PlateBox b)
    {
        int x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y), x1 = Math.Min(a.X + a.Width, b.X + b.Width), y1 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        float inter = Math.Max(0, x1 - x0) * (float)Math.Max(0, y1 - y0);
        float uni = a.Width * (float)a.Height + b.Width * (float)b.Height - inter;
        return uni <= 0 ? 0 : inter / uni;
    }
    static int Lev(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    public void Add(long frame, IReadOnlyList<PlateReading> r1, IReadOnlyList<PlateReading> r2, StreamWriter? csv, string tag)
    {
        _n1 += r1.Count; _n2 += r2.Count;
        _v1 += r1.Count(r => r.IsValidTr); _v2 += r2.Count(r => r.IsValidTr);
        if (r1.Count == 0 && r2.Count == 0) { _bothEmpty++; _identical++; return; }
        var used = new bool[r2.Count];
        bool same = r1.Count == r2.Count;
        foreach (var a in r1)
        {
            int best = -1; float bi = 0.5f;
            for (int j = 0; j < r2.Count; j++) { if (used[j]) continue; float i = Iou(a.Box, r2[j].Box); if (i >= bi) { bi = i; best = j; } }
            if (best < 0)
            {
                _only1++; same = false; if (a.IsValidTr) _only1Valid++;
                if (_diffs.Count < 12) _diffs.Add($"    kare {frame}: YALNIZ 1.  {(a.IsValidTr ? "PLAKA" : "geçersiz")} det={a.DetectorConfidence:F2} ocr={a.OcrConfidence:F2} kutu={a.Box.Width}x{a.Box.Height} bölge={a.InZone}");
                csv?.WriteLine(FormattableString.Invariant($"{frame},{tag},0,{a.DetectorConfidence:F4},,{a.OcrConfidence:F4},,{(a.IsValidTr ? 1 : 0)},,{a.RawText.Length},,,{a.Box.Width},{(a.InZone ? 1 : 0)}"));
                continue;
            }
            used[best] = true; _matched++; _sumIou += bi;
            var b = r2[best];
            double dOcr = b.OcrConfidence - a.OcrConfidence, dDet = b.DetectorConfidence - a.DetectorConfidence;
            _sumOcrAbs += Math.Abs(dOcr); _maxOcrAbs = Math.Max(_maxOcrAbs, Math.Abs(dOcr)); _sumOcrSigned += dOcr;
            _sumDetAbs += Math.Abs(dDet); _sumDetSigned += dDet;
            if (a.InZone != b.InZone) _zoneFlip++;
            int lev = a.RawText == b.RawText ? 0 : Lev(a.RawText, b.RawText);
            csv?.WriteLine(FormattableString.Invariant($"{frame},{tag},{bi:F3},{a.DetectorConfidence:F4},{b.DetectorConfidence:F4},{a.OcrConfidence:F4},{b.OcrConfidence:F4},{(a.IsValidTr ? 1 : 0)},{(b.IsValidTr ? 1 : 0)},{a.RawText.Length},{b.RawText.Length},{lev},{a.Box.Width},{(a.InZone ? 1 : 0)}"));
            if (lev == 0) _textSame++;
            else
            {
                _textDiff++; same = false;
                if (a.OcrConfidence >= 0.85f || b.OcrConfidence >= 0.85f) _hi1Diff++;
                if (a.OcrConfidence >= 0.85f && b.OcrConfidence >= 0.85f) _hiBoth++;
                if (_diffs.Count < 12) _diffs.Add($"    kare {frame}: METİN FARKI  1.(ocr {a.OcrConfidence:F3}, {a.RawText.Length} krk) ≠ 2.(ocr {b.OcrConfidence:F3}, {b.RawText.Length} krk) · lev={lev} kutu={a.Box.Width}px IoU={bi:F2}");
            }
        }
        for (int j = 0; j < r2.Count; j++)
            if (!used[j])
            {
                _only2++; same = false; if (r2[j].IsValidTr) _only2Valid++;
                if (_diffs.Count < 12) _diffs.Add($"    kare {frame}: YALNIZ 2.  {(r2[j].IsValidTr ? "PLAKA" : "geçersiz")} det={r2[j].DetectorConfidence:F2} ocr={r2[j].OcrConfidence:F2} kutu={r2[j].Box.Width}x{r2[j].Box.Height} bölge={r2[j].InZone}");
                csv?.WriteLine(FormattableString.Invariant($"{frame},{tag},0,,{r2[j].DetectorConfidence:F4},,{r2[j].OcrConfidence:F4},,{(r2[j].IsValidTr ? 1 : 0)},,{r2[j].RawText.Length},,{r2[j].Box.Width},{(r2[j].InZone ? 1 : 0)}"));
            }
        if (same) _identical++;
    }

    public void Print(long frames)
    {
        long m = Math.Max(1, _matched);
        Console.WriteLine($"── {_name}");
        Console.WriteLine($"   birebir aynı kare {_identical}/{frames} (%{100.0 * _identical / Math.Max(1, frames):F2}) · okuma {_n1} ↔ {_n2} (geçerli plaka {_v1} ↔ {_v2}) · eşleşen {_matched} · yalnız 1. {_only1} (geçerli {_only1Valid}) · yalnız 2. {_only2} (geçerli {_only2Valid})");
        Console.WriteLine($"   eşleşenlerde metin farkı {_textDiff}/{_matched} (%{100.0 * _textDiff / m:F1}) — bir taraf ocr≥0,85: {_hi1Diff} · iki taraf da ≥0,85: {_hiBoth} · bölge sınıfı değişen {_zoneFlip}");
        Console.WriteLine($"   ort. IoU {_sumIou / m:F3} · Δocr (2.−1.) ort {_sumOcrSigned / m:+0.0000;-0.0000} |ort| {_sumOcrAbs / m:F4} azami {_maxOcrAbs:F3} · Δdet (2.−1.) ort {_sumDetSigned / m:+0.0000;-0.0000} |ort| {_sumDetAbs / m:F4}");
        foreach (var d in _diffs) Console.WriteLine(d);
    }
}

sealed class BenchOptions
{
    public string Det = "", Ocr = "", OcrCfg = "";
    public double Fps = 3, SettleSec = 3, DedupSec = 8;
    public float EarlyEmit = 0;
    public float SpatialIou = 0;   // uzamsal iz birleştirme (0=kapalı, baseline)
    public float SkipChar = 0;     // SKIP_CHAR seviyesi (0=kapalı, baseline)
    public bool EnhanceRectify = false; // WPOD rektifikasyonu (rectify teşhis modu / A/B)
    public float QualityBlurMin = 0;  // kalite kapısı (0=kapalı)
    public float QualityDarkMax = 1;
    public float QualityBrightMax = 1;
    public string Trace = "";
    public string Roi = "";
    public float MinConf, MinOcr;
    public int MinConfirm = 2;
    public bool CharVote = true;
    public bool Deskew = false;
    public bool Spin = false;        // ONNX intra-op spinning (Faz 2 A/B; SDK varsayılanı false)
    public bool Arena = true;        // ONNX CPU bellek arenası (membench A/B)
    public bool MemPattern = true;   // ONNX bellek deseni
    public int Threads = 2;          // ONNX intra-op iş parçacığı
    public int Engines = 2;          // membench: kaç kamera motoru kurulsun
    public int Frames = 60;          // membench: kare döngüsü uzunluğu
    public int MaxFrames = 0;        // replayab: en çok kaç kare (0 = hepsi)
    public string Via = "jpeg";      // detect: kare yolu (jpeg | yuvview | yuvrgba)
    public float MinPlateW = 0;
    public float MinAspect = 1.0f;   // geometri kapısı (0 = kapalı) — A/B için --aspect
    public float Pad = 0.06f;        // CropPaddingRatio — A/B için --pad
    public bool Constrained = true;
    public bool MergeProvince = true;
    public int Fuzzy = 0;
    public float EmitFloor = 0;
    public int Degrade = 0;
    public int FusionMax = 5;
    public string? SnapFile;        // KnownPlates whitelist (JSON compact dizisi) — pipeline snap ölçümü
    public float SnapGate = 1.01f;  // SnapMaxOcrConfidence: bu güvenin ALTINDA snap uygulanır
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
