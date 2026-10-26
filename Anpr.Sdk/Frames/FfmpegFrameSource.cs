using System.Diagnostics;
using System.Globalization;
using System.Text;
using Anpr.Sdk.Configuration;
using Microsoft.Extensions.Logging;

namespace Anpr.Sdk.Frames;

/// <summary>
/// ffmpeg alt-süreci ile RTSP'ten kare çeken kaynak. Yalnızca EN GÜNCEL kare tutulur; kamera çevrimdışı
/// olur ya da kare akışı durursa üstel bekleme ile yeniden bağlanır.
/// <para><b>İki çıkış yolu (<see cref="FfmpegOptions.OutputFormat"/>):</b></para>
/// <list type="bullet">
/// <item><b>Bmp (varsayılan, Faz 2):</b> ffmpeg kareleri sıkıştırmasız BMP/BGRA yazar; her karenin başlığı tam
/// uzunluğunu taşıdığı için <see cref="BmpFrameReader"/> kareyi tek okumada doğrudan tamponuna alır. JPEG
/// kodlama (ffmpeg) + çözme (C#) tamamen kalkar. Ölçüm (2026-09-01, 4 MP HEVC 25 fps, kare başına):
/// ffmpeg %28 → %18 tek-çekirdek; donanım çözmeyle %10.</item>
/// <item><b>Mjpeg (eski):</b> MJPEG akışı; JPEG sınırlayıcıları (SOI 0xFFD8 / EOI 0xFFD9) taranır.</item>
/// </list>
/// <para><b>Donanım çözme (<see cref="FfmpegOptions.HwAccel"/>):</b> "auto" ffmpeg'in <c>-hwaccels</c>
/// listesine bakıp işletim sistemine uygun olanı seçer (Windows: d3d11va/dxva2/qsv · macOS: videotoolbox).
/// ffmpeg, donanım çözücü akışı desteklemezse kendi içinde yazılıma düşer; buna ek olarak iki ardışık
/// karesiz denemeden sonra bu kaynak da donanım çözmeyi kapatır (emniyet kemeri).</para>
///
/// OpenCvSharp yerine ffmpeg kullanmamızın nedeni: bu kameralar HEVC/H.265 yayınlıyor ve
/// dev makine arm64-macOS (OpenCvSharp'ın resmi native runtime'ı yok). ffmpeg her iki platformda da
/// (mac + Windows) çalışır ve HEVC çözer.
/// </summary>
public sealed class FfmpegFrameSource : IFrameSource
{
    private readonly string _cameraId;
    private readonly string _rtspUrl;
    /// <summary>Kaynak URL'deki şifre — log satırlarından kaba-kuvvet temizlemek için (bkz. UrlMasking).</summary>
    private readonly string? _secret;
    private readonly FfmpegOptions _opts;
    private readonly double _fps;
    private readonly ILogger _logger;

    private Task? _runTask;
    private volatile Process? _activeProc;   // canlı ffmpeg süreci; Dispose/Stop bunu öldürüp asılı okumayı açar
    private VideoFrame? _latestFrame;
    private long _framesReceived;
    private long _sequence;
    // Donanım çözme: ilk StartFfmpeg'de çözülür (probe bir kez, süreç boyunca önbellekli).
    private string? _hwAccel;
    private bool _hwAccelResolved;
    private bool _gpuFrameDrop;              // donanım çözmede fps süzgeci ekran kartında (bkz. FfmpegOptions.GpuFrameDrop)
    private int _framelessHwAttempts;
    private long _resyncBytes;
    private volatile bool _connected;
    // Son kare zamanı UTC-ticks olarak (0 = henüz kare yok). PMS thread'i IsConnected/LastFrameUtc'yi
    // KENDİ thread'inden okur; DateTimeOffset? (>8 bayt, nullable) atomik değil → torn-read olası idi.
    // long okuma/yazma 64-bit'te atomik; Volatile ile görünürlük. (Concurrency-hijyeni, 2026-07-22).
    private long _lastFrameTicks;
    private DateTimeOffset _attemptStartUtc;
    private bool _gotFrameThisAttempt;
    private int _restartCount;

    private const int MaxBufferBytes = 8 * 1024 * 1024;

    public FfmpegFrameSource(string cameraId, string rtspUrl, FfmpegOptions opts, double fps, ILogger logger)
    {
        _cameraId = cameraId;
        _rtspUrl = rtspUrl;
        _secret = System.Text.RegularExpressions.Regex.Match(rtspUrl ?? "", @"://[^:/@\s]+:([^@\s]+)@") is { Success: true } m
            ? m.Groups[1].Value : null;
        _opts = opts;
        _fps = fps > 0 ? fps : 1;
        _logger = logger;
    }

    // "Bağlı" = süreç canlı VE kare akışı taze. Asılı ffmpeg'de (süreç var, kare yok) sağlık
    // yanlış-pozitif vermesin diye kare yaşına bağlanır (2026-07-07 canlı vakası: 3 saat bayat
    // kaynak 'connected=true' görünüyordu).
    public bool IsConnected
    {
        get
        {
            if (!_connected) return false;
            long ticks = Volatile.Read(ref _lastFrameTicks);
            if (ticks == 0) return false;
            double staleSec = Math.Max(30, _opts.NoFrameTimeoutSeconds * 3);
            return (DateTimeOffset.UtcNow.UtcTicks - ticks) / (double)TimeSpan.TicksPerSecond < staleSec;
        }
    }
    public DateTimeOffset? LastFrameUtc
    {
        get { long t = Volatile.Read(ref _lastFrameTicks); return t == 0 ? null : new DateTimeOffset(t, TimeSpan.Zero); }
    }
    public long FramesReceived => Interlocked.Read(ref _framesReceived);
    public int RestartCount => _restartCount;

    public VideoFrame? TryGetLatestFrame()
    {
        var f = Volatile.Read(ref _latestFrame);
        if (f == null) return null;
        // Alındı olarak işaretle; kaynak bu karenin tamponunu artık geri dönüştüremez.
        // false → kare tam o anda geri alındı: yokmuş gibi davran (bir sonraki yoklama yenisini görür).
        return f.TryTake() ? f : null;
    }

    // "En güncel kare" değiş-tokuşu ile almadan-bakış (TryInspectLatest) aynı kilidi tutar: bakış sürerken
    // okuyucu, bakılan karenin tamponunu bir sonraki kare için geri ALAMAZ. Kilit mikrosaniyeler sürer
    // (okuyucu: referans değiş-tokuşu; bakan: ızgara örneklemesi). TryGetLatestFrame kilitsiz kalır (Dekker protokolü).
    private readonly object _swapLock = new();

    /// <inheritdoc/>
    public long LatestSequence => Volatile.Read(ref _latestFrame)?.Sequence ?? -1;

    /// <inheritdoc/>
    public bool TryInspectLatest<T>(Func<VideoFrame, T> inspect, out T result)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        lock (_swapLock)
        {
            var f = _latestFrame;
            if (f == null || f.IsRetired) { result = default!; return false; }
            result = inspect(f);
            return true;
        }
    }

    // Geri verilen (kiralanıp işi biten, kimsenin saklamadığı) karelerin tamponları — sıradaki kareler için.
    // _swapLock altında. Sınırlı: fazlası çöp toplayıcıya bırakılır (boşta 3 tampon dönüşümde kalır).
    private readonly Stack<byte[]> _free = new();
    private const int MaxFreeBuffers = 2;

    /// <inheritdoc/>
    public VideoFrame? TryLeaseLatestFrame()
    {
        lock (_swapLock)
        {
            var f = _latestFrame;
            if (f == null || f.IsRetired) return null;
            f.Leased = true;      // değiş-tokuşta "yerine geçilen" olsa bile tamponu ALINMAZ (çalışan okuyor)
            return f;
        }
    }

    /// <inheritdoc/>
    public void ReturnFrame(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_swapLock)
        {
            if (!frame.Leased) return;                       // kiralanmamış ya da zaten geri verilmiş
            frame.Leased = false;
            if (ReferenceEquals(frame, _latestFrame)) return; // hâlâ en güncel kare: sıradaki değiş-tokuşta değerlendirilir
            // Biri sakladıysa (Keep/TryGetLatestFrame) TryRetire başarısız olur → tampon ASLA geri alınmaz.
            if (frame.IsRaw && _free.Count < MaxFreeBuffers && frame.TryRetire())
                _free.Push(frame.Data);
        }
    }

    /// <summary>
    /// Yeni kareyi yayınlar ve bir SONRAKİ okuma için yeniden kullanılabilecek tamponu döner (yoksa null → çağıran
    /// yeni tampon ayırır). Kaynak: yerine geçilen kare (kimse almadıysa/kiralamadıysa) ya da geri verilmiş kareler.
    /// </summary>
    internal byte[]? PublishFrame(VideoFrame frame, int bufferSize)
    {
        lock (_swapLock)
        {
            var replaced = _latestFrame;
            OnFrame(frame);
            byte[]? spare = replaced is { Leased: true } ? null : ChooseSpare(replaced, bufferSize);
            while (spare == null && _free.Count > 0)
            {
                var b = _free.Pop();
                if (b.Length == bufferSize) spare = b;        // boyut değiştiyse (akış yeniden başladı) at
            }
            return spare;
        }
    }

    private long _recycledBuffers;
    /// <summary>Yeniden kullanılan kare tamponu sayısı (BMP yolu). Boşta kaynak/tüketim hız farkının ölçüsü.</summary>
    public long RecycledBuffers => Interlocked.Read(ref _recycledBuffers);

    /// <summary>
    /// Yeni kare yayınlandıktan sonra, YERİNE GEÇİLEN karenin tamponu bir sonraki okuma için alınabilir mi?
    /// Yalnız hiçbir tüketicinin almadığı ve aynı boyuttaki kareler geri dönüştürülür (saf/test-edilebilir).
    /// </summary>
    internal static byte[]? ChooseSpare(VideoFrame? replaced, int expectedSize)
        => replaced != null && replaced.IsRaw
           && replaced.Data.Length == expectedSize && replaced.TryRetire()
            ? replaced.Data : null;

    /// <summary>Etkin donanım çözme yöntemi (null = yazılım). Teşhis.</summary>
    public string? ActiveHwAccel => _hwAccel;
    /// <summary>Donanım çözmede kare düşürme ekran kartında mı yapılıyor. Teşhis.</summary>
    public bool ActiveGpuFrameDrop => _hwAccel != null && _gpuFrameDrop;
    /// <summary>BMP yolunda yeniden hizalama için atlanan toplam bayt (normalde 0).</summary>
    public long ResyncBytes => Interlocked.Read(ref _resyncBytes);

    public void Start(CancellationToken cancellationToken)
    {
        _runTask = Task.Run(() => RunAsync(cancellationToken), CancellationToken.None);
    }

    /// <summary>ffmpeg ikilisinin BAŞLATILABİLİR olduğunu açılışta doğrular (H2 — fail-fast). ffmpeg
    /// yoksa/PATH'te değilse, StartFfmpeg her denemede Win32Exception atıp SESSİZCE sonsuz yeniden-bağlanır
    /// (kare gelmez, bariyer hiç açılmaz, StartAsync başarı döner). Bu probe onu açılışta NET config
    /// hatasına çevirir. Path="ffmpeg" PATH'e dayandığından File.Exists yetmez — gerçekten başlatmayı dener.</summary>
    public static void EnsureAvailable(FfmpegOptions opts)
    {
        var psi = new ProcessStartInfo
        {
            FileName = opts.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-version");

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ffmpeg başlatılamadı (yol: '{opts.Path}'): {ex.Message}. ffmpeg KURULU ve PATH'te olmalı " +
                "(ya da AnprServiceOptions.Ffmpeg.Path ile tam yolu verin). ffmpeg olmadan kameralardan kare " +
                "okunamaz — bariyer hiç açılmaz.", ex);
        }
        if (proc == null)
            throw new InvalidOperationException($"ffmpeg başlatılamadı (yol: '{opts.Path}'): süreç oluşturulamadı.");
        try
        {
            // Pipe'ları boşalt: tam ffmpeg build'inin `-version` çıktısı (çok-KB 'configuration:' satırı)
            // Windows'un ~4KB pipe tamponunu taşırsa süreç yazımda bloklar → gereksiz 5s açılış gecikmesi.
            // Asenkron drenaj EOF'a (süreç çıkışı) kadar okur; içerik kullanılmıyor, yalnız tampon açılıyor.
            _ = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(5000)) { try { proc.Kill(entireProcessTree: true); } catch { /* yoksay */ } }
        }
        finally { proc.Dispose(); }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            Process? proc = null;
            _gotFrameThisAttempt = false;
            _attemptStartUtc = DateTimeOffset.UtcNow;

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task watchdog = Task.CompletedTask;
            try
            {
                proc = StartFfmpeg();
                _activeProc = proc;
                _logger.LogInformation("[{Cam}] ffmpeg başlatıldı (pid {Pid})", _cameraId, proc.Id);

                _ = PumpStderrAsync(proc, attemptCts.Token);
                watchdog = WatchdogAsync(proc, attemptCts);

                // Kareler AYRI bir iş parçacığında BLOKLAYAN okumayla alınır (bkz. BlockingReadStream: eşzamansız
                // okuma 6 MB'lık karede kare başına ~11 ms fazladan işlemci harcıyordu). Bloklayan okuma iptali
                // göremez → iptalde (kapanış ya da bekçi) ffmpeg sonlandırılır, boru kapanır, okuma 0 döner.
                var stdout = new BlockingReadStream(proc.StandardOutput.BaseStream);
                var running = proc;
                using (attemptCts.Token.Register(() => KillOnly(running)))
                {
                    await Task.Factory.StartNew(() => ReadFramesAsync(stdout, attemptCts.Token), CancellationToken.None,
                        TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
                }
                if (ct.IsCancellationRequested) break;
                if (attemptCts.IsCancellationRequested)
                    _logger.LogWarning("[{Cam}] bekçi okuma döngüsünü sonlandırdı (asılı akış)", _cameraId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Bekçi iptali (asılı ffmpeg): normal kopma gibi ele al -> yeniden bağlan.
                _logger.LogWarning("[{Cam}] bekçi okuma döngüsünü sonlandırdı (asılı akış)", _cameraId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[{Cam}] kare okuma hatası: {Msg}", _cameraId, ex.Message);
            }
            finally
            {
                // Watchdog HER ZAMAN iptal edilip beklensin; CTS dispose edilmeden önce token'a
                // dokunan görev kalmasın (aksi halde disposed CTS -> gözlenmeyen istisna).
                attemptCts.Cancel();
                try { await watchdog.ConfigureAwait(false); } catch { /* yoksay */ }
                _activeProc = null;
                TryKill(proc);
            }

            _connected = false;
            if (ct.IsCancellationRequested) break;

            // DONANIM ÇÖZME EMNİYET KEMERİ (üç basamak): ekran kartında kare düşürme → düz donanım çözme → yazılım.
            // Her basamak, iki ardışık deneme hiç kare vermediyse bir alta iner. (Tek deneme yetmez: karesiz
            // deneme çoğu zaman ağ/kamera kaynaklıdır, donanım çözmenin suçu değil.)
            if (_hwAccel != null)
            {
                _framelessHwAttempts = _gotFrameThisAttempt ? 0 : _framelessHwAttempts + 1;
                if (_framelessHwAttempts >= 2)
                {
                    if (_gpuFrameDrop)
                    {
                        _logger.LogWarning("[{Cam}] donanım çözme ({Hw}) + ekran kartında kare düşürme ile iki denemede kare gelmedi → düz donanım çözme deneniyor",
                            _cameraId, _hwAccel);
                        _gpuFrameDrop = false;
                    }
                    else
                    {
                        _logger.LogWarning("[{Cam}] donanım çözme ({Hw}) ile iki denemede kare gelmedi → yazılım çözmeye dönülüyor",
                            _cameraId, _hwAccel);
                        _hwAccel = null;
                    }
                    _framelessHwAttempts = 0;
                }
            }

            // Backoff yalnızca ANLAMLI bir bağlantıdan sonra sıfırlanır (tek kare alıp sürekli
            // kopan kamera için sıkı yeniden bağlanma döngüsünü önler).
            bool meaningfulRun = _gotFrameThisAttempt &&
                                 (DateTimeOffset.UtcNow - _attemptStartUtc) > TimeSpan.FromSeconds(5);
            attempt = meaningfulRun ? 0 : attempt + 1;
            int delay = Math.Clamp((int)Math.Pow(2, Math.Min(attempt, 5)), 1, Math.Max(1, _opts.MaxBackoffSeconds));
            // SOĞUMA MODU (saha vakası 2026-07-13): uzun kamera kesintisi/yeniden başlatması sırasında
            // 30 sn'lik denemeler kamerada RTSP oturum artığı biriktiriyor (Hikvision eşzamanlı oturum
            // sınırı) ve kamera açıldıktan sonra bile "ffmpeg başladı ama kare yok" kısır döngüsü oluşuyor
            // (ffprobe aynı anda sorunsuzdu; temiz servis restart'ı anında düzeltti). 10+ ardışık
            // başarısız denemeden sonra bekleme 120 sn'ye çıkar — kameranın oturumları temizlemesine izin verir.
            if (attempt >= 10) delay = Math.Max(delay, 120);
            _restartCount++;
            _logger.LogWarning("[{Cam}] bağlantı koptu; {Delay}s sonra yeniden bağlanılacak (deneme #{Attempt})",
                _cameraId, delay, attempt);

            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
            catch (OperationCanceledException) { break; }
        }
        _connected = false;
        _logger.LogInformation("[{Cam}] kare kaynağı durduruldu", _cameraId);
    }

    private Process StartFfmpeg()
    {
        if (!_hwAccelResolved)
        {
            _hwAccelResolved = true;
            _hwAccel = ResolveHwAccel(_opts.HwAccel, ProbeHwAccels(_opts.Path),
                                      OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
            _gpuFrameDrop = _opts.GpuFrameDrop && HwOutputFormat(_hwAccel) != null;
            _logger.LogInformation("[{Cam}] kare yolu: çıkış {Fmt}, donanım çözme {Hw}{Gpu}, çözücü iş parçacığı {Thr}",
                _cameraId, _opts.OutputFormat, _hwAccel ?? "yok (yazılım)",
                _hwAccel != null && _gpuFrameDrop ? " (kare düşürme ekran kartında)" : "",
                ResolveDecoderThreads(_opts.DecoderThreads, Environment.ProcessorCount, _hwAccel != null) is var thr and > 0 ? thr.ToString(CultureInfo.InvariantCulture) : "ffmpeg varsayılanı");
        }
        // Donanım basamağı bir alta inebilir (bkz. RunAsync) → iş parçacığı her denemede güncel duruma göre seçilir.
        var args = BuildArgs(_opts, _rtspUrl, _fps, _hwAccel, _gpuFrameDrop,
                             ResolveDecoderThreads(_opts.DecoderThreads, Environment.ProcessorCount, _hwAccel != null));

        var psi = new ProcessStartInfo
        {
            FileName = _opts.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        return proc;
    }

    private async Task ReadFramesAsync(Stream stdout, CancellationToken ct)
    {
        if (_opts.OutputFormat == FfmpegOutputFormat.Bmp)
        {
            await ReadBmpFramesAsync(stdout, ct);
            return;
        }
        if (_opts.OutputFormat == FfmpegOutputFormat.Y4m)
        {
            await ReadY4mFramesAsync(stdout, ct);
            return;
        }
        var read = new byte[64 * 1024];
        var acc = new byte[256 * 1024];
        int accLen = 0;

        while (!ct.IsCancellationRequested)
        {
            int n = await stdout.ReadAsync(read.AsMemory(0, read.Length), ct);
            if (n <= 0) break; // EOF -> ffmpeg çıktı

            if (accLen + n > acc.Length)
                Array.Resize(ref acc, Math.Max(acc.Length * 2, accLen + n));
            Buffer.BlockCopy(read, 0, acc, accLen, n);
            accLen += n;

            accLen = ExtractFrames(acc, accLen);

            if (accLen > MaxBufferBytes)
            {
                _logger.LogWarning("[{Cam}] tampon taştı, sıfırlanıyor", _cameraId);
                accLen = 0;
            }
        }
    }

    private async Task ReadBmpFramesAsync(Stream stdout, CancellationToken ct)
    {
        var header = new byte[14];
        byte[]? spare = null;
        while (!ct.IsCancellationRequested)
        {
            var bmp = await BmpFrameReader.ReadNextAsync(stdout, header, ct,
                skipped =>
                {
                    Interlocked.Add(ref _resyncBytes, skipped);
                    _logger.LogWarning("[{Cam}] BMP akışı hizasız: {N} bayt atlandı", _cameraId, skipped);
                }, reuse: spare);
            if (bmp == null) break; // EOF -> ffmpeg çıktı
            bool reused = ReferenceEquals(bmp, spare);
            spare = null;

            if (VideoFrame.TryFromBmp(bmp, DateTimeOffset.UtcNow, Interlocked.Increment(ref _sequence), out var frame, out var why))
            {
                if (reused) Interlocked.Increment(ref _recycledBuffers);
                // Yerine geçilen (ya da geri verilen) karenin tamponu bir sonraki kareye kalır (16 MB tahsis + Gen2 GC yok).
                spare = PublishFrame(frame!, bmp.Length);
            }
            else
                _logger.LogWarning("[{Cam}] BMP kare atıldı: {Why}", _cameraId, why);
        }
    }

    private async Task ReadY4mFramesAsync(Stream stdout, CancellationToken ct)
    {
        var header = await Y4mFrameReader.ReadHeaderAsync(stdout, ct);
        if (header is not { } h) return; // EOF -> ffmpeg kare vermeden çıktı
        var marker = new byte[8];
        byte[]? spare = null;
        while (!ct.IsCancellationRequested)
        {
            var data = await Y4mFrameReader.ReadFrameAsync(stdout, h, marker, ct, reuse: spare);
            if (data == null) break; // EOF -> ffmpeg çıktı
            if (ReferenceEquals(data, spare)) Interlocked.Increment(ref _recycledBuffers);
            spare = null;

            var frame = VideoFrame.FromYuv420(data, h.Width, h.Height, h.FullRange, DateTimeOffset.UtcNow,
                                              Interlocked.Increment(ref _sequence));
            // Yerine geçilen (ya da geri verilen) karenin tamponu bir sonraki kareye kalır (6 MB tahsis + Gen2 GC yok).
            spare = PublishFrame(frame, data.Length);
        }
    }

    // Tamamlanmış tüm JPEG'leri ayıklar, en günceli yayınlar; kalan (yarım) baytları başa taşır.
    private int ExtractFrames(byte[] acc, int accLen)
    {
        byte[]? latest = null;
        int i = 0;
        int retainStart;

        while (true)
        {
            int soi = FindMarker(acc, i, accLen, 0xD8);
            if (soi < 0)
            {
                // SOI yok: olası bölünmüş 0xFF dışındaki her şeyi at
                retainStart = (accLen > 0 && acc[accLen - 1] == 0xFF) ? accLen - 1 : accLen;
                goto compact;
            }

            int eoi = FindMarker(acc, soi + 2, accLen, 0xD9);
            if (eoi < 0)
            {
                retainStart = soi; // yarım kare; SOI'dan itibaren sakla
                goto compact;
            }

            int end = eoi + 2;
            latest = new byte[end - soi];
            Buffer.BlockCopy(acc, soi, latest, 0, end - soi);
            i = end;
        }

    compact:
        if (latest != null) OnFrame(VideoFrame.FromJpeg(latest, DateTimeOffset.UtcNow, Interlocked.Increment(ref _sequence)));

        int remaining = accLen - retainStart;
        if (remaining > 0 && retainStart > 0)
            Buffer.BlockCopy(acc, retainStart, acc, 0, remaining);
        return Math.Max(0, remaining);
    }

    private static int FindMarker(byte[] buf, int from, int len, byte second)
    {
        for (int k = Math.Max(0, from); k < len - 1; k++)
            if (buf[k] == 0xFF && buf[k + 1] == second)
                return k;
        return -1;
    }

    private void OnFrame(VideoFrame frame)
    {
        Volatile.Write(ref _latestFrame, frame);
        Volatile.Write(ref _lastFrameTicks, frame.CapturedUtc.UtcTicks);
        Interlocked.Increment(ref _framesReceived);
        _connected = true;
        _gotFrameThisAttempt = true;
    }

    /// <summary>
    /// Bekçinin "kare gelmedi" sayacının başlangıç noktası. Son kare BU denemeden sonra geldiyse onu,
    /// gelmediyse denemenin başlangıcını kullanır — yani her yeni ffmpeg denemesine tam
    /// NoFrameTimeoutSeconds kadar RTSP el-sıkışma süresi tanınır.
    /// <para>REGRESYON KORUMASI (2026-07-27, canlı ağ kesintisinde yakalandı): eskiden yalnız
    /// <c>lastTicks == 0</c> (hiç kare gelmemiş) durumunda deneme başlangıcı kullanılıyordu. Bir kez kare
    /// alındıktan sonra kaynak koparsa referans ESKİ kare zamanında kalıyor, yeni ffmpeg süreci
    /// başlar başlamaz "12s kare gelmedi" ile öldürülüyordu (el sıkışmaya ~1sn bile kalmadan) —
    /// yani GEÇİCİ ağ kesintisi KALICI yeniden-bağlanamamaya dönüşüyordu (bariyer kör kalır).</para>
    /// </summary>
    internal static DateTimeOffset StaleReference(long lastFrameTicks, DateTimeOffset attemptStartUtc)
        => lastFrameTicks > attemptStartUtc.UtcTicks
            ? new DateTimeOffset(lastFrameTicks, TimeSpan.Zero)   // bu denemede kare aktı → ondan say
            : attemptStartUtc;                                    // bu denemede henüz kare yok → el sıkışma süresi tanı

    private async Task WatchdogAsync(Process proc, CancellationTokenSource attemptCts)
    {
        var attemptToken = attemptCts.Token;
        try
        {
            while (!attemptToken.IsCancellationRequested)
            {
                await Task.Delay(1000, attemptToken);
                long lastTicks = Volatile.Read(ref _lastFrameTicks);
                var reference = StaleReference(lastTicks, _attemptStartUtc);
                if ((DateTimeOffset.UtcNow - reference).TotalSeconds > _opts.NoFrameTimeoutSeconds)
                {
                    _logger.LogWarning("[{Cam}] {Sec}s boyunca kare gelmedi; ffmpeg yeniden başlatılıyor",
                        _cameraId, _opts.NoFrameTimeoutSeconds);
                    TryKill(proc);
                    // KRİTİK: kill asılı boruyu açmayabilir (2026-07-07 vakası: okuma sonsuz bloklu
                    // kaldı, bekçi dönmüştü, kaynak 'bağlı' görünürken 3 saat kare gelmedi).
                    // Okumayı token üzerinden GARANTİLİ sonlandır -> finally -> restart döngüsü işler.
                    try { attemptCts.Cancel(); } catch (ObjectDisposedException) { /* yarış: attempt bitti */ }
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    private async Task PumpStderrAsync(Process proc, CancellationToken ct)
    {
        try
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync(ct)) != null)
            {
                // ffmpeg -loglevel error ile çalışıyor (StartFfmpeg) → stderr yalnız GERÇEK hataları
                // içerir (bağlantı/auth/timeout/codec). Warning'de yayınla ki kameranın çalışmama
                // SEBEBİ üretimde (Information seviyesi) görünür olsun (H3 — saha teşhisi).
                if (!string.IsNullOrWhiteSpace(line))
                {
                    // ffmpeg hata satırında TAM RTSP URL'sini basar → kamera admin şifresi düz metin
                    // olarak dosya loguna düşerdi (2026-07-28: tek günde 20 satır). İki katmanlı maskele.
                    string safe = Anpr.Sdk.Internal.UrlMasking.MaskKnownSecret(
                        Anpr.Sdk.Internal.UrlMasking.MaskCredentials(line), _secret);
                    _logger.LogWarning("[{Cam}] ffmpeg: {Line}", _cameraId, safe);
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
        catch { /* yoksay */ }
    }

    /// <summary>ffmpeg'i yalnız sonlandırır (Dispose ETMEZ): iptal geri çağrısında okuma iş parçacığı borunun öbür
    /// ucunda blokluyken çağrılır; süreç nesnesi denemenin sonunda <see cref="TryKill"/> ile kapatılır.</summary>
    private static void KillOnly(Process? proc)
    {
        try { if (proc != null && !proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch { /* yoksay: süreç zaten çıkmış ya da kapatılmış */ }
    }

    private void TryKill(Process? proc)
    {
        if (proc == null) return;
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch { /* yoksay */ }
        finally { proc.Dispose(); }
    }

    /// <summary>
    /// ffmpeg argüman listesi (saf/test-edilebilir). Sıra önemlidir: giriş seçenekleri (<c>-rtsp_transport</c>,
    /// <c>-threads</c>, <c>-hwaccel</c>, ekstra) <c>-i</c>'den ÖNCE; çıkış seçenekleri sonra.
    /// <para><paramref name="gpuFrameDrop"/>: donanım çözmede kareler ekran kartında bırakılır, <c>fps</c> süzgeci
    /// orada çalışır ve yalnız geçen kareler indirilir (<c>hwdownload,format=nv12</c>).</para>
    /// <para><paramref name="decoderThreads"/> &gt; 0 ise çözücü iş parçacığı sayısı (<c>-threads</c>).</para>
    /// </summary>
    internal static List<string> BuildArgs(FfmpegOptions o, string rtspUrl, double fps, string? hwAccel,
                                           bool gpuFrameDrop = false, int decoderThreads = 0)
    {
        string? hwOut = !string.IsNullOrWhiteSpace(hwAccel) && gpuFrameDrop ? HwOutputFormat(hwAccel) : null;

        var vf = new StringBuilder($"fps={fps.ToString(CultureInfo.InvariantCulture)}");
        if (hwOut != null)
            vf.Append(",hwdownload,format=nv12");   // fps ekran kartında uygulandı; yalnız geçen kareler indirilir
        if (o.ScaleWidth > 0)
            vf.Append(CultureInfo.InvariantCulture, $",scale={o.ScaleWidth}:-2");

        var args = new List<string> { "-nostdin", "-loglevel", "error" };
        // -rtsp_transport yalnız RTSP girişinde geçerlidir; dosya/HTTP girişinde ffmpeg "seçenek bulunamadı" der.
        if (rtspUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
            rtspUrl.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase))
            args.AddRange(new[] { "-rtsp_transport", o.RtspTransport });
        if (decoderThreads > 0)
            args.AddRange(new[] { "-threads", decoderThreads.ToString(CultureInfo.InvariantCulture) });
        if (!string.IsNullOrWhiteSpace(hwAccel))
        {
            args.AddRange(new[] { "-hwaccel", hwAccel! });
            if (hwOut != null) args.AddRange(new[] { "-hwaccel_output_format", hwOut });
        }
        if (!string.IsNullOrWhiteSpace(o.ExtraInputArgs))
            args.AddRange(SplitArgs(o.ExtraInputArgs!));

        args.AddRange(new[] { "-i", rtspUrl, "-an" });
        switch (o.OutputFormat)
        {
            case FfmpegOutputFormat.Mjpeg:
                args.AddRange(new[] { "-f", "image2pipe", "-vcodec", "mjpeg", "-q:v", o.JpegQuality.ToString(CultureInfo.InvariantCulture) });
                break;
            case FfmpegOutputFormat.Y4m:
                // Y4M: çözücünün kendi biçimi (YUV 4:2:0) — renk dönüşümü yok; başlık kare boyutunu taşır.
                args.AddRange(new[] { "-f", "yuv4mpegpipe", "-pix_fmt", "yuv420p" });
                break;
            default:
                // BMP/BGRA: sıkıştırma yok, kanal düzeni SkiaSharp Bgra8888 ile bire bir (ffmpeg yukarıdan-aşağı yazar).
                args.AddRange(new[] { "-f", "image2pipe", "-vcodec", "bmp", "-pix_fmt", "bgra" });
                break;
        }
        args.AddRange(new[] { "-vf", vf.ToString(), "pipe:1" });
        return args;
    }

    /// <summary>
    /// Donanım çözme yönteminin "kare ekran kartında kalsın" biçim adı (<c>-hwaccel_output_format</c>);
    /// bilinmeyen yöntemde null (o zaman kareler ffmpeg'in varsayılanıyla sistem belleğine iner).
    /// </summary>
    internal static string? HwOutputFormat(string? hwAccel) => (hwAccel ?? "").Trim().ToLowerInvariant() switch
    {
        "videotoolbox" => "videotoolbox_vld",
        "d3d11va" => "d3d11",
        "dxva2" => "dxva2_vld",
        "qsv" => "qsv",
        "vaapi" => "vaapi",
        "cuda" => "cuda",
        _ => null,
    };

    /// <summary>
    /// Çözücü iş parçacığı sayısı: ayar &gt; 0 ise o; -1 ise 0 (= ffmpeg'e bırak); 0 (otomatik) ise donanım çözmede
    /// <b>1</b>, yazılım çözmede en çok 2. Saf/test-edilebilir.
    /// <para>Donanım çözmede 1 (2026-10-20, M4, 4 MP HEVC, kare düşürme ekran kartında): ffmpeg belleği 75 → 60 MB;
    /// işlemci (40 sn'de 2,1–2,9 ↔ 2,1–2,7 sn) ve yetişme payı (120 sn video 15,3 sn'de) değişmedi — çözme ekran
    /// kartında yapılıyor. Yazılım çözmede 4 MP 25 fps'i zayıf işlemcide tek iş parçacığı yetiştiremeyebilir.</para>
    /// </summary>
    internal static int ResolveDecoderThreads(int setting, int processorCount, bool hardwareDecode = false)
        => setting > 0 ? setting : setting < 0 ? 0 : hardwareDecode ? 1 : Math.Clamp(processorCount, 1, 2);

    /// <summary>
    /// Ayarı ("auto" / "none" / açık ad) ffmpeg'in desteklediği yöntemlerle kesiştirip kullanılacak
    /// <c>-hwaccel</c> değerini seçer; null = yazılım çözme. Saf/test-edilebilir.
    /// <para>"auto": Windows'ta d3d11va &gt; dxva2 &gt; qsv, macOS'ta videotoolbox; Linux'ta hiçbiri (vaapi
    /// aygıt yolu ister — açıkça yazılmalı). Probe boş döndüyse (ffmpeg -hwaccels okunamadı) "auto" yazılıma
    /// düşer; AÇIK ad ise probe boşken güvenilir (kullanıcı ne yaptığını bilir), probe doluyken listede olmalıdır.</para>
    /// </summary>
    internal static string? ResolveHwAccel(string? setting, IReadOnlyCollection<string> available, bool isWindows, bool isMacOS)
    {
        string v = (setting ?? "").Trim().ToLowerInvariant();
        if (v is "" or "none" or "off" or "false" or "0") return null;
        if (v == "auto")
        {
            string[] prefs = isWindows ? new[] { "d3d11va", "dxva2", "qsv" }
                           : isMacOS  ? new[] { "videotoolbox" }
                           : Array.Empty<string>();
            foreach (var p in prefs) if (available.Contains(p)) return p;
            return null;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^[a-z0-9_]+$")) return null;
        return available.Count == 0 || available.Contains(v) ? v : null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyCollection<string>> s_hwProbe = new();

    /// <summary>
    /// <c>ffmpeg -hwaccels</c> çıktısını okur (süreç ömrü boyunca yol başına bir kez). Başarısızlıkta boş küme —
    /// "auto" o zaman yazılıma düşer, hiçbir şey kırılmaz.
    /// </summary>
    internal static IReadOnlyCollection<string> ProbeHwAccels(string ffmpegPath)
        => s_hwProbe.GetOrAdd(ffmpegPath ?? "ffmpeg", path =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path, RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-hwaccels");
                using var proc = Process.Start(psi);
                if (proc == null) return Array.Empty<string>();
                var outTask = proc.StandardOutput.ReadToEndAsync();
                _ = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(5000)) { try { proc.Kill(entireProcessTree: true); } catch { /* yoksay */ } return Array.Empty<string>(); }
                return ParseHwAccels(outTask.Result);
            }
            catch { return Array.Empty<string>(); }
        });

    internal static IReadOnlyCollection<string> ParseHwAccels(string output)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.EndsWith(':')) continue;   // "Hardware acceleration methods:" başlığı
            if (System.Text.RegularExpressions.Regex.IsMatch(line, "^[a-z0-9_]+$")) set.Add(line.ToLowerInvariant());
        }
        return set;
    }

    private static IEnumerable<string> SplitArgs(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async ValueTask DisposeAsync()
    {
        // KRİTİK (öksüz ffmpeg): iptal token'ı asılı stdout okumasını her platformda kesmeyebilir
        // (2026-07-07/07-21 saha vakası). Aktif süreci DOĞRUDAN öldür -> boru kapanır -> ReadFramesAsync
        // döner -> RunAsync finally'si çalışır -> _runTask biter. Aksi halde ffmpeg öksüz kalıp Hikvision
        // eşzamanlı-oturum sınırını tüketir (tekrarlayan "ffmpeg başladı ama kare yok" kısır döngüsü).
        var p = _activeProc;
        if (p != null)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* yoksay */ }
        }
        if (_runTask != null)
        {
            try { await _runTask.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch { /* yoksay */ }
        }
    }
}
