using Anpr.Sdk.Events;

namespace Anpr.Tests;

public class FrequentPlateSnapTests
{
    private static FrequentPlateRegistry WithKnown(params string[] known) =>
        new() { KnownPlates = new HashSet<string>(known) };

    [Fact]
    public void Snaps_misread_to_known_plate_within_lev1()
    {
        // Siyah resmi plaka 25TGG001'in yanlış-okuması 25TCG001 (lev1) -> düzeltilir.
        var r = WithKnown("25TGG001", "25LT424");
        Assert.Equal("25TGG001", r.SnapTarget("25TCG001", maxLev: 1, includeFrequent: false));
        Assert.Equal("25TGG001", r.SnapTarget("25TGG004", maxLev: 1, includeFrequent: false)); // son hane
        Assert.Equal("25LT424", r.SnapTarget("25TT424", maxLev: 1, includeFrequent: false));
    }

    [Fact]
    public void Does_not_snap_when_too_far()
    {
        var r = WithKnown("25TGG001");
        // lev≥2 -> lev1 eşiğinde snaplenmez
        Assert.Equal("25NVG001", r.SnapTarget("25NVG001", maxLev: 1, includeFrequent: false)); // lev3
        Assert.Equal("05ZGG001", r.SnapTarget("05ZGG001", maxLev: 1, includeFrequent: false)); // lev2
    }

    [Fact]
    public void Lev2_catches_more()
    {
        var r = WithKnown("25TGG001");
        Assert.Equal("25TGG001", r.SnapTarget("05ZGG001", maxLev: 2, includeFrequent: false)); // lev2 -> snap
    }

    [Fact]
    public void Does_not_snap_a_genuine_distinct_plate()
    {
        // Whitelist'e uzak gerçek bir plaka dokunulmadan geçer (yanlış-snap yok).
        var r = WithKnown("25TGG001", "25LT424");
        Assert.Equal("34ABC123", r.SnapTarget("34ABC123", maxLev: 1, includeFrequent: false));
    }

    [Fact]
    public void Known_plate_itself_is_not_snapped()
    {
        var r = WithKnown("25TGG001", "25TGD001"); // iki yakın bilinen
        Assert.Equal("25TGG001", r.SnapTarget("25TGG001", maxLev: 1, includeFrequent: false));
    }

    [Fact]
    public void Frequent_snap_opt_in_only()
    {
        var r = new FrequentPlateRegistry { MinSightings = 2 };
        for (int i = 0; i < 3; i++) r.Record("25TGG001", 0.99f); // müdavim yap
        // includeFrequent=false -> snaplenmez
        Assert.Equal("25TCG001", r.SnapTarget("25TCG001", maxLev: 1, includeFrequent: false));
        // includeFrequent=true -> müdavime snap
        Assert.Equal("25TGG001", r.SnapTarget("25TCG001", maxLev: 1, includeFrequent: true));
    }

    [Fact]
    public void Disabled_when_maxlev_zero()
    {
        var r = WithKnown("25TGG001");
        Assert.Equal("25TCG001", r.SnapTarget("25TCG001", maxLev: 0, includeFrequent: false));
    }

    [Fact]
    public void NormalizeKnownPlates_skips_null_and_blank_without_crashing()
    {
        // Config'de null/boş öğe SDK init'ini ÇÖKERTMEMELİ (inceleme bulgusu).
        var set = FrequentPlateRegistry.NormalizeKnownPlates(new string?[] { "25 TGG 001", null, "", "  ", "25lt424", "x" });
        Assert.Contains("25TGG001", set);
        Assert.Contains("25LT424", set);   // boşluk temizlenir, büyük harfe çevrilir
        Assert.DoesNotContain("X", set);    // <5 uzunluk atılır
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void NormalizeKnownPlates_handles_null_list()
    {
        Assert.Empty(FrequentPlateRegistry.NormalizeKnownPlates(null));
    }
}
