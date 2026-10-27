using Anpr.Core;

namespace Anpr.Tests;

/// <summary>
/// Geometri kapısı (2026-07-27): dedektörün dikey/parçalı kutusundan OCR uydurma plaka üretiyordu.
/// Canlı ölçüm (882 roi olayı, kamera ANPR referans): oran&lt;1.5 bandı %47.8 doğrulanma vs üstü ~%80;
/// eşik 1.0 → NET +41 olay. Vakalar gerçek evidence dosyalarından alınmıştır.
/// </summary>
public class PlateAspectGateTests
{
    [Theory]
    // Canlı yakalanan HATALI kutular (dikey şerit) — OCR bunlardan uydurma plaka üretmişti:
    [InlineData(52, 124)]    // 20260723-164255_giris → "25 AZF 16" (görselde plakanın dikey şeridi)
    [InlineData(70, 146)]    // 20260724-174927_giris → "25 AD 706"
    [InlineData(59, 120)]    // 20260723-145641_giris → "25 ADD 16"
    [InlineData(84, 164)]    // 20260727-093330_giris → "25 EE 365"
    public void Rejects_vertical_boxes_that_cannot_be_a_plate(int w, int h)
    {
        Assert.False(AnprEngine.HasPlateLikeAspect(w, h, 1.0f));
    }

    [Theory]
    // Gerçek plaka kırpıntıları — kapı bunlara DOKUNMAMALI (tek satır + açılı/çift satır dahil):
    [InlineData(255, 119)]   // 25 ACE 052 (net okuma, güven 1.00)
    [InlineData(116, 48)]    // 25 AEK 262
    [InlineData(145, 50)]    // 66 ACG 230
    [InlineData(188, 80)]    // 25 ACE 052 (diğer kamera)
    [InlineData(91, 77)]     // 66 ACG 20 — kesik ama YATAY: kapı bunu elemez (farklı sorun, bkz. padding)
    public void Keeps_real_plate_crops(int w, int h)
    {
        Assert.True(AnprEngine.HasPlateLikeAspect(w, h, 1.0f));
    }

    [Fact]
    public void Keeps_two_row_plates_at_default_threshold()
    {
        // Motosiklet/ticari çift satır plaka ≈2:1 — varsayılan 1.0 eşiği bunları ELEMEMELİ
        // (ölçümde 1.8 eşiği NET −55 çıkmıştı; bu yüzden eşik 1.0'da tutuldu).
        Assert.True(AnprEngine.HasPlateLikeAspect(120, 60, 1.0f));
        Assert.True(AnprEngine.HasPlateLikeAspect(100, 90, 1.0f));   // neredeyse kare ama yatık → geçer
    }

    [Fact]
    public void Disabled_when_ratio_is_zero_or_negative()
    {
        // Geri alma yolu: kapı config ile tamamen kapatılabilir (ölçüm başka sahada farklı çıkarsa).
        Assert.True(AnprEngine.HasPlateLikeAspect(52, 124, 0f));
        Assert.True(AnprEngine.HasPlateLikeAspect(52, 124, -1f));
    }

    [Fact]
    public void Rejects_degenerate_box_without_dividing_by_zero()
    {
        Assert.False(AnprEngine.HasPlateLikeAspect(100, 0, 1.0f));
    }

    [Fact]
    public void Square_box_is_exactly_on_the_boundary_and_passes()
    {
        // Sınır davranışı NET olsun: oran == eşik geçer (>=), altı elenir.
        Assert.True(AnprEngine.HasPlateLikeAspect(100, 100, 1.0f));
        Assert.False(AnprEngine.HasPlateLikeAspect(99, 100, 1.0f));
    }
}
