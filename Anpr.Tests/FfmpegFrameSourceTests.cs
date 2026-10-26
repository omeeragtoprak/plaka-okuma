using Anpr.Sdk.Configuration;
using Anpr.Sdk.Frames;

namespace Anpr.Tests;

/// <summary>ffmpeg açılış-probe'u (H2): eksik ffmpeg SESSİZCE sonsuz-retry yerine NET hata vermeli.</summary>
public class FfmpegFrameSourceTests
{
    [Fact]
    public void EnsureAvailable_throws_clear_error_when_ffmpeg_missing()
    {
        // Var olmayan ikili -> Process.Start Win32Exception atar -> EnsureAvailable net InvalidOperationException'a çevirir.
        var opts = new FfmpegOptions { Path = "ffmpeg-definitely-not-installed-xyz-123" };
        var ex = Assert.Throws<InvalidOperationException>(() => FfmpegFrameSource.EnsureAvailable(opts));
        Assert.Contains("ffmpeg", ex.Message);
        Assert.Contains("bariyer", ex.Message);   // net remediation mesajı (sessiz başarısızlık değil)
    }

    // --- Bekçi referansı (2026-07-27 canlı ağ kesintisinde yakalanan regresyon) ---
    // Kesinti sonrası her ffmpeg denemesi el-sıkışma şansı bulmadan öldürülüyordu → kalıcı kör bariyer.

    [Fact]
    public void StaleReference_gives_full_handshake_window_after_reconnect_attempt()
    {
        // REGRESYON: bu denemeden ÖNCE gelmiş bir kare varken yeni deneme başlıyor.
        // Referans eski kare olursa bekçi ffmpeg'i derhal öldürür; deneme başlangıcı olmalı.
        var attemptStart = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        long oldFrame = attemptStart.AddMinutes(-5).UtcTicks;   // kesintiden önceki son kare

        var reference = FfmpegFrameSource.StaleReference(oldFrame, attemptStart);

        Assert.Equal(attemptStart, reference);
        // 1 sn sonra bekçi bakarsa 12s'lik pencere DOLMAMIŞ olmalı (eskiden 5 dk görünüp öldürüyordu).
        Assert.True((attemptStart.AddSeconds(1) - reference).TotalSeconds < 12);
    }

    [Fact]
    public void StaleReference_uses_last_frame_once_frames_flow_in_this_attempt()
    {
        // Akış başladıysa asıl işlev korunur: donan akım (kare durur) yine timeout ile yeniden başlatılır.
        var attemptStart = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        long freshFrame = attemptStart.AddSeconds(3).UtcTicks;

        var reference = FfmpegFrameSource.StaleReference(freshFrame, attemptStart);

        Assert.Equal(new DateTimeOffset(freshFrame, TimeSpan.Zero), reference);
        Assert.True((attemptStart.AddSeconds(20) - reference).TotalSeconds > 12);   // donma hâlâ yakalanır
    }

    [Fact]
    public void StaleReference_falls_back_to_attempt_start_when_no_frame_ever()
    {
        // İlk açılış: hiç kare gelmemiş (ticks=0) → deneme başlangıcı (mevcut davranış korunur).
        var attemptStart = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(attemptStart, FfmpegFrameSource.StaleReference(0, attemptStart));
    }

    // ─────────────── Faz 2: argüman kurulumu ve donanım çözme seçimi (saf fonksiyonlar) ───────────────

    [Fact]
    public void BuildArgs_default_is_y4m_yuv_without_color_conversion_or_encoding()
    {
        // 2.0.0: varsayılan çıkış Y4M (YUV 4:2:0). Varsayılan değişirse bu test bilerek kırılır — işlemci/bellek
        // ölçümleri (README "Kaynak kullanımı") bu yola aittir.
        Assert.Equal(FfmpegOutputFormat.Y4m, new FfmpegOptions().OutputFormat);
        var args = FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "rtsp://cam/x", 5, null);
        string joined = string.Join(" ", args);
        Assert.Contains("-f yuv4mpegpipe -pix_fmt yuv420p", joined);
        Assert.DoesNotContain("-q:v", joined);
        Assert.DoesNotContain("mjpeg", joined);
        Assert.DoesNotContain("bgra", joined);
        Assert.Contains("-vf fps=5 pipe:1", joined);
        Assert.Equal("-nostdin", args[0]);
        Assert.True(args.IndexOf("-i") < args.IndexOf("-f"), "giriş seçenekleri -i'den önce, çıkış seçenekleri sonra");
    }

    [Fact]
    public void BuildArgs_bmp_is_uncompressed_bgra_without_jpeg_quality()
    {
        var args = FfmpegFrameSource.BuildArgs(new FfmpegOptions { OutputFormat = FfmpegOutputFormat.Bmp }, "rtsp://cam/x", 5, null);
        string joined = string.Join(" ", args);
        Assert.Contains("-f image2pipe -vcodec bmp -pix_fmt bgra", joined);
        Assert.DoesNotContain("-q:v", joined);
        Assert.DoesNotContain("mjpeg", joined);
        Assert.Contains("-vf fps=5 pipe:1", joined);
    }

    [Fact]
    public void BuildArgs_mjpeg_keeps_legacy_encoder_and_quality()
    {
        var args = FfmpegFrameSource.BuildArgs(new FfmpegOptions { OutputFormat = FfmpegOutputFormat.Mjpeg, JpegQuality = 4 }, "rtsp://cam/x", 2, null);
        string joined = string.Join(" ", args);
        Assert.Contains("-vcodec mjpeg -q:v 4", joined);
        Assert.DoesNotContain("bmp", joined);
    }

    [Fact]
    public void BuildArgs_places_hwaccel_and_extra_input_args_before_input()
    {
        var args = FfmpegFrameSource.BuildArgs(new FfmpegOptions { ExtraInputArgs = "-rw_timeout 5000000", ScaleWidth = 1920 },
                                               "rtsp://cam/x", 5, "d3d11va");
        int hw = args.IndexOf("-hwaccel"), input = args.IndexOf("-i"), extra = args.IndexOf("-rw_timeout");
        Assert.True(hw >= 0 && hw < input);
        Assert.Equal("d3d11va", args[hw + 1]);
        Assert.True(extra >= 0 && extra < input);
        Assert.Contains("fps=5,scale=1920:-2", string.Join(" ", args));
    }

    // ─────────────── 2.0.0: YUV (Y4M) çıkışı, çözücü iş parçacığı sınırı, ekran kartında kare düşürme ───────────────

    [Fact]
    public void BuildArgs_y4m_emits_native_yuv_without_color_conversion()
    {
        var args = FfmpegFrameSource.BuildArgs(new FfmpegOptions { OutputFormat = FfmpegOutputFormat.Y4m }, "rtsp://cam/x", 5, null);
        string joined = string.Join(" ", args);
        Assert.Contains("-f yuv4mpegpipe -pix_fmt yuv420p", joined);
        Assert.DoesNotContain("bgra", joined);
        Assert.DoesNotContain("image2pipe", joined);
        Assert.Contains("-vf fps=5 pipe:1", joined);
    }

    [Fact]
    public void BuildArgs_decoder_threads_is_an_input_option_and_omitted_when_zero()
    {
        var with = FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "rtsp://cam/x", 5, null, gpuFrameDrop: false, decoderThreads: 2);
        int t = with.IndexOf("-threads");
        Assert.True(t >= 0 && t < with.IndexOf("-i"), "-threads, -i'den ÖNCE olmalı (çözücü seçeneği; sonra gelirse kodlayıcıya uygulanır)");
        Assert.Equal("2", with[t + 1]);
        Assert.DoesNotContain("-threads", FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "rtsp://cam/x", 5, null));
    }

    [Theory]
    [InlineData(0, 1, 1)]    // otomatik, tek çekirdek
    [InlineData(0, 2, 2)]
    [InlineData(0, 16, 2)]   // otomatik: en çok 2 (fazlası yalnız bellek harcıyordu — ölçüldü)
    [InlineData(4, 16, 4)]   // açık değer korunur
    [InlineData(-1, 16, 0)]  // -1: ffmpeg'e bırak → argüman yazılmaz
    public void ResolveDecoderThreads_caps_auto_at_two(int setting, int cores, int expected)
        => Assert.Equal(expected, FfmpegFrameSource.ResolveDecoderThreads(setting, cores));

    [Theory]
    [InlineData(0, 16, 1)]   // otomatik + donanım çözme: tek iş parçacığı (bellek 75 → 60 MB, işlemci aynı — ölçüldü)
    [InlineData(4, 16, 4)]   // açık değer donanım çözmede de korunur
    [InlineData(-1, 16, 0)]  // -1: ffmpeg'e bırak
    public void ResolveDecoderThreads_uses_one_thread_for_hardware_decode(int setting, int cores, int expected)
        => Assert.Equal(expected, FfmpegFrameSource.ResolveDecoderThreads(setting, cores, hardwareDecode: true));

    [Fact]
    public void BuildArgs_gpu_frame_drop_keeps_frames_on_gpu_and_downloads_after_fps()
    {
        var args = FfmpegFrameSource.BuildArgs(new FfmpegOptions { OutputFormat = FfmpegOutputFormat.Y4m }, "rtsp://cam/x", 5, "d3d11va", gpuFrameDrop: true);
        string joined = string.Join(" ", args);
        Assert.Contains("-hwaccel d3d11va -hwaccel_output_format d3d11", joined);
        Assert.Contains("-vf fps=5,hwdownload,format=nv12 pipe:1", joined);
        Assert.True(args.IndexOf("-hwaccel_output_format") < args.IndexOf("-i"));

        // kapalıyken ya da yazılım çözmede süzgeç zincirine dokunulmaz
        Assert.DoesNotContain("hwdownload", string.Join(" ", FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "rtsp://cam/x", 5, "d3d11va", gpuFrameDrop: false)));
        Assert.DoesNotContain("hwdownload", string.Join(" ", FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "rtsp://cam/x", 5, null, gpuFrameDrop: true)));
        // bilinmeyen yöntem: biçim adı yok → düz donanım çözme
        var unknown = string.Join(" ", FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "rtsp://cam/x", 5, "mediacodec", gpuFrameDrop: true));
        Assert.Contains("-hwaccel mediacodec", unknown);
        Assert.DoesNotContain("hwdownload", unknown);
        // ölçekleme indirmeden SONRA gelir (ölçekleyici sistem belleğinde çalışır)
        Assert.Contains("fps=5,hwdownload,format=nv12,scale=1920:-2",
            string.Join(" ", FfmpegFrameSource.BuildArgs(new FfmpegOptions { ScaleWidth = 1920 }, "rtsp://cam/x", 5, "videotoolbox", gpuFrameDrop: true)));
    }

    [Theory]
    [InlineData("videotoolbox", "videotoolbox_vld")]
    [InlineData("d3d11va", "d3d11")]
    [InlineData("dxva2", "dxva2_vld")]
    [InlineData("qsv", "qsv")]
    [InlineData("D3D11VA", "d3d11")]
    [InlineData("mediacodec", null)]
    [InlineData(null, null)]
    public void HwOutputFormat_maps_known_methods(string? accel, string? expected)
        => Assert.Equal(expected, FfmpegFrameSource.HwOutputFormat(accel));

    [Fact]
    public void BuildArgs_omits_rtsp_transport_for_non_rtsp_inputs()
    {
        // Dosya/HTTP girişinde -rtsp_transport "seçenek bulunamadı" hatası verir → kayıttan oynatma çalışmaz.
        Assert.DoesNotContain("-rtsp_transport", FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "/tmp/kayit.mkv", 5, null));
        Assert.Contains("-rtsp_transport", FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "rtsp://cam/x", 5, null));
        Assert.Contains("-rtsp_transport", FfmpegFrameSource.BuildArgs(new FfmpegOptions(), "RTSPS://cam/x", 5, null));
    }

    [Fact]
    public void ResolveHwAccel_auto_picks_platform_method_only_if_ffmpeg_supports_it()
    {
        var win = new[] { "cuda", "dxva2", "qsv", "d3d11va" };
        Assert.Equal("d3d11va", FfmpegFrameSource.ResolveHwAccel("auto", win, isWindows: true, isMacOS: false));
        Assert.Equal("dxva2", FfmpegFrameSource.ResolveHwAccel("auto", new[] { "dxva2" }, true, false));
        Assert.Equal("videotoolbox", FfmpegFrameSource.ResolveHwAccel("auto", new[] { "videotoolbox" }, false, true));
        Assert.Null(FfmpegFrameSource.ResolveHwAccel("auto", new[] { "cuda" }, true, false));   // uygun yöntem yok → yazılım
        Assert.Null(FfmpegFrameSource.ResolveHwAccel("auto", Array.Empty<string>(), true, false)); // probe boş → yazılım (güvenli)
        Assert.Null(FfmpegFrameSource.ResolveHwAccel("auto", new[] { "vaapi" }, false, false));  // Linux: auto seçmez
    }

    [Fact]
    public void ResolveHwAccel_explicit_and_none()
    {
        Assert.Null(FfmpegFrameSource.ResolveHwAccel("none", new[] { "d3d11va" }, true, false));
        Assert.Null(FfmpegFrameSource.ResolveHwAccel("", new[] { "d3d11va" }, true, false));
        Assert.Null(FfmpegFrameSource.ResolveHwAccel(null, new[] { "d3d11va" }, true, false));
        Assert.Equal("qsv", FfmpegFrameSource.ResolveHwAccel("QSV", new[] { "qsv" }, true, false));   // büyük/küçük harf duyarsız
        Assert.Equal("vaapi", FfmpegFrameSource.ResolveHwAccel("vaapi", Array.Empty<string>(), false, false)); // probe yoksa kullanıcıya güven
        Assert.Null(FfmpegFrameSource.ResolveHwAccel("vaapi", new[] { "d3d11va" }, true, false));   // probe var, listede değil → yazılım
        Assert.Null(FfmpegFrameSource.ResolveHwAccel("d3d11va; rm -rf", new[] { "d3d11va" }, true, false)); // enjeksiyon → reddedilir
    }

    [Fact]
    public void ParseHwAccels_reads_ffmpeg_output()
    {
        var set = FfmpegFrameSource.ParseHwAccels("Hardware acceleration methods:\nvideotoolbox\ncuda\n\n");
        Assert.Contains("videotoolbox", set);
        Assert.Contains("cuda", set);
        Assert.Equal(2, set.Count);
        Assert.Empty(FfmpegFrameSource.ParseHwAccels(""));
    }
}
