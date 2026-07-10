using Anpr.Core;
using Anpr.Core.Confirmation;
using Anpr.Service.Configuration;
using Anpr.Service.Events;
using Anpr.Service.Frames;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Anpr.Service.Workers;

/// <summary>
/// Tek bir kamera için okuma döngüsü: en güncel kareyi al -> tespit+oku -> TR doğrula
/// -> çok kareli onay (PlateConfirmer) -> geçerli, onaylı ve YENİ ise olay yayınla.
/// Her worker kendi AnprEngine, frame source ve confirmer'ına sahiptir.
/// </summary>
public sealed class CameraWorker : IAsyncDisposable
{
    private readonly CameraOptions _camera;
    private readonly AnprServiceOptions _options;
    private readonly IFrameSource _frameSource;
    private readonly AnprEngine _engine;
    private readonly IReadOnlyList<IEventSink> _sinks;
    private readonly ILogger _logger;
    private readonly PlateConfirmer _confirmer;
    private readonly PlateConfirmer _obsConfirmer; // ROI dışı "gözlem" okumaları (bariyer kararına girmez)
    private readonly List<Task> _pendingPublishes = new();

    // Çok-kareli füzyon: iz başına en iyi kırpıntılar (yalnız bölge-içi; düşük güvenli emitte füzyon denenir).
    private sealed class CropBucket
    {
        public readonly HashSet<string> Variants = new();
        public readonly List<(SKBitmap Bmp, float Conf)> Crops = new();
        public DateTimeOffset Last;
        public void DisposeAll() { foreach (var (b, _) in Crops) b.Dispose(); Crops.Clear(); }
    }
    private readonly List<CropBucket> _cropBuckets = new();

    // Emit-sonrası varyant bastırma: kapsam başına son emitler (compact + aday kümesi + zaman).
    private sealed record RecentEmit(DateTimeOffset At, string Compact, HashSet<string> Candidates, string Scope, float OcrConf);
    private readonly List<RecentEmit> _recentEmits = new();

    private CancellationToken _ct;
    private Task? _loopTask;

    public string CameraId => _camera.Id;
    public string CameraName => _camera.Name;
    public bool IsConnected => _frameSource.IsConnected;
    public DateTimeOffset? LastFrameUtc => _frameSource.LastFrameUtc;
    public long FramesReceived => _frameSource.FramesReceived;
    public int RestartCount => _frameSource.RestartCount;
    public long FramesProcessed { get; private set; }
    public long EventsEmitted { get; private set; }

    // Panel: son işlenen kare (ffmpeg'den gelen JPEG bytes — kopyasız) + son tespit kutuları.
    private volatile byte[]? _lastFrameJpeg;
    private volatile IReadOnlyList<PlateBox>? _lastBoxes;
    public byte[]? LastFrameJpeg => _lastFrameJpeg;
    /// <summary>Kaynağın EN SON karesi (ffmpeg varış hızında; işleme hızından bağımsız) — canlı MJPEG için.</summary>
    public byte[]? PeekSourceFrame() => _frameSource.TryGetLatestFrame();
    public IReadOnlyList<PlateBox> LastBoxes => _lastBoxes ?? Array.Empty<PlateBox>();
    public string CurrentRoi => _engine.RoiSpec;
    public void ApplyRoi(string? spec) => _engine.SetRoi(spec);
    public DateTimeOffset? LastEventUtc { get; private set; }
    public string? LastPlate { get; private set; }

    public CameraWorker(
        CameraOptions camera,
        AnprServiceOptions options,
        IFrameSource frameSource,
        AnprEngine engine,
        IReadOnlyList<IEventSink> sinks,
        ILogger logger)
    {
        _camera = camera;
        _options = options;
        _frameSource = frameSource;
        _engine = engine;
        _sinks = sinks;
        _logger = logger;
        PlateConfirmerOptions MakeOpts() => new()
        {
            DedupWindow = TimeSpan.FromSeconds(Math.Max(1, options.DedupWindowSeconds)),
            SettleWindow = TimeSpan.FromSeconds(Math.Max(0, options.SettleWindowSeconds)),
            MinConfirmations = Math.Max(1, options.MinConfirmations),
            CharacterVoting = options.CharacterVoting,
            ConstrainedDecoding = options.ConstrainedDecoding,
            MergeAcrossProvince = options.MergeAcrossProvince,
            FuzzyMergeDistance = options.FuzzyMergeDistance,
            EarlyEmitMinOcr = options.EarlyEmitMinOcr,
            SpatialMergeIou = options.SpatialMergeIou,
            SkipCharLevel = options.SkipCharLevel,
            CandidateCount = options.CandidateCount,
        };
        _confirmer = new PlateConfirmer(MakeOpts());
        _obsConfirmer = new PlateConfirmer(MakeOpts());
    }

    public void Start(CancellationToken cancellationToken)
    {
        _ct = cancellationToken;
        _frameSource.Start(cancellationToken);
        _loopTask = Task.Run(() => LoopAsync(cancellationToken), CancellationToken.None);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        double activeFps = _options.Fps > 0 ? _options.Fps : 1;
        double idleFps = _options.IdleFps > 0 ? _options.IdleFps : activeFps;
        var activeHold = TimeSpan.FromSeconds(Math.Max(0, _options.ActiveHoldSeconds));
        DateTimeOffset activeUntil = DateTimeOffset.MinValue;

        _logger.LogInformation("[{Cam}] okuma döngüsü başladı (aktif {Fps} / boşta {Idle} fps, onay={Min}, oylama={Vote})",
            _camera.Id, activeFps, idleFps, _options.MinConfirmations, _options.CharacterVoting);

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            var frame = _frameSource.TryGetLatestFrame();
            if (frame != null)
            {
                try
                {
                    if (ObserveFrame(frame, now)) activeUntil = now + activeHold; // tespit -> aktif moda geç
                }
                catch (Exception ex) { _logger.LogWarning("[{Cam}] kare işleme hatası: {Msg}", _camera.Id, ex.Message); }
            }

            foreach (var best in _confirmer.DrainReady(now))
                Emit(best, now, scope: "roi");
            foreach (var best in _obsConfirmer.DrainReady(now))
                Emit(best, now, scope: "gozlem");

            ReapPublishes();
            CleanCropBuckets(now);

            // Uyarlanabilir hız: araç varken aktif, yoksa boşta fps.
            double fps = now < activeUntil ? activeFps : idleFps;
            try { await Task.Delay(TimeSpan.FromSeconds(1.0 / fps), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // Bölge-içi tespit olup olmadığını döner (aktif moda geçiş için — ROI dışı cadde trafiği
    // aktif fps'i tetiklememeli, yoksa zayıf CPU bütün gün yüksek hızda döner).
    private bool ObserveFrame(byte[] frame, DateTimeOffset now)
    {
        FramesProcessed++;

        IReadOnlyList<PlateReading> readings;
        if (_options.MultiFrameFusion || _options.EvidenceCrops)
        {
            var withCrops = _engine.RecognizeWithCrops(frame);
            var list = new List<PlateReading>(withCrops.Count);
            foreach (var (r, crop) in withCrops)
            {
                list.Add(r);
                // Geçerli okumaların kırpıntıları saklanır (füzyon + kanıt görüntüsü için; sahiplenilir).
                if (r.IsValidTr && crop != null) StoreCrop(r, crop, now);
                else crop?.Dispose();
            }
            readings = list;
        }
        else
        {
            readings = _engine.Recognize(frame);
        }

        _lastFrameJpeg = frame;
        _lastBoxes = readings.Select(r => r.Box).ToArray();

        bool zoneDetection = false;
        foreach (var r in readings)
        {
            if (r.InZone) zoneDetection = true;
            if (!r.IsValidTr) continue;                          // çöp/geçersiz okumaları gönderme
            if (r.Confidence < _options.EmitMinConfidence) continue;
            if (r.OcrConfidence < _options.EmitMinOcrConfidence) continue; // belirsiz/kötü okuma -> ele
            (r.InZone ? _confirmer : _obsConfirmer).Observe(r, now);
        }
        return zoneDetection;
    }

    // ---- Çok-kareli füzyon yardımcıları ----

    private void StoreCrop(PlateReading r, SKBitmap crop, DateTimeOffset now)
    {
        string compact = Compact(r.Text);
        var bucket = FindBucket(compact);
        if (bucket == null)
        {
            bucket = new CropBucket();
            _cropBuckets.Add(bucket);
        }
        bucket.Variants.Add(compact);
        bucket.Last = now;

        bucket.Crops.Add((crop, r.OcrConfidence));
        if (bucket.Crops.Count > Math.Max(2, _options.FusionMaxFrames))
        {
            // En düşük güvenliyi at (füzyona en iyi kareler girsin).
            int worst = 0;
            for (int i = 1; i < bucket.Crops.Count; i++)
                if (bucket.Crops[i].Conf < bucket.Crops[worst].Conf) worst = i;
            bucket.Crops[worst].Bmp.Dispose();
            bucket.Crops.RemoveAt(worst);
        }
    }

    private CropBucket? FindBucket(string compact)
    {
        foreach (var b in _cropBuckets)
            foreach (var v in b.Variants)
                if (Levenshtein(compact, v) <= Math.Max(1, _options.FuzzyMergeDistance)) return b;
        return null;
    }

    // Düşük güvenli uzlaşı emitinde füzyon dener; daha güvenli GEÇERLİ sonuç çıkarsa onu döner.
    private PlateReading? TryFuse(PlateReading best)
    {
        var bucket = FindBucket(Compact(best.Text));
        if (bucket == null || bucket.Crops.Count < 2) return null;

        try
        {
            var fused = _engine.RecognizeFused(bucket.Crops.Select(c => c.Bmp).ToList());
            if (fused == null || !fused.IsValidTr) return null;
            if (fused.OcrConfidence <= best.OcrConfidence) return null;

            _logger.LogInformation("[{Cam}] füzyon: '{Old}' (ocr={OldC:P0}) -> '{New}' (ocr={NewC:P0}, {N} kare)",
                _camera.Id, best.Text, best.OcrConfidence, fused.Text, fused.OcrConfidence, bucket.Crops.Count);

            return best with
            {
                Text = fused.Text,
                RawText = fused.RawText,
                OcrConfidence = fused.OcrConfidence,
                Confidence = best.DetectorConfidence * fused.OcrConfidence,
                IsValidTr = true,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[{Cam}] füzyon hatası: {Msg}", _camera.Id, ex.Message);
            return null;
        }
        finally
        {
            bucket.DisposeAll();
            _cropBuckets.Remove(bucket);
        }
    }

    // İzin en yüksek güvenli kırpıntısını evidence/ altına JPEG kaydeder; göreli yolu döner.
    private int _evidenceCount = -1;
    private string? SaveEvidence(PlateReading best, DateTimeOffset now)
    {
        try
        {
            var bucket = FindBucket(Compact(best.Text));
            if (bucket == null || bucket.Crops.Count == 0) return null;

            var top = bucket.Crops[0];
            for (int i = 1; i < bucket.Crops.Count; i++)
                if (bucket.Crops[i].Conf > top.Conf) top = bucket.Crops[i];

            string dir = Path.Combine(AppContext.BaseDirectory, "evidence");
            Directory.CreateDirectory(dir);
            string name = $"{now.ToLocalTime():yyyyMMdd-HHmmss}_{_camera.Id}_{Compact(best.Text)}.jpg";

            using var img = SKImage.FromBitmap(top.Bmp);
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, 85);
            File.WriteAllBytes(Path.Combine(dir, name), data.ToArray());

            // Disk sınırı: her 100 kayıtta bir — (a) KVKK saklama süresi dolanlar, (b) adet fazlası budanır.
            if (_evidenceCount < 0 || ++_evidenceCount % 100 == 0)
            {
                var files = new DirectoryInfo(dir).GetFiles("*.jpg");
                if (_options.EvidenceRetentionDays > 0)
                {
                    var cutoff = DateTime.UtcNow.AddDays(-_options.EvidenceRetentionDays);
                    foreach (var f in files.Where(f => f.LastWriteTimeUtc < cutoff))
                        try { f.Delete(); _logger.LogInformation("kanıt saklama süresi doldu, silindi: {File}", f.Name); } catch { /* yoksay */ }
                    files = new DirectoryInfo(dir).GetFiles("*.jpg");
                }
                _evidenceCount = files.Length;
                int excess = files.Length - Math.Max(100, _options.EvidenceMaxFiles);
                if (excess > 0)
                    foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc).Take(excess))
                        try { f.Delete(); } catch { /* yoksay */ }
            }

            return "evidence/" + name;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[{Cam}] kanıt kaydı hatası: {Msg}", _camera.Id, ex.Message);
            return null;
        }
    }

    private void CleanCropBuckets(DateTimeOffset now)
    {
        if (_cropBuckets.Count == 0) return;
        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.DedupWindowSeconds));
        for (int i = _cropBuckets.Count - 1; i >= 0; i--)
        {
            if (now - _cropBuckets[i].Last > ttl)
            {
                _cropBuckets[i].DisposeAll();
                _cropBuckets.RemoveAt(i);
            }
        }
    }

    private static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var dp = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) dp[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = dp[0]; dp[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cur = dp[j];
                dp[j] = Math.Min(Math.Min(dp[j] + 1, dp[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = cur;
            }
        }
        return dp[b.Length];
    }

    private void Emit(PlateReading best, DateTimeOffset now, string scope)
    {
        // Kanıt kırpıntısı: füzyon bucket'ı bozmadan ÖNCE en iyi kırpıntıyı JPEG'e al.
        string? evidence = _options.EvidenceCrops ? SaveEvidence(best, now) : null;

        // Bulanık/düşük güvenli uzlaşı: çok-kareli füzyonla ikinci şans (yalnız bölge-içi, araç başına 1 kez).
        if (scope == "roi" && _options.MultiFrameFusion && best.OcrConfidence < _options.FusionTriggerOcr)
            best = TryFuse(best) ?? best;

        string compact = Compact(best.Text);
        if (compact.Length == 0) return;

        // Emit-seviyesi güven tabanı: birleşik/uzlaşı okumanın güveni de tabanı geçmeli
        // (taban kare-başında uygulanıyor ama uzlaşının güveni altına düşebilir -> sızıntıyı keser).
        if (best.OcrConfidence < _options.EmitMinOcrConfidence) return;
        if (best.Confidence < _options.EmitMinConfidence) return;

        // Varyant bastırma: aynı kapsamda kısa süre önce, bu plakayı ADAY olarak içeren bir emit
        // olduysa bu YENİ olay değil aynı geçişin hayalet varyantıdır -> yayınlama (25DDS84 vakası).
        var candSet = new HashSet<string>();
        if (best.Candidates != null) foreach (var c in best.Candidates) candSet.Add(c.Compact);
        if (_options.VariantSuppressSeconds > 0)
        {
            var window = TimeSpan.FromSeconds(_options.VariantSuppressSeconds);
            _recentEmits.RemoveAll(r => now - r.At > window);
            foreach (var r in _recentEmits)
            {
                if (r.Scope != scope) continue;
                bool dup = r.Compact == compact || r.Candidates.Contains(compact) || candSet.Contains(r.Compact);
                // Düşük güvenli yakın-varyant: eşik altı yeni okuma, az önceki YÜKSEK güvenli emitin
                // ≤2 harf komşusuysa aynı geçişin bozuk kuyruğudur (25AEA791→25AEA181 vakası).
                // Gerçek ikinci araç EarlyEmit sayesinde ya ≥eşik okunur ya bu düşük-güven yoluna düşmez.
                if (!dup && best.OcrConfidence < _options.ManualReviewMinOcr && r.OcrConf >= 0.9f
                    && Levenshtein(compact, r.Compact) <= 2)
                    dup = true;
                if (dup)
                {
                    _logger.LogInformation("[{Cam}] varyant bastırıldı: '{New}' (ocr={NewC:P0}) ~ önceki emit '{Old}' ({Ago:F0} sn önce)",
                        _camera.Id, best.Text, best.OcrConfidence, r.Compact, (now - r.At).TotalSeconds);
                    return;
                }
            }
        }
        _recentEmits.Add(new RecentEmit(now, compact, candSet, scope, best.OcrConfidence));
        if (_recentEmits.Count > 32) _recentEmits.RemoveAt(0);

        var evt = new PlateEvent
        {
            Timestamp = now,
            CameraId = _camera.Id,
            CameraName = _camera.Name,
            Plate = best.Text,
            PlateCompact = compact,
            RawText = best.RawText,
            Confidence = best.Confidence,
            DetectorConfidence = best.DetectorConfidence,
            OcrConfidence = best.OcrConfidence,
            Box = best.Box,
            Scope = scope,
            EvidencePath = evidence,
            LowConfidence = best.OcrConfidence < _options.ManualReviewMinOcr,
            Candidates = best.Candidates,
        };

        EventsEmitted++;
        if (scope == "roi") { LastEventUtc = now; LastPlate = best.Text; } // sağlıkta bariyer-kapsamı görünsün

        _pendingPublishes.Add(PublishAsync(evt, _ct));
    }

    private async Task PublishAsync(PlateEvent evt, CancellationToken ct)
    {
        foreach (var sink in _sinks)
        {
            try { await sink.PublishAsync(evt, ct); }
            catch (Exception ex) { _logger.LogWarning("[{Cam}] sink hatası: {Msg}", _camera.Id, ex.Message); }
        }
    }

    private void ReapPublishes()
    {
        if (_pendingPublishes.Count == 0) return;
        _pendingPublishes.RemoveAll(t => t.IsCompleted);
    }

    private static string Compact(string text)
    {
        Span<char> buf = stackalloc char[text.Length];
        int n = 0;
        foreach (char c in text)
            if (char.IsLetterOrDigit(c)) buf[n++] = c;
        return new string(buf[..n]);
    }

    public async ValueTask DisposeAsync()
    {
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* yoksay */ }
        }

        // Bekleyen yayınları en fazla 5 sn boşalt (kapanışta son olayları kaybetme).
        try { await Task.WhenAll(_pendingPublishes).WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* yoksay */ }

        foreach (var b in _cropBuckets) b.DisposeAll();
        _cropBuckets.Clear();

        await _frameSource.DisposeAsync();
        _engine.Dispose();
    }
}
