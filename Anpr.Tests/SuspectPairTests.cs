using Anpr.Service.Panel;

namespace Anpr.Tests;

/// <summary>
/// ŞÜPHELİ ÇİFT TRİYAJI — operatörün bir sonraki korele hatayı BENSİZ bulabilmesi için.
///
/// <para><b>Neden gerekli (2026-07-31):</b> <c>25ACV952</c> (gerçeği <c>25 ACY 952</c>) 39 olay üretip
/// 39'unda da bariyeri otomatik açtı ve bunu <b>hiçbir otomatik koruma göremedi</b> — güven 0,999,
/// kamera da aynı yönde yanılıyor. Hatayı ancak kanıt görselini gözle okuyarak buldum. Operatörün bu
/// sınıfı kendi başına yakalayabilmesi için, bakılacak yeri DARALTAN bir kuyruk gerekiyor.</para>
///
/// <para><b>NE DEĞİL (dürüstlük):</b> bu bir DEDEKTÖR değildir. Ölçüldü: üretilen 9 çiftin 8'inde
/// nadir taraf yalnızca baskın plakanın zayıf yanlış okumasıdır (masum); yalnız 1'inde nadir taraf
/// GERÇEK plakadır. Veride ikisi AYNI görünür — ayrımı yalnız insan, kanıt görselinden yapabilir.
/// Değeri, 477 plakalık evreni gözle bakılabilir 9 kaleme indirmesidir.</para>
/// </summary>
public class SuspectPairTests
{
    private static AnalyticsService.OurEvent E(string plate, string scope, float ocr = 0.99f)
        => new(DateTimeOffset.UnixEpoch, "giris", plate, ocr, ocr, false, scope, null,
               "evidence/x.jpg", null, Array.Empty<string>(), Guid.NewGuid(), 0.8f, 150, 60);

    private static List<AnalyticsService.OurEvent> Many(string plate, string scope, int n, float ocr = 0.99f)
        => Enumerable.Range(0, n).Select(_ => E(plate, scope, ocr)).ToList();

    [Fact]
    public void The_measured_live_case_is_surfaced()
    {
        var evs = Many("25ACV952", "roi", 31);
        evs.Add(E("25ACY952", "gozlem", 0.888f));   // GERÇEK plaka, yalnız gözlem katmanında, 1 kez

        var pairs = AnalyticsService.BuildSuspectPairs(evs, minDominantRoi: 20);

        var p = Assert.Single(pairs);
        Assert.Equal("25ACV952", p.Dominant);
        Assert.Equal("25ACY952", p.Rare);
        Assert.Equal(31, p.DominantRoiCount);
    }

    [Fact]
    public void Observation_scope_MUST_be_included_roi_only_is_structurally_blind()
    {
        // ÖLÇÜLMÜŞ KÖK NEDEN: gerçek plaka `25ACY952` roi'de HİÇ okunmamıştı — yalnız gözlem
        // katmanında 1 kez. Yalnız roi'ye bakan bir analiz bu vakayı ASLA göremez. Bu test o
        // kararı kilitler: gözlem olayları elenirse çift kaybolur.
        var roiOnly = Many("25ACV952", "roi", 31);
        Assert.Empty(AnalyticsService.BuildSuspectPairs(roiOnly, minDominantRoi: 20));

        roiOnly.Add(E("25ACY952", "gozlem", 0.888f));
        Assert.Single(AnalyticsService.BuildSuspectPairs(roiOnly, minDominantRoi: 20));
    }

    [Fact]
    public void A_rare_plate_that_DID_open_the_barrier_is_not_suspicious()
    {
        // Nadir taraf roi'de de okunduysa kendi başına "gerçek araç" kanıtı vardır; bu çift
        // sıradan iki komşu plakadır (bu sahada 25ADL989/990/991/992 hepsi gerçek).
        var evs = Many("25ADL990", "roi", 31);
        evs.AddRange(Many("25ADL991", "roi", 2));
        Assert.Empty(AnalyticsService.BuildSuspectPairs(evs, minDominantRoi: 20));
    }

    [Fact]
    public void An_infrequent_dominant_is_not_systematic_enough_to_report()
    {
        // "Sistematik" tanımı gereği SIK tekrar eder; eşiğin altındakiler kuyruğu gürültüye boğar
        // (ölçüldü: eşik 20 → 9 çift, 10 → 21, 3 → 46).
        var evs = Many("25ACV952", "roi", 19);
        evs.Add(E("25ACY952", "gozlem", 0.888f));
        Assert.Empty(AnalyticsService.BuildSuspectPairs(evs, minDominantRoi: 20));
    }

    [Fact]
    public void Distant_plates_are_not_paired()
    {
        var evs = Many("25ACV952", "roi", 31);
        evs.Add(E("34XYZ111", "gozlem"));
        Assert.Empty(AnalyticsService.BuildSuspectPairs(evs, minDominantRoi: 20));
    }

    [Fact]
    public void Positional_similarity_pairs_are_included_not_just_lev1()
    {
        // Ağır bozulmuş okumada karakterlerin çoğu değişir; lev≤1 yetmediği ÖLÇÜLDÜ (çapraz kanıt
        // panelinde metin yakalaması %27'de kalmıştı).
        var evs = Many("25ATE548", "roi", 31);
        evs.Add(E("25AFL548", "gozlem"));            // lev=2, konum-benzerliği 0,75
        Assert.Single(AnalyticsService.BuildSuspectPairs(evs, minDominantRoi: 20));
    }

    [Fact]
    public void Pairs_are_ranked_by_impact_most_frequent_dominant_first()
    {
        // Operatör yukarıdan aşağı bakar; en çok bariyer açan hata en üstte olmalı.
        var evs = new List<AnalyticsService.OurEvent>();
        evs.AddRange(Many("25AAA111", "roi", 25)); evs.Add(E("25AAA112", "gozlem"));
        evs.AddRange(Many("25BBB222", "roi", 60)); evs.Add(E("25BBB223", "gozlem"));
        var pairs = AnalyticsService.BuildSuspectPairs(evs, minDominantRoi: 20);
        Assert.Equal("25BBB222", pairs[0].Dominant);
        Assert.Equal("25AAA111", pairs[1].Dominant);
    }

    [Fact]
    public void One_dominant_may_have_several_rare_neighbours()
    {
        // Canlıda 25ACV952'nin ÜÇ nadir komşusu vardı (25KCV852 · 25ACY952 · 25ACV932);
        // hangisinin gerçek olduğunu yalnız insan ayırt eder, o yüzden hepsi gösterilir.
        var evs = Many("25ACV952", "roi", 31);
        evs.Add(E("25ACY952", "gozlem")); evs.Add(E("25ACV932", "gozlem"));
        Assert.Equal(2, AnalyticsService.BuildSuspectPairs(evs, minDominantRoi: 20).Count);
    }

    [Fact]
    public void Empty_input_returns_empty_instead_of_throwing()
        => Assert.Empty(AnalyticsService.BuildSuspectPairs(
            new List<AnalyticsService.OurEvent>(), minDominantRoi: 20));

    [Fact]
    public void Result_is_capped_so_the_queue_stays_reviewable()
    {
        // Kuyruğun tek değeri KISA olmasıdır; uzarsa operatör bakmaz ve araç hiç kullanılmaz.
        var evs = new List<AnalyticsService.OurEvent>();
        for (int i = 0; i < 40; i++)
        {
            evs.AddRange(Many($"25AB{i:D1}111".PadRight(8, '0')[..8], "roi", 25));
            evs.Add(E($"25AB{i:D1}112".PadRight(8, '0')[..8], "gozlem"));
        }
        Assert.True(AnalyticsService.BuildSuspectPairs(evs, minDominantRoi: 20).Count <= 25);
    }
}
