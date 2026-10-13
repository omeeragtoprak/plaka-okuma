using Anpr.Service.Panel;

namespace Anpr.Tests;

/// <summary>
/// Operasyonel kayma izleme. Bu sinyal yer-gerçeği GEREKTİRMEZ ve doğruluk İDDİA ETMEZ — yalnız
/// "bugün dünlerden anlamlı biçimde farklı" der. Testler iki şeyi birlikte kilitler: gerçek kaymayı
/// KAÇIRMAMAK ve önemsiz farkta GÜRÜLTÜ YAPMAMAK (uyarı gürültüsü operatörü sağırlaştırır).
/// </summary>
public class DriftMonitorTests
{
    private static MetricSample P(string n, double v, int k) => new(n, v, k, true);
    private static MetricSample V(string n, double v, int k) => new(n, v, k, false);

    [Fact]
    public void Real_regression_is_flagged_as_warning()
    {
        // Kırpma bozulursa kesilme imzası ARTAR: %2 → %12.
        var sig = DriftMonitor.Compare(
            new[] { P("kesilme imzası", 0.02, 900) },
            new[] { P("kesilme imzası", 0.12, 300) }).Single();
        Assert.Equal("uyari", sig.Severity);
        Assert.True(sig.Delta > 0);
    }

    [Fact]
    public void Statistically_significant_but_tiny_shift_is_only_watched_not_warned()
    {
        // Büyük n'de z-testi önemsiz farkı da "anlamlı" bulur. Etki eşiği olmadan panel
        // sürekli uyarır ve operatör sinyale karşı körleşir.
        var sig = DriftMonitor.Compare(
            new[] { P("otomatik açma", 0.928, 20000) },
            new[] { P("otomatik açma", 0.908, 20000) }).Single();
        Assert.Equal("izle", sig.Severity);
    }

    [Fact]
    public void Noise_level_difference_is_normal()
    {
        var sig = DriftMonitor.Compare(
            new[] { P("otomatik açma", 0.928, 500) },
            new[] { P("otomatik açma", 0.921, 500) }).Single();
        Assert.Equal("normal", sig.Severity);
    }

    [Fact]
    public void Small_sample_refuses_to_judge_instead_of_guessing()
    {
        // 2 günde 2 gece olayı vardı — böyle bir örneklemde "gece bozuldu" demek uydurmadır.
        var sig = DriftMonitor.Compare(
            new[] { P("gece otomatik açma", 0.90, 40) },
            new[] { P("gece otomatik açma", 0.50, 2) }).Single();
        Assert.Equal("veri-yetersiz", sig.Severity);
        Assert.Contains("Örneklem küçük", sig.Explanation);
    }

    [Fact]
    public void Metric_missing_from_baseline_is_reported_not_silently_dropped()
    {
        var sig = DriftMonitor.Compare(
            Array.Empty<MetricSample>(),
            new[] { P("yeni gösterge", 0.5, 100) }).Single();
        Assert.Equal("veri-yok", sig.Severity);
    }

    [Fact]
    public void Non_proportion_metric_uses_relative_change()
    {
        // Kare işleme gecikmesi 34 ms → 55 ms = %62 artış → uyarı.
        var sig = DriftMonitor.Compare(
            new[] { V("p50 ms", 34.0, 900) },
            new[] { V("p50 ms", 55.0, 900) }).Single();
        Assert.Equal("uyari", sig.Severity);

        // %10 artış eşik altı.
        var ok = DriftMonitor.Compare(
            new[] { V("p50 ms", 34.0, 900) },
            new[] { V("p50 ms", 37.4, 900) }).Single();
        Assert.Equal("normal", ok.Severity);
    }

    [Fact]
    public void Warnings_are_sorted_first_so_the_operator_sees_them()
    {
        var sigs = DriftMonitor.Compare(
            new[] { P("a", 0.90, 500), P("b", 0.50, 500), P("c", 0.30, 500) },
            new[] { P("a", 0.89, 500), P("b", 0.20, 500), P("c", 0.31, 500) });
        Assert.Equal("uyari", sigs[0].Severity);
        Assert.Equal("b", sigs[0].Metric);
    }

    [Fact]
    public void TwoProportionZ_is_zero_when_proportions_match()
        => Assert.Equal(0, DriftMonitor.TwoProportionZ(0.5, 100, 0.5, 100), 6);

    [Fact]
    public void TwoProportionZ_sign_shows_direction()
    {
        Assert.True(DriftMonitor.TwoProportionZ(0.5, 500, 0.7, 500) > 0);   // arttı
        Assert.True(DriftMonitor.TwoProportionZ(0.7, 500, 0.5, 500) < 0);   // düştü
    }

    [Fact]
    public void Degenerate_sample_sizes_do_not_throw()
    {
        Assert.Equal(0, DriftMonitor.TwoProportionZ(0.5, 0, 0.5, 0));
        var sig = DriftMonitor.Compare(new[] { P("x", 0, 0) }, new[] { P("x", 0, 0) }).Single();
        Assert.Equal("veri-yetersiz", sig.Severity);
    }
}
