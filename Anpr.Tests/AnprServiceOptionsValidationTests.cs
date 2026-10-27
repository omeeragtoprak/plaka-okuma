using Anpr.Sdk.Configuration;

namespace Anpr.Tests;

/// <summary>M6: açılış config doğrulaması — aralık-dışı eşik/fps/port bariyeri sessizce kilitlemesin.</summary>
public class AnprServiceOptionsValidationTests
{
    private static AnprServiceOptions Valid() =>
        new() { Cameras = { new CameraOptions { Id = "giris", RtspUrl = "rtsp://x:554/1" } } };

    [Fact]
    public void Valid_config_passes() => Valid().Validate();   // fırlatmamalı

    [Fact]
    public void Default_config_with_no_cameras_passes() => new AnprServiceOptions().Validate();   // üretim varsayılanları geçer

    [Fact]
    public void Fps_zero_throws()
    {
        var o = Valid(); o.Fps = 0;
        Assert.Contains("Fps", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Threshold_typo_above_one_throws()   // 0.45 yerine 45 → hiç emit olmaz
    {
        var o = Valid(); o.EmitMinOcrConfidence = 45f;
        Assert.Contains("EmitMinOcrConfidence", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(31.0)]
    public void MotionGateSourceFps_out_of_range_throws(double fps)
    {
        var o = Valid(); o.MotionGateSourceFps = fps;
        Assert.Contains("MotionGateSourceFps", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void OnnxIntraOpThreads_zero_throws()
    {
        var o = Valid(); o.OnnxIntraOpThreads = 0;
        Assert.Contains("OnnxIntraOpThreads", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Camera_without_url_or_ip_throws()
    {
        var o = new AnprServiceOptions { Cameras = { new CameraOptions { Id = "x" } } };
        Assert.Contains("RtspUrl", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Duplicate_camera_ids_throw()
    {
        var o = new AnprServiceOptions
        {
            Cameras =
            {
                new CameraOptions { Id = "a", RtspUrl = "rtsp://1" },
                new CameraOptions { Id = "a", RtspUrl = "rtsp://2" },
            }
        };
        Assert.Contains("yinelenmiş", Assert.Throws<InvalidOperationException>(o.Validate).Message);
    }

    [Fact]
    public void Aggregates_all_problems_into_one_error()
    {
        var o = new AnprServiceOptions { Fps = 0, HttpPort = 0, Cameras = { new CameraOptions { Id = "" } } };
        var msg = Assert.Throws<InvalidOperationException>(o.Validate).Message;
        Assert.Contains("Fps", msg);
        Assert.Contains("HttpPort", msg);
        Assert.Contains("Id boş", msg);   // tek fırlatmada tüm sorunlar
    }
}
