using System.Net;
using Microsoft.Extensions.Options;
using Anpr.Service;
using Anpr.Sdk.Configuration;
using Anpr.Service.Panel;
using Anpr.Service.Events;
using Anpr.Sdk.Events;
using Anpr.Sdk.Workers;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// H6: Windows servis barındırma (SCM auto-start + çökme-restart). Non-Windows'ta no-op (Mac'te console).
builder.Host.UseWindowsService();

// H5: console'a EK olarak rolling DOSYA log — Windows serviste console hiçbir yere gitmez, saha teşhisi
// için gün-bazlı dosya (logs/anpr-YYYYMMDD.log, 14 gün saklama). shared:true çoklu-süreç-güvenli.
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    // ÖLÇÜM (2026-07-28): saha teşhisi için eklenen dosya logunun %98'i HTTP boru-hattı gürültüsüydü
    // (66.345 satırın 65.330'u; ANPR'a ait yalnız 1.015 satır, 8 MB dosyanın ~7,9 MB'ı çöp). Sebep:
    // appsettings'teki "Logging:LogLevel:Microsoft.AspNetCore=Warning" ayarı Serilog'a UYGULANMIYOR —
    // Serilog kendi "Serilog:MinimumLevel" bölümünü okur. Panel saniyede birkaç istek attığı için
    // gerçek arıza satırları bu gürültünün içinde kayboluyordu; override'lar kodda sabitlendi.
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Extensions.Http", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "anpr-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true));

// Kapanış bütçesi: .NET varsayılanı 5 sn, bizim kapanışımız (ffmpeg kill + döngü + yayın boşaltma)
// bundan uzun sürebilir. Kısa bütçe süreci SERT öldürür ve ffmpeg öksüz kalırdı. Windows SCM toleransı
// 30 sn olduğu için onun ALTINDA kalıyoruz.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));

// --- Yapılandırma ---
builder.Services.Configure<AnprServiceOptions>(
    builder.Configuration.GetSection(AnprServiceOptions.SectionName));
var opts = builder.Configuration.GetSection(AnprServiceOptions.SectionName).Get<AnprServiceOptions>()
           ?? new AnprServiceOptions();

// --- Kestrel: config'teki adres/port ---
builder.WebHost.ConfigureKestrel(k =>
{
    IPAddress ip;
    switch (opts.HttpBindAddress)
    {
        case "0.0.0.0":
            ip = IPAddress.Any;   // TÜM arayüzler (LAN'a açık) — yalnız açıkça istenirse
            break;
        case "localhost" or "":
            ip = IPAddress.Loopback;   // güvenli varsayılan: yalnız bu makine
            break;
        default:
            if (IPAddress.TryParse(opts.HttpBindAddress, out var parsed))
            {
                ip = parsed;
            }
            else
            {
                // Çözülemeyen adres: SESSİZCE tüm arayüzlere açma (fail-open). Güvenli tarafa
                // (loopback) düş ve uyar — kullanıcı maruziyeti kısıtlamak isterken yanlışlıkla
                // genişletmesin.
                ip = IPAddress.Loopback;
                Console.Error.WriteLine(
                    $"[UYARI] HttpBindAddress '{opts.HttpBindAddress}' geçerli bir IP değil; loopback'e (127.0.0.1) bağlanılıyor.");
            }
            break;
    }
    k.Listen(ip, opts.HttpPort);

    // H4: LAN'a açık bind + boş PanelToken = canlı görüntü/plaka/ROI kimlik-doğrulamasız tüm ağa açık.
    // Varsayılan artık loopback; operatör bilerek 0.0.0.0 yaptıysa ve token yoksa GÜRÜLTÜLÜ uyar (KVKK).
    if (SecurityChecks.IsInsecureExposure(ip, opts.PanelToken))
        Console.Error.WriteLine(
            $"[GÜVENLİK UYARISI] Panel {ip}:{opts.HttpPort} adresinde TÜM AĞA açık ve PanelToken BOŞ — " +
            "canlı kamera görüntüsü, plaka geçmişi ve ROI kimlik doğrulaması olmadan erişilebilir. " +
            "PanelToken ayarlayın YA DA HttpBindAddress'i 'localhost' yapın (KVKK/güvenlik).");
});

// --- Müdavim plaka kaydı (varyant bastırma kural-D) ---
builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<AnprServiceOptions>>().Value;
    var reg = new FrequentPlateRegistry
    {
        KnownPlates = FrequentPlateRegistry.NormalizeKnownPlates(o.KnownPlates),
    };
    int n = reg.LoadFromJsonl(Path.Combine(AppContext.BaseDirectory, "events.jsonl"));
    sp.GetRequiredService<ILogger<FrequentPlateRegistry>>()
      .LogInformation("müdavim kaydı: {N} olaydan {M} plaka yüklendi | whitelist {W}", n, reg.Count, reg.KnownPlates.Count);
    return reg;
});

// --- Olay hedefleri (sinks) ---
builder.Services.AddSingleton<InMemoryEventStore>();
builder.Services.AddSingleton<IEventSink>(sp => sp.GetRequiredService<InMemoryEventStore>());
builder.Services.AddSingleton<IEventSink, ConsoleEventSink>();
if (!string.IsNullOrWhiteSpace(opts.WebhookUrl))
{
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IEventSink>(sp => new WebhookEventSink(
        sp.GetRequiredService<IHttpClientFactory>(),
        opts.WebhookUrl!,
        sp.GetRequiredService<ILogger<WebhookEventSink>>()));
    builder.Services.AddHostedService<HeartbeatService>(); // kalp-atışı itmesi (E5)
}

// --- Canlı karşılaştırma paneli ---
builder.Services.AddSingleton<PanelHub>();
builder.Services.AddSingleton<RoiStore>();
builder.Services.AddSingleton<IEventSink, PanelEventSink>();
builder.Services.AddSingleton<AnalyticsService>();
builder.Services.AddSingleton<GroundTruthStore>();
builder.Services.AddSingleton<CameraRecordPoller>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CameraRecordPoller>());

// --- Kamera yöneticisi (hem singleton hem hosted service) ---
// ROI çözücü: panelden çizilip diske kaydedilen ROI, yapılandırmadakini ezer (Sdk bunu bilmez).
builder.Services.AddSingleton<RoiResolver>(sp =>
{
    var store = sp.GetRequiredService<RoiStore>();
    return (cameraId, configured) => store.LoadOrDefault(cameraId, configured);
});
builder.Services.AddSingleton<AnprCameraSupervisor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AnprCameraSupervisor>());

var app = builder.Build();

// Erişim koruması (G3): PanelToken doluysa panel/API uçları belirteç ister (?token= veya X-Anpr-Token).
// /health açık kalır (izleme sistemleri için). Boş token = koruma kapalı (geliştirme).
if (!string.IsNullOrWhiteSpace(opts.PanelToken))
{
    app.Use(async (ctx, next) =>
    {
        // KARAR PanelAuth'ta ve TESTLİDİR — burada yalnız uygulanır. Gerekçe: bu projenin en ciddi
        // güvenlik kusuru (2026-07-28 CRITICAL, büyük/küçük harf duyarlı yol karşılaştırması) tam
        // olarak bu kararın içindeydi ve hiçbir testle korunmuyordu. Yeni bir plaka/kanıt ucu
        // eklerken PanelAuth.Protected listesine de ekleyin.
        if (!PanelAuth.IsAuthorized(ctx.Request.Path.Value, opts.PanelToken,
                                    ctx.Request.Query["token"].FirstOrDefault(),
                                    ctx.Request.Headers["X-Anpr-Token"].FirstOrDefault()))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsync("token gerekli");
            return;
        }
        await next();
    });
}

// --- Endpoint'ler ---
app.MapGet("/", () => Results.Json(new
{
    service = "OtoparkAnpr",
    endpoints = new[] { "/health", "/events/recent?count=50" },
}));

app.MapGet("/health", (AnprCameraSupervisor sup) =>
{
    var cameras = sup.Workers.Select(w => new
    {
        id = w.CameraId,
        name = w.CameraName,
        connected = w.IsConnected,
        lastFrameUtc = w.LastFrameUtc,
        framesReceived = w.FramesReceived,
        framesProcessed = w.FramesProcessed,
        // Kaynaktan AYNI kare tekrar geldiği için atlanan yinelemeler. Yüksekse tüketim
        // hızı kaynak hızını gereksiz aşıyor demektir (CPU israfı; eskiden bu kopyalar
        // ÇOK-KARELİ UZLAŞIYA sahte gözlem olarak giriyordu).
        duplicateFramesSkipped = w.DuplicateFramesSkipped,
        // H1: metni FARKLI bir okuma, yayınlanmış ve taze bir ize katıldı ("kuyruktaki ikinci araç
        // bastırıldı" riskinin tek gözlenebilir imzası). Yalnız SAYAÇ çıkar — eşlik eden plaka
        // çiftleri (w.RecentAbsorbed) KVKK gereği token-muaf /health'e KOYULMAZ.
        absorbedAfterEmit = w.AbsorbedAfterEmit,
        // #70: EarlyEmit tutmalarının hacmi ve KAZANCI (gecikme-doğruluk takası). Plaka metni yok.
        earlyEmitHolds = w.EarlyEmitHolds,
        earlyEmitHoldsResolved = w.EarlyEmitHoldsResolved,
        earlyEmitAvgGain = Math.Round(w.EarlyEmitAvgGain, 4),
        qualityDropped = w.QualityDropped,
        aspectDropped = w.AspectDropped,
        trRejected = w.TrRejected,
        frameMsP50 = w.FrameMsP50,
        frameMsP95 = w.FrameMsP95,
        restartCount = w.RestartCount,
        // Faz 2 — kare yolu ve hareket kapısı (plaka metni YOK). Gölge modunda kapı yalnız sayar;
        // gateMissedDetections sıfır kalmadan MotionGate=On yapılmaz.
        sourceFormat = w.SourceFormat,
        sourceRecycledBuffers = w.SourceRecycledBuffers,
        motionGate = w.MotionGateMode,
        gateEvaluated = w.GateEvaluated,
        gateWouldSkip = w.GateWouldSkip,
        gateSkipped = w.GateSkipped,
        gateMissedDetections = w.GateMissedDetections,
        gateForced = w.GateForced,
        // MotionGate=On (hareket tetikli zamanlama): işlenen karelerin gerekçe dökümü. gateForcedHits =
        // zorunlu bakışta plaka bulunan kare → kapının kaçırdığının göstergesi, küçük kalmalı. (Yalnız sayaç.)
        gateMotionFrames = w.GateMotionFrames,
        gateHoldFrames = w.GateHoldFrames,
        gateActiveFrames = w.GateActiveFrames,
        gateForcedHits = w.GateForcedHits,
        eventsEmitted = w.EventsEmitted,
        lastEventUtc = w.LastEventUtc,
        // lastPlate KASITLI dışarıda: /health token-muaf (izleme için açık); gerçek plaka metni KVKK
        // PII'sidir, kimlik-doğrulamasız sızmamalı. Zaman damgası (lastEventUtc) izleme için yeterli.
        // Plaka verisi yalnız token-korumalı /events + /panel üzerinden.
    }).ToArray();

    // M14: "bir kamera bağlıysa ok" KÖR bir bariyer kamerasını maskeliyordu (giriş düşükken sistem
    // sağlıklı görünürdü). Üst-durum artık TÜM kameraların bağlı olmasını ister (izleme gerçeği görsün).
    string status = HealthStatus.Compute(cameras.Length, cameras.Count(c => c.connected));

    return Results.Json(new
    {
        status,
        cpu = sup.CpuFeatures,
        startedAtUtc = sup.StartedAtUtc,
        nowUtc = DateTimeOffset.UtcNow,
        // Faz 2 — süreç kaynak kullanımı (zayıf PC teşhisi): toplam CPU saniyesi, bellek, Gen2 GC sayısı
        // (ham 16 MB kareler LOH'a düşer; Gen2 hızla artıyorsa kare tahsisi izlenmeli).
        process = new
        {
            cpuSeconds = Math.Round(System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds, 1),
            workingSetMb = Math.Round(Environment.WorkingSet / 1048576.0, 1),
            // Bellek ayrıştırması (2026-10-13): çalışma kümesi = yönetilen yığın + yerel (ONNX oturumları, Skia,
            // ffmpeg boruları). İkisini ayırmadan "bellek nereye gidiyor" sorusu cevaplanamıyordu. Yalnız sayı.
            gcHeapMb = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
            gcCommittedMb = Math.Round(GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576.0, 1),
            serverGc = System.Runtime.GCSettings.IsServerGC,
            gen2Gc = GC.CollectionCount(2),
            processorCount = Environment.ProcessorCount,
        },
        cameras,
    });
});

app.MapGet("/events/recent", (InMemoryEventStore store, int? count, string? scope) =>
{
    // scope=roi -> yalnız tetik-bölgesi olayları (otopark yazılımı için); varsayılan: hepsi (panel/ölçüm).
    var events = store.GetRecent(count ?? 50);
    if (!string.IsNullOrEmpty(scope))
        events = events.Where(e => e.Scope == scope).ToList();
    return Results.Json(new { total = store.TotalCount, returned = events.Count, events });
});

// H1 TEŞHİSİ: "kuyruktaki ikinci araç bastırıldı" adaylarının plaka ÇİFTLERİ.
// /health'te YALNIZ sayaç var (o uç token-MUAF, plaka metni KVKK'dır); ayrıntı buraya konur çünkü
// "/api" öneki PanelAuth tarafından korunuyor. Sayacın tetiklenmesi tek başına kusur DEĞİLDİR —
// aynı aracın varyantları da buraya düşer. Kusur işareti, bir çiftin İKİ AYRI ARACA ait çıkmasıdır;
// bu ancak kırpıntılara GÖZLE bakılarak anlaşılır (bkz. PlateConfirmer.AbsorbedAfterEmit).
app.MapGet("/api/absorbed", (AnprCameraSupervisor sup) =>
    Results.Json(sup.Workers.Select(w => new
    {
        cameraId = w.CameraId,
        absorbedAfterEmit = w.AbsorbedAfterEmit,
        recent = w.RecentAbsorbed,          // "yeniOkuma→yayınlanmışPlaka", son 16
    })));

// Operatör düzeltmesi (FR-36): {eventId, correctedPlate} -> denetim izi (corrections.jsonl) +
// kanıt kırpıntısı eğitim arşivine kopyalanır (dataset/corrections/<PLAKA>/) -> fine-tune verisi.
app.MapPost("/api/correction", async (HttpRequest req, InMemoryEventStore store, ILogger<Program> log) =>
{
    using var reader = new StreamReader(req.Body);
    var body = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(await reader.ReadToEndAsync());
    if (body == null || !body.TryGetValue("eventId", out var ids) || !Guid.TryParse(ids, out var id) ||
        !body.TryGetValue("correctedPlate", out var plate) || string.IsNullOrWhiteSpace(plate))
        return Results.BadRequest(new { error = "eventId (guid) ve correctedPlate gerekli" });

    string compact = new string(plate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    var evt = store.GetRecent(int.MaxValue).FirstOrDefault(e => e.EventId == id);

    var rec = new
    {
        at = DateTimeOffset.Now, eventId = id, original = evt?.PlateCompact,
        corrected = compact, evidence = evt?.EvidencePath,
    };
    await File.AppendAllTextAsync(Path.Combine(AppContext.BaseDirectory, "corrections.jsonl"),
        System.Text.Json.JsonSerializer.Serialize(rec) + Environment.NewLine);

    string? archived = null;
    if (evt?.EvidencePath is { } ev)
    {
        string src = Path.Combine(AppContext.BaseDirectory, ev);
        if (File.Exists(src))
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "dataset-corrections", compact);
            Directory.CreateDirectory(dir);
            archived = Path.Combine(dir, Path.GetFileName(src));
            File.Copy(src, archived, overwrite: true);
        }
    }
    log.LogInformation("düzeltme kaydı: {Id} '{Orig}' -> '{New}' (kanıt: {Ev})", id, evt?.PlateCompact, compact, archived ?? "yok");
    return Results.Json(new { ok = true, original = evt?.PlateCompact, corrected = compact, archivedEvidence = archived != null });
});

// --- KAMERA-BAĞIMSIZ DOĞRULUK ÖLÇÜMÜ (/dogrulama) ---
// Paneldeki "birebir uyum" bir MUTABAKAT ölçüsüdür, doğruluk değil: kamera da yanılır ve çoğu zaman
// AYNI YÖNDE yanılır (ölçülmüş vaka: 25 ACY 952 → iki sistem de 25ACV952, mutabakat %100 doğruluk %0).
// Ürün "herhangi bir IP kamera" ile çalışacağı için referans kamera ZATEN olmayacak. Doğruluğu
// kanıtlamanın tek dürüst yolu: kanıt görselinden gözle doğrulama + tabakalı örnekleme.

// Sıradaki doğrulanacak olayı TABAKALI seçer (en az doğrulanmış güven bandından).
app.MapGet("/api/groundtruth/next", (AnalyticsService svc, GroundTruthStore gt, string? scope) =>
{
    string want = string.IsNullOrWhiteSpace(scope) ? "roi" : scope;
    var pending = svc.LoadOurs()
        .Where(e => e.Scope == want && e.EventId != Guid.Empty && e.Evidence != null && !gt.IsVerified(e.EventId))
        .ToList();
    // FrameBox taşıyan olaylar ÖNCELİKLİ: yalnız onlardan kalıcı etiketli set üretilebilir.
    var pick = Anpr.Service.Panel.GroundTruth.PickNext(pending, e => e.Ocr, gt.VerifiedPerBand(),
                                                      preferable: e => e.HasFrameBox);
    if (pick is null) return Results.Json(new { done = true, pending = 0 });
    return Results.Json(new
    {
        done = false,
        pending = pending.Count,
        eventId = pick.EventId,
        cameraId = pick.CameraId,
        ours = pick.Plate,
        ocr = pick.Ocr,
        band = Anpr.Service.Panel.GroundTruth.BandOf(pick.Ocr).ToString(),
        lowConfidence = pick.LowConfidence,
        evidence = pick.Evidence,
        frame = pick.Frame,
        candidates = pick.Candidates,
        at = pick.Ts,
    });
});

// Operatörün verdict'ini kaydeder. HER İKİ sonuç da yazılır (doğru + yanlış) — yalnız hataları
// kaydeden eski /api/correction bu yüzden doğruluk oranı üretemiyordu (payda yok).
app.MapPost("/api/groundtruth", async (HttpRequest req, AnalyticsService svc, GroundTruthStore gt,
                                       ILogger<Program> log) =>
{
    using var reader = new StreamReader(req.Body);
    var body = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(await reader.ReadToEndAsync());
    if (body == null || !body.TryGetValue("eventId", out var ids) || !Guid.TryParse(ids, out var id) ||
        !body.TryGetValue("verdict", out var vs) ||
        !Enum.TryParse<Anpr.Service.Panel.GroundTruthVerdict>(vs, ignoreCase: true, out var verdict))
        return Results.BadRequest(new { error = "eventId (guid) + verdict (Dogru|Yanlis|YanlisBelirsiz|Okunamadi) gerekli" });

    var evt = svc.LoadOurs().FirstOrDefault(e => e.EventId == id);
    if (evt is null) return Results.NotFound(new { error = "olay bulunamadı" });

    string? truth = null;
    if (verdict == Anpr.Service.Panel.GroundTruthVerdict.Yanlis)
    {
        body.TryGetValue("truth", out var t);
        truth = AnalyticsService.Compact(t);
        if (string.IsNullOrWhiteSpace(truth))
            return Results.BadRequest(new { error = "verdict=Yanlis ise gerçek plaka (truth) zorunlu" });
    }

    gt.Record(new Anpr.Service.Panel.GroundTruthEntry(
        DateTimeOffset.Now, id, evt.CameraId, evt.Evidence, evt.Plate, truth, verdict, evt.Ocr));
    log.LogInformation("yer-gerçeği: {Id} '{Ours}' → {Verdict}{Truth}", id, evt.Plate, verdict,
        truth is null ? "" : $" (doğrusu '{truth}')");
    return Results.Json(new { ok = true });
});

// Tabakalı, popülasyona taşınmış GERÇEK doğruluk raporu (Wilson %95 aralığıyla).
app.MapGet("/api/groundtruth/report", (AnalyticsService svc, GroundTruthStore gt,
                                       IOptions<AnprServiceOptions> opt, string? scope) =>
{
    string want = string.IsNullOrWhiteSpace(scope) ? "roi" : scope;
    var inScope = svc.LoadOurs().Where(e => e.Scope == want).ToList();
    var pop = inScope.GroupBy(e => Anpr.Service.Panel.GroundTruth.BandOf(e.Ocr))
                     .ToDictionary(g => g.Key, g => g.Count());
    // PAY ⊆ PAYDA: popülasyon tek kapsamdan sayılır ama doğrulama kayıtları her kapsamdan gelebilir.
    // Kapsam dışı bir doğrulama paya girip paydaya girmezse doğruluk sessizce sapar.
    var verified = Anpr.Service.Panel.GroundTruth.RestrictToPopulation(
        gt.All(), inScope.Select(e => e.EventId).ToHashSet());
    // Eşik CANLI config'ten okunur — sabitlenirse operatör eşiği değiştirdiğinde rapor sessizce
    // yanlış bantları "otomatik açan" saymaya devam ederdi.
    var rep = Anpr.Service.Panel.GroundTruth.Compute(pop, verified, opt.Value.ManualReviewMinOcr);
    return Results.Json(rep);
});

app.MapGet("/dogrulama", () => Results.Content(VerifyPage.Html, "text/html; charset=utf-8"));

// ŞÜPHELİ ÇİFT TRİYAJI: operatörün bir sonraki korele hatayı kendi başına bulabilmesi için.
// Bu bir DEDEKTÖR DEĞİL, inceleme kuyruğudur — sözleşme yanıtın `contract` alanında yazılı.
app.MapGet("/api/groundtruth/suspects", (AnalyticsService svc, int? minRoi) =>
{
    var pairs = AnalyticsService.BuildSuspectPairs(svc.LoadOurs(), Math.Clamp(minRoi ?? 20, 3, 500));
    return Results.Json(new
    {
        pairs,
        contract = "İNCELEME KUYRUĞU, hüküm DEĞİL. Ölçüldü (2026-07-31, 9 çift): 8'inde nadir taraf " +
                   "yalnızca baskın plakanın zayıf yanlış okumasıdır (masum); yalnız 1'inde nadir taraf " +
                   "GERÇEK plakadır (25ACY952 ← 25ACV952). Veride ikisi AYNI görünür — ayrımı yalnız " +
                   "insan, kanıt görselinden yapabilir. Kuyruğun değeri bakılacak yeri daraltmasıdır.",
    });
});

// ÇAPRAZ KANIT: doğrulanan olayın plakasının DİĞER okumaları, en NET olan başta.
// Ölçülmüş gerekçe: bulanık tek kırpıntı doğrulayıcıyı iki kez yanılttı (25ADL989→"26…",
// 25ADL991→"…981") ve yanlış bir "Yanlış" verdict'i yer-gerçeğini zehirleyecekti. En büyük kutulu
// kanıta bakmak ikisini de düzeltti — bu uç o manuel adımı otomatikleştirir.
app.MapGet("/api/groundtruth/context", (AnalyticsService svc, string eventId, int? max) =>
{
    if (!Guid.TryParse(eventId, out var id))
        return Results.BadRequest(new { error = "eventId (guid) gerekli" });
    var shots = AnalyticsService.BuildVerificationContext(svc.LoadOurs(), id, Math.Clamp(max ?? 6, 1, 12));
    return Results.Json(new
    {
        note = "En NET kanıt başta (kutu ALANINA göre — güven kalibre değil, piksel sayısı yalan söylemez). " +
               "'komsu' = lev1 farklı plaka; bu sahada ardışık filo plakaları GERÇEK araçlardır.",
        shots,
    });
});

// İNSAN GÖZÜ İÇİN kanıt iyileştirme (OCR hattını ETKİLEMEZ). Tam kareden, kareyle TUTARLI kutunun
// (FrameBox — v3) çevresinde geniş kadrajla kırpar, persentil gerer, keskinleştirir, büyütür.
// Ölçülmüş kazanç: 5 belirsiz doğrulama vakasının 2'si bu işlemle okunabilir hâle geldi.
app.MapGet("/api/enhance", (AnalyticsService svc, string eventId, float? pad) =>
{
    if (!Guid.TryParse(eventId, out var id)) return Results.BadRequest(new { error = "eventId (guid) gerekli" });

    // Kutuyu ve kareyi ham olaydan al: FrameBox kareyle TUTARLI olan kutudur (Box olmayabilir).
    string jsonl = Path.Combine(AppContext.BaseDirectory, "events.jsonl");
    if (!File.Exists(jsonl)) return Results.NotFound();
    Anpr.Sdk.Events.PlateEvent? evt = null;
    foreach (var line in File.ReadLines(jsonl))
    {
        try
        {
            var e = System.Text.Json.JsonSerializer.Deserialize<Anpr.Sdk.Events.PlateEvent>(line);
            if (e?.EventId == id) { evt = e; break; }
        }
        catch (System.Text.Json.JsonException) { }
    }
    if (evt is null) return Results.NotFound(new { error = "olay bulunamadı" });

    float padRatio = Math.Clamp(pad ?? 0.35f, 0f, 3f);
    string root = AppContext.BaseDirectory;

    // Tam kare varsa ORADAN kırp (native piksel = gerçek kazanç); yoksa kırpıntıya düş.
    SkiaSharp.SKBitmap? work = null;
    if (evt.FramePath is { } fp && File.Exists(Path.Combine(root, fp)))
    {
        using var frame = SkiaSharp.SKBitmap.Decode(Path.Combine(root, fp));
        if (frame != null)
        {
            var box = evt.FrameBox ?? evt.Box;   // v3 alanı yoksa eski davranışa düş
            work = EvidenceEnhancer.CropWide(frame, box.X, box.Y, box.Width, box.Height, padRatio);
        }
    }
    if (work is null && evt.EvidencePath is { } ep && File.Exists(Path.Combine(root, ep)))
        work = SkiaSharp.SKBitmap.Decode(Path.Combine(root, ep));
    if (work is null) return Results.NotFound(new { error = "kanıt görseli yok" });

    using (work)
    using (var enhanced = EvidenceEnhancer.Enhance(work))
    using (var big = EvidenceEnhancer.UpscaleTo(enhanced, 1600))
    using (var img = SkiaSharp.SKImage.FromBitmap(big))
    using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 92))
        return Results.File(data.ToArray(), "image/jpeg");
});

// OPERASYONEL KAYMA İZLEME: yer-gerçeği GEREKTİRMEZ, doğruluk İDDİA ETMEZ. Yalnız "son N saat
// dünlerden anlamlı biçimde farklı mı?" sorusunu yanıtlar — sessiz saha bozulmalarını (kamera odağı,
// ışık koşulu, kırpma kayması, ffmpeg yavaşlaması) doğruluk ölçmeye gerek kalmadan görünür kılar.
app.MapGet("/api/monitor", (AnalyticsService svc, int? hours, int? baselineHours) =>
{
    int curH = Math.Clamp(hours ?? 24, 1, 24 * 30);
    int basH = Math.Clamp(baselineHours ?? 72, 1, 24 * 90);
    var all = svc.LoadOurs();
    if (all.Count == 0)
        return Results.Json(new { error = "olay yok", signals = Array.Empty<DriftSignal>() });

    var now = all.Max(e => e.Ts);
    var curFrom = now - TimeSpan.FromHours(curH);
    var basFrom = curFrom - TimeSpan.FromHours(basH);

    var current = all.Where(e => e.Ts >= curFrom).ToList();
    var baseline = all.Where(e => e.Ts >= basFrom && e.Ts < curFrom).ToList();

    var signals = DriftMonitor.Compare(AnalyticsService.BuildDriftMetrics(baseline),
                                       AnalyticsService.BuildDriftMetrics(current));
    return Results.Json(new
    {
        window = new { currentHours = curH, baselineHours = basH, now, currentFrom = curFrom, baselineFrom = basFrom },
        counts = new { current = current.Count, baseline = baseline.Count },
        // Bu sinyal DOĞRULUK ÖLÇMEZ; tüketici bunu karıştırmasın diye sözleşmede açıkça yazılı.
        contract = "Kayma sinyali — yer-gerçeği gerektirmez, doğruluk oranı İDDİA ETMEZ. " +
                   "Doğruluk için /dogrulama (gözle doğrulama) kullanın.",
        signals,
    });
});

// Kanıt kırpıntıları (panel küçük resimleri). Dosya adı sıkı doğrulanır (path traversal yok).
app.MapGet("/evidence/{file}", (string file) =>
{
    if (string.IsNullOrEmpty(file) || file.Contains("..") || file.Contains('/') || file.Contains('\\') || !file.EndsWith(".jpg"))
        return Results.NotFound();
    string path = Path.Combine(AppContext.BaseDirectory, "evidence", file);
    return File.Exists(path) ? Results.File(path, "image/jpeg") : Results.NotFound();
});

// --- Canlı karşılaştırma paneli ---
app.MapGet("/panel", () => Results.Content(PanelPage.Html, "text/html; charset=utf-8"));

// Analiz paneli: denetlenebilir karşılaştırma tablosu + iş akışı kapıları + sağlık (bkz. AnalysisPage).
app.MapGet("/analiz", () => Results.Content(AnalysisPage.Html, "text/html; charset=utf-8"));

app.MapGet("/api/analytics", (AnalyticsService svc, IOptions<AnprServiceOptions> opt, string? cameraId, int? minutes) =>
{
    string? cam = string.IsNullOrWhiteSpace(cameraId) ? null : cameraId;
    DateTimeOffset? since = minutes is > 0 ? DateTimeOffset.UtcNow.AddMinutes(-minutes.Value) : null;
    var r = svc.Compare(cam, since);
    var o = opt.Value;

    // TEK MOTOR (ComparisonEngine) — canlı panel ve /analiz aynı sonucu görür. Her sayının paydası
    // açıktır; eşleşmeyen kamera geçişleri körü körüne "kaçırma" sayılmaz, nedenine göre kovalanır.
    return Results.Json(new
    {
        window = r.Window is null ? null : new { from = r.Window.From, to = r.Window.To },
        ours = new
        {
            passes = r.OursPasses, roi = r.Rows.Count,
            exact = r.Exact, near = r.Near, onlyOurs = r.OursOnly,
        },
        camera = new
        {
            passes = r.CamPasses,
            matched = r.Matched, cross = r.Cross, street = r.Street,
            invalid = r.CamFaulty, inObservation = r.InObservation,
            outOfWindow = r.OutOfWindow, missCandidate = r.MissCandidate,
        },
        metrics = new
        {
            exactAgreement = r.ExactAgreement,   // birebir / eşleşen — MUTABAKAT, doğruluk değil
            capture = r.Capture,                 // ÜST sınır (cadde varsayımı doğruysa)
            captureLow = r.CaptureLow,           // ALT sınır (cadde varsayımı çökerse)
            caveat = r.AgreementCaveat,
        },
        rows = r.Rows.Select(x => new
        {
            time = x.Ours.Ts.ToLocalTime().ToString("dd.MM HH:mm:ss"),
            cameraId = x.Ours.CameraId, ours = x.Ours.Plate, camera = x.Camera?.Plate,
            distance = x.Distance, lagSeconds = x.LagSeconds, verdict = x.Verdict,
            ocr = x.Ours.Ocr, lowConfidence = x.Ours.LowConfidence, snappedFrom = x.Ours.SnappedFrom,
            evidence = x.Ours.Evidence, frame = x.Ours.Frame, rawCount = x.Ours.RawCount,
        }),
        cameraOnly = r.CameraOnly.Select(x => new
        {
            time = x.Camera.Ts.ToLocalTime().ToString("dd.MM HH:mm:ss"),
            cameraId = x.Camera.CameraId, camera = x.Camera.Plate,
            verdict = x.Verdict.ToString(), note = x.SimilarNote, rawCount = x.Camera.RawCount,
        }),
        config = new
        {
            fps = o.Fps, idleFps = o.IdleFps, minAspect = o.MinPlateAspectRatio,
            minConfirm = o.MinConfirmations, settle = o.SettleWindowSeconds,
            snapLev = o.SnapMaxLev, snapGate = o.SnapMaxOcrConfidence,
            emitMinOcr = o.EmitMinOcrConfidence, variantSec = o.VariantSuppressSeconds,
            manualMin = o.ManualReviewMinOcr,
        },
    });
});

// Canlı panelin skor kaynağı: AYNI motor, hafif pencere (varsayılan son 3 saat).
// Panel artık kendi eşleştirmesini YAPMAZ — iki ekran aynı sayıyı göstermek zorunda (2026-07-28).
app.MapGet("/api/board", (AnalyticsService svc, string? cameraId, int? minutes) =>
{
    string? cam = string.IsNullOrWhiteSpace(cameraId) ? null : cameraId;
    var since = DateTimeOffset.UtcNow.AddMinutes(-(minutes is > 0 ? minutes.Value : 180));
    var r = svc.Compare(cam, since);
    return Results.Json(new
    {
        window = r.Window is null ? null : new { from = r.Window.From, to = r.Window.To },
        exact = r.Exact, near = r.Near, onlyOurs = r.OursOnly,
        cross = r.Cross, street = r.Street, invalid = r.CamFaulty,
        inObservation = r.InObservation, outOfWindow = r.OutOfWindow, missCandidate = r.MissCandidate,
        oursPasses = r.Rows.Count, camPasses = r.CamPasses,
        exactAgreement = r.ExactAgreement, capture = r.Capture, captureLow = r.CaptureLow,
    });
});

app.MapGet("/api/stream", async (HttpContext ctx, PanelHub hub) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    using var sub = hub.Subscribe();
    await ctx.Response.WriteAsync(": bağlı\n\n", ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    try
    {
        await foreach (var msg in sub.Reader.ReadAllAsync(ctx.RequestAborted))
        {
            await ctx.Response.WriteAsync($"data: {msg}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException) { /* istemci ayrıldı */ }
});

app.MapGet("/api/snapshot/{cameraId}", (string cameraId, AnprCameraSupervisor sup) =>
{
    var w = sup.Workers.FirstOrDefault(w => w.CameraId == cameraId);
    var frame = w?.PeekSourceFrame() ?? w?.LastFrame;
    if (frame == null) return Results.StatusCode(503);
    // Ham (BGRA) karede JPEG BURADA üretilir; panel için 1280 px yeter (zayıf PC'de 4 MP kodlamak boşuna).
    return Results.Bytes(frame.ToJpeg(80, maxWidth: 1280), "image/jpeg");
});

app.MapGet("/api/detections/{cameraId}", (string cameraId, AnprCameraSupervisor sup) =>
{
    var w = sup.Workers.FirstOrDefault(w => w.CameraId == cameraId);
    if (w == null) return Results.NotFound();
    return Results.Json(new
    {
        cameraId,
        roi = w.CurrentRoi,
        boxes = w.LastBoxes.Select(b => new { x = b.X, y = b.Y, w = b.Width, h = b.Height }),
    });
});

app.MapGet("/api/roi/{cameraId}", (string cameraId, AnprCameraSupervisor sup, RoiStore store) =>
{
    var w = sup.Workers.FirstOrDefault(w => w.CameraId == cameraId);
    return Results.Json(new { cameraId, roi = w?.CurrentRoi ?? store.LoadOrDefault(cameraId) });
});

app.MapPut("/api/roi/{cameraId}", async (string cameraId, HttpRequest req, AnprCameraSupervisor sup, RoiStore store) =>
{
    using var reader = new StreamReader(req.Body);
    string body = await reader.ReadToEndAsync();
    string roi = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body)?.GetValueOrDefault("roi") ?? "";
    // Doğrulama: boş = temizle (geçerli); doluysa parse edilebilir olmalı.
    if (roi.Length > 0 && Anpr.Core.Imaging.RoiPolygon.Parse(roi) == null)
        return Results.BadRequest(new { error = "geçersiz ROI (en az 3 nokta, 'x,y;x,y;...' 0-1000)" });
    store.Save(cameraId, roi);
    var w = sup.Workers.FirstOrDefault(w => w.CameraId == cameraId);
    w?.ApplyRoi(roi);
    return Results.Json(new { ok = true, cameraId, roi, applied = w != null });
});

// Canlı MJPEG akışı (kaynak hızında, ~15 fps üst sınır; istemci koptuğunda döngü biter).
app.MapGet("/api/mjpeg/{cameraId}", async (string cameraId, AnprCameraSupervisor sup, HttpContext ctx) =>
{
    var w = sup.Workers.FirstOrDefault(w => w.CameraId == cameraId);
    if (w == null) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
    Anpr.Sdk.Frames.VideoFrame? last = null;
    try
    {
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            var f = w.PeekSourceFrame();
            if (f != null && !ReferenceEquals(f, last))
            {
                last = f;
                // Ham karede JPEG burada üretilir; panel için 1280 px ve ≤5 fps yeter (Faz 2: zayıf PC'yi panel yormasın).
                var jpeg = f.ToJpeg(75, maxWidth: 1280);
                await ctx.Response.WriteAsync($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n", ctx.RequestAborted);
                await ctx.Response.Body.WriteAsync(jpeg, ctx.RequestAborted);
                await ctx.Response.WriteAsync("\r\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
            await Task.Delay(200, ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException) { /* istemci ayrıldı */ }
});

// Sayfa-açılış dolumu: kameranın son kayıtları (poller tamponundan).
app.MapGet("/api/camera/recent", (string cameraId, CameraRecordPoller poller) =>
    Results.Json(new { cameraId, records = poller.Recent(cameraId) }));

app.Run();
