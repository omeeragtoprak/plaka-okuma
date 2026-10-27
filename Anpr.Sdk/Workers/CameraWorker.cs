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
        // En yüksek güvenli okumanın geldiği TAM KARE — otopark yazılımı "son okunan aracı"
        // görselleyebilsin diye plaka kırpıntısının yanında bütün sahne saklanır. Kare kaynağı her kare
        // için ayrı VideoFrame verdiği için referans tutmak güvenlidir (mutasyon yok). Ham karede JPEG
        // ancak kanıt yazılırken üretilir (emit anı; kare başına değil).
        public VideoFrame? BestFrame;
        public float BestFrameConf = -1f;
        // O KARENİN plaka kutusu. Kanıt kadrajı bu kareden kırpıldığı için kutunun AYNI kareye ait olması
        // şart: araç hareket halinde olduğundan başka bir karenin kutusu kadrajı kaydırır (plaka kenarda
        // ya da kadraj dışında kalır) — 2026-07-27'de canlı görselde bu şekilde yakalandı.
        public PlateBox BestFrameBox;
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

    // Kaynaktan AYNI kare tekrar geldiği için atlanan yinelemeler (sağlık göstergesi:
    // yüksekse tüketim hızı kaynak hızını gereksiz yere aşıyor demektir).
    public long DuplicateFramesSkipped { get; private set; }
    private VideoFrame? _lastProcessedFrame;

    // ── HAREKET KAPISI (Faz 2) — bkz. MotionGate. Gölge modunda yalnız sayar. ──
    private readonly MotionGate? _gate;
    private DateTimeOffset _gateLastForced = DateTimeOffset.MinValue;
    private string? _gateRoiSpec; private int _gateRoiW, _gateRoiH;
    private (int X, int Y, int W, int H)? _gateRoiRect;
    private Anpr.Core.Imaging.RoiPolygon? _gateRoiPoly;   // kapı maskesi: çokgen dışındaki hücreler sayılmaz
    /// <summary>Kapının değerlendirdiği kare sayısı.</summary>
    public long GateEvaluated { get; private set; }
    /// <summary>Gölge modunda "atlanacaktı" sayısı (uygulanmadı).</summary>
    public long GateWouldSkip { get; private set; }
    /// <summary>Açık modda (hareket tetikli zamanlama) dedektöre verilmeden atlanan kaynak karesi sayısı.</summary>
    public long GateSkipped { get; private set; }
    /// <summary>Gölge modunda kapının atlayacağı ama İÇİNDE bölge-içi plaka tespiti çıkan kare sayısı — açmadan önce SIFIR olmalı.</summary>
    public long GateMissedDetections { get; private set; }
    /// <summary>Zorunlu periyotla (hareket görülmediği hâlde) işlenen kare sayısı.</summary>
    public long GateForced { get; private set; }
    /// <summary>Kapı modu (teşhis).</summary>
    public string MotionGateMode => _options.MotionGate.ToString();
    /// <summary>Son karenin biçimi (bgra/jpeg) — kare yolunun gerçekten hangi modda çalıştığını gösterir.</summary>
    public string SourceFormat => _lastFrame?.Format.ToString() ?? "-";
    /// <summary>Kaynağın geri dönüştürdüğü kare tamponu sayısı (yalnız ffmpeg BMP yolu; başka kaynakta 0).</summary>
    public long SourceRecycledBuffers => (_frameSource as Frames.FfmpegFrameSource)?.RecycledBuffers ?? 0;

    /// <summary>
    /// H1 göstergesi: bariyer kanalında (roi), metni FARKLI bir okumanın ZATEN YAYINLANMIŞ bir ize
    /// katılma sayısı. "Kuyruktaki ikinci araç bastırıldı" riskinin tek gözlenebilir imzası;
    /// gerekçe <see cref="PlateConfirmer.AbsorbedAfterEmit"/> belgesinde. Sıfırdan büyük olması
    /// tek başına kusur DEĞİLDİR (aynı aracın varyantları da buraya düşer) — oranı anlamlıdır.
    /// </summary>
    public long AbsorbedAfterEmit => _confirmer.AbsorbedAfterEmit;

    /// <summary>Son absorpsiyon çiftleri (teşhis). ⚠️ Plaka metni taşır — <c>/health</c>'e BAĞLANMAZ.</summary>
    public IReadOnlyCollection<string> RecentAbsorbed => _confirmer.RecentAbsorbed;

    /// <summary>#70: EarlyEmit tarafından tutulmuş iz sayısı (gecikme–doğruluk takasının hacmi).</summary>
    public long EarlyEmitHolds => _confirmer.EarlyEmitHolds;
    /// <summary>#70: tutulanların kaçı sonunda eşiğin üstüne çıktı (takasın KAZANDIĞI vakalar).</summary>
    public long EarlyEmitHoldsResolved => _confirmer.EarlyEmitHoldsResolved;
    /// <summary>#70: tutma anından yayına ortalama güven kazancı. ~0 ise tutma yalnız gecikme ekliyor.</summary>
    public double EarlyEmitAvgGain => _confirmer.EarlyEmitAvgGain;

    // Panel: son işlenen kare (kaynaktan gelen VideoFrame — kopyasız) + son tespit kutuları.
    private volatile VideoFrame? _lastFrame;
    private volatile IReadOnlyList<PlateBox>? _lastBoxes;
    /// <summary>Son İŞLENEN kare (ham ya da JPEG). Panel/teşhis için; kopyasız.</summary>
    public VideoFrame? LastFrame
    {
        get
        {
            // Son işlenen kare kiralıktır: bir sonraki kare işlenince kaynağa geri verilir. Dışarı verilen kare
            // SAKLANIR (geri dönüştürülmez); tam o anda geri verilmişse null döner (çağıran sonraki istekte alır).
            var f = _lastFrame;
            return f != null && f.Keep() ? f : null;
        }
    }
    /// <summary>Son işlenen kare JPEG olarak. Ham karede burada KODLANIR (seyrek çağrılmalı).</summary>
    public byte[]? LastFrameJpeg => LastFrame?.ToJpeg(85);
    /// <summary>Kaynağın EN SON karesi (ffmpeg varış hızında; işleme hızından bağımsız) — canlı görüntü için.</summary>
    public VideoFrame? PeekSourceFrame() => _frameSource.TryGetLatestFrame();
    public IReadOnlyList<PlateBox> LastBoxes => _lastBoxes ?? Array.Empty<PlateBox>();
    public string CurrentRoi => _engine.RoiSpec;
    /// <summary>Kalite kapısının OCR öncesi elediği kırpıntı sayısı (sağlık).</summary>
    public long QualityDropped => _engine.QualityDroppedCount;
    public long AspectDropped => _engine.AspectDroppedCount;
    public long SizeDropped => _engine.SizeDroppedCount;
    public long TrRejected => _engine.TrRejectedCount;

    // Son yayınlanan roi olaylarının plaka genişlikleri — eşik seçimi için dağılım (bkz. ICameraStatus).
    internal const int PlateWidthWindowSize = 200;
    private readonly PlateWidthWindow _plateWidths = new(PlateWidthWindowSize);
    public PlateWidthStats? RecentPlateWidths => _plateWidths.Stats();

    /// <summary>Son işlenen karenin MOTORUN GÖRDÜĞÜ genişliği (px) — kalibrasyondaki <c>ReferenceFrameWidthPx</c> için.
    /// Kare yoksa null. (<c>VideoFrame.Width</c> değil: JPEG karede çözülmeden 0'dır.)</summary>
    public int? FrameWidthPx => _frameWidthPx > 0 ? _frameWidthPx : null;
    private volatile int _frameWidthPx;

    public bool RectifierAvailable => _options.EnhanceRectify && _engine.RectifierAvailable;
    public long EnhanceTracks => Interlocked.Read(ref _enhanceTracks);
    public long EnhanceInjected => Interlocked.Read(ref _enhanceInjected);
    public long EnhanceNoCrops => Interlocked.Read(ref _enhanceNoCrops);
    private long _enhanceTracks, _enhanceInjected, _enhanceNoCrops;

    /// <summary>
    /// Olaya boyut alanlarını yazar (şema v9). Ayrı ve testli: alanlar olay kurulurken sessizce düşerse
    /// <see cref="RecentPlateWidths"/> ve kalibrasyon ölçümü görünmeden bozulur.
    /// </summary>
    internal static PlateEvent WithMeasuredSize(PlateEvent e, PlateReading best) =>
        e with { PlateWidthPx = best.PlateWidthPx, EstimatedDistanceMeters = best.EstimatedDistanceMeters };

    /// <summary>
    /// İyileştirme (rektifikasyon/SR) gözlemi kırpıntıdan okunur ve kutu ölçüsü taşımaz; izin kendi ölçüsünü
    /// aktarır. Aksi hâlde bu gözlem izin en iyisi olunca olay genişliksiz/mesafesiz yayınlanırdı — ve tam olarak
    /// zor (genelde küçük) plakalar <see cref="RecentPlateWidths"/> dağılımından düşerdi.
    /// </summary>
    internal static PlateReading AsEnhancedObservation(PlateReading reading, PlateReading consensus) =>
        reading with
        {
            InZone = true,
            PlateWidthPx = consensus.PlateWidthPx,
            EstimatedDistanceMeters = consensus.EstimatedDistanceMeters,
        };

    /// <summary>
    /// Okunan VE boyut sınırı yüzünden okunmayan tespitleri tek tespit kümesinde birleştirir: canlı kutular, yön
    /// izleyici ve "bölgede plaka var" (aktif izleme) sinyali sınırdan ETKİLENMESİN diye.
    /// </summary>
    internal static (PlateBox[] Boxes, bool ZoneDetection) DetectionSignals(
        IReadOnlyList<PlateReading> readings, IReadOnlyList<SizeSkippedDetection> skipped, Action<PlateBox> observeDirection)
    {
        var boxes = new PlateBox[readings.Count + skipped.Count];
        bool zone = false;
        int i = 0;
        foreach (var r in readings) { boxes[i++] = r.Box; observeDirection(r.Box); zone |= r.InZone; }
        foreach (var s in skipped) { boxes[i++] = s.Box; observeDirection(s.Box); zone |= s.InZone; }
        return (boxes, zone);
    }
    public void ApplyRoi(string? spec) => _engine.SetRoi(spec);
    public DateTimeOffset? LastEventUtc { get; private set; }
    public string? LastPlate { get; private set; }

    private readonly FrequentPlateRegistry _frequent;

    // Küratörlü tam-dizgi düzeltmeler. Reddedilen girdiler BURADA loglanmaz — başlangıçta
    // AnprRuntime tek noktadan gürültülü uyarır (kamera başına tekrarlamasın).
    private readonly PlateCorrectionMap _corrections;

    // TÜM kameraların son yayınları (worker'lar arasında PAYLAŞILAN): çapraz-kamera kesilme
    // kapısı için. Yukarıdaki _recentEmits ile karıştırılmamalı — o worker-İÇİ varyant
    // bastırma listesidir ve diğer kamerayı GÖREMEZ (bu kapının var olma sebebi tam da bu).
    private readonly RecentEmitRegistry _crossCameraEmits;

    public CameraWorker(
        CameraOptions camera,
        AnprServiceOptions options,
        IFrameSource frameSource,
        AnprEngine engine,
        IReadOnlyList<IEventSink> sinks,
        FrequentPlateRegistry frequent,
        RecentEmitRegistry recentEmits,
        ILogger logger)
    {
        _camera = camera;
        _options = options;
        _frameSource = frameSource;
        _engine = engine;
        _sinks = sinks;
        _frequent = frequent;
        _gate = options.MotionGate == Configuration.MotionGateMode.Off ? null
              : new MotionGate(Math.Max(4, options.MotionGateGridColumns), Math.Max(4, options.MotionGateGridRows),
                               Math.Clamp(options.MotionGatePixelDelta, 1, 254), Math.Clamp(options.MotionGateChangedFraction, 1e-6, 1.0));
        _corrections = PlateCorrectionMap.Build(options, out _);
        _crossCameraEmits = recentEmits;
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
            // Bu ikisi eskiden HİÇ geçirilmiyordu → PlateConfirmerOptions varsayılanında sabit kalıyor,
            // sahada ayarlanamıyordu. SpatialMergeMaxGap fps'e bağımlı olduğu için bu bir eksikti.
            SpatialMergeMaxGap = TimeSpan.FromSeconds(Math.Max(0.1, options.SpatialMergeMaxGapSeconds)),
            SpatialMergeMaxDistance = Math.Max(0, options.SpatialMergeMaxDistance),
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
        // MotionGate = On ve ham kare yolu → hareket tetikli zamanlama (ayrı döngü). Off/Shadow ve MJPEG yolu
        // aşağıdaki zamanlayıcılı döngüde kalır (davranışı 1.x ile aynıdır).
        if (_gate != null && _options.UsesMotionTriggeredLoop)
        {
            await LoopMotionTriggeredAsync(ct);
            return;
        }

        double activeFps = _options.Fps > 0 ? _options.Fps : 1;
        double idleFps = _options.IdleFps > 0 ? _options.IdleFps : activeFps;
        var activeHold = TimeSpan.FromSeconds(Math.Max(0, _options.ActiveHoldSeconds));
        DateTimeOffset activeUntil = DateTimeOffset.MinValue;

        _logger.LogInformation("[{Cam}] okuma döngüsü başladı (aktif {Fps} / boşta {Idle} fps, onay={Min}, oylama={Vote})",
            _camera.Id, activeFps, idleFps, _options.MinConfirmations, _options.CharacterVoting);

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            // KİRALA: kare işlenir, saklanması gerekmiyorsa (çoğu karede plaka yok) tamponu kaynağa geri döner.
            var frame = _frameSource.TryLeaseLatestFrame();
            // KOPYA KARE KAPISI: aynı fiziksel kareyi iki kez işlemek CPU'yu boşa harcar VE
            // çok-kareli uzlaşıya bağımsız olmayan "gözlem" enjekte eder (bkz. IsNewFrame).
            if (frame is null || !IsNewFrame(frame, _lastProcessedFrame)) { if (frame != null) DuplicateFramesSkipped++; }
            else
            {
                _lastProcessedFrame = frame;

                // HAREKET KAPISI — GÖLGE: bu döngüde kapı yalnız SAYAR, kare atlamaz (atlayan mod = On, ayrı
                // döngü: LoopMotionTriggeredAsync). Yalnız ham karede; aktif izleme süresinde ve zorunlu periyotta
                // "atlardım" denmez. Sayaçlar yoklanan kareler üzerindendir (boşta 2 fps) → On modunun kare-kare
                // davranışını YAKLAŞIK gösterir; kesin benzetim için kayıt + scripts/gate_sim.py.
                bool gateSaysSkip = false;
                if (_gate != null && frame.IsRaw)
                {
                    var decision = _gate.Evaluate(frame, GateRoiRect(frame), _gateRoiPoly);
                    GateEvaluated++;
                    bool forced = (now - _gateLastForced).TotalSeconds >= Math.Max(0.5, _options.MotionGateForcedIntervalSeconds);
                    if (forced) { _gateLastForced = now; GateForced++; }
                    gateSaysSkip = !decision.Motion && !forced && now >= activeUntil;
                    if (gateSaysSkip) GateWouldSkip++;
                }

                try
                {
                    var swf = System.Diagnostics.Stopwatch.StartNew();
                    bool zone = ObserveFrame(frame, now);
                    if (zone) activeUntil = now + activeHold; // tespit -> aktif moda geç
                    if (zone && gateSaysSkip) GateMissedDetections++; // gölge: kapı yanılmış olacaktı
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
            CleanFallbackCrops(now);

            // Uyarlanabilir hız: araç varken aktif, yoksa boşta fps.
            double fps = now < activeUntil ? activeFps : idleFps;
            try { await Task.Delay(TimeSpan.FromSeconds(1.0 / fps), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>İşlenen karelerin gerekçe dökümü (yalnız <c>MotionGate = On</c>): hareket.</summary>
    public long GateMotionFrames { get; private set; }
    /// <summary>Hareket bittikten sonraki bekleme süresinde işlenen kare sayısı.</summary>
    public long GateHoldFrames { get; private set; }
    /// <summary>Aktif izleme (yakın zamanda bölge-içi tespit) nedeniyle işlenen kare sayısı.</summary>
    public long GateActiveFrames { get; private set; }
    /// <summary>
    /// <c>MotionGate = On</c> sağlık göstergesi: ZORUNLU bakışta bölge-içi plaka bulunan kare sayısı — yani kapı
    /// hareket görmemişti, son saniyelerde tespit de yoktu, ama sahnede plaka vardı. Olay sayısına göre küçük
    /// kalmalıdır; büyüyorsa kapı bu sahnede bir şeyi kaçırıyor demektir (eşiği düşürün ya da kapıyı kapatın).
    /// </summary>
    public long GateForcedHits { get; private set; }

    // HAREKET TETİKLİ DÖNGÜ (MotionGate = On). Kaynağın HER karesine kapı bakar (kareyi ALMADAN — atlanan
    // karenin tamponu geri dönüştürülmeye devam eder); işlenecek kareye MotionSchedule karar verir.
    // Zamanlayıcılı döngüden farkı: boşta hiçbir kare işlenmez (yalnız zorunlu aralıkta bir bakış), hareket
    // varken kaynağın verdiği her kare işlenir → hızlı geçen araç yoklama fazına bağlı olarak kaçmaz.
    private async Task LoopMotionTriggeredAsync(CancellationToken ct)
    {
        double srcFps = _options.ResolveSourceFps();   // ffmpeg ile aynı kaynaktan (bkz. AnprCameraSupervisor)
        var schedule = new MotionSchedule(
            TimeSpan.FromSeconds(Math.Max(0, _options.MotionGateHoldSeconds)),
            TimeSpan.FromSeconds(Math.Max(0, _options.ActiveHoldSeconds)),
            TimeSpan.FromSeconds(Math.Max(0.5, _options.MotionGateForcedIntervalSeconds)));
        // Yeni kareyi gecikmeden fark etmek için kare aralığının dörtte biri kadar uyunur (10–50 ms).
        // Uyanışta yapılan iş bir sayı karşılaştırmasıdır; kare gelmediyse hiçbir şey çalışmaz.
        var poll = TimeSpan.FromMilliseconds(Math.Clamp(1000.0 / srcFps / 4.0, 10, 50));
        long seenSeq = -1;

        _logger.LogInformation("[{Cam}] okuma döngüsü başladı (HAREKET TETİKLİ: kaynak {Fps} fps, bekleme {Hold} sn, zorunlu bakış {Forced} sn, onay={Min}, oylama={Vote})",
            _camera.Id, srcFps, _options.MotionGateHoldSeconds, _options.MotionGateForcedIntervalSeconds, _options.MinConfirmations, _options.CharacterVoting);

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            long seq = _frameSource.LatestSequence;
            VideoFrame? frame = null;
            ScheduleReason reason = ScheduleReason.Skip;

            if (seq < 0)
            {
                // Kaynak "almadan bakış" desteklemiyor (özel IFrameSource): kareyi al, kapıyı alınan karede değerlendir.
                var f = _frameSource.TryGetLatestFrame();
                if (f != null && IsNewFrame(f, _lastProcessedFrame))
                {
                    _lastProcessedFrame = f;
                    bool motion = !f.IsRaw || _gate!.Evaluate(f, GateRoiRect(f), _gateRoiPoly).Motion;
                    GateEvaluated++;
                    reason = schedule.Decide(now, motion);
                    if (reason != ScheduleReason.Skip) frame = f;
                    else GateSkipped++;
                }
            }
            else if (seq != seenSeq)
            {
                if (_frameSource.TryInspectLatest(f => (f.Sequence, Motion: !f.IsRaw || _gate!.Evaluate(f, GateRoiRect(f), _gateRoiPoly).Motion), out var seen))
                {
                    seenSeq = seen.Sequence;
                    GateEvaluated++;
                    reason = schedule.Decide(now, seen.Motion);
                    if (reason == ScheduleReason.Skip) GateSkipped++;
                    else
                    {
                        var f = _frameSource.TryLeaseLatestFrame();
                        if (f != null && IsNewFrame(f, _lastProcessedFrame)) { _lastProcessedFrame = f; frame = f; }
                    }
                }
            }

            if (frame != null)
            {
                switch (reason)
                {
                    case ScheduleReason.Motion: GateMotionFrames++; break;
                    case ScheduleReason.MotionHold: GateHoldFrames++; break;
                    case ScheduleReason.ActiveTracking: GateActiveFrames++; break;
                    case ScheduleReason.Forced: GateForced++; break;
                }
                try
                {
                    var swf = System.Diagnostics.Stopwatch.StartNew();
                    bool zone = ObserveFrame(frame, now);
                    schedule.OnProcessed(now, zone);
                    if (zone && reason == ScheduleReason.Forced) GateForcedHits++;
                    _frameMs[_frameMsIdx] = swf.Elapsed.TotalMilliseconds;
                    _frameMsIdx = (_frameMsIdx + 1) % _frameMs.Length;
                    if (_frameMsCount < _frameMs.Length) _frameMsCount++;
                }
                catch (Exception ex)
                {
                    schedule.OnProcessed(now, false);   // hata döngüsünde zorunlu-aralık sayacı ilerlesin (sıkı döngü olmasın)
                    _logger.LogWarning("[{Cam}] kare işleme hatası: {Msg}", _camera.Id, ex.Message);
                }
            }

            EnhancePendingTracks(now);
            foreach (var best in _confirmer.DrainReady(now))
                Emit(best, now, scope: "roi");
            foreach (var best in _obsConfirmer.DrainReady(now))
                Emit(best, now, scope: "gozlem");
            ReapPublishes();
            CleanCropBuckets(now);
            CleanFallbackCrops(now);

            try { await Task.Delay(poll, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
    // Bölge-içi tespit olup olmadığını döner (aktif moda geçiş için — ROI dışı cadde trafiği
    // aktif fps'i tetiklememeli, yoksa zayıf CPU bütün gün yüksek hızda döner).
    // Kapı için ROI sınır kutusu (piksel). ROI spec'i ya da kare boyutu değişince yeniden hesaplanır.
    private (int X, int Y, int W, int H)? GateRoiRect(VideoFrame frame)
    {
        string spec = _engine.RoiSpec;
        if (!ReferenceEquals(spec, _gateRoiSpec) && spec != _gateRoiSpec || _gateRoiW != frame.Width || _gateRoiH != frame.Height)
        {
            _gateRoiSpec = spec; _gateRoiW = frame.Width; _gateRoiH = frame.Height;
            var poly = Anpr.Core.Imaging.RoiPolygon.Parse(spec);
            _gateRoiPoly = poly;
            _gateRoiRect = poly?.BoundingRectPixels(frame.Width, frame.Height, 0.05f);
        }
        return _gateRoiRect;
    }

    private bool ObserveFrame(VideoFrame frame, DateTimeOffset now)
    {
        FramesProcessed++;

        // YUV kare: motor kareyi KOPYASIZ bir görünümden okur (dedektör girdisi doğrudan örneklenir, yalnız
        // plaka kırpıntıları RGBA'ya çevrilir) — tam kare hiçbir zaman RGB'ye çevrilmez.
        // BGRA/JPEG kare (eski yollar): RGBA bitmap üretilir ve motor onu eskisi gibi görür.
        SKBitmap? bitmap = null;
        Anpr.Core.Imaging.IFrameView image = frame.TryGetView()
            ?? new Anpr.Core.Imaging.SkBitmapFrameView(bitmap = frame.ToRgbaBitmap());
        using var bitmapOwner = bitmap;
        _frameWidthPx = image.Width;

        IReadOnlyList<PlateReading> readings;
        var keep = ShouldKeepCrops(_options, _engine.RectifierAvailable, _engine.SuperResolverAvailable);
        if (keep != CropKeep.None)
        {
            var withCrops = _engine.RecognizeWithCrops(image);
            var list = new List<PlateReading>(withCrops.Count);
            foreach (var (r, crop) in withCrops)
            {
                list.Add(r);
                // Geçerli okumaların kırpıntıları saklanır (füzyon + kanıt görüntüsü için; sahiplenilir).
                // Tam kare de (frame) en iyi okumayla birlikte saklanır (araç görselleme).
                if (r.IsValidTr && crop != null) StoreCrop(r, crop, keep == CropKeep.Full ? frame : null, now);
                else if (crop != null && keep == CropKeep.Full) StoreFallbackCrop(r, crop, frame, now);   // yedek: yalnız kanıt/füzyon için
                else crop?.Dispose();
            }
            readings = list;
        }
        else
        {
            readings = _engine.Recognize(image);
        }

        // Son işlenen kare panel için tutulur; ÖNCEKİ kare kaynağa geri verilir (kimse saklamadıysa tamponu
        // yeniden kullanılır — kanıt için saklananlar Keep() ile işaretlendi, onlara dokunulmaz).
        var previous = _lastFrame;
        _lastFrame = frame;
        if (previous != null && !ReferenceEquals(previous, frame)) _frameSource.ReturnFrame(previous);
        // Yön izleyiciye HER tespit kutusunu besle (ROI-içi + ROI-dışı, OCR geçerli olsun olmasın; boyut sınırı
        // yüzünden OKUNMAYANLAR dahil): aracın TÜM yaklaşma yörüngesini yakala (uzakta küçük -> yakında büyük).
        // Dar ROI'ye sıkışmaz; uzaklaşan arka plaka OCR'lanamasa bile kutusu görünür -> yoğun yön verisi.
        var (boxes, zoneDetection) = DetectionSignals(readings, _engine.LastSizeSkipped, b => _direction.Observe(b, now));
        _lastBoxes = boxes;

        foreach (var r in readings)
        {
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
            if (bucket == null || bucket.Crops.Count == 0) { Interlocked.Increment(ref _enhanceNoCrops); continue; }
            Interlocked.Increment(ref _enhanceTracks);

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
                            _confirmer.InjectObservation(key, AsEnhancedObservation(reading, consensus));
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
                            _confirmer.InjectObservation(key, AsEnhancedObservation(reading, consensus));
                            injected++;
                        }
                    }
                }
            }
            if (injected > 0) Interlocked.Add(ref _enhanceInjected, injected);
            if (injected > 0)
                _logger.LogInformation(
                    "[{Cam}] enhance: '{Text}' (ocr={Conf:P0}) izine {N} rektifiye gözlem eklendi ({Ms} ms)",
                    _camera.Id, consensus.Text, consensus.OcrConfidence, injected, sw.ElapsedMilliseconds);
        }
    }

    // ---- Çok-kareli füzyon yardımcıları ----

    internal enum CropKeep { None, CropsOnly, Full }

    // Test gözlemi (yalnız döngü DURDUKTAN sonra okunur; listeler döngü iş parçacığına aittir).
    internal int CropBucketCount => _cropBuckets.Count;
    internal int CropBucketFramesHeld => _cropBuckets.Count(b => b.BestFrame != null);
    internal int FallbackCropCount => _fallbackCrops.Count;

    /// <summary>
    /// Okumaların plaka kırpıntıları saklansın mı? <see cref="CropKeep.Full"/>: kanıt/füzyon için (kırpıntı + yedek +
    /// tam kare, 2.2.0 öncesiyle AYNI). <see cref="CropKeep.CropsOnly"/>: yalnız zenginleştirme (rektifikasyon / SR)
    /// için küçük plaka kırpıntıları — tam kare TUTULMAZ, diske hiçbir şey yazılmaz.
    /// NEDEN (2026-10-27): kırpıntılar eskiden YALNIZ <c>EvidenceCrops</c>/<c>MultiFrameFusion</c> açıkken saklanıyordu;
    /// kanıt kaydını kapatan (KVKK) kurulumda zenginleştirme aşaması kırpıntı bulamayıp SESSİZCE hiçbir şey yapmıyordu
    /// (iki kameralı 30 dk oynatmada düzeltme açık/kapalı birebir aynı sonucu verdi). Saf (test edilebilir).
    /// </summary>
    internal static CropKeep ShouldKeepCrops(AnprServiceOptions o, bool rectifierAvailable, bool superResolverAvailable)
    {
        if (o.MultiFrameFusion || o.EvidenceCrops) return CropKeep.Full;
        bool enhance = (o.EnhanceRectify && rectifierAvailable) || (o.EnhanceSr && superResolverAvailable);
        return enhance ? CropKeep.CropsOnly : CropKeep.None;
    }

    private void StoreCrop(PlateReading r, SKBitmap crop, VideoFrame? frame, DateTimeOffset now)
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
        if (frame != null && _options.SaveFullFrame && r.OcrConfidence > bucket.BestFrameConf && frame.Keep())
        {
            bucket.BestFrame = frame;   // Keep(): kare kanıt için saklanıyor → geri dönüştürülmez
            bucket.BestFrameConf = r.OcrConfidence;
            bucket.BestFrameBox = r.Box;   // kadraj bu kareden kırpılacak → kutu AYNI kareden olmalı
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

    // ---- TR-GEÇERSİZ OKUMALARIN KIRPINTILARI (yalnız KANIT için, füzyona GİRMEZ) ----
    //
    // NEDEN VAR (2026-08-01, canlı ölçüm): `StoreCrop` yalnız `IsValidTr` okumalarda çağrılıyordu.
    // Çok-kareli uzlaşı, tek tek KARELERİ TR-geçersiz okunmuş bir araçtan GEÇERLİ bir plaka
    // üretebilir; o zaman hiç kova oluşmadığı için `SaveEvidence` sessizce `(null,null,null)` döner.
    // Ölçüm: 2844 olayın 8'inde (roi) kanıt kırpıntısı VE tam kare YOK — ve **sekizinin de
    // `LowConfidence=true`** olduğu, yani tam da operatörün gözle bakması gereken olaylar olduğu
    // görüldü (ocr 0,46–0,77). Operatör kuyruğunun %4,3'ü görüntüsüzdü.
    //
    // NEDEN KOVA MEKANİZMASI GENİŞLETİLMEDİ: `FindBucket` METİN benzerliğiyle (lev≤1) eşleştirir.
    // Geçersiz okumaları da kovaya koymak, çöp bir dizginin başka bir aracın uzlaşı metnine lev≤1
    // düşüp ona YANLIŞ kanıt vermesi riskini büyütürdü. **Yanlış kanıt, kanıtsızlıktan KÖTÜDÜR** —
    // yer-gerçeğini zehirler (§P.4 2026-07-30 (15): dosya adı çakışması tam bunu yapmıştı).
    // Bu yüzden yedek yol METİNLE DEĞİL, UZAMSAL ÖRTÜŞMEYLE eşleşir: aynı kamera, aynı kısa pencere
    // ve kutular gerçekten kesişiyor. Bu, aynı aracı işaret ettiğinin çok daha güçlü kanıtıdır.
    private readonly List<(SKBitmap Bmp, VideoFrame? Frame, PlateBox Box, float Ocr, DateTimeOffset At)> _fallbackCrops = new();
    private const int FallbackCropCapacity = 6;

    /// <summary>
    /// Yedek kayıtlardan en çok kaçında TAM KARE tutulur (en yeniler). 4 MP karede bir kare ~6 MB'tır; 6 kaydın
    /// hepsinde kare tutmak trafikte kamera başına 36 MB demekti (2026-10-20). Seçim zamanca en yakın kesişen kaydı
    /// aldığı için (<see cref="PickFallbackCrop"/>) eski kayıtların karesi neredeyse hiç kullanılmaz; eski kayıt
    /// seçilirse kanıt yalnız kırpıntıyla üretilir (kırpıntı her kayıtta durur).
    /// </summary>
    internal const int FallbackFrameCapacity = 2;

    private void StoreFallbackCrop(PlateReading r, SKBitmap crop, VideoFrame? frame, DateTimeOffset now)
    {
        if (r.Box.Width <= 0 || r.Box.Height <= 0) { crop.Dispose(); return; }
        // Kare saklanacaksa Keep() (geri dönüştürülmesin); saklanamıyorsa (çoktan geri verilmiş) kare'siz kaydet.
        AddFallback(_fallbackCrops, (crop, _options.SaveFullFrame && frame != null && frame.Keep() ? frame : null, r.Box, r.OcrConfidence, now),
                    FallbackCropCapacity, FallbackFrameCapacity);
    }

    /// <summary>Yedek halkaya ekler: kapasiteyi aşan en eski kaydın kırpıntısını bırakır (dispose), sonra yalnız en yeni
    /// <paramref name="frameCapacity"/> kayıtta tam kare bırakır. Saf (test edilebilir).</summary>
    internal static void AddFallback(
        List<(SKBitmap Bmp, VideoFrame? Frame, PlateBox Box, float Ocr, DateTimeOffset At)> list,
        (SKBitmap Bmp, VideoFrame? Frame, PlateBox Box, float Ocr, DateTimeOffset At) entry, int capacity, int frameCapacity)
    {
        list.Add(entry);
        while (list.Count > capacity)
        {
            list[0].Bmp.Dispose();
            list.RemoveAt(0);
        }
        DropOldFallbackFrames(list, frameCapacity);
    }

    /// <summary>En yeni <paramref name="keep"/> kare dışındaki tam karelerin referansını bırakır (kırpıntılar kalır).
    /// Saf (test edilebilir); liste eskiden yeniye sıralıdır.</summary>
    internal static void DropOldFallbackFrames(
        List<(SKBitmap Bmp, VideoFrame? Frame, PlateBox Box, float Ocr, DateTimeOffset At)> list, int keep)
    {
        int withFrame = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Frame == null) continue;
            if (++withFrame > keep)
            {
                var e = list[i];
                list[i] = (e.Bmp, null, e.Box, e.Ocr, e.At);
            }
        }
    }

    /// <summary>
    /// İki kutu kesişiyor mu? Yedek kanıt seçiminin TEK kimlik ölçütü budur — metin DEĞİL.
    /// Kesişim yoksa aday elenir; "yakındı" diye kabul etmek başka bir aracın görüntüsünü
    /// olaya iliştirebilirdi.
    /// </summary>
    internal static bool BoxesIntersect(PlateBox a, PlateBox b)
        => a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0
           && a.X < b.X + b.Width && b.X < a.X + a.Width
           && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    /// <summary>
    /// Adaylar arasından olayın kutusuyla KESİŞEN ve pencere içinde olanların **ZAMANCA EN YAKINININ**
    /// dizinini döner (eşitlikte daha güvenli olan); yoksa -1. Saf fonksiyon (test edilebilir).
    ///
    /// <para><b>NEDEN ZAMAN, GÜVEN DEĞİL (2026-08-01, kendi kodumu gözden geçirirken düzeltildi):</b>
    /// ilk sürüm kesişenler arasından EN GÜVENLİSİNİ seçiyordu. Güven bir <b>kimlik</b> ölçütü
    /// değildir — bunu kesişim testinde zaten yazmıştım ama seçimde uygulamamıştım. ÖLÇÜM: pencere
    /// <c>DedupWindowSeconds</c> = 8 sn ve canlı veride <b>8 sn içinde aynı kameraya başka bir araç
    /// gelme oranı ~%13</b> (3 sn'de %6,5 · 10 sn'de %15,6). Yani başka bir aracın çöp kırpıntısı
    /// aynı ekran bölgesinde bulunup daha yüksek güvenle seçilebilirdi ve olaya YANLIŞ ARACIN
    /// görüntüsü iliştirilirdi.</para>
    ///
    /// <para><b>Zaman neden doğru ölçüt:</b> halka emit ANINDA sorgulanır, dolayısıyla içinde yalnız
    /// GEÇMİŞ kırpıntılar vardır. Olayı üreten aracın kırpıntıları zorunlu olarak <b>en yeniler</b>dir;
    /// daha eski bir kayıt ancak ÖNCEKİ bir araca ait olabilir. Bu, mekanizmadan gelen bir
    /// argümandır, istatistiksel bir tercih değil.</para>
    /// </summary>
    internal static int PickFallbackCrop(
        IReadOnlyList<(PlateBox Box, float Ocr, DateTimeOffset At)> candidates,
        PlateBox eventBox, DateTimeOffset now, TimeSpan window)
    {
        int best = -1;
        double bestGap = double.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (now - c.At > window || c.At - now > window) continue;
            if (!BoxesIntersect(c.Box, eventBox)) continue;
            double gap = Math.Abs((now - c.At).TotalSeconds);
            if (best < 0 || gap < bestGap || (gap == bestGap && c.Ocr > candidates[best].Ocr))
            {
                best = i;
                bestGap = gap;
            }
        }
        return best;
    }

    private void CleanFallbackCrops(DateTimeOffset now)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.DedupWindowSeconds));
        for (int i = _fallbackCrops.Count - 1; i >= 0; i--)
            if (now - _fallbackCrops[i].At > ttl)
            {
                _fallbackCrops[i].Bmp.Dispose();
                _fallbackCrops.RemoveAt(i);
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
    /// <summary>
    /// Kanıt kadrajı: plaka kutusunu her kenardan <paramref name="padRatio"/> katı genişletip kare
    /// sınırlarına kırpar. OCR kadrajından ayrıdır (bkz. AnprServiceOptions.EvidencePaddingRatio).
    /// </summary>
    internal static SKRectI EvidenceRect(PlateBox box, int imgW, int imgH, float padRatio)
    {
        int padX = (int)(box.Width * padRatio);
        int padY = (int)(box.Height * padRatio);
        int left = Math.Clamp(box.X - padX, 0, Math.Max(0, imgW));
        int top = Math.Clamp(box.Y - padY, 0, Math.Max(0, imgH));
        int right = Math.Clamp(box.X + box.Width + padX, 0, imgW);
        int bottom = Math.Clamp(box.Y + box.Height + padY, 0, imgH);
        return new SKRectI(left, top, Math.Max(left, right), Math.Max(top, bottom));
    }

    /// <summary>Tam kare JPEG'inden plakayı geniş kadrajla yeniden kırpar. Kare yoksa/çözülemezse null
    /// (çağıran OCR kırpıntısına düşer) — kanıt kaydı asla bu yüzden başarısız olmamalı.</summary>
    private static SKBitmap? CropFromFrame(VideoFrame? source, PlateBox box, float padRatio)
    {
        if (source == null || box.Width <= 0 || box.Height <= 0) return null;
        // YUV kare: yalnız kadraj bölgesi RGBA'ya çevrilir (tam kare çevrilmez).
        if (source.TryGetView() is { } view)
        {
            try
            {
                var vr = EvidenceRect(box, view.Width, view.Height, padRatio);
                return vr.Width < 2 || vr.Height < 2 ? null : view.ExtractRgba(vr);
            }
            catch { return null; }
        }
        SKBitmap? frame = null, outBmp = null;
        try
        {
            frame = source.ToRgbaBitmap();
            var r = EvidenceRect(box, frame.Width, frame.Height, padRatio);
            if (r.Width < 2 || r.Height < 2) return null;
            outBmp = new SKBitmap(r.Width, r.Height);
            if (!frame.ExtractSubset(outBmp, r)) { outBmp.Dispose(); return null; }
            return outBmp;
        }
        catch { outBmp?.Dispose(); return null; }
        finally { frame?.Dispose(); }
    }

    /// <summary>JPEG yazar. Kalite ≥90 libjpeg'i 4:4:4 kroma'ya geçirir (küçük plaka görüntüsünde
    /// 4:2:0 renk yarılaması TR mavi bandını eziyordu); Downsample444 açıkça talep edilir.</summary>
    private static void EncodeJpeg(SKBitmap bmp, string path, int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        using var img = SKImage.FromBitmap(bmp);
        using var pix = img.PeekPixels();
        if (pix != null)
        {
            using var fs = File.Create(path);
            using var wstream = new SKManagedWStream(fs);
            var opts = new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample444,
                                                SKJpegEncoderAlphaOption.Ignore);
            if (pix.Encode(wstream, opts)) return;
        }
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, quality);   // yedek yol
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>
    /// Kanıt dosya kökündeki plakayı olayın NİHAİ plakasıyla hizalar; değişiklik gerekmiyorsa null döner.
    /// Dosya adı "yyyyMMdd-HHmmss_kamera_PLAKA" biçimindedir ve SON alt çizgiden sonrası plakadır.
    /// </summary>
    /// <summary>
    /// Kanıt dosya adını BENZERSİZ kılar: ad zaten alınmışsa "-2", "-3" … ekler.
    ///
    /// <para><b>Neden gerekli (2026-07-30 canlı bug):</b> ad <c>yyyyMMdd-HHmmss_kamera_PLAKA</c>
    /// biçimindeydi ve aynı SANİYE içinde iki olay üretilebiliyor (aynı aracın roi + gozlem okuması,
    /// ya da hızlı ardışık emit). Bu durumda ikinci olay birincinin kırpıntısını VE tam karesini
    /// SESSİZCE EZİYORDU. ÖLÇÜM: 2033 olayda 12 çakışma (%1,2) → 12 olayın kanıtı başka bir ana ait.
    /// Sonuç zincirleme: o olayın <see cref="PlateEvent.FrameBox"/>'ı EZİLMİŞ kareye işaret ediyor,
    /// kırpıntı kayıyor ve üzerine kurulan gözle doğrulama / etiketli set BOZULUYOR — bu bug tam da
    /// etiketli set kurarken bir kırpıntının boş çıkmasıyla yakalandı (34EV5774).</para>
    ///
    /// <para>Zaman damgasına milisaniye eklemek çakışmayı pratikte bitirir; bu döngü ikinci savunmadır
    /// (aynı milisaniyede iki olay). <paramref name="taken"/> enjekte edilir ki mantık dosya sistemi
    /// olmadan test edilebilsin.</para>
    /// </summary>
    internal static string EnsureUniqueStem(string stem, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(stem);
        ArgumentNullException.ThrowIfNull(taken);
        if (!taken(stem)) return stem;
        for (int i = 2; i < 100; i++)
        {
            string candidate = $"{stem}-{i}";
            if (!taken(candidate)) return candidate;
        }
        return $"{stem}-{Guid.NewGuid():N}"[..Math.Min(stem.Length + 9, 120)];
    }

    internal static string? AlignedStem(string cropFileName, string finalCompact)
    {
        if (string.IsNullOrEmpty(cropFileName) || string.IsNullOrEmpty(finalCompact)) return null;
        string stem = Path.GetFileNameWithoutExtension(cropFileName);
        int i = stem.LastIndexOf('_');
        if (i < 0) return null;                                   // beklenmedik ad: dokunma
        return stem[(i + 1)..] == finalCompact ? null : stem[..(i + 1)] + finalCompact;
    }

    /// <summary>
    /// Kanıt dosyalarını olayın nihai plakasına göre yeniden adlandırır. SaveEvidence bilinçli olarak
    /// füzyon/snap'ten ÖNCE çağrılır (TryFuse bucket'ı dispose ediyor), bu yüzden dosya adı DÜZELTME
    /// ÖNCESİ ham okumayı taşır — ölçüm (2026-07-28): snap uygulanan 10 olayın 10'unda dosya adı ≠ olay
    /// plakası. Panel etkilenmez (yolu olay taşır) ama operatör dizinde plakaya göre ARAYINCA bulamaz.
    /// Taşıma başarısız olursa eski ad ve eski yollar aynen korunur — kanıt asla kaybolmaz.
    /// </summary>
    private (string? Crop, string? Frame) AlignEvidenceName(string? crop, string? frame, string finalCompact)
    {
        if (crop == null) return (crop, frame);
        try
        {
            string? newStem = AlignedStem(crop, finalCompact);
            if (newStem == null) return (crop, frame);            // zaten hizalı

            string dir = Path.Combine(AppContext.BaseDirectory, "evidence");
            string oldStem = Path.GetFileNameWithoutExtension(crop);
            string newCrop = newStem + ".jpg";
            if (File.Exists(Path.Combine(dir, newCrop))) return (crop, frame);   // ad çakışması: dokunma
            File.Move(Path.Combine(dir, oldStem + ".jpg"), Path.Combine(dir, newCrop));

            string? newFrameRel = frame;
            if (frame != null)
            {
                string cand = newStem + "_full.jpg";
                try
                {
                    if (!File.Exists(Path.Combine(dir, cand)))
                    {
                        File.Move(Path.Combine(dir, oldStem + "_full.jpg"), Path.Combine(dir, cand));
                        newFrameRel = "evidence/" + cand;
                    }
                }
                catch { /* kare taşınamadı: eski yol geçerli kalır */ }
            }
            return ("evidence/" + newCrop, newFrameRel);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[{Cam}] kanıt adı hizalanamadı: {Msg}", _camera.Id, ex.Message);
            return (crop, frame);
        }
    }

    private int _evidenceCount = -1;
    /// <summary>
    /// Kova yoksa (tüm kareler TR-geçersiz okunmuş, plaka yalnız uzlaşıdan doğmuş) kanıtı
    /// UZAMSAL olarak eşleşen yedek kırpıntıdan üretir. Eşleşme yoksa GÜRÜLTÜLÜ uyarır —
    /// eskiden bu durum sessizce kanıtsız olay üretiyordu ve fark edilmesi 4 gün aldı.
    /// </summary>
    private (string? Crop, string? Frame, PlateBox? FrameBox) SaveFallbackEvidence(PlateReading best, DateTimeOffset now)
    {
        var window = TimeSpan.FromSeconds(Math.Max(1, _options.DedupWindowSeconds));
        var view = new List<(PlateBox, float, DateTimeOffset)>(_fallbackCrops.Count);
        foreach (var f in _fallbackCrops) view.Add((f.Box, f.Ocr, f.At));
        int idx = PickFallbackCrop(view, best.Box, now, window);
        if (idx < 0)
        {
            _logger.LogWarning("[{Cam}] '{Plate}' için KANIT ÜRETİLEMEDİ (kova yok, uzamsal yedek de yok) — " +
                "operatör bu olayı gözle doğrulayamaz", _camera.Id, M(best.Text));
            return (null, null, null);
        }

        var pick = _fallbackCrops[idx];
        string dir = Path.Combine(AppContext.BaseDirectory, "evidence");
        Directory.CreateDirectory(dir);
        string stem = EnsureUniqueStem(
            $"{now.ToLocalTime():yyyyMMdd-HHmmssfff}_{_camera.Id}_{Compact(best.Text)}",
            s => File.Exists(Path.Combine(dir, s + ".jpg")));

        using SKBitmap? wide = _options.EvidencePaddingRatio > 0 && pick.Frame != null && pick.Box.Width > 0
            ? CropFromFrame(pick.Frame, pick.Box, _options.EvidencePaddingRatio)
            : null;
        EncodeJpeg(wide ?? pick.Bmp, Path.Combine(dir, stem + ".jpg"), _options.EvidenceJpegQuality);

        string? frameName = null;
        if (pick.Frame != null)
        {
            frameName = stem + "_full.jpg";
            File.WriteAllBytes(Path.Combine(dir, frameName), pick.Frame.ToJpeg(_options.EvidenceJpegQuality));
        }
        _logger.LogInformation("[{Cam}] '{Plate}' kanıtı UZAMSAL YEDEKTEN üretildi (tüm kareler TR-geçersiz " +
            "okunmuştu; kutular kesişiyor, ocr={Ocr:P0})", _camera.Id, M(best.Text), pick.Ocr);
        return (Path.Combine("evidence", stem + ".jpg"),
                frameName == null ? null : Path.Combine("evidence", frameName),
                pick.Frame == null ? null : pick.Box);
    }

    private (string? Crop, string? Frame, PlateBox? FrameBox) SaveEvidence(PlateReading best, DateTimeOffset now)
    {
        try
        {
            var bucket = FindBucket(Compact(best.Text));
            if (bucket == null || bucket.Crops.Count == 0)
                return SaveFallbackEvidence(best, now);

            var top = bucket.Crops[0];
            for (int i = 1; i < bucket.Crops.Count; i++)
                if (bucket.Crops[i].Conf > top.Conf) top = bucket.Crops[i];

            string dir = Path.Combine(AppContext.BaseDirectory, "evidence");
            Directory.CreateDirectory(dir);
            // Milisaniye ŞART: saniye çözünürlüğünde aynı saniyeye düşen iki olay birbirinin
            // kanıtını eziyordu (ölçüm: 2033 olayda 12 çakışma). Ayrıca ikinci savunma olarak
            // ad zaten alınmışsa "-2" eklenir.
            string stem = EnsureUniqueStem(
                $"{now.ToLocalTime():yyyyMMdd-HHmmssfff}_{_camera.Id}_{Compact(best.Text)}",
                s => File.Exists(Path.Combine(dir, s + ".jpg")));
            string cropName = stem + ".jpg";

            // KANIT KADRAJI (operatör doğrulaması için) OCR kadrajından AYRIDIR: OCR kırpıntısı bilinçli
            // olarak dardır ve kırpıntıların ölçülebilir bir kısmında plaka kenara değiyor, son karakter
            // kadraj dışında kalabiliyor (2026-07-29 bağımsız ölçüm: sağ pay < 0,20 karakter olan okuma
            // oranı %21,4; < 0,05 için %16,7. Daha önce burada yazan %60,7 rakamı yeniden ÜRETİLEMEDİ,
            // alıntılanmamalı). Tam kare elimizdeyken plakayı çevresiyle birlikte YENİDEN kırp;
            // bu gerçek piksel kazancıdır (büyütme değil). Tam kare yoksa eski davranışa düşülür.
            // Kutu, kareyle AYNI ana ait olmalı (bucket.BestFrameBox) — best.Box başka bir kareden gelir
            // ve hareketli araçta kadrajı kaydırır. Kutu yoksa (eski/boş bucket) kadraj üretme.
            using SKBitmap? wide = _options.EvidencePaddingRatio > 0 && bucket.BestFrameBox.Width > 0
                ? CropFromFrame(bucket.BestFrame, bucket.BestFrameBox, _options.EvidencePaddingRatio)
                : null;
            EncodeJpeg(wide ?? top.Bmp, Path.Combine(dir, cropName), _options.EvidenceJpegQuality);

            // Tam kare (araç görselleme): en iyi okumanın karesi. JPEG kare yeniden kodlanmadan yazılır;
            // ham (BGRA) kare yalnız BURADA, emit anında kodlanır (kare başına değil — Faz 2).
            string? frameName = null;
            if (bucket.BestFrame != null)
            {
                frameName = stem + "_full.jpg";
                File.WriteAllBytes(Path.Combine(dir, frameName), bucket.BestFrame.ToJpeg(_options.EvidenceJpegQuality));
            }

            // Disk sınırı: her 100 kayıtta bir — (a) KVKK saklama süresi dolanlar, (b) adet fazlası budanır.
            // Crop ve tam kare aynı dizinde birlikte döner (adet sınırı ikisini de sayar).
            if (_evidenceCount < 0 || ++_evidenceCount % 100 == 0)
            {
                // Dizin TEK KEZ taranır: eskiden süre-budamasından sonra ikinci bir GetFiles yapılıyordu;
                // 6000+ dosyalık dizinde bu, kamera işleme döngüsünün İÇİNDE iki tam dizin taraması demekti.
                // Silinenleri listeden düşmek aynı sonucu verir, ikinci taramaya gerek yok.
                var files = new DirectoryInfo(dir).GetFiles("*.jpg").ToList();
                if (_options.EvidenceRetentionDays > 0)
                {
                    var cutoff = DateTime.UtcNow.AddDays(-_options.EvidenceRetentionDays);
                    int removed = files.RemoveAll(f =>
                    {
                        if (f.LastWriteTimeUtc >= cutoff) return false;
                        try { f.Delete(); return true; }
                        catch { return false; }   // silinemedi: listede kalsın, sayım gerçeği yansıtsın
                    });
                    if (removed > 0)
                        _logger.LogInformation("kanıt saklama süresi doldu: {N} dosya silindi", removed);
                }
                _evidenceCount = files.Count;
                int excess = files.Count - Math.Max(100, _options.EvidenceMaxFiles);
                if (excess > 0)
                {
                    // KVKK/işletme görünürlüğü: adet tavanı, saklama-GÜN hedefini SESSİZCE ezebilir.
                    // Ölçüldü (2026-07-27): 6000 dosya tavanı = ~3000 olay; 1175 olay/gün'lük sahada bu
                    // EFEKTİF ~2.6 GÜN demek — oysa EvidenceRetentionDays 30 gün "iddia" ediyordu. Eski bir
                    // ihtilafta kanıtın olmadığı ancak o an fark edilirdi. Artık yüksek sesle söylenir.
                    if (_options.EvidenceRetentionDays > 0 && files.Count > 0)
                    {
                        double effDays = (DateTime.UtcNow - files.Min(f => f.LastWriteTimeUtc)).TotalDays;
                        if (effDays < _options.EvidenceRetentionDays)
                            _logger.LogWarning(
                                "kanıt ADET tavanı ({Max} dosya) saklama hedefini eziyor: efektif saklama ~{Eff:F1} gün " +
                                "< EvidenceRetentionDays={Target}. Daha uzun kanıt için EvidenceMaxFiles artırılmalı (disk!).",
                                _options.EvidenceMaxFiles, effDays, _options.EvidenceRetentionDays);
                    }
                    foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc).Take(excess))
                        try { f.Delete(); } catch { /* yoksay */ }
                }
            }

            // Kareyle TUTARLI kutu da taşınır: PlateEvent.Box (best.Box) başka bir kareden gelebilir,
            // bu yüzden kaydedilen _full.jpg üzerinde denetim yapılamıyordu (ölçüm: olayların %24,3'ü).
            return ("evidence/" + cropName, frameName != null ? "evidence/" + frameName : null,
                    frameName != null && bucket.BestFrameBox.Width > 0 ? bucket.BestFrameBox : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[{Cam}] kanıt kaydı hatası: {Msg}", _camera.Id, ex.Message);
            return (null, null, null);
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

    // Varyant-bastırma kararı (saf/test-edilebilir). Yeni okuma, yakın bir önceki emitin AYNI geçişinin
    // hayalet varyantı mı? H1 DÜZELTMESİ: aday-eşleşmesi artık güven-korumalı — model ≥0.99 okuduğunda
    // GERÇEK farklı araç sayılır (kuyrukta 2. araç bastırılmaz), <0.99 belirsiz eşleşme hayalet sayılır.
    // Kural 2 (düşük-güven yakın-varyant) ve Kural D (müdavim varyantı) zaten güven-korumalıydı.
    internal static bool IsVariantOfRecentEmit(
        string compact, HashSet<string> candSet, float ocrConf, bool isFrequent,
        string recentCompact, HashSet<string> recentCandidates, float recentOcrConf, bool recentIsFrequent,
        float manualReviewMinOcr)
    {
        if (recentCompact == compact) return true;                                   // aynı plaka tekrar-emit → dedup
        bool candMatch = recentCandidates.Contains(compact) || candSet.Contains(recentCompact);
        // KESİK VARYANT (2026-07-29 canlı vakası, kullanıcı bildirdi): yeni okuma önceki emitin KESİN
        // ÖN-EKİ ise güven tavanı UYGULANMAZ. Gerekçe: araç uzaklaşıp kutu küçüldüğünde OCR kırpıntısı
        // son karakteri kesebiliyor; karakter kırpıntının DIŞINDA kaldığı için "burada karakter yok"
        // olasılığı ~1.0 olur ve güven ŞİŞER (34YR9926 → "34YR992" ocr=0.9977). Şişmiş güven tam da
        // koruma tavanlarının üstüne çıktığı için üç kapıyı birden (bu 0.99, Kural 2'nin 0.95, snap'in
        // 0.99) devre dışı bırakıyordu — yani hata, kendi ürettiği güvenle korunuyordu.
        // İki kanıt BİRLİKTE aranır: (a) yeni okuma daha KISA ve tam ön-ek, (b) önceki emit bu dizgiyi
        // zaten ADAY olarak taşıyor (canlı vakada 0.648). Tek başına ön-ek yetmez: "34 HEC 26" gibi
        // gerçek kısa plakalar mevcuttur ve ayrı bir araç olabilir. Ters yön (yeni okuma DAHA UZUN)
        // bilinçli olarak muaf değildir — orada uzun okuma düzelmiş okuma olabilir.
        if (candMatch && compact.Length < recentCompact.Length
            && recentCompact.StartsWith(compact, StringComparison.Ordinal))
            return true;                                                             // kesik ön-ek varyantı
        if (candMatch && ocrConf < 0.99f) return true;                               // H1: yalnız belirsiz aday-eşleşmesi
        // Kural 2 — YAKIN-VARYANT HAYALETİ (2026-07-28 ölçümüyle yeniden kalibre edildi).
        // Eski koşul (ocrConf < manualReviewMinOcr=0.85 && recentOcrConf >= 0.9) canlı vakaların
        // HİÇBİRİNİ yakalamıyordu; iki ayrı kaçış yolu vardı: (a) ilk emit 0.85-0.87 bandındaysa
        // "recentOcrConf >= 0.9" tutmuyor, (b) hayalet okuma 0.85-0.86 bandındaysa "ocrConf < 0.85"
        // tutmuyor. Sonuç: aynı araç 2-4 sn arayla İKİ olay üretiyordu ve ikincisi VAR OLMAYAN bir
        // plakaydı — biri (25KUY368→25KUY278, 0.864) LowConfidence=false ile otomatik bariyer açıyordu.
        // Yeni ölçüt fiziksel gerçeğe dayanır: hayalet varyant, ürediği gerçek okumadan DAHA DÜŞÜK
        // güvenlidir (canlı 4 vakanın 4'ünde de öyle). ÖLÇÜM (4 ardışık yakın-varyant çifti, kamera
        // ANPR'ı referans): eski kural 0 hayalet kesiyordu; bu kural 4/4 kesiyor ve GERÇEK araç kaybı 0.
        // 0.95 tavanı bilinçli: 0.95 üstü okumalar "belirsiz" sayılmaz, kuyruktaki gerçek ikinci araç
        // (PlateConfirmer'daki mekânsal ayırma ile birlikte) korunur.
        if (ocrConf < 0.95f && recentOcrConf > ocrConf && Levenshtein(compact, recentCompact) <= 2)
            return true;                                                             // Kural 2
        if (ocrConf < 0.95f && !isFrequent && recentIsFrequent && Levenshtein(compact, recentCompact) <= 2)
            return true;                                                             // Kural D — müdavim varyantı
        return false;
    }

    /// <summary>
    /// Küratörlü TAM-DİZGİ düzeltmesini okumaya uygular (saf fonksiyon — I/O ve log yok, bu yüzden
    /// test edilebilir). Eşleşme yoksa ya da hedef geçersizse okuma AYNEN döner.
    /// <para>Aday listesi de güncellenir: <c>Candidates[0] == PlateCompact</c> değişmezi bu projede
    /// belgelenmiş bir sözleşmedir (PMS aday kümesini böyle okur); düzeltilmiş plaka başa alınmazsa
    /// olay kendi aday listesiyle çelişirdi.</para>
    /// </summary>
    internal static (bool Applied, PlateReading Reading, string Compact, string? SnappedFrom)
        ApplyCuratedCorrection(PlateReading best, string compact, PlateCorrectionMap corrections)
    {
        if (corrections is null || !corrections.HasEntries) return (false, best, compact, null);

        string fixedPlate = corrections.Apply(compact);
        if (fixedPlate == compact) return (false, best, compact, null);

        // Hedef ikinci kez doğrulanır: harita kurulurken de bakıldı, ama bu fonksiyon haritayı
        // kuran koddan bağımsız çağrılabilir olmalı (geçersiz plaka bariyeri açamaz).
        var tr = Anpr.Core.TurkishPlates.TurkishPlateNormalizer.Normalize(fixedPlate);
        if (!tr.IsValid) return (false, best, compact, null);

        var (cands, rank, ocr) = RebuildCandidatesForSnap(best.Candidates, tr.Compact!, best.OcrConfidence);

        return (true,
                best with
                {
                    Text = tr.Normalized!, RawText = tr.Compact!, Candidates = cands,
                    SnapTargetRank = rank, SnapTargetOcr = ocr,
                },
                tr.Compact!,
                compact);
    }

    /// <summary>
    /// Whitelist/müdavim SNAP'ini uygular (saf fonksiyon — I/O ve log yok, bu yüzden test edilebilir).
    /// Eşleşme yoksa ya da hedef geçersizse okuma AYNEN döner.
    /// <para><b>Neden ayrı fonksiyon (2026-08-22, §P.4 (366)):</b> bu mantık emit yolunda SATIR-İÇİ
    /// duruyordu ve <c>ApplyCuratedCorrection</c> ile aynı aday-yeniden-yazma desenini <b>kopyalıyordu</b>.
    /// Enstrüman eklenirken kırmızı-yeşil denemesi bunu yakaladı: <c>SnapTargetRank</c>'ı bu yolda
    /// <c>null</c>'a sabitlemek <b>hiçbir testi düşürmüyordu</b> — yani ikinci yol ölçümsüz kalabilir
    /// ve kimse fark etmezdi. Çıkarım: kopyalanan desenin ikinci kopyası, test edilemediği için
    /// sessizce ayrışır. Güven eşiği (<c>SnapMaxOcrConfidence</c>) bilinçli olarak ÇAĞIRANDA kaldı —
    /// o bir ayar kapısıdır, snap'in kendisi değil.</para>
    /// </summary>
    internal static (bool Applied, PlateReading Reading, string Compact, string? SnappedFrom)
        ApplyFrequentSnap(PlateReading best, string compact, FrequentPlateRegistry frequent,
                          int maxLev, bool snapToFrequent)
    {
        string snapped = frequent.SnapTarget(compact, maxLev, snapToFrequent);
        if (snapped == compact) return (false, best, compact, null);

        var tr = Anpr.Core.TurkishPlates.TurkishPlateNormalizer.Normalize(snapped);
        if (!tr.IsValid) return (false, best, compact, null);

        var (cands, rank, ocr) = RebuildCandidatesForSnap(best.Candidates, tr.Compact!, best.OcrConfidence);
        return (true,
                best with
                {
                    Text = tr.Normalized!, RawText = tr.Compact!, Candidates = cands,
                    SnapTargetRank = rank, SnapTargetOcr = ocr,
                },
                tr.Compact!,
                compact);
    }

    /// <summary>
    /// Snap sonrası aday listesini kurar <b>ve snap'in OCR tarafından desteklenip desteklenmediğini
    /// ÖLÇER</b>. İki snap yolu da (küratörlü tam-eşleşme + whitelist/müdavim) bunu kullanır — desen
    /// eskiden iki yerde kopyalanmıştı ve ölçüm eklenirken ikisinden biri unutulabilirdi.
    /// <para>Sözleşme korunur: hedef <b>başa</b> alınır (<c>Candidates[0] == PlateCompact</c>) ve
    /// <paramref name="borrowedConf"/> ile yazılır — bu, ham okumanın güvenidir, hedefin DEĞİL
    /// (§P.4 (365)). Hedefe eşit olan özgün aday listeden elenir; onun sırası/skoru <b>elenmeden
    /// önce</b> okunup dışa verilir ki ölçüm boşluğu kapansın.</para>
    /// </summary>
    internal static (List<PlateCandidate> Candidates, int? TargetRank, float? TargetOcr)
        RebuildCandidatesForSnap(IReadOnlyList<PlateCandidate>? pre, string targetCompact, float borrowedConf)
    {
        int? rank = null;
        float? ocr = null;
        if (pre != null)
            for (int i = 0; i < pre.Count; i++)
                if (pre[i].Compact == targetCompact) { rank = i + 1; ocr = pre[i].Confidence; break; }

        var cands = new List<PlateCandidate> { new(targetCompact, borrowedConf) };
        if (pre != null)
            foreach (var c in pre) if (c.Compact != targetCompact) cands.Add(c);
        return (cands, rank, ocr);
    }

    /// <summary>
    /// Bu kare, en son işlenenden FARKLI bir kare mi? Ölçüt REFERANS KİMLİĞİ — içerik değil.
    /// <para><b>Neden gerekli (2026-07-31 canlı bulgu):</b> <c>TryGetLatestFrame()</c> yalnız son kare
    /// referansını okur, "yeni kare geldi mi" takibi YOKTUR. Döngü kaynaktan hızlı yoklarsa AYNI
    /// <c>byte[]</c> tekrar döner ve yeni bir gözlemmiş gibi işlenirdi. Canlı kanıt: <c>/health</c>
    /// çıkışta işlenen (4187) &gt; alınan (4165). Araç varken döngü activeFps (5) hızında, kaynak
    /// 2 fps üretir → her fiziksel kare ~2 kez işleniyordu.</para>
    /// <para><b>Neden ciddi:</b> kopyalar <c>PlateConfirmer</c>'a AYRI gözlem gibi girer ve
    /// <c>MinConfirmations=2</c> TEK BİR KAREDEN karşılanabilir — "çok-kareli uzlaşı" güvencesi
    /// (ölçülmüş katkı +6,4 puan) kısmen aynı karenin tekrarına dayanır. Kopya bağımsız kanıt
    /// DEĞİLDİR: aynı piksellerden aynı sonuç çıkar.</para>
    /// <para><b>Neden içerik karşılaştırılmaz:</b> statik sahnede iki AYRI kare bit-bit aynı olabilir;
    /// onları atlamak gerçek gözlem kaybı olurdu. Kaynak her kare için ayrı dizi ürettiğinden
    /// referans eşitliği kesin ve O(1)'dir.</para>
    /// </summary>
    internal static bool IsNewFrame(object? frame, object? lastProcessed)
        => frame is not null && !ReferenceEquals(frame, lastProcessed);

    /// <summary>
    /// SON-HANE BELİRSİZLİĞİ (2026-07-29, saf/test-edilebilir): ilk iki aday yalnızca SON karakterde
    /// farklıysa (biri diğerinin tam ön-eki, uzunluk farkı 1) VE güven farkı <paramref name="maxGap"/>
    /// altındaysa, model son karakterin VAR olup olmadığına karar VEREMEMİŞ demektir.
    /// <para>Neden ön-ek çiftinin kendisi yetmez: ölçümde olayların %83,8'inde zaten bir ön-ek çifti var
    /// (OCR doğal olarak "bir kısa bir uzun" üretir). Ayırt edici olan FARK — gerçekten emin olduğu
    /// okumalarda fark büyük (ör. 0,45-0,52), belirsizken küçük (0,000-0,092).</para>
    /// <para>ÖLÇÜM (1308 canlı roi olayı, eşik 0,10): 33 tetiklenme (%2,5), 20'si ZATEN
    /// LowConfidence=true (kapı onlara sıfır katkı) → gerçek marjinal etki <b>13 yeni işaret (%1,0)</b>,
    /// bunların <b>4'ü bilinen/şüpheli yanlış okuma</b> (34ZBG21, 25UJA59, 25JMA77, 25UJB72).
    /// Yani 4 hayalet bariyer açılması engellenirken 9 doğru okuma operatöre düşer — marjinal kesinlik
    /// %31. Bilinen yanlış-alarm sınıfı: gerçekten 2 haneli plakalar (ör. "34 HEC 26").</para>
    /// ⚠️ 2026-08-10: bu blok 40 satır YUKARIDA, <c>ApplyCuratedCorrection</c>'ın üstünde ÖKSÜZ
    /// duruyordu (iki <c>&lt;summary&gt;</c> üst üste) — yani küratörlü düzeltmenin belgesini okuyan
    /// biri TAMAMEN BAŞKA bir konuyu okuyordu, ve <c>maxGap</c> paramref'i CS1734 üretiyordu.
    /// Sahibine taşındı; CS1734 artık yayımlanan paketlerde HATA (bkz. csproj WarningsAsErrors).
    /// </summary>
    internal static bool HasAmbiguousLastChar(IReadOnlyList<PlateCandidate>? candidates, float maxGap)
    {
        if (maxGap <= 0f || candidates is not { Count: >= 2 }) return false;
        string a = candidates[0].Compact, b = candidates[1].Compact;
        bool prefix = (a.Length == b.Length + 1 && a.StartsWith(b, StringComparison.Ordinal))
                   || (b.Length == a.Length + 1 && b.StartsWith(a, StringComparison.Ordinal));
        return prefix && Math.Abs(candidates[0].Confidence - candidates[1].Confidence) < maxGap;
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
        (string? evidence, string? framePath, PlateBox? frameBox) =
            _options.EvidenceCrops ? SaveEvidence(best, now) : (null, null, (PlateBox?)null);

        // Bulanık/düşük güvenli uzlaşı: çok-kareli füzyonla ikinci şans (yalnız bölge-içi, araç başına 1 kez).
        if (scope == "roi" && _options.MultiFrameFusion && best.OcrConfidence < _options.FusionTriggerOcr)
            best = TryFuse(best) ?? best;

        string compact = Compact(best.Text);
        if (compact.Length == 0) return;

        string? snappedFrom = null;   // snap uygulanırsa snap ÖNCESİ ham okuma (denetim kanıtı; PlateEvent.SnappedFrom)

        // KÜRATÖRLÜ TAM-DİZGİ DÜZELTME — bulanık snap'ten ÖNCE ve GÜVEN KAPISI OLMADAN.
        //
        // 🔴🔴 AŞAĞIDAKİ GEREKÇE OLGUSAL OLARAK YANLIŞTIR — YÖNÜ TERS (2026-08-05'te kanıtla saptandı).
        // Aracın GERÇEK plakası '25 LMV 166'dir, '25 LMY 166' DEĞİL. Üç bağımsız kanıt:
        //   · KANIT GÖRSELLERİ: dört ayrı geçişin kırpıntısı da tartışmasız "25 LMV 166" okunuyor —
        //     sistemin '25LMY166' yayınladığı olayların kırpıntıları DÂHİL.
        //   · KAMERANIN BAĞIMSIZ KAYDI: '25LMV166' 124 kez, '25LMY166' yalnız 1 kez.
        //   · YER-GERÇEĞİ: '25LMV166' yayınlayan 9 olay 'Dogru'; '25LMY166' yayınlayan 7 olay
        //     'Yanlis' + Truth='25LMV166'.
        // Yani bu gerekçeyle kurulan düzeltme, DOĞRU okumaları YANLIŞA çeviriyordu: ölçülen zarar
        // 7 bozulmuş olay (§P.4 2026-08-04 (48) — "snap doğru okumayı bozuyor" bulgusunun kaynağı).
        //
        // ⚠️ ŞU AN ZARARSIZ AMA TUZAK: `PlateCorrections` haritası sahada BOŞ ({}), yani düzeltme
        // AKTİF DEĞİL. Tehlike koddaki bu yorumun kendisidir — "ölçüldü" diye sunulan yanlış bir
        // gerekçe, aynı bozuk düzeltmenin yeniden eklenmesini HAKLI gösterir. Bu yüzden silinmeyip
        // BAŞINA bu uyarı konuldu (ölçüm silinmez, çürütülür).
        // 📌 DERS: "39 kez aynı şekilde okundu" bir DOĞRULUK kanıtı değildir — tekrar eden bir yanlış
        //    okuma da tam olarak bunu üretir. Yön ancak BAĞIMSIZ bir kaynakla (kanıt görseli, karşı
        //    kameranın kaydı) belirlenir. Aşağıdaki metin bu hatanın kaydı olarak DURUYOR:
        //
        // Gerekçe (2026-07-31 canlı ölçüm): '25 LMY 166' plakalı araç 39 kez '25LMV166' okundu ve
        // 39'u da bariyeri OTOMATİK açtı (güven 0,852–0,999; 27'si ≥0,99). Güven skoru bu hatayı
        // göremez (model emin ve yanlış), kamera çaprazı göremez (aynı yönde yanılıyor), bulanık snap
        // göremez (SnapMaxOcrConfidence kapısı tam bu okumaları geçiriyor ve kapıyı kaldırmak
        // ÖLÇÜLDÜ: yanlış-snap 1→11). Güven kapısı BULANIK eşleşmenin korumasıydı; tam-dizgi
        // eşleşme lev1 komşuya kayma riskini yapısal olarak taşımadığı için burada uygulanmaz.
        // Varsayılan harita BOŞ → kutudan çıkan üründe davranış birebir aynıdır.
        if (scope == "roi")
        {
            var fix = ApplyCuratedCorrection(best, compact, _corrections);
            if (fix.Applied)
            {
                _logger.LogInformation("[{Cam}] plaka düzeltme (küratörlü tam-eşleşme): '{Old}' -> '{New}'",
                    _camera.Id, M(best.Text), M(fix.Reading.Text));
                best = fix.Reading;
                compact = fix.Compact;
                snappedFrom = fix.SnappedFrom;   // denetim izi: düzeltme öncesi ham okuma
            }
        }

        // Whitelist/müdavim SNAP: okuma bilinen bir plakaya lev≤SnapMaxLev yakınsa düzelt (siyah resmi
        // plaka gibi frekanslı araçların sistematik yanlış-okumasını düzeltir; ölçüm: lev≤1 siyah +27pp).
        // Güven-kapısı: yüksek-güvenli okuma (gerçek farklı plaka olabilir) SnapMaxOcrConfidence ile korunur.
        if (scope == "roi" && _options.SnapMaxLev > 0 && best.OcrConfidence < _options.SnapMaxOcrConfidence)
        {
            var snap = ApplyFrequentSnap(best, compact, _frequent, _options.SnapMaxLev, _options.SnapToFrequent);
            if (snap.Applied)
            {
                _logger.LogInformation("[{Cam}] plaka snap: '{Old}' -> '{New}' (bilinen/müdavim, lev≤{Lev})",
                    _camera.Id, M(best.Text), M(snap.Reading.Text), _options.SnapMaxLev);
                // Snap öncesi ham okumayı SAKLA: best.RawText de snap sonrası değerle yazılır,
                // bu alan olmadan düzeltme olayda izsiz kalır (canlı denetim/geri-bildirim imkânsızlaşır).
                snappedFrom = snap.SnappedFrom;
                best = snap.Reading;
                compact = snap.Compact;
            }
        }

        // Emit-seviyesi güven tabanı: birleşik/uzlaşı okumanın güveni de tabanı geçmeli
        // (taban kare-başında uygulanıyor ama uzlaşının güveni altına düşebilir -> sızıntıyı keser).
        if (best.OcrConfidence < _options.EmitMinOcrConfidence) return;
        if (best.Confidence < _options.EmitMinConfidence) return;

        // Varyant bastırma: aynı kapsamda kısa süre önce, bu plakayı ADAY olarak içeren bir emit
        // olduysa bu YENİ olay değil aynı geçişin hayalet varyantıdır -> yayınlama (25URB67 vakası).
        var candSet = new HashSet<string>();
        if (best.Candidates != null) foreach (var c in best.Candidates) candSet.Add(c.Compact);
        if (_options.VariantSuppressSeconds > 0)
        {
            var window = TimeSpan.FromSeconds(_options.VariantSuppressSeconds);
            _recentEmits.RemoveAll(r => now - r.At > window);
            foreach (var r in _recentEmits)
            {
                if (r.Scope != scope) continue;
                if (IsVariantOfRecentEmit(compact, candSet, best.OcrConfidence, _frequent.IsFrequent(compact),
                        r.Compact, r.Candidates, r.OcrConf, _frequent.IsFrequent(r.Compact), _options.ManualReviewMinOcr))
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

        // ÇAPRAZ-KAMERA KAPILARI — nihai plaka belli olduktan SONRA sorulur (snap/düzeltme
        // uygulanmışsa artık bozuk değildir, boşuna operatöre atmayalım).
        // ⚠️ Eskiden burada `bool weakerThanCrossCameraRead` da vardı: iki dalda da true'ya
        // set ediliyor ama HİÇBİR YERDE OKUNMUYORDU (CS0219). Aynı bilgiyi `crossCameraReason`
        // zaten taşıyor (null DEĞİLSE bayraklanmış demektir) ve satır ~1121'de listeye giriyor.
        // 2026-08-10'da kaldırıldı: KAPI MANTIĞI DEĞİŞMEDİ — ölü değişken, kapının çalışmadığı
        // izlenimi veriyordu; oysa 42 bayrağın 33/34'ü doğru çıkmıştı (canlı kanıt).
        string? crossCameraReason = null;
        if (scope == "roi")
        {
            if (_crossCameraEmits.IsTruncationOfRecent(_camera.Id, compact, best.OcrConfidence, now))
            {
                crossCameraReason = PlateEvent.ReasonCrossCameraTruncation;
                _logger.LogWarning("[{Cam}] '{Plate}' başka bir kameranın daha uzun+daha güvenli okumasının " +
                    "ÖN-EKİ (kesilme şüphesi) → operatör onayına düşürüldü", _camera.Id, M(best.Text));
            }
            else if (_crossCameraEmits.IsWeakVariantOfRecent(_camera.Id, compact, best.OcrConfidence, now))
            {
                crossCameraReason = PlateEvent.ReasonCrossCameraSubstitution;
                _logger.LogWarning("[{Cam}] '{Plate}' saniyeler içinde okunmuş aynı uzunlukta + tek karakter " +
                    "farklı + DAHA GÜVENLİ bir okumanın bozulmuş hâli olabilir (ikame şüphesi, ölçülmüş " +
                    "isabet %89) → operatör onayına düşürüldü", _camera.Id, M(best.Text));
            }
            // Kayda HER roi emit'i girer (bozuk olan da): sıradaki kamera onu referans alabilsin.
            //
            // GÖZLEM KATMANI BİLİNÇLİ OLARAK DIŞARIDA — ÖLÇÜLDÜ ve REDDEDİLDİ (2026-08-01).
            // Hipotez makuldü: gozlem ROI dışını da okur, dolayısıyla bariyer kanalının göremediği
            // doğru okumayı taşıyabilir (§P.4 2026-07-31 (10)'da `25LMY166` tam böyle bulunmuştu).
            // ÖLÇÜM (2505 roi + 339 gozlem): gozlem'i referans olarak eklemek 43 → 50 bayrak yapıyor
            // ama EK 7 bayrağın **0'ı gerçek hata**, buna karşılık **1'i gözle DOĞRU doğrulanmış**
            // bir okuma (`25ZD612`). En kötüsü: `25LMY166` (0,999 — `PlateCorrections`'ın ürettiği
            // DOĞRU plaka) gozlem'deki `25LMV166` (0,999, bilinen sistematik hata) yüzünden
            // bayraklanıyordu; yani düzelttiğimiz hatanın kendisi düzeltmeyi operatöre attırıyordu.
            // SEBEP: gozlem okumaları ROI dışından, daha uzak/eğik gelir ve güvenleri roi ile
            // KIYASLANABİLİR DEĞİLDİR — "daha güvenli" yön şartı orada anlamını yitiriyor.
            // Gözlem katmanı İNSAN incelemesi için aday üretmekte değerlidir (bkz. SuspectPairs),
            // OTOMATİK kapı referansı olarak değil.
            _crossCameraEmits.Record(_camera.Id, compact, best.OcrConfidence, now);
        }

        // Tüm kapılardan geçildi ve nihai plaka belli: kanıt dosya adını olayla hizala (snap/füzyon adı
        // değiştirmiş olabilir). Burada yapılır ki elenen olaylar için boşuna dosya taşınmasın.
        (evidence, framePath) = AlignEvidenceName(evidence, framePath, compact);

        // Operatör onayı nedenleri — TEK bool yerine gerekçeli liste (v4). Sıra bilinçli:
        // en verimli neden (çapraz-kamera, isabet ~%89) listede önce görünsün.
        var lowConfReasons = new List<string>(3);
        if (crossCameraReason is not null) lowConfReasons.Add(crossCameraReason);
        if (best.OcrConfidence < _options.ManualReviewMinOcr) lowConfReasons.Add(PlateEvent.ReasonWeakReading);
        if (HasAmbiguousLastChar(best.Candidates, _options.LastCharAmbiguityMaxGap))
            lowConfReasons.Add(PlateEvent.ReasonLastCharAmbiguity);

        // SEÇİCİ KORUMA (#75): bayraklı olayın kanıtını ADET tavanının erken tahliyesinden muaf tut.
        // Burada yapılır çünkü bayrak gerekçeleri ANCAK bu noktada bellidir ve kanıt adı da hizalanmıştır.
        // ⚠️ KVKK gün sınırı DEĞİŞMEZ — korunan dosyalara da aynı kesim uygulanır (PruneEvidence).
        if (_options.EvidencePreserveFlagged && lowConfReasons.Count > 0)
            (evidence, framePath) = PreserveEvidence(evidence, framePath);

        var evt = new PlateEvent
        {
            // Sürüm AÇIKÇA yazılır: varsayılana güvenmek, varsayılanın "alan yok" anlamıyla
            // ÇAKIŞMASINA yol açıyordu (bkz. PlateEvent.SchemaVersion remarks, 2026-08-10).
            SchemaVersion = PlateEvent.CurrentSchemaVersion,
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
            FrameBox = frameBox,
            Scope = scope,
            EvidencePath = evidence,
            FramePath = framePath,
            // Operatör onayı: güven tabanı VEYA son-hane belirsizliği. İkincisi, güvenin tek başına
            // göremediği "son karakter var mı yok mu" kararsızlığını yakalar (plakayı DEĞİŞTİRMEZ).
            // Operatör onayı: güven tabanı · son-hane belirsizliği · ÇAPRAZ-KAMERA BOZULMASI
            // (kesilme YA DA ikame). Üçüncüsü, güvenin YAPISAL olarak göremediği hatayı yakalar:
            // kesik okumanın güveni ŞİŞER (düşen karakterin pad'i "kesin"dir, ölçümde 0,998'e kadar),
            // ikamede ise hatalı okuma 0,994'e kadar güvenle geldi.
            LowConfidence = lowConfReasons.Count > 0,
            // v4: bayrağın GEREKÇESİ. Nedenlerin ölçülmüş isabeti %13 ile %89 arasında değişiyor;
            // tek bir bool bunları ayırt edilemez kılıyordu. Bayrağın ANLAMI değişmedi:
            // LowConfidence == (LowConfidenceReasons.Count > 0) — teste bağlandı.
            LowConfidenceReasons = lowConfReasons,
            Candidates = best.Candidates,
            SizeTrend = trend,
            SnappedFrom = snappedFrom,
            // (şema v7) Snap'in OCR tarafından desteklenip desteklenmediği — §P.4 (366).
            // Snap yoksa best üzerinde de null'dır, ayrı koşula gerek yok.
            SnapTargetRank = best.SnapTargetRank,
            SnapTargetOcr = best.SnapTargetOcr,
            // (şema v8) Geçiş başına kare sayısı — §P.4 (425). PlateConfirmer'da zaten tutuluyordu,
            // yalnız olaya yazılmıyordu; #69'un kadraj takası bu ölçüm olmadan varsayımla tartışılıyordu.
            TrackFrames = best.TrackFrames,
            TrackAbsorbed = best.TrackAbsorbed,
            // v5 (#70): erken-yayın tutulduysa ilk tutulma anındaki güven. null = tutulmadı.
            // Davranışa ETKİSİ YOK; takasın kazanç tarafını yer-gerçeğiyle eşleştirilebilir kılar.
            EarlyEmitHeldAtOcr = best.HeldAtOcr,
            EarlyEmitHeldAs = best.HeldAtText,
        };
        // (şema v9) boyut/mesafe sınırlarının ölçtüğü değer; mesafe yalnız kalibrasyonluyken dolu.
        evt = WithMeasuredSize(evt, best);

        EventsEmitted++;
        if (scope == "roi")
        {
            LastEventUtc = now; LastPlate = best.Text; // sağlıkta bariyer-kapsamı görünsün
            _plateWidths.Add(evt.PlateWidthPx);
        }

        _pendingPublishes.Add(PublishAsync(evt, _ct));
    }

    /// <summary>
    /// Bayraklı olayın kanıtını <c>evidence/preserved/</c> altına TAŞIR ve yeni göreli yolları döner.
    /// Alt dizin olduğu için ana budayıcının <c>GetFiles("*.jpg")</c> taramasına GİRMEZ — adet
    /// tavanından muafiyet buradan gelir. Taşıma başarısızsa dosya yerinde kalır ve ESKİ yol döner
    /// (koruma bir iyileştirmedir; başarısızlığı olayı kaybettirmemelidir).
    /// </summary>
    private (string?, string?) PreserveEvidence(string? crop, string? frame)
    {
        if (crop == null) return (crop, frame);
        try
        {
            string baseDir = Path.Combine(AppContext.BaseDirectory, "evidence");
            string keep = Path.Combine(baseDir, "preserved");
            Directory.CreateDirectory(keep);
            string? Move(string? rel)
            {
                if (rel == null) return null;
                string src = Path.Combine(AppContext.BaseDirectory, rel);
                if (!File.Exists(src)) return rel;
                string name = Path.GetFileName(rel);
                string dst = Path.Combine(keep, name);
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
                return "evidence/preserved/" + name;
            }
            return (Move(crop), Move(frame));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "kanıt korumaya alınamadı; dosya yerinde bırakıldı");
            return (crop, frame);
        }
    }

    /// <summary>
    /// Korunan alanı budar: (a) <see cref="AnprServiceOptions.EvidenceRetentionDays"/> gün kesimi
    /// AYNEN uygulanır — koruma KVKK süresini UZATMAZ; (b) kendi adet tavanı dolduğunda en eskiden
    /// başlayarak budanır — koruma bir ÖNCELİKTİR, sonsuz garanti değil.
    /// </summary>
    private void PrunePreserved()
    {
        try
        {
            string keep = Path.Combine(AppContext.BaseDirectory, "evidence", "preserved");
            if (!Directory.Exists(keep)) return;
            var files = new DirectoryInfo(keep).GetFiles("*.jpg").ToList();
            if (_options.EvidenceRetentionDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-_options.EvidenceRetentionDays);
                files.RemoveAll(f =>
                {
                    if (f.LastWriteTimeUtc >= cutoff) return false;
                    try { f.Delete(); return true; } catch { return false; }
                });
            }
            int excess = files.Count - Math.Max(50, _options.EvidencePreserveMaxFiles);
            if (excess > 0)
            {
                // Görünürlük: korunan alan da dolabilir. Sessizce budarsak "korundu" sanılır.
                _logger.LogWarning("korunan kanıt tavanı doldu ({Max}): {N} en eski dosya budanıyor. " +
                                   "Daha uzun koruma için EvidencePreserveMaxFiles artırılmalı (disk!).",
                                   _options.EvidencePreserveMaxFiles, excess);
                foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc).Take(excess))
                    try { f.Delete(); } catch { /* yoksay */ }
            }
            // Korunan alan ayrı budanır: gün kesimi AYNI, adet tavanı KENDİNE ait.
            if (_options.EvidencePreserveFlagged) PrunePreserved();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "korunan kanıt budaması başarısız");
        }
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
        // ÖNCE ffmpeg'i öldür (2026-07-28): bu adım döngüden bağımsız olarak HER ZAMAN güvenlidir ve
        // beklemeye ihtiyaç duymaz. Eskiden EN SONDA yapılıyordu — yani ffmpeg, 30 sn'lik döngü ve 5 sn'lik
        // yayın beklemesi boyunca yaşamaya ve RTSP oturumunu tutmaya devam ediyordu. Windows servis olarak
        // koşarken SCM toleransı (30 sn) bizim bütçemizden (worker başına 45 sn) küçük olduğu için süreç
        // SERT öldürülüyor ve ffmpeg öksüz kalıyordu → Hikvision eşzamanlı-oturum sınırı tükeniyor, yeniden
        // başlayan servis "ffmpeg başladı ama kare yok" kısır döngüsüne giriyordu (§P.4'te iki kez saha
        // vakası). Artık ilk iş: kaynağı kapat; kare akışı kesilince döngü de daha hızlı çıkar.
        await _frameSource.DisposeAsync();

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

        if (loopStopped)
        {
            foreach (var b in _cropBuckets) b.DisposeAll();
            _cropBuckets.Clear();
            foreach (var f in _fallbackCrops) f.Bmp.Dispose();
            _fallbackCrops.Clear();
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
