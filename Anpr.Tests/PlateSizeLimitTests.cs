using Anpr.Core;
using Anpr.Sdk;
using Anpr.Sdk.Configuration;
using Anpr.Sdk.Events;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// Okuma mesafesi / plaka boyutu sınırları ve mesafe kalibrasyonu (2026-10-27, gap analizi G1/G2).
/// Kilitlenen sözleşme: (1) varsayılanlarla davranış DEĞİŞMEZ, (2) kalibrasyon yoksa mesafe hiçbir yerde sayı
/// olarak üretilmez, (3) sınır DEDEKTÖR kutusuna (dolgusuz) uygulanır ve sınır değerinde okuma yapılır.
/// </summary>
public class PlateSizeLimitTests
{
    private static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    private static AnprEngine Engine(Action<AnprOptions>? tweak = null)
    {
        var o = new AnprOptions
        {
            DetectorModelPath = Path.Combine(ModelsDir, "yolo-v9-t-384-license-plates-end2end.onnx"),
            OcrModelPath = Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"),
            OcrConfigPath = Path.Combine(ModelsDir, "cct_xs_v1_global_plate_config.yaml"),
            OnnxIntraOpThreads = 1,
            MinConfidence = 0.25f,
        };
        tweak?.Invoke(o);
        return AnprEngine.Create(o);
    }

    private static byte[] Sample => File.ReadAllBytes(Path.Combine(ModelsDir, "sample_car.jpg"));

    private static (int PlateW, int FrameW) Baseline()
    {
        using var e = Engine();
        var r = Assert.Single(e.Recognize(Sample));
        using var bmp = SkiaSharp.SKBitmap.Decode(Sample);
        return (r.PlateWidthPx!.Value, bmp.Width);
    }

    // ---------------------------------------------------------------- kalibrasyon matematiği

    private static readonly PlateDistanceCalibration Cal = new()
    {
        ReferenceDistanceMeters = 6.0, ReferencePlateWidthPx = 150, ReferenceFrameWidthPx = 2688,
    };

    [Theory]
    [InlineData(150, 2688, 6.0)]   // referansın kendisi
    [InlineData(100, 2688, 9.0)]   // daha dar → daha uzak (ters orantı)
    [InlineData(300, 2688, 3.0)]
    [InlineData(75, 1344, 6.0)]    // yarım çözünürlükte aynı plaka yarım genişlikte → aynı mesafe
    public void Calibration_estimates_inverse_to_width_and_scales_with_resolution(int w, int frameW, double expected)
        => Assert.Equal(expected, Cal.EstimateMeters(w, frameW)!.Value, 6);

    [Fact]
    public void Calibration_width_at_distance_is_the_inverse()
        => Assert.Equal(100.0, Cal.WidthPxAt(9.0, 2688), 6);

    [Theory]
    [InlineData(0, 150, 2688)]
    [InlineData(6, 0, 2688)]
    [InlineData(6, 150, 0)]
    [InlineData(6, 3000, 2688)]    // plaka kareden geniş olamaz
    public void Invalid_calibration_never_produces_a_distance(double z, double w, int frameW)
    {
        var c = new PlateDistanceCalibration { ReferenceDistanceMeters = z, ReferencePlateWidthPx = w, ReferenceFrameWidthPx = frameW };
        Assert.False(c.IsValid);
        Assert.Null(c.EstimateMeters(150, 2688));
    }

    // ---------------------------------------------------------------- etkin sınırlar

    [Fact]
    public void Default_options_have_no_limits() => Assert.Equal((0, 0), new AnprOptions().ResolvePlateWidthLimits(2688));

    [Fact]
    public void Distance_limits_convert_to_pixel_limits()
    {
        var o = new AnprOptions { DistanceCalibration = Cal, MaxReadDistanceMeters = 9.0, MinReadDistanceMeters = 3.0 };
        Assert.Equal((100, 300), o.ResolvePlateWidthLimits(2688));
    }

    [Fact]
    public void Float_residue_does_not_shift_the_limit_by_one_pixel()
    {
        // 4,0 × 153 / 5,1 = 120 tam, ama double'da 120,00000000000001 → saf Ceiling 121 verir ve TAM 5,1 m'deki
        // plakayı atardı (inceleme bulgusu, 2026-10-27).
        var cal = new PlateDistanceCalibration { ReferenceDistanceMeters = 4.0, ReferencePlateWidthPx = 153, ReferenceFrameWidthPx = 2688 };
        var o = new AnprOptions { DistanceCalibration = cal, MaxReadDistanceMeters = 5.1, MinReadDistanceMeters = 5.1 };
        Assert.Equal((120, 120), o.ResolvePlateWidthLimits(2688));
    }

    [Fact]
    public void Near_limit_never_rounds_down_to_off()
    {
        // 0 px "sınır yok" demek: absürt bir yakın mesafe üst sınırı SESSİZCE kapatmamalı.
        var o = new AnprOptions { DistanceCalibration = Cal, MinReadDistanceMeters = 1e6 };
        Assert.Equal((0, 1), o.ResolvePlateWidthLimits(2688));
    }

    [Fact]
    public void Non_finite_calibration_is_invalid()
    {
        var c = new PlateDistanceCalibration { ReferenceDistanceMeters = double.PositiveInfinity, ReferencePlateWidthPx = 150, ReferenceFrameWidthPx = 2688 };
        Assert.False(c.IsValid);
        Assert.Null(c.EstimateMeters(150, 2688));
    }

    [Fact]
    public void Tightest_of_pixel_and_distance_limit_wins()
    {
        var o = new AnprOptions
        {
            DistanceCalibration = Cal, MaxReadDistanceMeters = 9.0, MinReadDistanceMeters = 3.0,
            MinPlateWidthPx = 120, MaxPlateWidthPx = 250,
        };
        Assert.Equal((120, 250), o.ResolvePlateWidthLimits(2688));
        o.MinPlateWidthPx = 80; o.MaxPlateWidthPx = 400;
        Assert.Equal((100, 300), o.ResolvePlateWidthLimits(2688));
    }

    [Fact]
    public void Distance_limits_without_valid_calibration_are_inert()
    {
        var o = new AnprOptions { MaxReadDistanceMeters = 9.0, MinReadDistanceMeters = 3.0 };
        Assert.Equal((0, 0), o.ResolvePlateWidthLimits(2688));
        o.DistanceCalibration = new PlateDistanceCalibration();   // geçersiz (sıfırlar)
        Assert.Equal((0, 0), o.ResolvePlateWidthLimits(2688));
    }

    // ---------------------------------------------------------------- motor uçtan uca (gerçek modeller)

    [Fact]
    public void Defaults_read_the_plate_and_report_width_but_no_distance()
    {
        using var e = Engine();
        var r = Assert.Single(e.Recognize(Sample));
        Assert.True(r.PlateWidthPx > 0);
        Assert.True(r.Box.Width > r.PlateWidthPx, "Box dolgulu kırpıntıdır, PlateWidthPx dolgusuz dedektör kutusu");
        Assert.Null(r.EstimatedDistanceMeters);
        Assert.Equal(0, e.SizeDroppedCount);
    }

    [Fact]
    public void Plate_narrower_than_min_is_not_read_and_counted()
    {
        var (w, _) = Baseline();
        using var e = Engine(o => o.MinPlateWidthPx = w + 1);
        Assert.Empty(e.Recognize(Sample));
        Assert.Equal(1, e.SizeDroppedCount);
    }

    [Fact]
    public void Plate_wider_than_max_is_not_read_and_counted()
    {
        var (w, _) = Baseline();
        using var e = Engine(o => o.MaxPlateWidthPx = w - 1);
        Assert.Empty(e.Recognize(Sample));
        Assert.Equal(1, e.SizeDroppedCount);
    }

    [Fact]
    public void Limits_are_inclusive_at_the_boundary()
    {
        var (w, _) = Baseline();
        using var e = Engine(o => { o.MinPlateWidthPx = w; o.MaxPlateWidthPx = w; });
        Assert.Single(e.Recognize(Sample));
        Assert.Equal(0, e.SizeDroppedCount);
    }

    [Fact]
    public void Calibrated_engine_reports_distance_and_applies_distance_limit()
    {
        var (w, frameW) = Baseline();
        var cal = new PlateDistanceCalibration { ReferenceDistanceMeters = 5.0, ReferencePlateWidthPx = w, ReferenceFrameWidthPx = frameW };

        using (var e = Engine(o => o.DistanceCalibration = cal))
            Assert.Equal(5.0, Assert.Single(e.Recognize(Sample)).EstimatedDistanceMeters!.Value, 6);

        // Plaka 5 m'de; en uzak okuma 4 m → okunmamalı. En uzak 6 m → okunmalı.
        using (var e = Engine(o => { o.DistanceCalibration = cal; o.MaxReadDistanceMeters = 4.0; }))
            Assert.Empty(e.Recognize(Sample));
        using (var e = Engine(o => { o.DistanceCalibration = cal; o.MaxReadDistanceMeters = 6.0; }))
            Assert.Single(e.Recognize(Sample));
    }

    // ---------------------------------------------------------------- SDK eşlemesi ve doğrulama

    [Fact]
    public void Camera_values_override_service_values_and_distance_comes_only_from_camera()
    {
        var o = new AnprServiceOptions { MinPlateWidthPx = 50, MaxPlateWidthPx = 400 };
        var cam = new CameraOptions
        {
            Id = "giris", MinPlateWidthPx = 90,
            Calibration = new CameraCalibration { ReferenceDistanceMeters = 6, ReferencePlateWidthPx = 150, ReferenceFrameWidthPx = 2688 },
            MaxReadDistanceMeters = 9,
        };
        var e = AnprCameraSupervisor.ToEngineOptions(o, "d", "o", "y", "", cam);
        Assert.Equal(90, e.MinPlateWidthPx);       // kamera ezer
        Assert.Equal(400, e.MaxPlateWidthPx);      // null → servis geneli
        Assert.Equal(9, e.MaxReadDistanceMeters);
        Assert.Equal(0, e.MinReadDistanceMeters);
        Assert.Equal(Cal, e.DistanceCalibration);

        var noCam = AnprCameraSupervisor.ToEngineOptions(o, "d", "o", "y", "");
        Assert.Equal(50, noCam.MinPlateWidthPx);
        Assert.Null(noCam.DistanceCalibration);
    }

    private static AnprServiceOptions WithCamera(Action<CameraOptions> tweak)
    {
        var c = new CameraOptions { Id = "giris", RtspUrl = "rtsp://x:554/1" };
        tweak(c);
        return new AnprServiceOptions { Cameras = { c } };
    }

    [Fact]
    public void Valid_size_and_distance_config_passes() => WithCamera(c =>
    {
        c.MinPlateWidthPx = 90; c.MaxPlateWidthPx = 350;
        c.Calibration = new CameraCalibration { ReferenceDistanceMeters = 6, ReferencePlateWidthPx = 150, ReferenceFrameWidthPx = 2688 };
        c.MinReadDistanceMeters = 2; c.MaxReadDistanceMeters = 12;
    }).Validate();

    [Theory]
    [InlineData(150, 150, "MaxPlateWidthPx")]   // eşit = hiçbir plaka okunamaz değil ama sınır anlamsız → reddedilir
    [InlineData(150, 100, "MaxPlateWidthPx")]
    [InlineData(-1, 0, "MinPlateWidthPx")]
    [InlineData(0, -5, "MaxPlateWidthPx")]
    public void Contradictory_or_negative_pixel_limits_throw(int min, int max, string expected)
    {
        var o = WithCamera(c => { c.MinPlateWidthPx = min; c.MaxPlateWidthPx = max; });
        Assert.Contains(expected, Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Distance_limit_without_calibration_throws()
    {
        var o = WithCamera(c => c.MaxReadDistanceMeters = 10);
        Assert.Contains("kalibrasyon OLMADAN", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Invalid_calibration_throws()
    {
        var o = WithCamera(c => c.Calibration = new CameraCalibration { ReferenceDistanceMeters = 6, ReferencePlateWidthPx = 0, ReferenceFrameWidthPx = 2688 });
        Assert.Contains("Calibration geçersiz", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Far_not_beyond_near_throws()
    {
        var o = WithCamera(c =>
        {
            c.Calibration = new CameraCalibration { ReferenceDistanceMeters = 6, ReferencePlateWidthPx = 150, ReferenceFrameWidthPx = 2688 };
            c.MinReadDistanceMeters = 8; c.MaxReadDistanceMeters = 8;
        });
        Assert.Contains("MaxReadDistanceMeters", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Service_wide_contradiction_is_caught_per_camera()
    {
        var o = WithCamera(_ => { });
        o.MinPlateWidthPx = 200; o.MaxPlateWidthPx = 100;
        Assert.Contains("MaxPlateWidthPx", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Shipped_defaults_keep_every_limit_off()
    {
        var o = new AnprServiceOptions();
        Assert.Equal(0, o.MinPlateWidthPx);
        Assert.Equal(0, o.MaxPlateWidthPx);
        var c = new CameraOptions();
        Assert.Null(c.MinPlateWidthPx);
        Assert.Null(c.MaxPlateWidthPx);
        Assert.Null(c.Calibration);
        Assert.Null(c.MinReadDistanceMeters);
        Assert.Null(c.MaxReadDistanceMeters);
        var core = new AnprOptions();
        Assert.Equal(0, core.MinPlateWidthPx);
        Assert.Equal(0, core.MaxPlateWidthPx);
        Assert.Null(core.DistanceCalibration);
    }

    // ---------------------------------------------------------------- dağılım ve olay şeması

    [Fact]
    public void Width_stats_use_nearest_rank_and_ignore_non_positive()
    {
        Assert.Null(PlateWidthStats.From(Array.Empty<int>()));
        Assert.Null(PlateWidthStats.From(new[] { 0, -3 }));
        var s = PlateWidthStats.From(Enumerable.Range(1, 11).Select(i => i * 10).Append(0))!.Value;   // 10..110
        Assert.Equal(new PlateWidthStats(11, 20, 60, 100), s);
    }

    private sealed class LegacyStatus : ICameraStatus
    {
        public string CameraId => "x"; public string CameraName => "x"; public bool IsConnected => false;
        public DateTimeOffset? LastFrameUtc => null; public long FramesReceived => 0; public long FramesProcessed => 0;
        public int RestartCount => 0; public double FrameMsP50 => 0; public double FrameMsP95 => 0;
        public long QualityDropped => 0; public long AspectDropped => 0; public long TrRejected => 0;
        public long EventsEmitted => 0; public DateTimeOffset? LastEventUtc => null;
    }

    [Fact]
    public void New_status_members_have_safe_defaults_for_existing_implementers()
    {
        ICameraStatus s = new LegacyStatus();   // 2.1.0'da yazılmış bir uygulayıcı derlenmeye devam eder
        Assert.Equal(0, s.SizeDropped);
        Assert.Null(s.RecentPlateWidths);
        Assert.False(s.RectifierAvailable);
        Assert.Equal(0, s.EnhanceTracks);
        Assert.Equal(0, s.EnhanceInjected);
    }

    [Theory]
    [InlineData(true, "models/iwpod_net.onnx", true)]    // 2.2.0 varsayılanı: pakette, models/ altında bulunur
    [InlineData(false, "models/iwpod_net.onnx", false)]  // kapatıldı
    [InlineData(true, "models/yok.onnx", false)]         // dosya yok → aşama kapanır, durum bunu SÖYLER
    public async Task Worker_reports_whether_rectification_can_run(bool enabled, string modelPath, bool expected)
    {
        var cam = new CameraOptions { Id = "t", Name = "t", RtspUrl = "file://x" };
        var opts = new AnprServiceOptions { EnhanceRectify = enabled, WpodModelPath = modelPath, EvidenceCrops = false, Cameras = { cam } };
        var engine = AnprEngine.Create(AnprCameraSupervisor.ToEngineOptions(opts,
            Path.Combine(ModelsDir, "yolo-v9-t-384-license-plates-end2end.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v1_global_plate_config.yaml"), "", cam));
        await using var worker = new CameraWorker(cam, opts, new RepeatingJpegSource(Sample), engine,
            Array.Empty<IEventSink>(), new FrequentPlateRegistry(), new RecentEmitRegistry(TimeSpan.Zero, TimeSpan.Zero),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Assert.Equal(expected, worker.RectifierAvailable);
        Assert.Equal(expected, ((ICameraStatus)worker).RectifierAvailable);   // arayüz üzerinden de (varsayılan gövde ezilmiş)
        Assert.Equal(0, worker.EnhanceTracks);
    }

    [Fact]
    public void Schema_v9_carries_width_and_distance()
    {
        Assert.Equal(9, PlateEvent.CurrentSchemaVersion);
        var e = new PlateEvent
        {
            SchemaVersion = PlateEvent.CurrentSchemaVersion,
            Timestamp = DateTimeOffset.UnixEpoch, CameraId = "c", CameraName = "c", Plate = "00 X 0000",
            PlateCompact = "00X0000", RawText = "00X0000", Confidence = 1, DetectorConfidence = 1, OcrConfidence = 1,
            Box = new PlateBox(0, 0, 10, 2), PlateWidthPx = 151, EstimatedDistanceMeters = null,
        };
        string json = System.Text.Json.JsonSerializer.Serialize(e);
        Assert.Contains("\"PlateWidthPx\":151", json);
        Assert.Contains("\"EstimatedDistanceMeters\":null", json);
    }

    // ---------------------------------------------------------------- inceleme bulguları (2026-10-27) — bağlantı testleri

    [Fact]
    public void Skipped_detection_is_reported_per_call_not_discarded()
    {
        var (w, _) = Baseline();
        using var e = Engine(o => o.MinPlateWidthPx = w + 1);
        Assert.Empty(e.Recognize(Sample));
        var s = Assert.Single(e.LastSizeSkipped);
        Assert.Equal(w, s.PlateWidthPx);
        Assert.True(s.InZone);                                  // ROI yok → bölge içi
        Assert.True(s.Box.Width > s.PlateWidthPx);              // okumalarla aynı (dolgulu) kutu kuralı

        using var blank = new SkiaSharp.SKBitmap(640, 360);
        blank.Erase(SkiaSharp.SKColors.Gray);
        Assert.Empty(e.Recognize(blank));
        Assert.Empty(e.LastSizeSkipped);                        // her çağrıda yenilenir
    }

    private static PlateReading Reading(int width, bool inZone, PlateBox box, double? dist = null) => new()
    {
        Text = "00 X 0000", RawText = "00X0000", Confidence = 0.9f, DetectorConfidence = 0.9f, OcrConfidence = 0.9f,
        Box = box, IsValidTr = true, InZone = inZone, PlateWidthPx = width, EstimatedDistanceMeters = dist,
    };

    [Fact]
    public void Detection_signals_include_size_skipped_detections()
    {
        var read = Reading(150, inZone: false, new PlateBox(0, 0, 168, 40));
        var skip = new SizeSkippedDetection(new PlateBox(500, 500, 90, 20), 80, InZone: true);
        var seen = new List<PlateBox>();
        var (boxes, zone) = CameraWorker.DetectionSignals(new[] { read }, new[] { skip }, seen.Add);
        Assert.Equal(new[] { read.Box, skip.Box }, boxes);     // canlı kutular okunmayanı da gösterir
        Assert.Equal(new[] { read.Box, skip.Box }, seen);      // yön izleyici okunmayanı da görür
        Assert.True(zone);                                      // bölgedeki plaka okunmasa da aktif izleme açılır

        var (_, noZone) = CameraWorker.DetectionSignals(new[] { read }, Array.Empty<SizeSkippedDetection>(), _ => { });
        Assert.False(noZone);
    }

    [Fact]
    public void Enhanced_observation_inherits_the_tracks_measured_size()
    {
        var consensus = Reading(151, inZone: true, new PlateBox(0, 0, 169, 40), dist: 6.2);
        var cropRead = Reading(0, inZone: false, default) with { PlateWidthPx = null };
        var injected = CameraWorker.AsEnhancedObservation(cropRead, consensus);
        Assert.True(injected.InZone);
        Assert.Equal(151, injected.PlateWidthPx);
        Assert.Equal(6.2, injected.EstimatedDistanceMeters);
    }

    [Fact]
    public void Event_gets_the_best_readings_measured_size()
    {
        var e = new PlateEvent
        {
            Timestamp = DateTimeOffset.UnixEpoch, CameraId = "c", CameraName = "c", Plate = "00 X 0000",
            PlateCompact = "00X0000", RawText = "00X0000", Confidence = 1, DetectorConfidence = 1, OcrConfidence = 1,
            Box = new PlateBox(0, 0, 10, 2),
        };
        var withSize = CameraWorker.WithMeasuredSize(e, Reading(151, true, default, dist: 7.5));
        Assert.Equal(151, withSize.PlateWidthPx);
        Assert.Equal(7.5, withSize.EstimatedDistanceMeters);
    }

    [Fact]
    public void Width_window_wraps_and_ignores_unknown_values()
    {
        var win = new PlateWidthWindow(3);
        Assert.Null(win.Stats());
        foreach (int? v in new int?[] { 10, null, 0, 20, 30, 40, 50 }) win.Add(v);
        Assert.Equal(new PlateWidthStats(3, 30, 40, 50), win.Stats());   // yalnız SON 3 pozitif değer
        Assert.Equal(200, CameraWorker.PlateWidthWindowSize);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Confirmer_keeps_the_best_readings_width_and_distance(bool voting)
    {
        var c = new Anpr.Core.Confirmation.PlateConfirmer(new Anpr.Core.Confirmation.PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8), SettleWindow = TimeSpan.FromSeconds(3), MinConfirmations = 2,
            CharacterVoting = voting,
        });
        var t = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading(120, true, new PlateBox(0, 0, 134, 30), dist: 8.0) with { OcrConfidence = 0.80f, Confidence = 0.80f }, t);
        c.Observe(Reading(160, true, new PlateBox(0, 0, 179, 40), dist: 6.0) with { OcrConfidence = 0.95f, Confidence = 0.95f }, t.AddSeconds(1));
        var e = Assert.Single(c.DrainReady(t.AddSeconds(1)));
        Assert.Equal(160, e.PlateWidthPx);                     // en iyi okumanınki
        Assert.Equal(6.0, e.EstimatedDistanceMeters);
    }

    [Fact]
    public void Pixel_limit_contradicting_distance_limit_throws_at_startup()
    {
        // Servis geneli MinPlateWidthPx=200; 6 m'de 150 px kalibrasyonla en yakın 5 m → üst sınır 180 px → aralık boş.
        var o = WithCamera(c =>
        {
            c.Calibration = new CameraCalibration { ReferenceDistanceMeters = 6, ReferencePlateWidthPx = 150, ReferenceFrameWidthPx = 2688 };
            c.MinReadDistanceMeters = 5;
        });
        o.MinPlateWidthPx = 200;
        Assert.Contains("çelişiyor", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    // ---------------------------------------------------------------- gerçek işçi (sahte kare kaynağı + gerçek motor)

    internal sealed class RepeatingJpegSource(byte[] jpeg) : Anpr.Sdk.Frames.IFrameSource
    {
        private long _seq;
        public void Start(CancellationToken cancellationToken) { }
        public Anpr.Sdk.Frames.VideoFrame? TryGetLatestFrame() =>
            Anpr.Sdk.Frames.VideoFrame.FromJpeg(jpeg, DateTimeOffset.UtcNow, Interlocked.Increment(ref _seq));
        public bool IsConnected => true;
        public DateTimeOffset? LastFrameUtc => DateTimeOffset.UtcNow;
        public long FramesReceived => Interlocked.Read(ref _seq);
        public int RestartCount => 0;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Worker_keeps_showing_and_tracking_a_plate_it_does_not_read()
    {
        var (w, frameW) = Baseline();
        var cam = new CameraOptions { Id = "t", Name = "t", RtspUrl = "file://x", MinPlateWidthPx = w + 1 };
        var opts = new AnprServiceOptions { MotionGate = MotionGateMode.Off, Fps = 5, IdleFps = 5, EvidenceCrops = false, Cameras = { cam } };
        var engine = AnprEngine.Create(AnprCameraSupervisor.ToEngineOptions(opts,
            Path.Combine(ModelsDir, "yolo-v9-t-384-license-plates-end2end.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v1_global.onnx"),
            Path.Combine(ModelsDir, "cct_xs_v1_global_plate_config.yaml"), "", cam));
        await using var worker = new CameraWorker(cam, opts, new RepeatingJpegSource(Sample), engine,
            Array.Empty<IEventSink>(), new FrequentPlateRegistry(), new RecentEmitRegistry(TimeSpan.Zero, TimeSpan.Zero),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        worker.Start(cts.Token);
        while (worker.FramesProcessed < 2 && !cts.IsCancellationRequested) await Task.Delay(50);
        cts.Cancel();   // döngü dursun (aksi hâlde DisposeAsync 30 sn'lik zaman aşımını bekler)

        Assert.True(worker.FramesProcessed >= 2, "işçi kare işlemedi");
        Assert.True(worker.SizeDropped >= 1);                  // sayaç işçiye ulaşıyor
        Assert.NotEmpty(worker.LastBoxes);                     // okunmayan plaka panelde görünmeye devam ediyor
        Assert.Equal(frameW, worker.FrameWidthPx);
        Assert.Null(worker.RecentPlateWidths);                 // olay yok → dağılım yok (sahte sayı üretilmez)
    }
}
