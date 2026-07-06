using System.Net;
using Anpr.Service.Configuration;
using Anpr.Service.Events;
using Anpr.Service.Workers;

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
        case "0.0.0.0" or "":
            ip = IPAddress.Any;
            break;
        case "localhost":
            ip = IPAddress.Loopback;
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
}

// --- Kamera yöneticisi (hem singleton hem hosted service) ---
builder.Services.AddSingleton<AnprCameraSupervisor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AnprCameraSupervisor>());

var app = builder.Build();

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
        restartCount = w.RestartCount,
        eventsEmitted = w.EventsEmitted,
        lastEventUtc = w.LastEventUtc,
        lastPlate = w.LastPlate,
    }).ToArray();

    string status = cameras.Length == 0 ? "no-cameras"
        : cameras.Any(c => c.connected) ? "ok" : "degraded";

    return Results.Json(new
    {
        status,
        startedAtUtc = sup.StartedAtUtc,
        nowUtc = DateTimeOffset.UtcNow,
        cameras,
    });
});

app.MapGet("/events/recent", (InMemoryEventStore store, int? count) =>
{
    var events = store.GetRecent(count ?? 50);
    return Results.Json(new { total = store.TotalCount, returned = events.Count, events });
});

app.Run();
