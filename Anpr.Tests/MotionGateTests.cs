using Anpr.Sdk.Frames;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// Hareket kapısı (Faz 2): boş sahnede "atla", araç girdiğinde "işle" demeli; ROI dışındaki hareket
/// kapıyı tetiklememeli; ilk kare ve referans değişimi her zaman "işle" olmalı. Kapı yalnız atlar,
/// asla okumaz — yanlış "hareket yok" tek kayıp türüdür; testler o yönü sıkar.
/// </summary>
public class MotionGateTests
{
    private static VideoFrame Frame(int w, int h, Func<int, int, byte> luma, long seq = 0)
        => VideoFrame.FromBmp(VideoFrameTests.MakeBmp(w, h, (x, y) => { var v = luma(x, y); return (v, v, v); }), DateTimeOffset.UnixEpoch, seq);

    [Fact]
    public void First_frame_is_always_motion_then_static_scene_is_not()
    {
        var gate = new MotionGate(cols: 8, rows: 8, pixelDelta: 20, changedFraction: 0.02);
        var a = Frame(64, 64, (x, y) => 100);
        var b = Frame(64, 64, (x, y) => 100);

        var d1 = gate.Evaluate(a, null);
        Assert.True(d1.Motion); Assert.True(d1.FirstFrame);

        var d2 = gate.Evaluate(b, null);
        Assert.False(d2.Motion); Assert.False(d2.FirstFrame); Assert.Equal(0.0, d2.ChangedFraction);
    }

    [Fact]
    public void Sensor_noise_below_delta_is_not_motion()
    {
        var gate = new MotionGate(8, 8, pixelDelta: 20, changedFraction: 0.02);
        gate.Evaluate(Frame(64, 64, (x, y) => 100), null);
        // ±8 seviye rastgele gürültü: eşik 20'nin altında → hareket yok
        var rnd = new Random(1);
        var noisy = Frame(64, 64, (x, y) => (byte)(100 + rnd.Next(-8, 9)));
        Assert.False(gate.Evaluate(noisy, null).Motion);
    }

    [Fact]
    public void Vehicle_sized_block_inside_roi_triggers_motion()
    {
        var gate = new MotionGate(16, 16, pixelDelta: 20, changedFraction: 0.02);
        var roi = (X: 32, Y: 32, W: 64, H: 64);
        gate.Evaluate(Frame(128, 128, (x, y) => 90), roi);
        // ROI'nin ~%25'ini kaplayan koyu blok (araç)
        var car = Frame(128, 128, (x, y) => (x >= 40 && x < 72 && y >= 40 && y < 72) ? (byte)20 : (byte)90);
        var d = gate.Evaluate(car, roi);
        Assert.True(d.Motion);
        Assert.InRange(d.ChangedFraction, 0.15, 0.35);
    }

    [Fact]
    public void Motion_outside_roi_does_not_trigger()
    {
        var gate = new MotionGate(16, 16, pixelDelta: 20, changedFraction: 0.02);
        var roi = (X: 64, Y: 64, W: 60, H: 60);
        gate.Evaluate(Frame(128, 128, (x, y) => 90), roi);
        // Sol üstte (ROI dışı) büyük değişim — cadde trafiği gibi
        var street = Frame(128, 128, (x, y) => (x < 50 && y < 50) ? (byte)10 : (byte)90);
        Assert.False(gate.Evaluate(street, roi).Motion);
    }

    [Fact]
    public void Roi_or_size_change_resets_reference_and_forces_processing()
    {
        var gate = new MotionGate(8, 8, 20, 0.02);
        gate.Evaluate(Frame(64, 64, (x, y) => 100), null);
        Assert.False(gate.Evaluate(Frame(64, 64, (x, y) => 100), null).Motion);
        // ROI değişti → referans geçersiz → işle
        var d = gate.Evaluate(Frame(64, 64, (x, y) => 100), (8, 8, 40, 40));
        Assert.True(d.Motion); Assert.True(d.FirstFrame);
        // Boyut değişti → yine işle
        Assert.True(gate.Evaluate(Frame(32, 32, (x, y) => 100), (8, 8, 20, 20)).FirstFrame);
    }

    [Fact]
    public void Jpeg_frame_always_passes()
    {
        var gate = new MotionGate(8, 8, 20, 0.02);
        var jpeg = VideoFrame.FromJpeg(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, DateTimeOffset.UnixEpoch);
        Assert.True(gate.Evaluate(jpeg, null).Motion);
        Assert.True(gate.Evaluate(jpeg, null).Motion);
    }

    // ─────────────── YUV kare + "hareket nerede" kutusu (2.0.0) ───────────────

    private static VideoFrame YuvFrame(int w, int h, Func<int, int, byte> luma)
        => VideoFrame.FromYuv420(Yuv420FrameViewTests.MakeYuv(w, h, (x, y) => (luma(x, y), 128, 128)), w, h, false, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Yuv_frames_are_gated_like_bgra_frames()
    {
        var gate = new MotionGate(16, 16, pixelDelta: 20, changedFraction: 0.02);
        Assert.True(gate.Evaluate(YuvFrame(128, 128, (x, y) => 90), null).FirstFrame);
        Assert.False(gate.Evaluate(YuvFrame(128, 128, (x, y) => 90), null).Motion);                 // boş sahne → atla
        var rnd = new Random(3);
        Assert.False(gate.Evaluate(YuvFrame(128, 128, (x, y) => (byte)(90 + rnd.Next(-6, 7))), null).Motion);   // sensör gürültüsü
        var car = gate.Evaluate(YuvFrame(128, 128, (x, y) => x >= 40 && x < 72 && y >= 40 && y < 72 ? (byte)30 : (byte)90), null);
        Assert.True(car.Motion);
        Assert.InRange(car.ChangedFraction, 0.04, 0.09);   // 32×32 / 128×128 = %6,25
    }

    [Fact]
    public void Limited_range_luma_uses_a_proportionally_smaller_threshold()
    {
        // BGRA yolunda eşik 20 seviye (0–255). Sınırlı aralıklı Y'de aynı görsel fark 20·219/255 ≈ 17 seviyedir:
        // Y'de 18'lik fark hareket SAYILMALI (BGRA'da 21'e denk), 16'lık fark sayılmamalı.
        var g1 = new MotionGate(8, 8, pixelDelta: 20, changedFraction: 0.5);
        g1.Evaluate(YuvFrame(64, 64, (x, y) => 100), null);
        Assert.True(g1.Evaluate(YuvFrame(64, 64, (x, y) => 118), null).Motion);
        var g2 = new MotionGate(8, 8, pixelDelta: 20, changedFraction: 0.5);
        g2.Evaluate(YuvFrame(64, 64, (x, y) => 100), null);
        Assert.False(g2.Evaluate(YuvFrame(64, 64, (x, y) => 116), null).Motion);
    }

    [Fact]
    public void ChangedBox_bounds_the_moving_region_in_pixels()
    {
        var gate = new MotionGate(16, 16, pixelDelta: 20, changedFraction: 0.01);
        var still = gate.Evaluate(Frame(160, 160, (x, y) => 90), null);
        Assert.Null(still.ChangedBox);                                         // ilk kare: referans yok
        Assert.Null(gate.Evaluate(Frame(160, 160, (x, y) => 90), null).ChangedBox);   // değişim yok

        // Araç: x 40..79, y 100..139 (ızgara hücresi 10 px → sütun 4..7, satır 10..13)
        var d = gate.Evaluate(Frame(160, 160, (x, y) => x >= 40 && x < 80 && y >= 100 && y < 140 ? (byte)20 : (byte)90), null);
        Assert.NotNull(d.ChangedBox);
        var b = d.ChangedBox!.Value;
        Assert.Equal((40, 100, 40, 40), (b.X, b.Y, b.W, b.H));
    }

    [Fact]
    public void ChangedBox_is_offset_by_the_roi_rect()
    {
        var gate = new MotionGate(8, 8, pixelDelta: 20, changedFraction: 0.01);
        var roi = (X: 80, Y: 40, W: 80, H: 80);                                  // hücre 10 px
        gate.Evaluate(Frame(200, 160, (x, y) => 90), roi);
        var d = gate.Evaluate(Frame(200, 160, (x, y) => x >= 100 && x < 120 && y >= 60 && y < 70 ? (byte)10 : (byte)90), roi);
        var b = d.ChangedBox!.Value;
        Assert.Equal((100, 60, 20, 10), (b.X, b.Y, b.W, b.H));
    }

    [Fact]
    public void Polygon_mask_ignores_motion_in_the_bbox_corner_outside_the_polygon()
    {
        // ROI: karenin sol üst köşesi DIŞARIDA kalan bir üçgen (sağ alt yarı). Sınır kutusu tüm kare.
        var poly = Anpr.Core.Imaging.RoiPolygon.Parse("1000,0;1000,1000;0,1000")!;
        var rect = (X: 0, Y: 0, W: 160, H: 160);
        Func<int, int, byte> corner = (x, y) => x < 50 && y < 50 ? (byte)10 : (byte)90;   // sol üst köşede hareket (çokgen dışı)
        Func<int, int, byte> inside = (x, y) => x >= 100 && y >= 100 ? (byte)10 : (byte)90; // sağ altta hareket (çokgen içi)

        var masked = new MotionGate(16, 16, pixelDelta: 20, changedFraction: 0.02);
        masked.Evaluate(Frame(160, 160, (x, y) => 90), rect, poly);
        var d1 = masked.Evaluate(Frame(160, 160, corner), rect, poly);
        Assert.False(d1.Motion);                       // çokgen dışındaki hareket tetiklemez
        Assert.Null(d1.ChangedBox);
        var d2 = masked.Evaluate(Frame(160, 160, inside), rect, poly);
        Assert.True(d2.Motion);                        // çokgen içindeki hareket tetikler
        Assert.True(masked.ActiveCells < 16 * 16 && masked.ActiveCells > 16 * 16 / 3);   // yaklaşık yarısı

        // Maskesiz kapı aynı köşe hareketinde TETİKLENİR — testin ayırt edici olduğunun kanıtı.
        var plain = new MotionGate(16, 16, pixelDelta: 20, changedFraction: 0.02);
        plain.Evaluate(Frame(160, 160, (x, y) => 90), rect);
        Assert.True(plain.Evaluate(Frame(160, 160, corner), rect).Motion);
    }

    [Fact]
    public void Polygon_change_resets_the_reference()
    {
        var gate = new MotionGate(8, 8, 20, 0.02);
        var rect = (X: 0, Y: 0, W: 64, H: 64);
        var p1 = Anpr.Core.Imaging.RoiPolygon.Parse("0,0;1000,0;1000,1000;0,1000")!;
        var p2 = Anpr.Core.Imaging.RoiPolygon.Parse("0,0;500,0;500,1000;0,1000")!;
        gate.Evaluate(Frame(64, 64, (x, y) => 100), rect, p1);
        Assert.False(gate.Evaluate(Frame(64, 64, (x, y) => 100), rect, p1).FirstFrame);
        Assert.True(gate.Evaluate(Frame(64, 64, (x, y) => 100), rect, p2).FirstFrame);   // çokgen değişti → işle
    }

    [Fact]
    public void ClampRect_keeps_rect_inside_frame()
    {
        Assert.Equal((0, 0, 10, 10), MotionGate.ClampRect((-5, -5, 15, 15), 10, 10));
        Assert.Equal((5, 5, 5, 5), MotionGate.ClampRect((5, 5, 50, 50), 10, 10));
    }
}
