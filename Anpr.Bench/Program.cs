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
            ConstrainedDecoding = o.Constrained,
            MergeAcrossProvince = o.MergeProvince,
            FuzzyMergeDistance = o.Fuzzy,
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
    });
    var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var emitted = new List<string>();
    var details = new List<string>(); // görsel doğrulama için: hangi saniyede, hangi kutuda
    var lastSeen = new Dictionary<string, double>(); // plaka -> son GÖRÜLME saniyesi (emit anı değil!)
    void Drain(DateTimeOffset now)
    {
        foreach (var b in confirmer.DrainReady(now))
            if (b.OcrConfidence >= o.EmitFloor)
            {
                emitted.Add(Norm(b.Text));
                double seenSec = lastSeen.TryGetValue(Norm(b.Text), out var s) ? s : (now - epoch).TotalSeconds;
                details.Add($"EMIT sec={seenSec:F1} plate={Norm(b.Text)} ocr={b.OcrConfidence:F2} box={b.Box.X},{b.Box.Y},{b.Box.Width},{b.Box.Height}");
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
    RoiPolygon = o.Roi,
});

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
        Trace = (ArgVal(a, "--trace") ?? "").ToUpperInvariant(),
        CharVote = (ArgVal(a, "--charvote") ?? "true") != "false",
        Deskew = (ArgVal(a, "--deskew") ?? "false") == "true",
        MinPlateW = float.Parse(ArgVal(a, "--minwidth") ?? "0", CultureInfo.InvariantCulture),
        Roi = ArgVal(a, "--roi") ?? "",
        Constrained = (ArgVal(a, "--constrained") ?? "true") != "false",
        MergeProvince = (ArgVal(a, "--mergeprovince") ?? "true") != "false",
        Fuzzy = int.Parse(ArgVal(a, "--fuzzy") ?? "0"),
        EmitFloor = float.Parse(ArgVal(a, "--emitfloor") ?? "0", CultureInfo.InvariantCulture),
        Degrade = int.Parse(ArgVal(a, "--degrade") ?? "0"),
        FusionMax = int.Parse(ArgVal(a, "--fusionmax") ?? "5"),
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
    public double Fps = 3, SettleSec = 3, DedupSec = 8;
    public float EarlyEmit = 0;
    public string Trace = "";
    public string Roi = "";
    public float MinConf, MinOcr;
    public int MinConfirm = 2;
    public bool CharVote = true;
    public bool Deskew = false;
    public float MinPlateW = 0;
    public bool Constrained = true;
    public bool MergeProvince = true;
    public int Fuzzy = 0;
    public float EmitFloor = 0;
    public int Degrade = 0;
    public int FusionMax = 5;
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
