using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Anpr.Service.Events;

/// <summary>
/// Olayı yapılandırılmış bir HTTP webhook URL'ine JSON olarak POST eder.
/// Yalnızca AnprServiceOptions.WebhookUrl doluysa kaydedilir. Hatalar yutulur (servisi durdurmaz).
/// </summary>
public sealed class WebhookEventSink : IEventSink
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _url;
    private readonly ILogger<WebhookEventSink> _logger;

    public WebhookEventSink(IHttpClientFactory httpFactory, string url, ILogger<WebhookEventSink> logger)
    {
        _httpFactory = httpFactory;
        _url = url;
        _logger = logger;
    }

    public async Task PublishAsync(PlateEvent e, CancellationToken cancellationToken)
    {
        try
        {
            // Her gönderimde fabrikadan client al (IHttpClientFactory handler rotasyonu/DNS yenilemesi yapsın).
            var http = _httpFactory.CreateClient();
            using var resp = await http.PostAsJsonAsync(_url, e, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Webhook {Url} -> HTTP {Status}", _url, (int)resp.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Webhook gönderimi başarısız: {Url}", _url);
        }
    }
}
