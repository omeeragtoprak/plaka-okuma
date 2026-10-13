using System.Threading.Channels;
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
    private Channel<PlateEvent> _eventChannel;   // StopAsync sonrası yeniden kurulabilir (restart)   // M2: handler'ı boru hattından izole eden kuyruk
    private Task _dispatchTask;                  // StopAsync sonrası yeniden kurulabilir (restart)
    private long _droppedEvents;
    private bool _started;
    private bool _disposed;

    /// <summary>M2: PlateDetected kuyruğu dolduğu için düşürülen olay sayısı (işleyici çok yavaşsa artar;
    /// sağlıklı entegrasyonda 0 kalır). PMS bunu izleyip yavaş işleyicisini tespit edebilir.</summary>
    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);

    /// <summary>
    /// Her yayınlanan plaka olayında tetiklenir (hem Scope="roi" hem "gozlem" — tüketici filtreler).
    /// <para><b>KONTRAT (M2 — bounded-dispatcher ile izole):</b></para>
    /// <list type="bullet">
    /// <item>İşleyici TEK bir iç dispatcher thread'inde <b>SERİLEŞMİŞ</b> çağrılır (eşzamanlı PlateDetected
    /// yok) — böylece worker boru hattı yavaş işleyiciden ETKİLENMEZ (kare düşmez).</item>
    /// <item>Yine de <b>makul hızlı dönün</b>: çok yavaş işleyici kuyruğu (256) doldurursa yeni olaylar
    /// DÜŞER (<see cref="DroppedEvents"/> sayılır + uyarı loglanır). Uzun işi KENDİ kuyruğunuza atın.</item>
    /// <item>İşleyicideki istisnalar yutulur ve loglanır (boru hattı düşmez).</item>
    /// <item>Kendi thread'lerinizden erişilen paylaşılan durumu kilitleyin; UI'yi kendi context'inize marshal edin.</item>
    /// </list>
    /// </summary>
    public event Action<PlateEvent>? PlateDetected;

    /// <summary>Müdavim plaka kaydı (varyant bastırma kural D'nin hafızası). İsteğe bağlı olarak
    /// geçmiş bir events.jsonl ile tohumlanabilir: <c>FrequentPlates.LoadFromJsonl(yol)</c>.</summary>
    public FrequentPlateRegistry FrequentPlates { get; }

    /// <summary>Kamera SALT-OKUNUR durum görünümü (bağlantı, kare hızı, p50/p95 ms, olay sayısı) — PMS
    /// kendi sağlık ekranında gösterebilir. Somut CameraWorker sızdırmaz (M1).</summary>
    public IReadOnlyList<ICameraStatus> CameraStatus => _supervisor.Workers;

    /// <summary>Kamera durum görünümü. SOMUT CameraWorker'ı (public Start/DisposeAsync lifecycle
    /// metotları dahil — yanlış çağrılırsa runtime bozulur) sızdırır.</summary>
    [Obsolete("Salt-okunur durum için CameraStatus (IReadOnlyList<ICameraStatus>) kullanın; Cameras " +
              "somut CameraWorker'ı sızdırır ve gelecek majör sürümde kaldırılacaktır.")]
    public IReadOnlyList<CameraWorker> Cameras => _supervisor.Workers;

    /// <summary>CPUID/mimari özeti (AVX yoksa performans uyarısı loglanır).</summary>
    public string CpuFeatures => _supervisor.CpuFeatures;

    /// <summary>
    /// Kameranın YÜRÜRLÜKTEKİ tetik bölgesi (ROI). Tanımsızsa boş dizge döner (filtre kapalı = tüm kare).
    /// Kamera bulunamazsa <c>null</c>.
    /// </summary>
    /// <remarks>ROI biçimi: <see cref="SetRoi"/> belgelerine bakın.</remarks>
    public string? GetRoi(string cameraId)
    {
        ArgumentNullException.ThrowIfNull(cameraId);
        foreach (var w in _supervisor.Workers)
            if (string.Equals(w.CameraId, cameraId, StringComparison.OrdinalIgnoreCase))
                return w.CurrentRoi;
        return null;
    }

    /// <summary>
    /// Tetik bölgesini (ROI) ÇALIŞIRKEN değiştirir; yeniden başlatma gerekmez. Kamera bulunduysa
    /// <c>true</c> döner. Thread-safe (motor içinde volatile takas).
    /// <para><b>BİÇİM:</b> <c>"x1,y1;x2,y2;x3,y3;..."</c> — en az 3 nokta, <b>0–1000 normalize</b>
    /// koordinat (kare çözünürlüğünden BAĞIMSIZ: 0 = sol/üst kenar, 1000 = sağ/alt kenar). Örnek:
    /// <c>"100,400;900,400;900,950;100,950"</c> = karenin alt-orta bölgesi. <c>null</c> veya boş dizge
    /// filtreyi KAPATIR (tüm kare tetik bölgesi olur). Geçersiz tanım da filtreyi kapatır.</para>
    /// <para><b>NEDEN ÖNEMLİ:</b> ROI, ürünün en kritik SAHA KALİBRASYONUDUR ve varsayılanı YOKTUR.
    /// Tanımlanmazsa kameranın gördüğü her plaka bariyer kararına girer — cadde trafiği, karşı şerit,
    /// park hâlindeki araçlar dâhil. Merkezi çokgenin dışında kalan tespitler <c>Scope="gozlem"</c>
    /// olarak yayınlanır (izleme/karşılaştırma için) ve bariyer kararı üretmez.</para>
    /// <para><b>NASIL BELİRLENİR:</b> birkaç dakika çalıştırıp <c>PlateEvent.FrameBox</c> +
    /// <c>FramePath</c> ile kaydedilen karelerde plakaların NEREDE okunduğuna bakın; bariyerin
    /// açılmasını istediğiniz aracın plakasının bulunduğu bölgeyi çokgenle çevreleyin. Kamera
    /// markasından bağımsızdır — belirli bir üreticinin şerit tanımına ihtiyaç YOKTUR.</para>
    /// </summary>
    public bool SetRoi(string cameraId, string? spec)
    {
        ArgumentNullException.ThrowIfNull(cameraId);
        foreach (var w in _supervisor.Workers)
            if (string.Equals(w.CameraId, cameraId, StringComparison.OrdinalIgnoreCase))
            {
                w.ApplyRoi(spec);
                return true;
            }
        return false;
    }

    /// <param name="options">Kamera listesi + eşikler. Üretimde doğrulanmış varsayılanlar geçerlidir;
    /// çoğu senaryoda yalnız <see cref="AnprServiceOptions.Cameras"/> doldurmak yeter.</param>
    /// <param name="loggerFactory">PMS'in log altyapısı (Serilog/NLog sağlayıcısı vb.). null = sessiz.</param>
    /// <param name="extraSinks">Ek olay hedefleri (IEventSink). <b>ZERO-LOSS/KALICI teslim için burayı
    /// kullanın:</b> extraSink'ler her olay için awaited çağrılır (PlateDetected'in aksine DÜŞMEZ). Kalıcılık
    /// PMS'in sorumluluğudur (SDK in-process olay verir; sizin DB'nizi bilmez) — DB/dosyaya yazan bir
    /// IEventSink geçin (hızlı/async yazın; yavaş sink boru hattını yavaşlatır). PlateDetected ise dispatch'li
    /// KOLAYLIK yoludur (yavaş işleyicide olay düşebilir).</param>
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

        // NOT: PlateCorrections doğrulaması/uyarısı AnprCameraSupervisor.StartAsync'te yapılır —
        // panel servisi (Anpr.Service) süpervizörü DOĞRUDAN kurar, buradan geçmez; uyarı burada
        // olsaydı o yolda hiç görünmezdi.

        // M2: PlateDetected'i bounded-channel + tek drain-task üzerinden yayınla. Worker'ın sink çağrısı
        // (EnqueueEvent) anında döner (non-blocking write) → yavaş PMS işleyicisi boru hattını DURDURMAZ;
        // dispatch tek thread'de serileşir (eşzamanlı çağrı yok). Kuyruk dolarsa (256) yeni olay düşer+sayılır.
        // FullMode=Wait + TryWrite (bloklamayan): kuyruk doluysa TryWrite FALSE döner → yeni olayı say+at.
        // (Drop* modlarında TryWrite drop etse bile true dönerdi → düşüş sayılamazdı.)
        StartDispatcher();

        var sinks = new List<IEventSink> { new DelegateEventSink(EnqueueEvent) };
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

        // YENİDEN BAŞLATMA (2026-07-28 CRITICAL): StopAsync, bekleyen olayları boşaltmak için kanalı
        // KALICI olarak kapatır (Writer.TryComplete). Eskiden StartAsync bunu yeniden kurmuyordu; sonuç
        // SESSİZ ve ölümcüldü — kameralar bağlanır, kareler işlenir, /health "ok" der, EventsEmitted artar,
        // kanıt JPEG'leri yazılır ama PlateDetected BİR DAHA HİÇ tetiklenmez, yani bariyer hiç açılmaz.
        // (Tek ipucu DroppedEvents sayacıydı.) Süpervizör yeniden başlatmayı zaten destekliyor
        // (StopAsync sonunda "_cts = null; // sonraki StartAsync taze token kaynağı oluşturur").
        if (_dispatchTask.IsCompleted) StartDispatcher();

        _started = true;
        await _supervisor.StartAsync(cancellationToken);
    }

    /// <summary>Olay kuyruğunu ve tek-thread dispatcher'ı (yeniden) kurar.</summary>
    private void StartDispatcher()
    {
        // M2: bounded kanal + tek drain-task. Worker'ın sink çağrısı anında döner (non-blocking write) →
        // yavaş PMS işleyicisi boru hattını DURDURMAZ; dispatch tek thread'de serileşir. Kuyruk dolarsa
        // (256) yeni olay düşer + sayılır. FullMode=Wait + TryWrite: doluyken TryWrite FALSE döner →
        // düşüş SAYILABİLİR (Drop* modlarında true dönerdi ve düşüş görünmezdi).
        _eventChannel = Channel.CreateBounded<PlateEvent>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
        _dispatchTask = Task.Run(DispatchEventsAsync);
    }

    /// <summary>Okumayı durdurur, kuyruktaki bekleyen olayları işleyiciye boşaltır (≤5 sn), kaynakları bırakır.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started || _disposed) return;
        _started = false;
        await _supervisor.StopAsync(cancellationToken);   // worker'lar durdu → yeni olay üretilmez
        await DrainDispatcherAsync();                      // kuyruktaki son olaylar işleyiciye gitsin
    }

    // Kuyruğu kapatıp dispatcher'ın kalan olayları boşaltmasını bekler (≤5s). İdempotent (TryComplete +
    // tamamlanmış task'i beklemek anındadır) → StopAsync ve DisposeAsync'ten güvenle çağrılır.
    private async Task DrainDispatcherAsync()
    {
        _eventChannel.Writer.TryComplete();
        try { await _dispatchTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* yoksay */ }
    }

    // M2: sink→kuyruk (non-blocking). Kuyruk doluysa (işleyici çok yavaş) yeni olay düşer + sayılır+uyarılır.
    // internal: test dispatcher davranışını doğrudan doğrular (üretimde yalnız DelegateEventSink çağırır).
    internal void EnqueueEvent(PlateEvent e)
    {
        if (_eventChannel.Writer.TryWrite(e)) return;
        long n = Interlocked.Increment(ref _droppedEvents);
        if (n % 50 == 1)   // spam önle: her 50 düşüşte bir uyar
            _logger.LogWarning("PlateDetected kuyruğu dolu — işleyici çok yavaş; {N} olay düşürüldü. " +
                "İşleyici HIZLI dönmeli (uzun işi kendi kuyruğunuza atın).", n);
    }

    // Tek-thread dispatch döngüsü: olayları SERİLEŞMİŞ olarak işleyiciye verir (eşzamanlı çağrı yok).
    private async Task DispatchEventsAsync()
    {
        try
        {
            await foreach (var e in _eventChannel.Reader.ReadAllAsync())
                RaisePlateDetected(e);
        }
        catch (Exception ex) { _logger.LogError(ex, "PlateDetected dispatcher döngüsü beklenmedik hata"); }
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
        await DrainDispatcherAsync();   // hiç StartAsync edilmediyse (StopAsync erken döner) dispatcher sızmasın
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
