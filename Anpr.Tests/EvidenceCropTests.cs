using Anpr.Core;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// Kanıt kadrajı (2026-07-27): OCR kırpıntısı insan doğrulaması için çok dardı — 3031 canlı kanıt
/// görselinin %60.7'sinde plaka en az bir kenara değiyordu (sağda medyan boşluk 5 px, %42.8'inde SIFIR).
/// Kanıt artık TAM KAREDEN geniş kadrajla üretilir; bu testler kadraj aritmetiğini sabitler.
/// </summary>
public class EvidenceCropTests
{
    // Sahadaki tipik durum: 2688x1520 kare, ortalarda ~150x68 plaka.
    private const int FrameW = 2688, FrameH = 1520;

    [Fact]
    public void Widens_box_by_ratio_on_every_side()
    {
        var box = new PlateBox(1000, 700, 150, 68);
        var r = CameraWorker.EvidenceRect(box, FrameW, FrameH, 1.0f);

        // 1.0 = her kenara kutu boyutu kadar → 3x genişlik/yükseklik
        Assert.Equal(150 * 3, r.Width);
        Assert.Equal(68 * 3, r.Height);
        // plaka çerçevenin ortasında kalmalı (her yanda eşit pay)
        Assert.Equal(1000 - 150, r.Left);
        Assert.Equal(700 - 68, r.Top);
    }

    [Fact]
    public void Gives_real_pixels_not_upscaling()
    {
        // Ölçülen kazanç: medyan 150x68 → 453x214 civarı. Burada 3x ile 450x204 (gerçek piksel).
        var r = CameraWorker.EvidenceRect(new PlateBox(1000, 700, 150, 68), FrameW, FrameH, 1.0f);
        Assert.True(r.Width * r.Height > 150 * 68 * 8, $"en az 8x piksel bekleniyordu, gelen {r.Width}x{r.Height}");
    }

    [Fact]
    public void Never_leaves_the_frame_at_edges()
    {
        // Kare kenarındaki plaka: kadraj taşmamalı (aksi halde ExtractSubset patlar).
        foreach (var box in new[]
                 {
                     new PlateBox(0, 0, 150, 68),                      // sol-üst köşe
                     new PlateBox(FrameW - 150, FrameH - 68, 150, 68), // sağ-alt köşe
                     new PlateBox(10, FrameH - 70, 300, 68),           // sol-alt, geniş
                 })
        {
            var r = CameraWorker.EvidenceRect(box, FrameW, FrameH, 1.0f);
            Assert.True(r.Left >= 0 && r.Top >= 0, $"negatif köşe: {r}");
            Assert.True(r.Right <= FrameW && r.Bottom <= FrameH, $"kare dışına taştı: {r}");
            Assert.True(r.Width > 0 && r.Height > 0);
        }
    }

    [Fact]
    public void Zero_ratio_returns_the_plate_box_itself()
    {
        // 0 = kapalı: kadraj büyütülmez (eski davranışa en yakın hâl).
        var box = new PlateBox(1000, 700, 150, 68);
        var r = CameraWorker.EvidenceRect(box, FrameW, FrameH, 0f);
        Assert.Equal(box.Width, r.Width);
        Assert.Equal(box.Height, r.Height);
    }

    [Fact]
    public void Handles_degenerate_boxes_without_throwing()
    {
        // Bozuk kutu gelse bile kanıt yolu çökmemeli (kanıt kaydı best-effort'tur).
        var r = CameraWorker.EvidenceRect(new PlateBox(-50, -50, 0, 0), FrameW, FrameH, 1.0f);
        Assert.True(r.Width >= 0 && r.Height >= 0);
        var r2 = CameraWorker.EvidenceRect(new PlateBox(5000, 5000, 150, 68), FrameW, FrameH, 1.0f);
        Assert.True(r2.Left >= 0 && r2.Right <= FrameW && r2.Width >= 0);
    }

    [Fact]
    public void Fixes_the_measured_right_edge_clipping_case()
    {
        // Ölçülen asıl şikayet: sağ kenarda boşluk 0 px → son karakter kadraj dışında.
        // Aynı kutu için kanıt kadrajı sağda gerçek boşluk bırakmalı.
        var box = new PlateBox(1200, 800, 160, 60);
        var r = CameraWorker.EvidenceRect(box, FrameW, FrameH, 1.0f);
        int rightGap = r.Right - (box.X + box.Width);
        int leftGap = box.X - r.Left;
        Assert.True(rightGap >= 100, $"sağda anlamlı boşluk bekleniyordu, gelen {rightGap} px");
        Assert.True(leftGap >= 100, $"solda anlamlı boşluk bekleniyordu, gelen {leftGap} px");
    }
}
