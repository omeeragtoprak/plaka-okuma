using System.Net;
using Anpr.Sdk;
using Anpr.Sdk.Cameras;

namespace Anpr.Tests;

/// <summary>
/// Kamera ayarlarının SALT-OKUNUR incelemesi (gap analizi G4/G5, 2026-10-27). XML örnekleri sahadaki Hikvision
/// kameranın GERÇEK yanıt yapısını (ad alanı, öğe adları, ×100 kare hızı, opt/min/max öznitelikleri) taşır; değerler
/// sadeleştirildi, kanal adı gibi sahaya özgü bilgi çıkarıldı.
/// </summary>
public class CameraInspectionTests
{
    private const string ImageXml = """
<?xml version="1.0" encoding="UTF-8"?>
<ImageChannel version="2.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
<id>1</id>
<IrcutFilter><IrcutFilterType>auto</IrcutFilterType></IrcutFilter>
<Exposure><ExposureType>auto</ExposureType></Exposure>
<FocusConfiguration><focusStyle>AUTO</focusStyle></FocusConfiguration>
<WDR><mode>close</mode><WDRLevel>50</WDRLevel></WDR>
<BLC><enabled>false</enabled></BLC>
<NoiseReduce><mode>advanced</mode><AdvancedMode><FrameNoiseReduceLevel>50</FrameNoiseReduceLevel><InterFrameNoiseReduceLevel>50</InterFrameNoiseReduceLevel></AdvancedMode></NoiseReduce>
<Gain><GainLevel>20</GainLevel></Gain>
<Shutter><ShutterLevel>1/150</ShutterLevel></Shutter>
<HLC><enabled>false</enabled><HLCLevel>50</HLCLevel></HLC>
<SupplementLight><supplementLightMode>irLight</supplementLightMode></SupplementLight>
</ImageChannel>
""";

    private const string ImageCapsXml = """
<?xml version="1.0" encoding="UTF-8"?>
<ImageChannel version="2.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
<Shutter><ShutterLevel opt="1/25,1/50,1/100,1/150,1/200,1/250,1/500,1/1000,1/2000,1/10000"></ShutterLevel></Shutter>
</ImageChannel>
""";

    private const string StreamXml = """
<?xml version="1.0" encoding="UTF-8"?>
<StreamingChannel version="2.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
<id>101</id>
<Video>
<videoCodecType>H.265</videoCodecType>
<videoResolutionWidth>2688</videoResolutionWidth>
<videoResolutionHeight>1520</videoResolutionHeight>
<videoQualityControlType>VBR</videoQualityControlType>
<constantBitRate>3072</constantBitRate>
<vbrUpperCap>3072</vbrUpperCap>
<maxFrameRate>2500</maxFrameRate>
<GovLength>50</GovLength>
<SmartCodec><enabled>true</enabled></SmartCodec>
</Video>
</StreamingChannel>
""";

    private const string StreamCapsXml = """
<?xml version="1.0" encoding="UTF-8"?>
<StreamingChannel version="2.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
<Video>
<videoCodecType opt="H.264,H.265">H.265</videoCodecType>
<vbrUpperCap min="32" max="8192">3072</vbrUpperCap>
<maxFrameRate opt="2500,2000,1500,1000,600,400">2500</maxFrameRate>
</Video>
</StreamingChannel>
""";

    // ---------------------------------------------------------------- ayrıştırıcı

    [Fact]
    public void Image_settings_are_parsed_from_real_isapi_structure()
    {
        var i = IsapiParser.ParseImage(ImageXml, ImageCapsXml);
        Assert.Equal("auto", i.ExposureMode);
        Assert.Equal("1/150", i.Shutter);
        Assert.Equal(1 / 150.0, i.ShutterSeconds!.Value, 9);
        Assert.Equal(20, i.GainLevel);
        Assert.False(i.Wdr);          // mode=close
        Assert.False(i.Blc);
        Assert.False(i.Hlc);
        Assert.Equal(50, i.TemporalNoiseReductionLevel);
        Assert.Equal("auto", i.DayNightMode);
        Assert.Equal("AUTO", i.FocusMode);
        Assert.Equal("irLight", i.Illumination);
        Assert.Equal(10, i.ShutterOptionsSeconds!.Count);
        Assert.Contains(1 / 250.0, i.ShutterOptionsSeconds);
    }

    [Fact]
    public void Stream_settings_are_parsed_and_fps_is_divided_by_100()
    {
        var s = IsapiParser.ParseStream(StreamXml, StreamCapsXml);
        Assert.Equal("H.265", s.Codec);
        Assert.Equal((2688, 1520), (s.Width!.Value, s.Height!.Value));
        Assert.Equal(25.0, s.Fps);
        Assert.Equal(3072, s.BitrateKbps);
        Assert.Equal("VBR", s.BitrateMode);
        Assert.Equal(50, s.GopFrames);
        Assert.True(s.SmartCodec);
        Assert.Equal(8192, s.MaxBitrateKbps);
        Assert.Equal(new[] { "H.264", "H.265" }, s.CodecOptions);
        Assert.Contains(10.0, s.FpsOptions!);
    }

    [Fact]
    public void Missing_fields_stay_unknown_not_false()
    {
        var i = IsapiParser.ParseImage("<ImageChannel><id>1</id></ImageChannel>", null);
        Assert.Null(i.Wdr); Assert.Null(i.ShutterSeconds); Assert.Null(i.ShutterOptionsSeconds);
        var problems = new List<string>();
        Assert.Null(IsapiParser.ParseStream("<not xml", null, problems));
        Assert.Single(problems);
    }

    [Fact]
    public void Non_isapi_reply_is_rejected_not_read_as_empty_settings()
    {
        // 200 dönen bir HTML giriş sayfası "her alanı boş ayar" diye kabul edilmemeli (inceleme bulgusu).
        var problems = new List<string>();
        Assert.Null(IsapiParser.ParseImage("<html><body>login</body></html>", null, problems));
        Assert.Null(IsapiParser.ParseStream("<ImageChannel/>", null, problems));   // yanlış belge türü
        Assert.Equal(2, problems.Count);
        Assert.Single(CameraAdvisor.Evaluate(null, null, null, 5), a => a.Code == "no-imaging");
    }

    [Fact]
    public void Temporal_noise_level_counts_only_in_advanced_mode()
    {
        var off = IsapiParser.ParseImage(ImageXml.Replace("<mode>advanced</mode>", "<mode>close</mode>"), null)!;
        Assert.Equal("close", off.NoiseReductionMode);
        Assert.Null(off.TemporalNoiseReductionLevel);
        Assert.DoesNotContain(CameraAdvisor.Evaluate(null, off, null, 5), a => a.Code == "temporal-nr");
    }

    [Fact]
    public void Wdr_auto_is_not_reported_as_on()
    {
        var auto = IsapiParser.ParseImage(ImageXml.Replace("<mode>close</mode><WDRLevel>", "<mode>auto</mode><WDRLevel>"), null)!;
        Assert.Equal("auto", auto.WdrMode);
        Assert.Null(auto.Wdr);
        var open = IsapiParser.ParseImage(ImageXml.Replace("<mode>close</mode><WDRLevel>", "<mode>open</mode><WDRLevel>"), null)!;
        Assert.True(open.Wdr);
    }

    [Fact]
    public void Vbr_uses_the_upper_cap_and_cbr_the_constant_rate()
    {
        var vbr = StreamXml.Replace("<constantBitRate>3072</constantBitRate>", "<constantBitRate>2048</constantBitRate>");
        Assert.Equal(3072, IsapiParser.ParseStream(vbr, null)!.BitrateKbps);
        var cbr = vbr.Replace("<videoQualityControlType>VBR</videoQualityControlType>", "<videoQualityControlType>CBR</videoQualityControlType>");
        Assert.Equal(2048, IsapiParser.ParseStream(cbr, null)!.BitrateKbps);
    }

    [Theory]
    [InlineData("1/150", 1 / 150.0)]
    [InlineData("1/1", 1.0)]
    [InlineData("1/100000", 1e-5)]
    public void Shutter_text_is_parsed(string text, double expected) => Assert.Equal(expected, IsapiParser.ParseShutter(text)!.Value, 12);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("1/0")]
    public void Unknown_shutter_text_is_null(string? text) => Assert.Null(IsapiParser.ParseShutter(text));

    // ---------------------------------------------------------------- yalnız GET + kilitlenme koruması

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Calls { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Xml(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public async Task Reader_only_ever_sends_GET()
    {
        var h = new RecordingHandler(r => r.RequestUri!.AbsolutePath switch
        {
            "/ISAPI/Image/channels/1" => Xml(ImageXml),
            "/ISAPI/Image/channels/1/capabilities" => Xml(ImageCapsXml),
            "/ISAPI/Streaming/channels/101" => Xml(StreamXml),
            "/ISAPI/Streaming/channels/101/capabilities" => Xml(StreamCapsXml),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var reader = new HikvisionIsapiReader(new Uri("http://kamera.test"), h);
        var snap = await reader.ReadAsync("giris");

        Assert.Equal(4, h.Calls.Count);
        Assert.All(h.Calls, c => Assert.Equal(HttpMethod.Get, c.Method));   // kamera ayarı YAZILMAZ
        Assert.DoesNotContain(h.Calls, c => c.Path.Contains("reset") || c.Path.Contains("restore") || c.Path.Contains("configurationData"));
        Assert.Equal("1/150", snap.Image!.Shutter);
        Assert.Equal(25.0, snap.Stream!.Fps);
        Assert.Empty(snap.Problems);
        Assert.False(snap.AuthRejected);
    }

    [Fact]
    public void Reader_has_no_write_path_in_source()
    {
        // "Yazma yolu yok" iddiasının KİLİDİ: çalışma anı testi yalnız ReadAsync'in ne gönderdiğini görür; okuyucuya bir PUT
        // yöntemi eklense o test geçerdi (inceleme bunu gösterdi). Kaynak taraması her yazma fiilini ve tehlikeli ucu yakalar.
        string dir = Path.Combine(RepoRoot(), "Anpr.Sdk", "Cameras");
        var forbidden = new[] { "PutAsync", "PostAsync", "DeleteAsync", "PatchAsync", "HttpMethod.Put", "HttpMethod.Post",
                                "HttpMethod.Delete", "HttpMethod.Patch", "/reset", "/restore", "configurationData" };
        var files = Directory.GetFiles(dir, "*.cs");
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            string src = string.Join('\n', File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("///") && !l.TrimStart().StartsWith("//")));
            foreach (var bad in forbidden)
                Assert.False(src.Contains(bad, StringComparison.Ordinal), $"{Path.GetFileName(f)}: yasak ifade '{bad}' (kameraya yazma yolu)");
        }
        var publicMethods = typeof(HikvisionIsapiReader).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Dispose", "ReadAsync" }, publicMethods);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OtoparkAnpr.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("depo kökü bulunamadı");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Reader_stops_on_401_and_403_alike(HttpStatusCode code)
    {
        var h = new RecordingHandler(_ => new HttpResponseMessage(code));
        using var reader = new HikvisionIsapiReader(new Uri("http://kamera.test"), h);
        var snap = await reader.ReadAsync("giris");
        Assert.Single(h.Calls);
        Assert.True(snap.AuthRejected);
    }

    [Fact]
    public async Task Unreachable_camera_is_tried_once_not_four_times()
    {
        var h = new RecordingHandler(_ => throw new HttpRequestException("ağ yok"));
        using var reader = new HikvisionIsapiReader(new Uri("http://kamera.test"), h);
        var snap = await reader.ReadAsync("giris");
        Assert.Single(h.Calls);
        Assert.Contains("atlandı", Assert.Single(snap.Problems));
        Assert.False(snap.AuthRejected);
    }

    [Fact]
    public void Invalid_camera_address_is_an_argument_error_not_a_crash()
    {
        Assert.Throws<ArgumentException>(() => new HikvisionIsapiReader("fe80::1", "u", "p"));
        using var ok = new HikvisionIsapiReader("[fe80::1]", "u", "p");   // köşeli parantezli IPv6 geçerli
    }

    [Fact]
    public async Task Inspector_reports_a_bad_address_instead_of_throwing()
    {
        var cam = new Anpr.Sdk.Configuration.CameraOptions { Id = "x", Ip = "fe80::1", Username = "u", Password = "p" };
        var opts = new Anpr.Sdk.Configuration.AnprServiceOptions { Ffmpeg = { Path = "/bu/yol/yok/ffmpeg" } };
        var r = await CameraInspector.InspectAsync(cam, opts);
        Assert.Contains(r.Problems, p => p.StartsWith("ISAPI: Kamera adresi geçerli"));
        Assert.Contains(r.Problems, p => p.StartsWith("RTSP yoklaması"));
        Assert.DoesNotContain(r.Problems, p => p.Contains("rtsp://") || p.Contains("p@"));   // adres/şifre sızmaz
    }

    [Fact]
    public async Task Reader_stops_after_first_auth_rejection()
    {
        var h = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var reader = new HikvisionIsapiReader(new Uri("http://kamera.test"), h);
        var snap = await reader.ReadAsync("giris");
        Assert.Single(h.Calls);                       // ikinci istek ATILMAZ (kilitlenme riski)
        Assert.True(snap.AuthRejected);
        Assert.Contains("kimlik doğrulama reddedildi", Assert.Single(snap.Problems));
        await reader.ReadAsync("giris");
        Assert.Single(h.Calls);                       // aynı okuyucu bir daha denemez
    }

    [Fact]
    public async Task Unsupported_endpoint_is_reported_not_thrown()
    {
        var h = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var reader = new HikvisionIsapiReader(new Uri("http://kamera.test"), h);
        var snap = await reader.ReadAsync("x");
        Assert.Null(snap.Image); Assert.Null(snap.Stream);
        Assert.Equal(4, snap.Problems.Count);
        Assert.False(snap.AuthRejected);
    }

    // ---------------------------------------------------------------- satıcıdan bağımsız RTSP yoklaması

    [Fact]
    public void Ffprobe_json_is_parsed()
    {
        const string json = """{"programs":[],"streams":[{"codec_name":"hevc","width":2688,"height":1520,"r_frame_rate":"25/1","avg_frame_rate":"25/1"}]}""";
        var s = RtspStreamProbe.ParseFfprobeJson(json)!;
        Assert.Equal("hevc", s.Codec);
        Assert.Equal(2688, s.Width);
        Assert.Equal(25.0, s.Fps);
        Assert.Null(s.BitrateKbps);                   // RTSP'de bit_rate çoğu zaman yok → bilinmiyor
        Assert.Null(RtspStreamProbe.ParseFfprobeJson("""{"streams":[]}"""));
        Assert.Null(RtspStreamProbe.ParseFfprobeJson("bozuk"));
        Assert.Null(RtspStreamProbe.ParseRate("0/0"));
    }

    [Theory]
    [InlineData(null, "ffprobe")]
    [InlineData("ffmpeg", "ffprobe")]
    [InlineData("/usr/local/bin/ffmpeg", "/usr/local/bin/ffprobe")]
    [InlineData("tools/ffmpeg.exe", "tools/ffprobe.exe")]
    [InlineData("/opt/custom-encoder", "/opt/ffprobe")]
    public void Ffprobe_path_is_derived_from_ffmpeg_path(string? ffmpeg, string expected)
        => Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), RtspStreamProbe.DeriveFfprobePath(ffmpeg?.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public async Task Missing_ffprobe_is_reported_not_thrown()
    {
        var (s, problem) = await RtspStreamProbe.ProbeAsync("/bu/yol/yok/ffprobe-xyz", "rtsp://k:554/x");
        Assert.Null(s);
        Assert.Contains("ffprobe çalıştırılamadı", problem);
        Assert.DoesNotContain("rtsp://", problem);    // adres (şifre içerebilir) metne girmez
    }

    [Fact]
    public async Task Ffprobe_is_killed_when_the_caller_cancels()
    {
        if (OperatingSystem.IsWindows()) return;   // sahte ffprobe bir sh betiği (CI ubuntu'da koşar)
        string dir = Directory.CreateTempSubdirectory("anpr-fakeprobe-").FullName;
        try
        {
            string pidFile = Path.Combine(dir, "pid"), script = Path.Combine(dir, "ffprobe");
            File.WriteAllText(script, $"#!/bin/sh\necho $$ > '{pidFile}'\nexec sleep 30\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // İptal, sahte ffprobe pid'ini YAZDIKTAN SONRA yapılır. Sabit 700 ms'lik iptal yüklü makinede yarışa düşüyordu:
            // betik pid'i yazamadan öldürülünce dosya okuma FileNotFoundException veriyordu (2026-10-27, temiz yayın
            // kopyasının ilk koşusunda bir kez görüldü; betiğe 'sleep 1' eklenerek aynı imzayla yeniden üretildi).
            using var cts = new CancellationTokenSource();
            var probe = RtspStreamProbe.ProbeAsync(script, "rtsp://k/x", timeout: TimeSpan.FromSeconds(60), ct: cts.Token);
            var waited = System.Diagnostics.Stopwatch.StartNew();
            int pid = 0;
            while (pid == 0 && waited.Elapsed < TimeSpan.FromSeconds(20))
            {
                if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out int pv)) pid = pv;
                else await Task.Delay(20);
            }
            Assert.True(pid > 0, "sahte ffprobe 20 sn içinde başlamadı");
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
            bool alive = true;   // öldürme eşzamansız tamamlanabilir → 5 sn'ye kadar yokla (sabit bekleme yükte yarışır)
            for (var w = System.Diagnostics.Stopwatch.StartNew(); alive && w.Elapsed < TimeSpan.FromSeconds(5); )
            {
                try { using var proc = System.Diagnostics.Process.GetProcessById(pid); alive = !proc.HasExited; }
                catch (ArgumentException) { alive = false; }
                if (alive) await Task.Delay(50);
            }
            Assert.False(alive, "iptalden sonra ffprobe süreci hâlâ çalışıyor");

            using var pre = new CancellationTokenSource(); pre.Cancel();
            File.Delete(pidFile);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RtspStreamProbe.ProbeAsync(script, "rtsp://k/x", ct: pre.Token));
            Assert.False(File.Exists(pidFile));         // iptal edilmiş çağrı süreç BAŞLATMAZ
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Vendor_values_win_and_gaps_are_filled_from_rtsp()
    {
        var vendor = new CameraStreamSettings { Codec = "H.265", Fps = 25, Width = null };
        var rtsp = new CameraStreamSettings { Codec = "hevc", Fps = 24.9, Width = 2688, Height = 1520 };
        var m = CameraInspector.Merge(vendor, rtsp)!;
        Assert.Equal("H.265", m.Codec);
        Assert.Equal(25, m.Fps);
        Assert.Equal(2688, m.Width);
        Assert.Same(rtsp, CameraInspector.Merge(null, rtsp));
    }

    [Theory]
    [InlineData("rtsp://u:p@1.2.3.4:554/Streaming/Channels/102", 102)]
    [InlineData("rtsp://1.2.3.4/live", 101)]
    public void Isapi_stream_channel_follows_the_rtsp_path(string url, int expected) => Assert.Equal(expected, CameraInspector.StreamChannelOf(url));

    // ---------------------------------------------------------------- öneriler

    private static IReadOnlyList<CameraAdvice> Advise(PlateWidthStats? widths = null, double kmh = 15) =>
        CameraAdvisor.Evaluate(IsapiParser.ParseStream(StreamXml, StreamCapsXml), IsapiParser.ParseImage(ImageXml, ImageCapsXml),
                               widths, processedFps: 5, assumedSpeedKmh: kmh);

    [Fact]
    public void Shutter_advice_shows_its_arithmetic_and_assumption()
    {
        var a = Advise(new PlateWidthStats(200, 122, 151, 181)).Single(x => x.Code == "shutter-blur");
        Assert.Equal(AdviceSeverity.Warning, a.Severity);   // 1/150 > 1/(11×15)=1/165
        Assert.Contains("2,8 cm", a.Detail);                 // 15 km/s × 1/150 s
        Assert.Contains("≈8,1 px", a.Detail);                // 151 px / 0,52 m × 0,0278 m
        Assert.Contains("VARSAYIMDIR", a.Detail);
        Assert.Contains("Hikvision", a.Detail);              // çelişen kaynak da anılır
        Assert.Contains("1/250", a.Detail);                  // kameranın desteklediği daha kısa değer
        Assert.False(a.MeasuredHere);                        // asıl iddia formül + VARSAYILAN hıza dayanır (yalnız ölçek ölçüldü)
        Assert.Contains("yalnız ölçek ölçüldü", a.Basis);
    }

    [Fact]
    public void Too_few_events_do_not_count_as_a_measurement()
    {
        // Canlı panelde yakalandı: 2 olayın orta değeriyle "bu sahada ölçüldü" deniyordu.
        var a = Advise(new PlateWidthStats(2, 111, 152, 152)).Single(x => x.Code == "shutter-blur");
        Assert.DoesNotContain(" px", a.Detail);
        Assert.False(a.MeasuredHere);
        var b = Advise(new PlateWidthStats(CameraAdvisor.MinEventsForPixelBlur, 111, 152, 160)).Single(x => x.Code == "shutter-blur");
        Assert.Contains($"Son {CameraAdvisor.MinEventsForPixelBlur} olayda", b.Detail);
        Assert.Contains("px hesabı", b.Basis);
    }

    [Fact]
    public void Shutter_alternatives_include_the_formula_value_and_common_references()
    {
        // Gerçek kameranın seçenek listesi: formülü karşılayan en uzun değer (15 km/s → 1/175) + 1/250 + 1/500.
        var caps = """<ImageChannel><Shutter><ShutterLevel opt="1/25,1/50,1/75,1/100,1/125,1/150,1/175,1/200,1/225,1/250,1/300,1/400,1/500,1/750,1/1000"></ShutterLevel></Shutter></ImageChannel>""";
        var a = CameraAdvisor.Evaluate(null, IsapiParser.ParseImage(ImageXml, caps), new PlateWidthStats(50, 120, 152, 180), 5)
            .Single(x => x.Code == "shutter-blur");
        Assert.Contains("1/175 ≈", a.Detail);
        Assert.Contains("1/250 ≈", a.Detail);
        Assert.Contains("1/500 ≈", a.Detail);
        Assert.DoesNotContain("1/200 ≈", a.Detail);
    }

    [Fact]
    public void Shutter_within_formula_is_info_not_warning()
        => Assert.Equal(AdviceSeverity.Info, Advise(kmh: 5).Single(x => x.Code == "shutter-blur").Severity);   // 1/150 ≤ 1/55

    [Fact]
    public void Without_plate_data_no_pixel_blur_is_invented()
    {
        var a = Advise(widths: null).Single(x => x.Code == "shutter-blur");
        Assert.DoesNotContain(" px", a.Detail);
        Assert.DoesNotContain("px", a.Basis);              // dayanak da yapılmamış hesabı anmaz (canlı panelde yakalandı)
        Assert.False(a.MeasuredHere);
        Assert.DoesNotContain(Advise(widths: null), x => x.Code == "plate-small");
    }

    [Fact]
    public void Site_measured_advice_is_flagged_and_unmeasured_is_labelled()
    {
        var all = Advise(new PlateWidthStats(200, 90, 140, 180));
        var small = all.Single(x => x.Code == "plate-small");
        Assert.True(small.MeasuredHere);
        Assert.Contains("90 px", small.Detail);
        foreach (var code in new[] { "smart-codec", "temporal-nr", "low-bitrate", "hevc-decode" })
        {
            var a = all.Single(x => x.Code == code);
            Assert.False(a.MeasuredHere);
            Assert.Contains("ölçülmedi", a.Detail);
        }
        var fps = all.Single(x => x.Code == "fps-oversupply");
        Assert.Contains("10 fps", fps.Detail);
        Assert.False(fps.MeasuredHere);                     // asıl iddia (çözme kazancı) ölçülmedi
        Assert.Contains("8192", all.Single(x => x.Code == "low-bitrate").Detail);
    }

    [Fact]
    public void Few_events_do_not_trigger_plate_size_advice()
        => Assert.DoesNotContain(Advise(new PlateWidthStats(5, 60, 70, 80)), x => x.Code == "plate-small");

    [Fact]
    public void No_image_settings_is_stated_explicitly()
    {
        var advice = CameraAdvisor.Evaluate(new CameraStreamSettings { Codec = "h264", Fps = 5 }, null, null, processedFps: 5);
        Assert.Single(advice, a => a.Code == "no-imaging");
        Assert.DoesNotContain(advice, a => a.Code == "shutter-blur");
    }
}
