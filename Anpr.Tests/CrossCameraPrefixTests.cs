using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// ÇAPRAZ-KAMERA KESİLME KAPISI. Ölçülmüş gerekçe (2026-07-31, 1766 canlı roi olayı):
/// <para>Bir okuma, BAŞKA bir kameranın saniyeler içinde okuduğu daha uzun plakanın tam ön-eki
/// olduğunda bu <b>her seferinde kesilmedir</b>. Canlı: <b>5 vaka ve 5'i de bariyeri kesik plakayla
/// OTOMATİK açtı</b> — kullanıcının ilk bildirdiği <c>34ZH764</c> vakası dahil
/// (giriş <c>34ZH764</c> 0,998 ← çıkış <c>34ZH7641</c> 0,999).</para>
///
/// <para><b>Neden mevcut koruma yetmiyor:</b> <c>IsVariantOfRecentEmit</c> ön-ek muafiyeti worker
/// içinde çalışır; her kamera AYRI worker olduğu için çapraz-kamera vakasını göremez.</para>
///
/// <para><b>Yanlış-pozitif riski ÖLÇÜLDÜ ve SIFIR çıktı:</b> tüm veride 11 ön-ek çifti var ve
/// <b>11'inde de kısa taraf tek seferlik</b> (1 olay / 1 gün) iken uzun taraf yerleşik (9 olaya /
/// 3 güne kadar). Her iki tarafı da yerleşik araç görünen çift: <b>0/11</b>.</para>
///
/// <para><b>YÖN ŞART:</b> uzun okuma DAHA YÜKSEK güvenli olmalı. Ölçülmüş karşı-örnek:
/// <c>25 A 4855</c> (polis plakası, 0,991) bizim <c>25A48550</c>/<c>25A48555</c> (0,814/0,784)
/// okumalarımızın ön-ekidir — burada KISA taraf doğrudur. Yön koşulu bu vakayı doğru şekilde eler.</para>
///
/// <para><b>EYLEM = BASTIRMA DEĞİL, OPERATÖRE DÜŞÜRME.</b> Giriş ve çıkış AYRI kapılardır; bir giriş
/// olayını bastırmak aracın giriş kaydını KAYBETTİRİR. Olay yayınlanır ama
/// <c>LowConfidence=true</c> ile — bariyeri otomatik açmaz, kararı operatör verir.</para>
/// </summary>
public class CrossCameraPrefixTests
{
    private static RecentEmitRegistry Reg(double windowSeconds = 15)
        => new(TimeSpan.FromSeconds(windowSeconds), TimeSpan.FromSeconds(windowSeconds));

    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Theory]
    // Dördü de CANLI vaka; hepsi bariyeri kesik plakayla otomatik açmıştı.
    [InlineData("34ZH764", 0.998f, "34ZH7641", 0.999f)]
    [InlineData("25AEE87", 0.935f, "25AEE873", 0.999f)]
    [InlineData("34GGE99", 0.884f, "34GGE993", 0.999f)]
    [InlineData("25AFG45", 0.883f, "25AFG450", 0.948f)]
    public void A_truncated_read_from_the_OTHER_camera_is_flagged(
        string shortPlate, float shortOcr, string longPlate, float longOcr)
    {
        var reg = Reg();
        reg.Record("cikis", longPlate, longOcr, T0);

        Assert.True(reg.IsTruncationOfRecent("giris", shortPlate, shortOcr, T0.AddSeconds(3)));
    }

    [Fact]
    public void The_police_plate_case_is_NOT_flagged_direction_matters()
    {
        // ÖLÇÜLMÜŞ KARŞI-ÖRNEK: gerçek plaka '25 A 4855' (0,991); bizim uzun okumalarımız
        // 25A48550 (0,814) ve 25A48555 (0,784) YANLIŞ. Kısa taraf DOĞRU olduğu için
        // bayraklanmamalı — yön koşulu (uzun DAHA güvenli) bunu sağlar.
        var reg = Reg();
        reg.Record("cikis", "25A48550", 0.814f, T0);
        reg.Record("cikis", "25A48555", 0.784f, T0);

        Assert.False(reg.IsTruncationOfRecent("giris", "25A4855", 0.991f, T0.AddSeconds(3)));
    }

    [Fact]
    public void Same_camera_also_works_it_is_a_superset_of_the_per_worker_guard()
    {
        var reg = Reg();
        reg.Record("giris", "34ZH7641", 0.999f, T0);
        Assert.True(reg.IsTruncationOfRecent("giris", "34ZH764", 0.998f, T0.AddSeconds(3)));
    }

    [Fact]
    public void Outside_the_window_it_is_not_flagged()
    {
        // ÖLÇÜLDÜ: ±8 sn 3 vaka, ±15 sn 5 vaka, ±30 sn yine 5 → 15 sn doğal pencere,
        // genişletmek yeni vaka getirmez, yalnız yanlış-eşleşme riskini büyütür.
        var reg = Reg(15);
        reg.Record("cikis", "34ZH7641", 0.999f, T0);
        Assert.False(reg.IsTruncationOfRecent("giris", "34ZH764", 0.998f, T0.AddSeconds(16)));
    }

    [Fact]
    public void Only_a_STRICT_prefix_one_char_shorter_counts()
    {
        var reg = Reg();
        reg.Record("cikis", "34ZH7641", 0.999f, T0);
        // aynı uzunluk (kesilme değil, farklı okuma)
        Assert.False(reg.IsTruncationOfRecent("giris", "34ZH7642", 0.9f, T0.AddSeconds(1)));
        // iki hane kısa: bu artık 'kesilme' değil, tamamen farklı bir okuma — kapsam dışı
        Assert.False(reg.IsTruncationOfRecent("giris", "34ZH76", 0.9f, T0.AddSeconds(1)));
        // ön-ek DEĞİL
        Assert.False(reg.IsTruncationOfRecent("giris", "34ZH765", 0.9f, T0.AddSeconds(1)));
    }

    [Fact]
    public void An_empty_registry_flags_nothing()
        => Assert.False(Reg().IsTruncationOfRecent("giris", "34ZH764", 0.998f, T0));

    [Fact]
    public void Old_entries_are_evicted_so_memory_stays_bounded()
    {
        var reg = Reg(15);
        for (int i = 0; i < 500; i++)
            reg.Record("giris", $"25AB{i:D4}", 0.99f, T0.AddSeconds(i));
        // Pencere 15 sn; 500 sn'lik veri girildi → kayıt sayısı pencereyle sınırlı kalmalı.
        Assert.True(reg.Count <= 40, $"kayıt sayısı {reg.Count} — pencere temizliği çalışmıyor");
    }

    [Fact]
    public void Recording_the_same_plate_twice_does_not_flag_itself()
    {
        // Aynı dizgi kendi ön-eki DEĞİLDİR; aksi hâlde her tekrar emit bayraklanırdı.
        var reg = Reg();
        reg.Record("giris", "34ZH764", 0.998f, T0);
        Assert.False(reg.IsTruncationOfRecent("cikis", "34ZH764", 0.999f, T0.AddSeconds(2)));
    }

    [Fact]
    public void A_disabled_window_turns_the_guard_off_entirely()
    {
        // Ürün sözleşmesi: pencere 0 ise davranış birebir eski hâlidir.
        var reg = new RecentEmitRegistry(TimeSpan.Zero, TimeSpan.Zero);
        reg.Record("cikis", "34ZH7641", 0.999f, T0);
        Assert.False(reg.IsTruncationOfRecent("giris", "34ZH764", 0.998f, T0.AddSeconds(1)));
    }
}
