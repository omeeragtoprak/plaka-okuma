using System.Diagnostics;
using System.Globalization;
using System.Text;
using Anpr.Sdk.Configuration;
using Microsoft.Extensions.Logging;

namespace Anpr.Sdk.Frames;

/// <summary>
/// ffmpeg alt-süreci ile RTSP'ten kare çeken kaynak. ffmpeg MJPEG akışını stdout'a yazar;
/// burada JPEG sınırlayıcıları (SOI 0xFFD8 / EOI 0xFFD9) ayrıştırılır ve yalnızca EN GÜNCEL
/// kare tutulur. Kamera çevrimdışı olur ya da kare akışı durursa, üstel bekleme ile yeniden bağlanır.
///
/// OpenCvSharp yerine ffmpeg kullanmamızın nedeni: bu kameralar HEVC/H.265 yayınlıyor ve
/// dev makine arm64-macOS (OpenCvSharp'ın resmi native runtime'ı yok). ffmpeg her iki platformda da
/// (mac + Windows) çalışır ve HEVC çözer.
/// </summary>
public sealed class FfmpegFrameSource : IFrameSource
{
    private readonly string _cameraId;
    private readonly string _rtspUrl;
    private readonly FfmpegOptions _opts;
    private readonly double _fps;
    private readonly ILogger _logger;

    private Task? _runTask;
    private volatile Process? _activeProc;   // canlı ffmpeg süreci; Dispose/Stop bunu öldürüp asılı okumayı açar
    private byte[]? _latestFrame;
    private long _framesReceived;
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

    public byte[]? TryGetLatestFrame() => Volatile.Read(ref _latestFrame);

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

                await ReadFramesAsync(proc.StandardOutput.BaseStream, attemptCts.Token);
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
        var vf = new StringBuilder($"fps={_fps.ToString(CultureInfo.InvariantCulture)}");
        if (_opts.ScaleWidth > 0)
            vf.Append(CultureInfo.InvariantCulture, $",scale={_opts.ScaleWidth}:-2");

        var args = new List<string>
        {
            "-nostdin",
            "-loglevel", "error",
            "-rtsp_transport", _opts.RtspTransport,
        };
        if (!string.IsNullOrWhiteSpace(_opts.ExtraInputArgs))
            args.AddRange(SplitArgs(_opts.ExtraInputArgs!));

        args.AddRange(new[]
        {
            "-i", _rtspUrl,
            "-an",
            "-f", "image2pipe",
            "-vcodec", "mjpeg",
            "-q:v", _opts.JpegQuality.ToString(CultureInfo.InvariantCulture),
            "-vf", vf.ToString(),
            "pipe:1",
        });

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
        if (latest != null) OnFrame(latest);

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

    private void OnFrame(byte[] frame)
    {
        Volatile.Write(ref _latestFrame, frame);
        Volatile.Write(ref _lastFrameTicks, DateTimeOffset.UtcNow.UtcTicks);
        Interlocked.Increment(ref _framesReceived);
        _connected = true;
        _gotFrameThisAttempt = true;
    }

    private async Task WatchdogAsync(Process proc, CancellationTokenSource attemptCts)
    {
        var attemptToken = attemptCts.Token;
        try
        {
            while (!attemptToken.IsCancellationRequested)
            {
                await Task.Delay(1000, attemptToken);
                long lastTicks = Volatile.Read(ref _lastFrameTicks);
                var reference = lastTicks == 0 ? _attemptStartUtc : new DateTimeOffset(lastTicks, TimeSpan.Zero);
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
                    _logger.LogWarning("[{Cam}] ffmpeg: {Line}", _cameraId, line);
            }
        }
        catch (OperationCanceledException) { /* normal */ }
        catch { /* yoksay */ }
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
