using Anpr.Sdk.Events;

namespace Anpr.Tests;

/// <summary>
/// Müdavim koruması (2026-07-28, kullanıcı canlı panelde yakaladı): tekrar görülen bir plaka,
/// whitelist'teki BENZER bir plakaya snap'lenmemeli.
/// <para>Vaka: 25EB859 gerçek araç (whitelist'te YOK), üç kez okundu — ikisi ocr=1.00. Whitelist'te
/// komşusu 25EB858 VAR. Eşik 5 olduğu için araç "müdavim" sayılmadı ve 0.92'lik okuması 858'e çekildi;
/// aynı geçişte ROI-dışı okuma 25EB859'u 1.00 güvenle doğru okumuştu.</para>
/// Ölçüm (444 canlı roi olayı, kamera referans): eşik 5 → 10 doğru/2 yanlış; eşik 2 → 10 doğru/0 yanlış.
/// </summary>
public class SnapProtectionTests
{
    private static FrequentPlateRegistry Reg(params string[] whitelist) => new()
    {
        KnownPlates = FrequentPlateRegistry.NormalizeKnownPlates(whitelist),
    };

    [Fact]
    public void Still_snaps_a_never_seen_reading_to_its_whitelist_neighbour()
    {
        // Temel işlev korunmalı: hiç görülmemiş okuma whitelist'e lev1 yakınsa düzeltilir.
        Assert.Equal("25EB858", Reg("25EB858").SnapTarget("25EB859", maxLev: 1, includeFrequent: false));
    }

    [Fact]
    public void Protects_plate_after_two_high_confidence_sightings()
    {
        // REGRESYON (varsayılan MinSightings=2): iki kez yüksek güvenle görülen araç artık korunur.
        var r = Reg("25EB858");
        r.Record("25EB859", 1.0f);
        r.Record("25EB859", 1.0f);
        Assert.Equal("25EB859", r.SnapTarget("25EB859", maxLev: 1, includeFrequent: false));
    }

    [Fact]
    public void One_sighting_is_not_enough()
    {
        // Tek görülme koruma sağlamamalı — aksi halde tek seferlik yanlış okuma kalıcılaşırdı.
        var r = Reg("25EB858");
        r.Record("25EB859", 1.0f);
        Assert.Equal("25EB858", r.SnapTarget("25EB859", maxLev: 1, includeFrequent: false));
    }

    [Fact]
    public void Low_confidence_sightings_do_not_grant_protection()
    {
        // Record güven eşiği (0.98) altındaki okumalar sayılmaz: zayıf okuma kendini "bilinen" yapamaz.
        var r = Reg("25EB858");
        r.Record("25EB859", 0.80f);
        r.Record("25EB859", 0.90f);
        Assert.Equal("25EB858", r.SnapTarget("25EB859", maxLev: 1, includeFrequent: false));
    }

    [Fact]
    public void Whitelisted_reading_is_never_snapped_away()
    {
        Assert.Equal("25EB859", Reg("25EB858", "25EB859").SnapTarget("25EB859", 1, false));
    }
}
