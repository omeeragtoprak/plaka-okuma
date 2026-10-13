using System.Net;
using Anpr.Core;
using Anpr.Sdk.Events;
using Anpr.Service.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anpr.Tests;

/// <summary>
/// Webhook sink'in <b>"hiçbir olay kaybolmaz"</b> iddiası (E3 kayıt kanalı sözleşmesi).
/// <para>
/// Bu iddia sınıfın XML belgesinde yazılıydı ama <b>hiçbir testle desteklenmiyordu</b>; üstelik
/// kuyruk yolu sabit koduydu, yani sınanamıyordu da. Bu projede sınanmamış bir iddia hipotezdir —
/// özellikle bu iddia, PMS'in giriş/ücret kaydının tamlığını garanti ediyor.
/// </para>
/// <para>
/// Testler <see cref="WebhookEventSink.DrainQueueOnceAsync"/>'i doğrudan çağırır; 2 dakikalık
/// retry döngüsünü beklemek testi ZAMANA bağlı ve kırılgan yapardı.
/// </para>
/// </summary>
public class WebhookEventSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "anpr-wh-" + Guid.NewGuid().ToString("N"));
    private string Queue => Path.Combine(_dir, "q.jsonl");

    public WebhookEventSinkTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* yoksay */ } }

    // --- Sahte HTTP: gönderimi anahtarla aç/kapat, gövdeleri kaydet ---
    private sealed class FakeHandler : HttpMessageHandler
    {
        public bool Up { get; set; }
        public List<string> Received { get; } = new();
        public Action? OnSend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            OnSend?.Invoke();
            if (!Up) throw new HttpRequestException("webhook kapalı");
            Received.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeFactory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private (WebhookEventSink Sink, FakeHandler Http) Make()
    {
        var h = new FakeHandler();
        return (new WebhookEventSink(new FakeFactory(h), "http://pms.local/hook",
                                     NullLogger<WebhookEventSink>.Instance, Queue), h);
    }

    private static PlateEvent Ev(string plate, string scope = "roi") => new()
    {
        Timestamp = new DateTimeOffset(2026, 8, 3, 1, 0, 0, TimeSpan.Zero),
        CameraId = "giris",
        CameraName = "Giriş",
        Plate = plate,
        PlateCompact = plate.Replace(" ", ""),
        RawText = plate.Replace(" ", ""),
        Confidence = 0.9f,
        DetectorConfidence = 0.9f,
        OcrConfidence = 0.99f,
        Box = new PlateBox(10, 10, 100, 40),
        Scope = scope,
    };

    private int QueueLines() => File.Exists(Queue) ? File.ReadAllLines(Queue).Length : 0;

    [Fact]
    public async Task Delivered_event_is_not_queued()
    {
        var (sink, http) = Make();
        using var _ = sink;
        http.Up = true;
        await sink.PublishAsync(Ev("25 ABC 123"), default);

        Assert.Single(http.Received);
        Assert.Equal(0, QueueLines());
        Assert.Equal(0, sink.QueueDepth);
    }

    [Fact]
    public async Task Failed_event_is_queued_and_delivered_after_recovery()
    {
        // ZERO-LOSS ÇEKİRDEĞİ: webhook düşükken üretilen olay diske yazılır ve geri geldiğinde teslim edilir.
        var (sink, http) = Make();
        using var _ = sink;
        http.Up = false;
        await sink.PublishAsync(Ev("25 ABC 123"), default);
        await sink.PublishAsync(Ev("25 ABC 124"), default);

        Assert.Empty(http.Received);
        Assert.Equal(2, sink.QueueDepth);

        http.Up = true;
        Assert.Equal(2, await sink.DrainQueueOnceAsync(default));

        Assert.Equal(2, http.Received.Count);
        Assert.Contains(http.Received, b => b.Contains("25ABC123"));
        Assert.Contains(http.Received, b => b.Contains("25ABC124"));
        Assert.Equal(0, sink.QueueDepth);
    }

    [Fact]
    public async Task Order_is_preserved_and_drain_stops_at_first_failure()
    {
        // Sıra KAYIT kanalı için anlamlıdır (giriş/çıkış eşleşmesi). İlk başarısızlıkta durulur ki
        // sonraki olaylar öncekilerden ÖNCE teslim edilmesin.
        var (sink, http) = Make();
        using var _ = sink;
        http.Up = false;
        foreach (var p in new[] { "25 AAA 001", "25 AAA 002", "25 AAA 003" })
            await sink.PublishAsync(Ev(p), default);
        Assert.Equal(3, sink.QueueDepth);

        // ikinci gönderimde tekrar düşsün
        http.Up = true;
        int n = 0;
        http.OnSend = () => { if (++n == 2) http.Up = false; };
        Assert.Equal(1, await sink.DrainQueueOnceAsync(default));

        Assert.Single(http.Received);
        Assert.Contains("25AAA001", http.Received[0]);
        Assert.Equal(2, sink.QueueDepth);                       // 002 ve 003 sırada KALDI

        http.OnSend = null; http.Up = true;
        Assert.Equal(2, await sink.DrainQueueOnceAsync(default));
        Assert.Equal(3, http.Received.Count);
        Assert.Contains("25AAA002", http.Received[1]);          // sıra korundu
        Assert.Contains("25AAA003", http.Received[2]);
    }

    [Fact]
    public async Task Event_queued_DURING_a_drain_is_not_lost()
    {
        // KAYIP-GÜNCELLEME YARIŞI. Gönderim kilit DIŞINDA yapılır; bu sırada bir worker yeni olay
        // ekleyebilir. Bayat listeyi geri yazan bir uygulama o olayı SESSİZCE silerdi — zero-loss
        // ihlali ve hiçbir hata üretmezdi. Kod bunu "taze oku, önden 'sent' kadar at" ile çözüyor;
        // bu test o çözümü kilitler.
        var (sink, http) = Make();
        using var _ = sink;
        http.Up = false;
        await sink.PublishAsync(Ev("25 AAA 001"), default);
        await sink.PublishAsync(Ev("25 AAA 002"), default);

        http.Up = true;
        bool injected = false;
        http.OnSend = () =>
        {
            if (injected) return;
            injected = true;
            http.Up = false;                                     // enjeksiyon kuyruğa gitsin
            sink.PublishAsync(Ev("25 ZZZ 999"), default).GetAwaiter().GetResult();
            http.Up = true;
        };

        await sink.DrainQueueOnceAsync(default);
        http.OnSend = null;

        // Araya giren olay HÂLÂ ya kuyrukta ya teslim edilmiş olmalı — kaybolmuş OLMAMALI.
        var stillQueued = File.Exists(Queue) ? File.ReadAllText(Queue) : "";
        bool present = stillQueued.Contains("25ZZZ999") || http.Received.Any(b => b.Contains("25ZZZ999"));
        Assert.True(present, "Drain sırasında kuyruğa yazılan olay KAYBOLDU (zero-loss ihlali).");

        // ve sonunda gerçekten teslim edilebiliyor
        http.Up = true;
        await sink.DrainQueueOnceAsync(default);
        Assert.Contains(http.Received, b => b.Contains("25ZZZ999"));
        Assert.Equal(0, sink.QueueDepth);
    }

    [Fact]
    public async Task Observation_scope_events_never_reach_the_pms_channel()
    {
        // Gözlem katmanı ROI DIŞINI de okur ve bariyer/PMS kanalına girmemelidir (§P.4 kalıcı kısıt).
        // Ne gönderilir ne kuyruklanır — "sonra deneriz" bile olmamalı.
        var (sink, http) = Make();
        using var _ = sink;
        http.Up = false;
        await sink.PublishAsync(Ev("25 ABC 123", scope: "gozlem"), default);
        Assert.Equal(0, QueueLines());

        http.Up = true;
        await sink.PublishAsync(Ev("25 ABC 124", scope: "gozlem"), default);
        Assert.Empty(http.Received);
    }

    [Fact]
    public async Task Drain_on_empty_or_missing_queue_is_a_no_op()
    {
        var (sink, http) = Make();
        using var _ = sink;
        http.Up = true;
        Assert.Equal(0, await sink.DrainQueueOnceAsync(default));   // dosya hiç yok
        Assert.Equal(0, sink.QueueDepth);

        File.WriteAllText(Queue, "");
        Assert.Equal(0, await sink.DrainQueueOnceAsync(default));   // boş dosya
        Assert.Empty(http.Received);
    }
}
