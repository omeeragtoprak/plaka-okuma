using Anpr.Service.Panel;

namespace Anpr.Tests;

/// <summary>
/// Analiz panelinin karşılaştırma tablosu (2026-07-28). Panelin istemci tarafındaki eşleştirmesi yalnız
/// ekrandaki son N kaydı görüyordu; bu yüzden eşleştirme sunucuya taşındı. Buradaki testler tablonun
/// "tam, hatasız ve mantıklı" olma koşullarını sabitler.
/// </summary>
public class AnalyticsServiceTests
{
    [Theory]
    [InlineData("25 ABC 123", "25ABC123")]
    [InlineData("25-abc-123", "25ABC123")]
    [InlineData("  25 abc 123  ", "25ABC123")]
    [InlineData(null, "")]
    public void Compact_normalizes_plate_for_comparison(string? input, string expected)
    {
        Assert.Equal(expected, AnalyticsService.Compact(input));
    }

    [Theory]
    [InlineData("25ABC123", "25ABC123", 0)]   // birebir
    [InlineData("25ABC123", "25ABC124", 1)]   // 1-fark (son rakam)
    [InlineData("25EB859", "25EB858", 1)]     // canlı vaka
    [InlineData("25ADM254", "25AZM224", 2)]
    [InlineData("25ABC123", "", 8)]
    public void Levenshtein_measures_plate_distance(string a, string b, int expected)
    {
        Assert.Equal(expected, AnalyticsService.Levenshtein(a, b));
    }

    [Fact]
    public void Levenshtein_is_symmetric()
    {
        // Tablodaki "fark" sütunu hangi tarafın önce geldiğine göre değişmemeli.
        Assert.Equal(AnalyticsService.Levenshtein("25ABC123", "25ABD123"),
                     AnalyticsService.Levenshtein("25ABD123", "25ABC123"));
    }

    [Fact]
    public void Levenshtein_handles_length_change_from_snap()
    {
        // Snap karakter ekleyebilir (06DKM58 -> 06DKM581): tablo bunu 1 fark göstermeli, patlamamalı.
        Assert.Equal(1, AnalyticsService.Levenshtein("06DKM58", "06DKM581"));
    }

    // --- Geçiş kümeleme (2026-07-28): kamera aynı aracı ardışık karelerde birden çok kez raporluyor.
    // Ölçüm: 703 ham kayıt -> 461 gerçek geçiş (%34 fazla). Fazlalar eşleşemeyip "sadece kamera"
    // görünüyor ve iki tabloyu senkronsuz gösteriyordu. Bizim tarafta tekrar YOK (%0).

    private static AnalyticsService.CamRecord Rec(string plate, string cam, int sec, int ms = 0) =>
        new(new DateTimeOffset(2026, 7, 28, 16, 36, sec, ms, TimeSpan.FromHours(3)), cam, plate);

    [Fact]
    public void ClusterPasses_collapses_camera_burst_into_one_pass()
    {
        // Canlı vaka: 25ADP187 giriş kamerasında 16:36:01.096 / .295 / .404
        var src = new[] { Rec("25ADP187", "giris", 1, 96), Rec("25ADP187", "giris", 1, 295), Rec("25ADP187", "giris", 1, 404) };
        var outp = AnalyticsService.ClusterPasses(src, TimeSpan.FromSeconds(20));
        Assert.Single(outp);
        Assert.Equal(96, outp[0].Ts.Millisecond);   // İLK rapor temsil eder (bizim olayımıza en yakın an)
    }

    [Fact]
    public void ClusterPasses_keeps_same_plate_on_different_cameras()
    {
        // Aynı araç giriş VE çıkıştan geçer — bunlar iki AYRI gerçek geçiştir, birleştirilmemeli.
        var src = new[] { Rec("25ADP187", "giris", 1), Rec("25ADP187", "cikis", 2) };
        Assert.Equal(2, AnalyticsService.ClusterPasses(src, TimeSpan.FromSeconds(20)).Count);
    }

    [Fact]
    public void ClusterPasses_keeps_genuine_second_visit_after_window()
    {
        // Pencere dışındaki tekrar GERÇEK ikinci geçiştir (araç çıkıp geri geldi) — korunmalı.
        var src = new[] { Rec("25ADP187", "giris", 1), Rec("25ADP187", "giris", 55) };
        Assert.Equal(2, AnalyticsService.ClusterPasses(src, TimeSpan.FromSeconds(20)).Count);
    }

    [Fact]
    public void ClusterPasses_window_slides_with_each_repeat()
    {
        // Kesintisiz seri: her tekrar pencereyi kaydırır, seri tek geçiş sayılır (parçalanmamalı).
        var src = new[] { Rec("25ADP187", "giris", 0), Rec("25ADP187", "giris", 15), Rec("25ADP187", "giris", 30) };
        Assert.Single(AnalyticsService.ClusterPasses(src, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void ClusterPasses_is_order_independent()
    {
        // Dosyaya sırasız yazılmış kayıtlar da doğru kümelenmeli (poller iki kamerayı paralel yazıyor).
        var src = new[] { Rec("25ADP187", "giris", 1, 404), Rec("25ADP187", "giris", 1, 96), Rec("25ADP187", "giris", 1, 295) };
        var outp = AnalyticsService.ClusterPasses(src, TimeSpan.FromSeconds(20));
        Assert.Single(outp);
        Assert.Equal(96, outp[0].Ts.Millisecond);
    }
}
