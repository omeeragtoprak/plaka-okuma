using System.Net;
using Microsoft.Extensions.Options;
using Anpr.Service;
using Anpr.Sdk.Configuration;
using Anpr.Service.Panel;
using Anpr.Service.Events;
using Anpr.Sdk.Events;
using Anpr.Sdk.Workers;

var builder = WebApplication.CreateBuilder(args);

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
        string path = ctx.Request.Path.Value ?? "";
        bool korumali = path.StartsWith("/panel") || path.StartsWith("/api") ||
                        path.StartsWith("/events") || path.StartsWith("/evidence");
        if (korumali)
        {
            string? tok = ctx.Request.Query["token"].FirstOrDefault()
                          ?? ctx.Request.Headers["X-Anpr-Token"].FirstOrDefault();
            if (tok != opts.PanelToken)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("token gerekli");
                return;
            }
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
        qualityDropped = w.QualityDropped,
        frameMsP50 = w.FrameMsP50,
        frameMsP95 = w.FrameMsP95,
        restartCount = w.RestartCount,
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
    var jpeg = w?.PeekSourceFrame() ?? w?.LastFrameJpeg;
    return jpeg == null ? Results.StatusCode(503) : Results.Bytes(jpeg, "image/jpeg");
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
    byte[]? last = null;
    try
    {
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            var f = w.PeekSourceFrame();
            if (f != null && !ReferenceEquals(f, last))
            {
                last = f;
                await ctx.Response.WriteAsync($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {f.Length}\r\n\r\n", ctx.RequestAborted);
                await ctx.Response.Body.WriteAsync(f, ctx.RequestAborted);
                await ctx.Response.WriteAsync("\r\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
            await Task.Delay(66, ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException) { /* istemci ayrıldı */ }
});

// Sayfa-açılış dolumu: kameranın son kayıtları (poller tamponundan).
app.MapGet("/api/camera/recent", (string cameraId, CameraRecordPoller poller) =>
    Results.Json(new { cameraId, records = poller.Recent(cameraId) }));

app.Run();
