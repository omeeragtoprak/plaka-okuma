using Anpr.Sdk.Events;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Anpr.Service.Events;

/// <summary>
/// Olayı yapılandırılmış HTTP webhook URL'ine JSON olarak POST eder (KAYIT kanalı sözleşmesi, E3):
/// başarısız gönderim DİSKE kuyruklanır (webhook-queue.jsonl) ve arka plan döngüsü 2 dakikada bir
/// sırayla yeniden dener — hiçbir olay kaybolmaz. Kuyruk üst sınırı aşılırsa en eskiler budanır
/// (loglanır). Yalnız Scope=roi olayları gönderilir (gözlem bariyer/PMS kanalına sızmaz).
/// NOT: bariyer AÇ kararı için ayrı, retry'siz bir kanal kullanılmalıdır (gecikmiş tekrar iletim
/// istenmeyen açılışa yol açar) — bu sink kayıt/PMS besleme kanalıdır.
/// </summary>
public sealed class WebhookEventSink : IEventSink, IDisposable
{
    private const int MaxQueueLines = 10_000;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpFactory;
    private readonly string _url;
    private readonly ILogger<WebhookEventSink> _logger;
    private readonly string _queuePath;
    private readonly object _queueLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _retryLoop;

    public WebhookEventSink(IHttpClientFactory httpFactory, string url, ILogger<WebhookEventSink> logger)
    {
        _httpFactory = httpFactory;
        _url = url;
        _logger = logger;
        _queuePath = Path.Combine(AppContext.BaseDirectory, "webhook-queue.jsonl");
        _retryLoop = Task.Run(() => RetryLoopAsync(_cts.Token));
    }

    public async Task PublishAsync(PlateEvent e, CancellationToken cancellationToken)
    {
        // Gözlem (ROI dışı) okumalar otopark yazılımına GÖNDERİLMEZ — kanal yalnız "roi".
        if (e.Scope != "roi") return;

        if (!await TrySendAsync(JsonSerializer.Serialize(e), cancellationToken))
            Enqueue(e);
    }

    private async Task<bool> TrySendAsync(string json, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient();
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(_url, content, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private void Enqueue(PlateEvent e)
    {
        try
        {
            lock (_queueLock)
            {
                File.AppendAllText(_queuePath, JsonSerializer.Serialize(e) + Environment.NewLine);
                var lines = File.ReadAllLines(_queuePath);
                if (lines.Length > MaxQueueLines)
                {
                    File.WriteAllLines(_queuePath, lines.Skip(lines.Length - MaxQueueLines));
                    _logger.LogWarning("webhook kuyruğu {Max} sınırını aştı; en eskiler budandı", MaxQueueLines);
                }
            }
            _logger.LogWarning("webhook ulaşılamadı; olay kuyruğa yazıldı ({Url})", _url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "webhook kuyruğa yazılamadı — olay yalnız events.jsonl'de");
        }
    }

    // Store-and-forward: kuyruk doluysa 2 dakikada bir SIRAYLA gönderilir; ilk başarısızlıkta durur
    // (sıra korunur), kalanlar sonraki tura kalır.
    private async Task RetryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(RetryInterval, ct); } catch (OperationCanceledException) { break; }

            string[] pending;
            lock (_queueLock)
            {
                if (!File.Exists(_queuePath)) continue;
                pending = File.ReadAllLines(_queuePath);
            }
            if (pending.Length == 0) continue;

            int sent = 0;
            foreach (var line in pending)
            {
                if (string.IsNullOrWhiteSpace(line)) { sent++; continue; }
                if (!await TrySendAsync(line, ct)) break;
                sent++;
            }
            if (sent > 0)
            {
                lock (_queueLock)
                {
                    // KRİTİK (kayıp-güncelleme yarışı): gönderim kilitsizdi; bu sırada worker thread'leri
                    // Enqueue ile SONA yeni satır eklemiş olabilir. Bayat 'pending'i geri yazmak o
                    // eklenenleri siler (zero-loss ihlali). Bunun yerine dosyayı TAZE oku ve yalnız
                    // ÖNden gönderdiğimiz 'sent' satırı at — append'ler sonda olduğundan korunur.
                    var current = File.Exists(_queuePath) ? File.ReadAllLines(_queuePath) : System.Array.Empty<string>();
                    var rest = current.Skip(sent).ToArray();
                    if (rest.Length == 0) File.Delete(_queuePath);
                    else File.WriteAllLines(_queuePath, rest);
                }
                _logger.LogInformation("webhook kuyruğu: {Sent} olay teslim edildi, {Left} bekliyor",
                    sent, pending.Length - sent);
            }
        }
    }

    /// <summary>Bekleyen kuyruk derinliği (sağlık raporu için).</summary>
    public int QueueDepth
    {
        get
        {
            try { lock (_queueLock) return File.Exists(_queuePath) ? File.ReadAllLines(_queuePath).Length : 0; }
            catch { return -1; }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _retryLoop.Wait(TimeSpan.FromSeconds(2)); } catch { /* yoksay */ }
        _cts.Dispose();
    }
}
