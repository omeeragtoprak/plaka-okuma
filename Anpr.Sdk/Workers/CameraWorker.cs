using Anpr.Core;
using Anpr.Core.Confirmation;
using Anpr.Sdk.Configuration;
using Anpr.Sdk.Events;
using Anpr.Sdk.Frames;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Anpr.Sdk.Workers;

/// <summary>
/// Tek bir kamera için okuma döngüsü: en güncel kareyi al -> tespit+oku -> TR doğrula
/// -> çok kareli onay (PlateConfirmer) -> geçerli, onaylı ve YENİ ise olay yayınla.
/// Her worker kendi AnprEngine, frame source ve confirmer'ına sahiptir.
/// </summary>
public sealed class CameraWorker : IAsyncDisposable, ICameraStatus
{
    private readonly CameraOptions _camera;
    private readonly AnprServiceOptions _options;
    private readonly IFrameSource _frameSource;
    private readonly AnprEngine _engine;
    private readonly IReadOnlyList<IEventSink> _sinks;
    private readonly ILogger _logger;
    private readonly PlateConfirmer _confirmer;
    private readonly PlateConfirmer _obsConfirmer; // ROI dışı "gözlem" okumaları (bariyer kararına girmez)
    private readonly DirectionTracker _direction;  // OCR'dan bağımsız yaklaşan/uzaklaşan yön izleyici (roi)
    private readonly List<Task> _pendingPublishes = new();

    // Çok-kareli füzyon: iz başına en iyi kırpıntılar (yalnız bölge-içi; düşük güvenli emitte füzyon denenir).
    private sealed class CropBucket
    {
        public readonly HashSet<string> Variants = new();
        public readonly List<(SKBitmap Bmp, float Conf)> Crops = new();
        // En yüksek güvenli okumanın geldiği TAM KARE (JPEG) — otopark yazılımı "son okunan aracı"
        // görselleyebilsin diye plaka kırpıntısının yanında bütün sahne saklanır. Kare kaynağı her kare
        // için ayrı byte[] snapshot verdiği için referans tutmak güvenlidir (mutasyon yok).
        public byte[]? BestFrameJpeg;
        public float BestFrameConf = -1f;
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

    // Kayan pencere kare-işleme süresi (son 200 kare) — hedef-PC pilotunda darboğaz teşhisi için.
    private readonly double[] _frameMs = new double[200];
    private int _frameMsIdx;
    private int _frameMsCount;
    public double FrameMsP50 => FrameMsPercentile(0.50);
    public double FrameMsP95 => FrameMsPercentile(0.95);
    private double FrameMsPercentile(double q)
    {
        int n = Math.Min(_frameMsCount, _frameMs.Length);
        if (n == 0) return 0;
        var copy = new double[n];
        Array.Copy(_frameMs, copy, n);
        Array.Sort(copy);
        return Math.Round(copy[(int)((n - 1) * q)], 1);
    }
    public long EventsEmitted { get; private set; }

    // Panel: son işlenen kare (ffmpeg'den gelen JPEG bytes — kopyasız) + son tespit kutuları.
    private volatile byte[]? _lastFrameJpeg;
    private volatile IReadOnlyList<PlateBox>? _lastBoxes;
    public byte[]? LastFrameJpeg => _lastFrameJpeg;
    /// <summary>Kaynağın EN SON karesi (ffmpeg varış hızında; işleme hızından bağımsız) — canlı MJPEG için.</summary>
    public byte[]? PeekSourceFrame() => _frameSource.TryGetLatestFrame();
    public IReadOnlyList<PlateBox> LastBoxes => _lastBoxes ?? Array.Empty<PlateBox>();
    public string CurrentRoi => _engine.RoiSpec;
    /// <summary>Kalite kapısının OCR öncesi elediği kırpıntı sayısı (sağlık).</summary>
    public long QualityDropped => _engine.QualityDroppedCount;
    public void ApplyRoi(string? spec) => _engine.SetRoi(spec);
    public DateTimeOffset? LastEventUtc { get; private set; }
    public string? LastPlate { get; private set; }

    private readonly FrequentPlateRegistry _frequent;

    public CameraWorker(
        CameraOptions camera,
        AnprServiceOptions options,
        IFrameSource frameSource,
        AnprEngine engine,
        IReadOnlyList<IEventSink> sinks,
        FrequentPlateRegistry frequent,
        ILogger logger)
    {
        _camera = camera;
        _options = options;
        _frameSource = frameSource;
        _engine = engine;
        _sinks = sinks;
        _frequent = frequent;
        _logger = logger;
        // hold: yalnız roi izlerinde yön için emit tutma açılır; gözlem izleri normal (tutmasız) akar.
        PlateConfirmerOptions MakeOpts(bool hold) => new()
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
            // Yalnız-yaklaşan filtresi açıksa, roi izini yön için yeterli kare birikene dek tut.
            ApproachHoldSamples = hold && options.RequireApproaching ? Math.Max(4, options.ApproachMinSamples) : 0,
        };
        _confirmer = new PlateConfirmer(MakeOpts(hold: true));
        _obsConfirmer = new PlateConfirmer(MakeOpts(hold: false));
        _direction = new DirectionTracker(new DirectionTrackerOptions
        {
            MinSamples = Math.Max(4, options.ApproachMinSamples),
            MinSpan = TimeSpan.FromSeconds(Math.Max(0, options.ApproachMinSpanSeconds)),
        });
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
                    var swf = System.Diagnostics.Stopwatch.StartNew();
                    if (ObserveFrame(frame, now)) activeUntil = now + activeHold; // tespit -> aktif moda geç
                    _frameMs[_frameMsIdx] = swf.Elapsed.TotalMilliseconds;
                    _frameMsIdx = (_frameMsIdx + 1) % _frameMs.Length;
                    if (_frameMsCount < _frameMs.Length) _frameMsCount++;
                }
                catch (Exception ex) { _logger.LogWarning("[{Cam}] kare işleme hatası: {Msg}", _camera.Id, ex.Message); }
            }

            EnhancePendingTracks(now); // İkinci-Şans: düşük güvenli izlere emit ÖNCESİ ek gözlem

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
                // Tam kare de (frame) en iyi okumayla birlikte saklanır (araç görselleme).
                if (r.IsValidTr && crop != null) StoreCrop(r, crop, frame, now);
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
            // Yön izleyiciye HER tespit kutusunu besle (ROI-içi + ROI-dışı, OCR geçerli olsun olmasın):
            // aracın TÜM yaklaşma yörüngesini yakala (uzakta küçük -> yakında büyük). Dar ROI'ye
            // sıkışmaz; uzaklaşan arka plaka OCR'lanamasa bile kutusu görünür -> yoğun yön verisi.
            _direction.Observe(r.Box, now);
            if (r.InZone) zoneDetection = true;
            if (!r.IsValidTr) continue;                          // çöp/geçersiz okumaları gönderme
            if (r.Confidence < _options.EmitMinConfidence) continue;
            if (r.OcrConfidence < _options.EmitMinOcrConfidence) continue; // belirsiz/kötü okuma -> ele
            (r.InZone ? _confirmer : _obsConfirmer).Observe(r, now);
        }
        return zoneDetection;
    }

    // İkinci-Şans Şeridi (Aşama 2): onaylı ama uzlaşı güveni eşiğin altındaki izler için, izin en iyi
    // kırpıntıları WPOD ile rektifiye edilip OCR'lanır ve oylamaya EK GÖZLEM olarak katılır.
    // İz başına 1 kez; süre bütçeli; yalnız bölge-içi (roi) izler. Ham gözlemler korunduğu için
    // kötü bir rektifikasyon çıktısı oylamada azınlıkta kalır (deskew-faciası yapısal engelli).
    private void EnhancePendingTracks(DateTimeOffset now)
    {
        bool anyStage = (_options.EnhanceRectify && _engine.RectifierAvailable)
                     || (_options.EnhanceSr && _engine.SuperResolverAvailable);
        if (!anyStage) return;

        var pending = _confirmer.PendingLowConf(now, _options.EnhanceTriggerOcr);
        if (pending.Count == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (key, consensus) in pending)
        {
            if (sw.ElapsedMilliseconds > _options.EnhanceBudgetMs) break; // bütçe: kalanlar sonraki tura

            _confirmer.MarkEnhanced(key); // tek deneme garantisi (kırpıntı bulunamasa da)
            var bucket = FindBucket(Compact(consensus.Text));
            if (bucket == null || bucket.Crops.Count == 0) continue;

            // En yüksek güvenli 2 kırpıntı
            var top = bucket.Crops.OrderByDescending(c => c.Conf).Take(2).ToList();
            int injected = 0;
            foreach (var (bmp, _) in top)
            {
                if (sw.ElapsedMilliseconds > _options.EnhanceBudgetMs) break;

                // Aşama 2: rektifikasyon (ölçüm: her dilimde kazanç)
                if (_engine.RectifierAvailable)
                {
                    var rect = _engine.TryRectify(bmp);
                    if (rect != null)
                    {
                        using var rimg = rect.Value.Image;
                        var reading = _engine.ReadCrop(rimg, consensus.DetectorConfidence, consensus.Box);
                        if (reading is { IsValidTr: true })
                        {
                            _confirmer.InjectObservation(key, reading with { InZone = true });
                            injected++;
                        }
                    }
                }

                // Aşama 4: küçük kırpıntıda FSRCNN x2 (ölçüm: küçük dilim +2/-0; tetik genişlik<SrMaxWidthPx)
                if (_engine.SuperResolverAvailable && bmp.Width < _options.SrMaxWidthPx
                    && sw.ElapsedMilliseconds <= _options.EnhanceBudgetMs)
                {
                    using var up = _engine.TrySuperResolve(bmp);
                    if (up != null)
                    {
                        var reading = _engine.ReadCrop(up, consensus.DetectorConfidence, consensus.Box);
                        if (reading is { IsValidTr: true })
                        {
                            _confirmer.InjectObservation(key, reading with { InZone = true });
                            injected++;
                        }
                    }
                }
            }
            if (injected > 0)
                _logger.LogInformation(
                    "[{Cam}] enhance: '{Text}' (ocr={Conf:P0}) izine {N} rektifiye gözlem eklendi ({Ms} ms)",
                    _camera.Id, consensus.Text, consensus.OcrConfidence, injected, sw.ElapsedMilliseconds);
        }
    }

    // ---- Çok-kareli füzyon yardımcıları ----

    private void StoreCrop(PlateReading r, SKBitmap crop, byte[]? frameJpeg, DateTimeOffset now)
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

        // Tam kareyi en güvenli okumayla eşle (SaveEvidence en yüksek Conf kırpıntıyı seçer;
        // burada da en yüksek OcrConfidence kareyi tutarak plaka kırpıntısı ile kare aynı ANA denk gelir).
        if (frameJpeg != null && _options.SaveFullFrame && r.OcrConfidence > bucket.BestFrameConf)
        {
            bucket.BestFrameJpeg = frameJpeg;
            bucket.BestFrameConf = r.OcrConfidence;
        }

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
                _camera.Id, M(best.Text), best.OcrConfidence, M(fused.Text), fused.OcrConfidence, bucket.Crops.Count);

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

    // İzin en yüksek güvenli plaka kırpıntısını ve o okumanın geldiği TAM KAREyi evidence/ altına
    // JPEG kaydeder; göreli yolları döner (crop = plaka kırpıntısı, frame = araç görünür bütün sahne).
    private int _evidenceCount = -1;
    private (string? Crop, string? Frame) SaveEvidence(PlateReading best, DateTimeOffset now)
    {
        try
        {
            var bucket = FindBucket(Compact(best.Text));
            if (bucket == null || bucket.Crops.Count == 0) return (null, null);

            var top = bucket.Crops[0];
            for (int i = 1; i < bucket.Crops.Count; i++)
                if (bucket.Crops[i].Conf > top.Conf) top = bucket.Crops[i];

            string dir = Path.Combine(AppContext.BaseDirectory, "evidence");
            Directory.CreateDirectory(dir);
            string stem = $"{now.ToLocalTime():yyyyMMdd-HHmmss}_{_camera.Id}_{Compact(best.Text)}";
            string cropName = stem + ".jpg";

            using (var img = SKImage.FromBitmap(top.Bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Jpeg, 85))
                File.WriteAllBytes(Path.Combine(dir, cropName), data.ToArray());

            // Tam kare (araç görselleme): en iyi okumanın karesi, ham JPEG olarak yeniden kodlanmadan yazılır.
            string? frameName = null;
            if (bucket.BestFrameJpeg != null)
            {
                frameName = stem + "_full.jpg";
                File.WriteAllBytes(Path.Combine(dir, frameName), bucket.BestFrameJpeg);
            }

            // Disk sınırı: her 100 kayıtta bir — (a) KVKK saklama süresi dolanlar, (b) adet fazlası budanır.
            // Crop ve tam kare aynı dizinde birlikte döner (adet sınırı ikisini de sayar).
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

            return ("evidence/" + cropName, frameName != null ? "evidence/" + frameName : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[{Cam}] kanıt kaydı hatası: {Msg}", _camera.Id, ex.Message);
            return (null, null);
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
        // Yön: OCR'dan bağımsız yön-izleyiciden plaka GENİŞLİĞİ eğilimi (son/erken yarı-max).
        // >1 yaklaşıyor, <1 uzaklaşıyor, -1 kararsız (yeterli kare yok). Yalnız roi izleri için anlamlı.
        // NOT (2026-07-21 ölçümü): bu kapı geometrisinde boyut VE konum sinyalleri kanıtlı düz kalıyor
        // (plaka yalnız yakın-durgun zonda tespit ediliyor); trend telemetri olarak taşınır (SizeTrend).
        float trend = scope == "roi" ? _direction.TrendFor(best.Box, now) : -1f;

        // Yalnız YAKLAŞAN araç: iz belirgin küçülüyorsa (uzaklaşıyor) roi olayını hiç üretme.
        // trend=-1 (kararsız/kısa iz) veya ≥eşik → geçer (recall-öncelikli). Gözlem kapsamı gatelenmez.
        if (scope == "roi" && _options.RequireApproaching
            && trend >= 0 && trend < _options.ApproachDepartRatio)
        {
            _logger.LogInformation("[{Cam}] uzaklaşan araç bastırıldı: '{Plate}' (genişlik-eğilimi {Trend:F2} < {Th:F2})",
                _camera.Id, M(best.Text), trend, _options.ApproachDepartRatio);
            return;
        }

        // Kanıt kırpıntısı + tam kare: füzyon bucket'ı bozmadan ÖNCE en iyi kırpıntı ve karesini JPEG'e al.
        (string? evidence, string? framePath) = _options.EvidenceCrops ? SaveEvidence(best, now) : (null, null);

        // Bulanık/düşük güvenli uzlaşı: çok-kareli füzyonla ikinci şans (yalnız bölge-içi, araç başına 1 kez).
        if (scope == "roi" && _options.MultiFrameFusion && best.OcrConfidence < _options.FusionTriggerOcr)
            best = TryFuse(best) ?? best;

        string compact = Compact(best.Text);
        if (compact.Length == 0) return;

        // Whitelist/müdavim SNAP: okuma bilinen bir plakaya lev≤SnapMaxLev yakınsa düzelt (siyah resmi
        // plaka gibi frekanslı araçların sistematik yanlış-okumasını düzeltir; ölçüm: lev≤1 siyah +27pp).
        // Güven-kapısı: yüksek-güvenli okuma (gerçek farklı plaka olabilir) SnapMaxOcrConfidence ile korunur.
        if (scope == "roi" && _options.SnapMaxLev > 0 && best.OcrConfidence < _options.SnapMaxOcrConfidence)
        {
            string snapped = _frequent.SnapTarget(compact, _options.SnapMaxLev, _options.SnapToFrequent);
            if (snapped != compact)
            {
                var tr = Anpr.Core.TurkishPlates.TurkishPlateNormalizer.Normalize(snapped);
                if (tr.IsValid)
                {
                    _logger.LogInformation("[{Cam}] plaka snap: '{Old}' -> '{New}' (bilinen/müdavim, lev≤{Lev})",
                        _camera.Id, M(best.Text), M(tr.Normalized!), _options.SnapMaxLev);
                    // Aday listesini tutarlı tut: düzeltilmiş plakayı başa koy (olayın Plate'i ile aday[0] eşleşsin).
                    var cands = new List<PlateCandidate> { new(tr.Compact!, best.OcrConfidence) };
                    if (best.Candidates != null)
                        foreach (var c in best.Candidates) if (c.Compact != tr.Compact) cands.Add(c);
                    best = best with { Text = tr.Normalized!, RawText = tr.Compact!, Candidates = cands };
                    compact = tr.Compact!;
                }
            }
        }

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
                // Kural D — müdavim varyantı: az önce emit edilen plaka MÜDAVİMSE (defalarca ≥0.98
                // görülmüş), yeni emit müdavim DEĞİLSE, yakın-varyantsa (lev≤2) ve güveni <0.95 ise
                // bu aynı geçişin yanlış-okumasıdır (25FZ069→25FZ289 vakası). Gerçek ikinci araç ya
                // kendisi müdavimdir ya da ≥0.95 okunur.
                if (!dup && best.OcrConfidence < 0.95f && !_frequent.IsFrequent(compact)
                    && _frequent.IsFrequent(r.Compact) && Levenshtein(compact, r.Compact) <= 2)
                    dup = true;
                if (dup)
                {
                    _logger.LogInformation("[{Cam}] varyant bastırıldı: '{New}' (ocr={NewC:P0}) ~ önceki emit '{Old}' ({Ago:F0} sn önce)",
                        _camera.Id, M(best.Text), best.OcrConfidence, M(r.Compact), (now - r.At).TotalSeconds);
                    return;
                }
            }
        }
        _recentEmits.Add(new RecentEmit(now, compact, candSet, scope, best.OcrConfidence));
        if (scope == "roi") _frequent.Record(compact, best.OcrConfidence);
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
            FramePath = framePath,
            LowConfidence = best.OcrConfidence < _options.ManualReviewMinOcr,
            Candidates = best.Candidates,
            SizeTrend = trend,
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

    // KVKK: MaskPlatesInLogs açıksa log'a giden plakayı maskeler (M8). Tek kaynak = ConsoleEventSink.Mask.
    private string M(string plate) => Events.ConsoleEventSink.MaskIf(_options.MaskPlatesInLogs, plate);

    public async ValueTask DisposeAsync()
    {
        // Döngü task'inin GERÇEKTEN bittiğini garanti et: aksi halde motoru (ONNX) ve kırpıntıları
        // (SKBitmap) döngü hâlâ Recognize/StoreCrop yaparken dispose etmek NATIVE use-after-dispose
        // (yakalanamaz access violation) -> tüm PMS çöker. Döngü iptalden sonra bir sonraki denetim
        // noktasında çıkar; 30 sn zayıf-CPU'daki en ağır tek iterasyon için fazlasıyla yeter.
        bool loopStopped = _loopTask == null;
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(30)); loopStopped = true; }
            catch { loopStopped = _loopTask.IsCompleted; }
        }

        // Bekleyen yayınları en fazla 5 sn boşalt (kapanışta son olayları kaybetme). YALNIZ döngü
        // durduysa: aksi halde loop thread hâlâ _pendingPublishes.Add yapabilir → List thread-safe
        // olmadığından WhenAll'ın enumerate'i ile yarışır (engine-dispose ile aynı loopStopped-geçidi).
        if (loopStopped)
        {
            try { await Task.WhenAll(_pendingPublishes).WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* yoksay */ }
        }

        // Frame source dispose'u döngüden bağımsız güvenli (ffmpeg'i öldürür) — HER ZAMAN.
        await _frameSource.DisposeAsync();

        if (loopStopped)
        {
            foreach (var b in _cropBuckets) b.DisposeAll();
            _cropBuckets.Clear();
            _engine.Dispose();
        }
        else
        {
            // Döngü durmadı: native dispose ATLA (çökme önlemi); kaynak GC/süreç sonuna bırakılır.
            _logger.LogWarning("[{Cam}] okuma döngüsü 30s'de durmadı; motor/kırpıntı native dispose atlandı " +
                               "(use-after-dispose çökme önlemi)", _camera.Id);
        }
    }
}
