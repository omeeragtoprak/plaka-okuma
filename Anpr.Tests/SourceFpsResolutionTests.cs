using Anpr.Sdk.Configuration;

namespace Anpr.Tests;

/// <summary>
/// Kaynak kare hızı çözümü (2.1.0): ffmpeg'in ürettiği hız ile kamera döngüsünün beklediği hız AYNI kaynaktan
/// (<c>AnprServiceOptions.ResolveSourceFps</c>) gelir. Ayrışırlarsa ya kareler boşuna üretilir ya da döngü
/// gelmeyecek kareyi bekler.
/// </summary>
public class SourceFpsResolutionTests
{
    [Fact]
    public void Default_motion_triggered_loop_reads_the_camera_at_5_fps()
    {
        var o = new AnprServiceOptions();
        Assert.True(o.UsesMotionTriggeredLoop);
        Assert.Equal(5.0, o.ResolveSourceFps());
    }

    [Fact]
    public void Explicit_ffmpeg_source_fps_wins_over_everything()
    {
        var o = new AnprServiceOptions();
        o.Ffmpeg.SourceFps = 3;
        Assert.Equal(3.0, o.ResolveSourceFps());
    }

    [Theory]
    [InlineData(MotionGateMode.Shadow)]
    [InlineData(MotionGateMode.Off)]
    public void Timer_loop_keeps_the_2_0_0_behaviour_source_equals_fps(MotionGateMode mode)
    {
        var o = new AnprServiceOptions { MotionGate = mode, Fps = 2 };
        Assert.False(o.UsesMotionTriggeredLoop);
        Assert.Equal(2.0, o.ResolveSourceFps());
    }

    [Fact]
    public void Mjpeg_path_cannot_gate_so_it_falls_back_to_fps()
    {
        var o = new AnprServiceOptions { Fps = 2 };
        o.Ffmpeg.OutputFormat = FfmpegOutputFormat.Mjpeg;
        Assert.False(o.UsesMotionTriggeredLoop);
        Assert.Equal(2.0, o.ResolveSourceFps());
    }

    [Fact]
    public void Motion_source_rate_is_independent_of_the_timer_fps()
    {
        var o = new AnprServiceOptions { Fps = 2, MotionGateSourceFps = 6 };
        Assert.Equal(6.0, o.ResolveSourceFps());
    }
}

/// <summary>
/// Servis → motor ayar eşlemesi (2.1.0): 2.0.0'da SDK katmanının spin değeri çekirdeğinkinden farklıydı ve paketi
/// kullanan herkes yanlış değeri aldı. Eşleme artık tek, saf bir yöntemde; ONNX ve bellek ayarları gerçekten taşınıyor mu?
/// </summary>
public class EngineOptionsMappingTests
{
    [Fact]
    public void Onnx_threading_and_memory_settings_reach_the_engine()
    {
        var o = new AnprServiceOptions
        {
            OnnxIntraOpThreads = 3, OnnxAllowSpinning = true, OnnxCpuMemArena = true, OnnxMemoryPattern = true,
            MinConfidence = 0.61f, EnhanceSr = false,
        };
        var e = Anpr.Sdk.Workers.AnprCameraSupervisor.ToEngineOptions(o, "d.onnx", "o.onnx", "o.yaml", "1,1;2,2;3,3");
        Assert.Equal(3, e.OnnxIntraOpThreads);
        Assert.True(e.OnnxAllowSpinning);
        Assert.True(e.OnnxCpuMemArena);
        Assert.True(e.OnnxMemoryPattern);
        Assert.Equal(0.61f, e.MinConfidence);
        Assert.False(e.EnhanceSr);
        Assert.Equal("d.onnx", e.DetectorModelPath);
        Assert.Equal("1,1;2,2;3,3", e.RoiPolygon);
    }

    [Fact]
    public void Shipped_service_defaults_map_to_the_low_footprint_engine_settings()
    {
        var e = Anpr.Sdk.Workers.AnprCameraSupervisor.ToEngineOptions(new AnprServiceOptions(), "d", "o", "y", "");
        Assert.Equal(1, e.OnnxIntraOpThreads);
        Assert.False(e.OnnxAllowSpinning);
        Assert.False(e.OnnxCpuMemArena);
        Assert.False(e.OnnxMemoryPattern);
    }
}
