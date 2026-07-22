using Anpr.Sdk.Configuration;
using Anpr.Sdk.Events;
using Anpr.Sdk.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anpr.Sdk;

/// <summary>
/// Otopark yazılımına (PMS) doğrudan gömülüm için tek-sınıf giriş noktası: kamera yapılandırması
/// ver, başlat, <see cref="PlateDetected"/> olayına abone ol. İçeride servisle AYNI boru hattı
/// çalışır (RTSP -> tespit -> OCR -> çok-kareli oylama -> varyant bastırma -> olay).
///
/// Kullanım:
/// <code>
/// var runtime = new AnprRuntime(new AnprServiceOptions {
///     Cameras = { new CameraOptions { Id = "giris", Name = "Giriş", RtspUrl = "rtsp://..." } }
/// });
/// runtime.PlateDetected += e => { if (e.Scope == "roi" &amp;&amp; !e.LowConfidence) Bariyer(e.Plate); };
/// await runtime.StartAsync();
/// </code>
///
/// Gereksinimler: net8.0+, ffmpeg (PATH'te ya da options.Ffmpeg.Path), modeller (paketle birlikte
/// çıktı dizinine models/ olarak kopyalanır). Olay işleyicisi HIZLI dönmelidir (uzun iş kuyruğa
/// atılmalı) — işleyicideki istisnalar yutulur ve loglanır, boru hattını durdurmaz.
/// </summary>
public sealed class AnprRuntime : IAsyncDisposable
{
    private readonly AnprCameraSupervisor _supervisor;
    private readonly ILogger _logger;
    private readonly bool _maskPlates;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Her yayınlanan plaka olayında tetiklenir (hem Scope="roi" hem "gozlem" — tüketici filtreler).
    /// Worker döngü thread'lerinden çağrılır; UI güncellemeleri kendi context'inize marshal edin.
    /// </summary>
    public event Action<PlateEvent>? PlateDetected;

    /// <summary>Müdavim plaka kaydı (varyant bastırma kural D'nin hafızası). İsteğe bağlı olarak
    /// geçmiş bir events.jsonl ile tohumlanabilir: <c>FrequentPlates.LoadFromJsonl(yol)</c>.</summary>
    public FrequentPlateRegistry FrequentPlates { get; }

    /// <summary>Kamera durum/sayaç görünümü (bağlantı, kare hızı, p50/p95 ms, olay sayısı) — PMS
    /// kendi sağlık ekranında gösterebilir.</summary>
    public IReadOnlyList<CameraWorker> Cameras => _supervisor.Workers;

    /// <summary>CPUID/mimari özeti (AVX yoksa performans uyarısı loglanır).</summary>
    public string CpuFeatures => _supervisor.CpuFeatures;

    /// <param name="options">Kamera listesi + eşikler. Üretimde doğrulanmış varsayılanlar geçerlidir;
    /// çoğu senaryoda yalnız <see cref="AnprServiceOptions.Cameras"/> doldurmak yeter.</param>
    /// <param name="loggerFactory">PMS'in log altyapısı (Serilog/NLog sağlayıcısı vb.). null = sessiz.</param>
    /// <param name="extraSinks">Olayları ayrıca almak isteyen ek hedefler (ör. veritabanı sink'i).</param>
    public AnprRuntime(AnprServiceOptions options, ILoggerFactory? loggerFactory = null,
        IEnumerable<IEventSink>? extraSinks = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var lf = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = lf.CreateLogger<AnprRuntime>();
        _maskPlates = options.MaskPlatesInLogs;   // KVKK: handler-istisna log'unda plakayı maskele (M8)

        // M7: sunucu-only alanlar SDK boru hattında UYGULANMAZ (yalnız Anpr.Service paneli okur). PMS
        // WebhookUrl set edip sessizce hiç POST gelmemesindense açık uyarı alsın (no-silent-ignore).
        if (!string.IsNullOrWhiteSpace(options.WebhookUrl))
            _logger.LogWarning("WebhookUrl DLL gömülümünde YOK SAYILIR (yalnız Anpr.Service paneli kullanır). " +
                "Olayları almak için PlateDetected'e abone olun ya da extraSinks (IEventSink) geçin.");
        FrequentPlates = new FrequentPlateRegistry
        {
            KnownPlates = FrequentPlateRegistry.NormalizeKnownPlates(options.KnownPlates),
        };

        var sinks = new List<IEventSink> { new DelegateEventSink(e => RaisePlateDetected(e)) };
        if (extraSinks != null) sinks.AddRange(extraSinks);

        _supervisor = new AnprCameraSupervisor(
            Microsoft.Extensions.Options.Options.Create(options), sinks, FrequentPlates, lf);
    }

    /// <summary>Kameraları kurar ve okuma döngülerini başlatır. Modeller bulunamaz/bozuksa fırlatır
    /// (sessiz yanlış çalışmaya izin verilmez).</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) throw new InvalidOperationException("AnprRuntime zaten başlatıldı.");
        _started = true;
        await _supervisor.StartAsync(cancellationToken);
    }

    /// <summary>Okumayı durdurur, bekleyen olay yayınlarını (≤5 sn) boşaltır, kaynakları bırakır.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started || _disposed) return;
        _started = false;
        await _supervisor.StopAsync(cancellationToken);
    }

    private void RaisePlateDetected(PlateEvent e)
    {
        try { PlateDetected?.Invoke(e); }
        catch (Exception ex)
        {
            // Tüketici istisnası boru hattını (worker döngüsünü) DÜŞÜRMEMELİ.
            _logger.LogWarning(ex, "PlateDetected işleyicisi istisna fırlattı ({Plate})",
                Events.ConsoleEventSink.MaskIf(_maskPlates, e.PlateCompact));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync();
        _disposed = true;
    }

    /// <summary>Olayları bir delegeye ileten iç sink (facade'ın C# event köprüsü).</summary>
    private sealed class DelegateEventSink : IEventSink
    {
        private readonly Action<PlateEvent> _handler;
        public DelegateEventSink(Action<PlateEvent> handler) => _handler = handler;
        public Task PublishAsync(PlateEvent e, CancellationToken ct)
        {
            _handler(e);
            return Task.CompletedTask;
        }
    }
}
