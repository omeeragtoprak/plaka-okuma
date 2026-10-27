using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// ÇAPRAZ-KAMERA <b>İKAME</b> KAPISI (kesilme kapısının kardeşi, ondan AYRI knob).
///
/// <para><b>Sınıf:</b> aynı araç iki kameradan saniyeler arayla okunur; okumalardan biri AYNI
/// UZUNLUKTA ama TEK KARAKTER farklıdır ve DAHA DÜŞÜK güvenlidir. Ölçüm (2505 canlı roi olayı):
/// <b>30 çift, 26'sı bariyeri OTOMATİK açmıştı</b>.</para>
///
/// <para><b>ÖLÇÜLMÜŞ İSABET %89 — kesilme kapısından ZAYIF ve bu bilinçli olarak kabul edildi.</b>
/// Gözle doğrulanan 19 vakanın 17'sinde zayıf taraf gerçekten yanlıştı; 2'sinde DOĞRUYDU. Karşı
/// örnekler testlerde ADI GEÇEN vakalardır (aşağıda) — kapı onları YANLIŞ bayraklar ve bunun bedeli
/// bilinçli olarak kabul edilmiştir: 1 gereksiz operatör onayı.</para>
///
/// <para><b>Neden yine de açık:</b> bu sınıf, bir OCR hatasının BAŞKA BİR GERÇEK ARACIN plakasına
/// düşebildiği tek doğrulanmış yoldur (<c>25EJ167</c> ← <c>25EJ169</c>, ikisi de gerçek filo aracı).
/// Orada sonuç geçerli bir TR plakasıdır, whitelist'te vardır ve güveni eşiğin üstündedir → gramer,
/// snap ve güven korumalarının HİÇBİRİ göremez.</para>
/// </summary>
public class CrossCameraVariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 31, 13, 0, 0, TimeSpan.Zero);

    private static RecentEmitRegistry Reg(double prefix = 15, double variant = 15)
        => new(TimeSpan.FromSeconds(prefix), TimeSpan.FromSeconds(variant));

    // ---- CANLI VAKALAR (gözle doğrulanmış yer-gerçeği) ----------------------------------------

    [Theory]
    // giriş 0,999 okundu, çıkış 12 sn sonra tek karakter bozuk + daha düşük güvenle okudu.
    [InlineData("25AFJ897", 0.999f, "25AFJ097", 0.911f)]   // gerçeği 25AFJ897 (kanıt görselinden)
    [InlineData("06LE481", 0.942f, "08LE481", 0.936f)]     // gerçeği 06LE481 — fark yalnız 0,006
    [InlineData("25ABZ039", 0.999f, "25ADZ039", 0.875f)]
    [InlineData("25AEL556", 0.996f, "25AEL566", 0.966f)]
    [InlineData("34GGE993", 0.999f, "34GBE993", 0.988f)]   // hatalı okuma 0,988 güvenle geldi
    [InlineData("25EJ180", 0.996f, "25FJ180", 0.916f)]
    [InlineData("25ADP187", 0.999f, "25AUP187", 0.853f)]
    [InlineData("25SL066", 0.960f, "25SL061", 0.866f)]
    [InlineData("25AEZ488", 0.993f, "25AEZ489", 0.938f)]
    public void Weaker_single_char_variant_of_a_recent_stronger_read_is_flagged(
        string strong, float strongOcr, string weak, float weakOcr)
    {
        var reg = Reg();
        reg.Record("giris", strong, strongOcr, T0);
        Assert.True(reg.IsWeakVariantOfRecent("cikis", weak, weakOcr, T0.AddSeconds(8)));
    }

    [Fact]
    public void The_first_verified_case_of_an_error_landing_on_another_real_vehicles_plate()
    {
        // 25EJ167 ve 25EJ169 BU SAHADA İKİSİ DE GERÇEK ARAÇ (29 ve 43 olay). Bu olayda kanıt
        // görseli 169 gösteriyor; yani bariyer YANLIŞ ARACIN plakasıyla açılmıştı. Ürün açısından
        // bu kapının varlık sebebi budur: sonuç geçerli+bilinen bir plaka olduğu için başka hiçbir
        // koruma göremez.
        var reg = Reg();
        reg.Record("giris", "25EJ169", 0.987f, T0);
        Assert.True(reg.IsWeakVariantOfRecent("cikis", "25EJ167", 0.894f, T0.AddSeconds(6.8)));
    }

    // ---- YÖN ŞARTI ----------------------------------------------------------------------------

    [Fact]
    public void The_stronger_read_is_never_flagged_against_the_weaker_one()
    {
        // Yön koşulu olmasaydı kapı ÇİFT taraflı olur ve doğru okumayı da operatöre atardı.
        var reg = Reg();
        reg.Record("cikis", "25AFJ097", 0.911f, T0);
        Assert.False(reg.IsWeakVariantOfRecent("giris", "25AFJ897", 0.999f, T0.AddSeconds(3)));
    }

    [Fact]
    public void Equal_confidence_is_not_enough_direction_must_be_strict()
    {
        var reg = Reg();
        reg.Record("giris", "25AFJ897", 0.930f, T0);
        Assert.False(reg.IsWeakVariantOfRecent("cikis", "25AFJ097", 0.930f, T0.AddSeconds(3)));
    }

    // ---- KAPSAM SINIRLARI ---------------------------------------------------------------------

    [Fact]
    public void Two_character_differences_are_not_flagged()
    {
        // lev≤2 ÖLÇÜLDÜ ve REDDEDİLDİ: hacim 30 → 62 çift, isabet %89 → %84 (karşı örnek 2 → 4).
        // Bu sahada yerleşik araçların %17'sinin lev-1 komşusu da GERÇEK bir araçtır; eşiği
        // gevşetmek doğrudan o çakışmayı büyütür.
        // 25EJ176 ve 25EJ188 bu sahada İKİSİ DE gerçek araç (25EJ1xx serisinde 10 ayrı plaka var).
        var reg = Reg();
        reg.Record("giris", "25EJ176", 0.999f, T0);
        Assert.False(reg.IsWeakVariantOfRecent("cikis", "25EJ188", 0.940f, T0.AddSeconds(2)));
    }

    [Fact]
    public void Identical_strings_are_not_flagged_that_is_dedups_job()
    {
        var reg = Reg();
        reg.Record("giris", "25AFJ897", 0.999f, T0);
        Assert.False(reg.IsWeakVariantOfRecent("cikis", "25AFJ897", 0.911f, T0.AddSeconds(3)));
    }

    [Fact]
    public void Different_lengths_are_the_prefix_guards_job_not_this_one()
    {
        var reg = Reg();
        reg.Record("cikis", "34ZH7641", 0.999f, T0);
        Assert.False(reg.IsWeakVariantOfRecent("giris", "34ZH764", 0.998f, T0.AddSeconds(2)));
        Assert.True(reg.IsTruncationOfRecent("giris", "34ZH764", 0.998f, T0.AddSeconds(2)));
    }

    [Fact]
    public void Outside_the_window_nothing_is_flagged()
    {
        // Ölçüm: ±8 sn 20 çift · ±15 sn 30 · ±30 sn 34 → 15 sn seçildi.
        var reg = Reg();
        reg.Record("giris", "25AFJ897", 0.999f, T0);
        Assert.False(reg.IsWeakVariantOfRecent("cikis", "25AFJ097", 0.911f, T0.AddSeconds(20)));
    }

    [Fact]
    public void The_window_is_symmetric_in_time()
    {
        var reg = Reg();
        reg.Record("giris", "25AFJ897", 0.999f, T0);
        Assert.True(reg.IsWeakVariantOfRecent("cikis", "25AFJ097", 0.911f, T0.AddSeconds(-8)));
    }

    // ---- İKİ KAPI BİRBİRİNDEN BAĞIMSIZ KAPATILABİLİR ------------------------------------------

    [Fact]
    public void Variant_gate_can_be_disabled_without_losing_the_zero_counterexample_prefix_gate()
    {
        // Ayrı knob olmasının TEK sebebi bu: ölçülmüş isabetleri farklı (kesilme 8/8, ikame 17/19).
        var reg = Reg(prefix: 15, variant: 0);
        reg.Record("cikis", "34ZH7641", 0.999f, T0);
        reg.Record("giris", "25AFJ897", 0.999f, T0);
        Assert.True(reg.IsTruncationOfRecent("giris", "34ZH764", 0.998f, T0.AddSeconds(2)));
        Assert.False(reg.IsWeakVariantOfRecent("cikis", "25AFJ097", 0.911f, T0.AddSeconds(2)));
    }

    [Fact]
    public void Prefix_gate_can_be_disabled_without_losing_the_variant_gate()
    {
        var reg = Reg(prefix: 0, variant: 15);
        reg.Record("cikis", "34ZH7641", 0.999f, T0);
        reg.Record("giris", "25AFJ897", 0.999f, T0);
        Assert.False(reg.IsTruncationOfRecent("giris", "34ZH764", 0.998f, T0.AddSeconds(2)));
        Assert.True(reg.IsWeakVariantOfRecent("cikis", "25AFJ097", 0.911f, T0.AddSeconds(2)));
    }

    [Fact]
    public void Records_are_still_kept_when_only_one_gate_is_enabled()
    {
        // Kayıt tutma penceresi İKİ pencerenin BÜYÜĞÜ olmalı; aksi hâlde açık olan kapı da körleşir.
        var reg = Reg(prefix: 0, variant: 15);
        reg.Record("giris", "25AFJ897", 0.999f, T0);
        Assert.Equal(1, reg.Count);
    }

    [Fact]
    public void Both_gates_disabled_means_the_registry_stores_nothing()
    {
        var reg = Reg(prefix: 0, variant: 0);
        reg.Record("giris", "25AFJ897", 0.999f, T0);
        Assert.Equal(0, reg.Count);
        Assert.False(reg.IsWeakVariantOfRecent("cikis", "25AFJ097", 0.911f, T0.AddSeconds(2)));
    }

    // ---- BELLEK -------------------------------------------------------------------------------

    [Fact]
    public void Old_entries_are_pruned_so_the_registry_cannot_grow_without_bound()
    {
        var reg = Reg();
        for (int i = 0; i < 200; i++)
            reg.Record("giris", $"25AB{i:D3}", 0.99f, T0.AddSeconds(i));
        Assert.True(reg.Count <= 16, $"kayıt sayısı pencereyle sınırlı olmalı, {reg.Count} bulundu");
    }
}
