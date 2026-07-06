using Anpr.Core.Imaging;

namespace Anpr.Tests;

public class RoiPolygonTests
{
    [Fact]
    public void Parse_rejects_invalid_and_short_specs()
    {
        Assert.Null(RoiPolygon.Parse(null));
        Assert.Null(RoiPolygon.Parse(""));
        Assert.Null(RoiPolygon.Parse("100,100;200,200"));       // 2 nokta yetmez
        Assert.Null(RoiPolygon.Parse("100,100;200;300,300"));   // bozuk çift
        Assert.NotNull(RoiPolygon.Parse("0,0;1000,0;500,1000"));
    }

    [Fact]
    public void ContainsPixel_scales_to_1000_grid_and_tests_inside()
    {
        // 0-1000 ölçekli üçgen; 2000x1000 piksellik karede test (x 2x ölçekli).
        var roi = RoiPolygon.Parse("0,0;1000,0;500,1000")!;
        Assert.True(roi.ContainsPixel(1000, 100, 2000, 1000));  // (500,100) merkez üstü -> içeride
        Assert.False(roi.ContainsPixel(40, 900, 2000, 1000));   // (20,900) sol-alt köşe -> dışarıda
    }

    [Fact]
    public void Camera_lane_quad_giris_contains_gate_center_not_far_street()
    {
        // Giriş kamerasının LaneConfig'inden türetilen dörtgen (salt-okuma ISAPI'den alınan noktalar).
        var roi = RoiPolygon.Parse("313,5;79,468;573,731;1000,525")!;
        Assert.True(roi.ContainsPixel(1344, 700, 2688, 1520));   // kapı korido merkezi (~500,460)
        Assert.False(roi.ContainsPixel(150, 1400, 2688, 1520));  // sol-alt dışı (~56,921)
    }
}
