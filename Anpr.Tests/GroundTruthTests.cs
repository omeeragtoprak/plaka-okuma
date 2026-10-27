using Anpr.Service.Panel;

namespace Anpr.Tests;

/// <summary>
/// Kamera-bağımsız doğruluk ölçümünün saf matematiği. Bu sınıf ürünün "doğruluk %X" iddiasının
/// dayanağıdır — yanlış hesaplarsa yanlış güven verir, o yüzden kenar durumları kilitlenir.
/// </summary>
public class GroundTruthTests
{
    private static GroundTruthEntry E(float ocr, GroundTruthVerdict v, string ours = "25ABC123")
        => new(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "giris", null, ours,
               v == GroundTruthVerdict.Yanlis ? "25ABC124" : null, v, ocr);

    // --- Bant sınırları ---
    [Theory]
    [InlineData(0.10f, ConfBand.Dusuk)]
    [InlineData(0.8499f, ConfBand.Dusuk)]
    [InlineData(0.85f, ConfBand.Orta)]
    [InlineData(0.9499f, ConfBand.Orta)]
    [InlineData(0.95f, ConfBand.Yuksek)]
    [InlineData(0.9899f, ConfBand.Yuksek)]
    [InlineData(0.99f, ConfBand.CokYuksek)]
    [InlineData(1.00f, ConfBand.CokYuksek)]
    public void BandOf_boundaries(float ocr, ConfBand expected)
        => Assert.Equal(expected, GroundTruth.BandOf(ocr));

    // --- Wilson: küçük örneklemde DÜRÜST olmalı ---
    [Fact]
    public void Wilson_does_not_claim_certainty_from_a_perfect_small_sample()
    {
        // 7/7 "mükemmel" görünür ama neredeyse hiçbir şey kanıtlamaz.
        var ci = GroundTruth.Wilson(7, 7);
        Assert.NotNull(ci);
        Assert.True(ci!.Value.Low < 0.70, $"7/7'nin alt sınırı gerçekçi olmalı, bulundu {ci.Value.Low:P1}");
        Assert.Equal(1.0, ci.Value.High, 3);
    }

    [Fact]
    public void Wilson_narrows_as_sample_grows()
    {
        double w10 = GroundTruth.Wilson(9, 10)!.Value.High - GroundTruth.Wilson(9, 10)!.Value.Low;
        double w1000 = GroundTruth.Wilson(900, 1000)!.Value.High - GroundTruth.Wilson(900, 1000)!.Value.Low;
        Assert.True(w1000 < w10 / 5, "n büyüdükçe aralık belirgin daralmalı");
    }

    [Fact]
    public void Wilson_returns_null_for_empty_sample_instead_of_inventing_a_number()
        => Assert.Null(GroundTruth.Wilson(0, 0));

    // --- Ağırlıklı doğruluk ---
    [Fact]
    public void Weighted_accuracy_follows_population_shares_not_sample_shares()
    {
        // Popülasyonun %90'ı CokYuksek bandında ve orada %100 doğruyuz; Dusuk bandı küçük ama %0.
        // Örneklemde ikisi de eşit sayıda — ağırlıklandırma yapılmazsa sonuç ~%50 çıkardı.
        var pop = new Dictionary<ConfBand, int> { [ConfBand.CokYuksek] = 900, [ConfBand.Dusuk] = 100 };
        var verified = new List<GroundTruthEntry>();
        for (int i = 0; i < 10; i++) verified.Add(E(0.999f, GroundTruthVerdict.Dogru));
        for (int i = 0; i < 10; i++) verified.Add(E(0.50f, GroundTruthVerdict.Yanlis));

        var r = GroundTruth.Compute(pop, verified);
        Assert.NotNull(r.WeightedAccuracy);
        Assert.InRange(r.WeightedAccuracy!.Value, 0.88, 0.92);   // 0,9·1,0 + 0,1·0,0 = 0,90
    }

    [Fact]
    public void Unreadable_records_leave_the_denominator_but_stay_visible()
    {
        var pop = new Dictionary<ConfBand, int> { [ConfBand.Orta] = 10 };
        var verified = new List<GroundTruthEntry>
        {
            E(0.90f, GroundTruthVerdict.Dogru),
            E(0.90f, GroundTruthVerdict.Dogru),
            E(0.90f, GroundTruthVerdict.Okunamadi),
        };
        var r = GroundTruth.Compute(pop, verified);
        Assert.Equal(1.0, r.WeightedAccuracy);        // 2/2 — okunamadı paydada YOK
        Assert.Equal(1, r.TotalUnreadable);           // ama sayısı görünür
        Assert.Contains(r.Caveats, c => c.Contains("okunamadı"));
    }

    [Fact]
    public void Unverified_bands_are_declared_not_silently_treated_as_perfect()
    {
        // En tehlikeli sessiz hata: hiç bakılmamış bandı "sorunsuz" saymak.
        var pop = new Dictionary<ConfBand, int> { [ConfBand.Orta] = 10, [ConfBand.CokYuksek] = 990 };
        var verified = new List<GroundTruthEntry> { E(0.90f, GroundTruthVerdict.Dogru) };
        var r = GroundTruth.Compute(pop, verified);

        Assert.Contains(r.Caveats, c => c.Contains("CokYuksek") && c.Contains("HİÇ doğrulanmamış"));
        Assert.Contains(r.Caveats, c => c.Contains("KAPSAMAZ"));
    }

    [Fact]
    public void All_correct_sample_must_NOT_produce_a_zero_width_interval()
    {
        // CANLI BULGU (2026-07-30): 18/18 doğru olduğunda ağırlıklı aralık "100,0% – 100,0%" çıkıyordu.
        // Sebep: normal-yaklaşımda p=1 iken p(1-p)=0 → varyans 0 → aralık NOKTAYA çöküyor. Oysa bant
        // bazlı Wilson aralıkları dürüstçe 44–100%, 57–100% diyordu. Küçük örneklemde "kesin %100"
        // iddiası, bu aracın tam da engellemek için var olduğu yanıltmadır.
        var pop = new Dictionary<ConfBand, int>
        {
            [ConfBand.Dusuk] = 100, [ConfBand.Orta] = 200,
            [ConfBand.Yuksek] = 200, [ConfBand.CokYuksek] = 900,
        };
        var verified = new List<GroundTruthEntry>();
        foreach (var (ocr, k) in new[] { (0.50f, 3), (0.90f, 5), (0.97f, 4), (0.999f, 6) })
            for (int i = 0; i < k; i++) verified.Add(E(ocr, GroundTruthVerdict.Dogru));

        var r = GroundTruth.Compute(pop, verified);
        Assert.Equal(1.0, r.WeightedAccuracy);          // nokta tahmin %100 olabilir
        Assert.NotNull(r.WeightedCiLow);
        Assert.True(r.WeightedCiLow! < 0.90,
            $"n=18'de alt sınır gerçekçi olmalı, bulundu {r.WeightedCiLow:P1}");
        Assert.True(r.WeightedCiHigh! - r.WeightedCiLow! > 0.05, "aralık noktaya çökmemeli");
    }

    [Fact]
    public void All_wrong_sample_also_keeps_a_real_interval()
    {
        // Simetrik kenar durum: p=0'da da varyans 0'dır.
        var pop = new Dictionary<ConfBand, int> { [ConfBand.Orta] = 100 };
        var verified = Enumerable.Range(0, 5).Select(_ => E(0.90f, GroundTruthVerdict.Yanlis)).ToList();
        var r = GroundTruth.Compute(pop, verified);
        Assert.Equal(0.0, r.WeightedAccuracy);
        Assert.True(r.WeightedCiHigh! > 0.10, "üst sınır 0'a çökmemeli");
    }

    [Fact]
    public void Wrong_but_undeterminable_still_counts_as_an_ERROR_not_as_unreadable()
    {
        // CANLI BULGU (2026-07-30): 34ME1156 vakasında okumanın YANLIŞ olduğu görülüyordu ama gerçek
        // plaka kesin belirlenemiyordu. Şema 'Yanlis' için truth ZORUNLU kıldığı için vaka
        // 'Okunamadi'ye düşüyor, PAYDADAN çıkıyor ve hata HİÇ SAYILMIYORDU — ölçülen doğruluk bu
        // sınıf kadar İYİMSER oluyordu. Artık paydada kalır ve hata sayılır.
        var pop = new Dictionary<ConfBand, int> { [ConfBand.Orta] = 100 };
        var verified = new List<GroundTruthEntry>
        {
            E(0.90f, GroundTruthVerdict.Dogru),
            E(0.90f, GroundTruthVerdict.YanlisBelirsiz),
        };
        var r = GroundTruth.Compute(pop, verified);
        Assert.Equal(1, r.TotalWrong);          // HATA sayılır
        Assert.Equal(0, r.TotalUnreadable);     // okunamadı DEĞİL
        Assert.Equal(0.5, r.WeightedAccuracy);  // paydada: 1/2
    }

    [Fact]
    public void Unreadable_and_undeterminable_are_not_the_same_bucket()
    {
        // İkisini karıştırmak ölçümü ters yöne bozar: biri paydadan çıkar, diğeri hata sayılır.
        var pop = new Dictionary<ConfBand, int> { [ConfBand.Orta] = 100 };
        var r = GroundTruth.Compute(pop, new List<GroundTruthEntry>
        {
            E(0.90f, GroundTruthVerdict.Dogru),
            E(0.90f, GroundTruthVerdict.Okunamadi),
        });
        Assert.Equal(0, r.TotalWrong);
        Assert.Equal(1, r.TotalUnreadable);
        Assert.Equal(1.0, r.WeightedAccuracy);   // okunamadı paydadan ÇIKAR
    }

    [Fact]
    public void Empty_verification_yields_no_accuracy_claim_at_all()
    {
        var r = GroundTruth.Compute(new Dictionary<ConfBand, int> { [ConfBand.Orta] = 100 },
                                    Array.Empty<GroundTruthEntry>());
        Assert.Null(r.WeightedAccuracy);   // "veri yok" → sayı UYDURMA
        Assert.Equal(0, r.TotalVerified);
    }

    // --- Tabakalı seçim ---
    [Fact]
    public void PickNext_prefers_the_least_verified_band()
    {
        var cands = new List<Box> { new(0.999f), new(0.999f), new(0.90f) };
        var done = new Dictionary<ConfBand, int> { [ConfBand.CokYuksek] = 5, [ConfBand.Orta] = 0 };
        var pick = GroundTruth.PickNext(cands, c => c.Ocr, done);
        Assert.NotNull(pick);
        Assert.Equal(ConfBand.Orta, GroundTruth.BandOf(pick!.Ocr));
    }

    [Fact]
    public void PickNext_prefers_candidates_that_can_build_a_labelled_set()
    {
        // ÖLÇÜLMÜŞ GEREKÇE (2026-07-30): doğrulama emeği ancak olayın kutusu KAYDEDİLEN KAREYLE
        // TUTARLIYSA (FrameBox, v3) kalıcı etiketli sete dönüşür. Eski `Box`'tan set kurmayı denedim:
        // 91 doğrulanmış olayda OCR isabeti %53,8 çıktı (üretimde doğru okunan olaylar) — kırpıntılar
        // KAYIYOR, set kullanılamaz. Bu yüzden tercih edilebilir aday varsa önce o doğrulanır.
        var cands = new List<Box> { new(0.90f, false), new(0.90f, true) };
        var pick = GroundTruth.PickNext(cands, c => c.Ocr, new Dictionary<ConfBand, int>(),
                                        preferable: c => c.Usable);
        Assert.NotNull(pick);
        Assert.True(pick!.Usable);
    }

    [Fact]
    public void PickNext_falls_back_when_no_preferred_candidate_exists()
    {
        // Tercih edilen aday kalmadıysa doğrulama DURMAMALI — eski olaylar da precision ölçümüne katkı
        // sağlar, yalnız etiketli set üretemez.
        var cands = new List<Box> { new(0.90f, false), new(0.95f, false) };
        Assert.NotNull(GroundTruth.PickNext(cands, c => c.Ocr, new Dictionary<ConfBand, int>(),
                                            preferable: c => c.Usable));
    }

    [Fact]
    public void PickNext_without_preference_behaves_as_before()
    {
        var cands = new List<Box> { new(0.999f, false), new(0.90f, false) };
        var done = new Dictionary<ConfBand, int> { [ConfBand.CokYuksek] = 5, [ConfBand.Orta] = 0 };
        var pick = GroundTruth.PickNext(cands, c => c.Ocr, done);
        Assert.Equal(ConfBand.Orta, GroundTruth.BandOf(pick!.Ocr));
    }

    [Fact]
    public void PickNext_returns_null_when_nothing_left()
        => Assert.Null(GroundTruth.PickNext(Array.Empty<Box>(), c => c.Ocr,
                                            new Dictionary<ConfBand, int>()));

    private sealed record Box(float Ocr, bool Usable = false);

    // --- OTOMATİK AÇMA METRİĞİ (2026-07-31) ---
    // Tek bir "doğruluk %X" sayısı, BARİYERİ AÇAN kararı operatör kuyruğuyla aynı potaya koyar.
    // Düşük bant zaten otomatik açmaz (LowConfidence=true → PMS operatöre sorar), dolayısıyla o
    // banttaki hatalar bariyer güvenliğini ETKİLEMEZ ama genel sayıyı aşağı çeker. Canlı ölçüm:
    // genel %95,1 iken otomatik açma %98,0 — iki ayrı gerçek, tek sayı ikisini de yanlış anlatır.

    [Fact]
    public void AutoOpen_accuracy_excludes_the_band_that_never_opens_the_barrier()
    {
        var pop = new Dictionary<ConfBand, int>
        {
            [ConfBand.Dusuk] = 100, [ConfBand.Orta] = 100,
            [ConfBand.Yuksek] = 100, [ConfBand.CokYuksek] = 100,
        };
        var verified = new List<GroundTruthEntry>();
        // Düşük bant BERBAT (5/20) ama otomatik açmıyor.
        for (int i = 0; i < 5; i++) verified.Add(E(0.50f, GroundTruthVerdict.Dogru));
        for (int i = 0; i < 15; i++) verified.Add(E(0.50f, GroundTruthVerdict.Yanlis));
        // Otomatik açan üç bant KUSURSUZ.
        foreach (float ocr in new[] { 0.90f, 0.97f, 0.995f })
            for (int i = 0; i < 20; i++) verified.Add(E(ocr, GroundTruthVerdict.Dogru));

        var r = GroundTruth.Compute(pop, verified);

        Assert.NotNull(r.WeightedAccuracy);
        Assert.NotNull(r.AutoOpenAccuracy);
        // Genel ölçüt düşük bandın çöküşünü taşır; bariyer ölçütü taşımaz.
        Assert.True(r.WeightedAccuracy < 0.85, $"genel {r.WeightedAccuracy}");
        Assert.Equal(1.0, r.AutoOpenAccuracy!.Value, 6);
        Assert.Equal(300, r.AutoOpenPopulation);   // Dusuk hariç
        Assert.Equal(60, r.AutoOpenVerified);
    }

    [Fact]
    public void AutoOpen_interval_does_not_collapse_at_p_equals_one()
    {
        // Genel aralıkta düzeltilen çöküş hatası (normal-yaklaşım varyansı p=1'de sıfırlanır)
        // ikinci metrikte TEKRARLANMAMALI — aynı Wilson-toplamı yolu kullanılır.
        var pop = new Dictionary<ConfBand, int> { [ConfBand.CokYuksek] = 900 };
        var verified = Enumerable.Range(0, 12).Select(_ => E(0.995f, GroundTruthVerdict.Dogru)).ToList();

        var r = GroundTruth.Compute(pop, verified);

        Assert.Equal(1.0, r.AutoOpenAccuracy!.Value, 6);
        Assert.True(r.AutoOpenCiLow < 0.80, $"alt sınır {r.AutoOpenCiLow} — n=12 için %100 iddiası olamaz");
        Assert.Equal(1.0, r.AutoOpenCiHigh!.Value, 6);
    }

    [Fact]
    public void Barrier_opened_on_a_plate_no_human_could_verify_is_counted_separately()
    {
        // ÖLÇÜLMÜŞ VAKA: doğrulanan kayıtların bir kısmı insan gözüyle DE okunamıyor ama
        // ocr ≥ eşik olduğu için bariyeri OTOMATİK açmış. Bunlar doğruluk paydasından çıkar
        // (model hatası sayılamaz) — ama sayıları GÖRÜNMEK ZORUNDA, aksi hâlde rapor
        // "doğrulanamayan bir plakayla bariyer açtık" gerçeğini gizler.
        var pop = new Dictionary<ConfBand, int> { [ConfBand.Dusuk] = 10, [ConfBand.CokYuksek] = 90 };
        var verified = new List<GroundTruthEntry>
        {
            E(0.995f, GroundTruthVerdict.Dogru),
            E(0.995f, GroundTruthVerdict.Okunamadi),   // otomatik açtı, kimse doğrulayamıyor
            E(0.50f, GroundTruthVerdict.Okunamadi),    // operatöre düştü — bariyer riski YOK
        };

        var r = GroundTruth.Compute(pop, verified);

        Assert.Equal(2, r.TotalUnreadable);
        Assert.Equal(1, r.AutoOpenUnreadable);   // yalnız otomatik açan bantta olan
        Assert.Contains(r.Caveats, c => c.Contains("otomatik açmış", StringComparison.Ordinal));
    }

    [Fact]
    public void Threshold_that_splits_a_band_is_reported_instead_of_silently_mixed()
    {
        // Operatör ManualReviewMinOcr'ı 0,90 yaparsa Orta bandı (0,85–0,95) İKİYE bölünür:
        // yarısı otomatik açar, yarısı açmaz. Bandı bütün sayıp metriği kirletmek yerine
        // bant DIŞARIDA bırakılır ve neden dışarıda bırakıldığı YAZILIR.
        var pop = new Dictionary<ConfBand, int> { [ConfBand.Orta] = 50, [ConfBand.CokYuksek] = 50 };
        var verified = new List<GroundTruthEntry>
        {
            E(0.90f, GroundTruthVerdict.Dogru), E(0.995f, GroundTruthVerdict.Dogru),
        };

        var r = GroundTruth.Compute(pop, verified, manualReviewMinOcr: 0.90f);

        Assert.Equal(50, r.AutoOpenPopulation);   // yalnız CokYuksek
        Assert.Contains(r.Caveats, c => c.Contains("bant sınırına denk gelmiyor", StringComparison.Ordinal));
    }

    // --- PAY ⊆ PAYDA (2026-07-31) ---
    [Fact]
    public void A_verification_outside_the_population_scope_cannot_inflate_the_numerator()
    {
        // Popülasyon YALNIZ roi'den sayılır (bariyeri açan kanal) ama doğrulama kayıtları her
        // kapsamdan gelebilir. Bir 'gozlem' olayı doğrulanırsa PAYA girip PAYDAYA girmez ve
        // doğruluk sessizce sapar. Canlıda şu an 229/229 kayıt roi — bu bir düzeltme değil,
        // sessiz bozulmaya karşı KİLİT.
        var inPop = Guid.NewGuid();
        var outOfPop = Guid.NewGuid();
        var all = new List<GroundTruthEntry>
        {
            new(DateTimeOffset.UnixEpoch, inPop, "giris", null, "25ABC123", null,
                GroundTruthVerdict.Dogru, 0.995f),
            new(DateTimeOffset.UnixEpoch, outOfPop, "giris", null, "25ABC124", null,
                GroundTruthVerdict.Dogru, 0.995f),
        };

        var kept = GroundTruth.RestrictToPopulation(all, new HashSet<Guid> { inPop });

        Assert.Single(kept);
        Assert.Equal(inPop, kept[0].EventId);
    }

    [Fact]
    public void RestrictToPopulation_keeps_everything_when_all_ids_are_in_scope()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var all = new List<GroundTruthEntry>
        {
            new(DateTimeOffset.UnixEpoch, a, "giris", null, "25ABC123", null, GroundTruthVerdict.Dogru, 0.9f),
            new(DateTimeOffset.UnixEpoch, b, "giris", null, "25ABC124", null, GroundTruthVerdict.Okunamadi, 0.5f),
        };
        Assert.Equal(2, GroundTruth.RestrictToPopulation(all, new HashSet<Guid> { a, b }).Count);
        Assert.Empty(GroundTruth.RestrictToPopulation(all, new HashSet<Guid>()));
    }

    [Fact]
    public void AutoOpen_is_null_when_no_auto_opening_band_was_ever_verified()
    {
        var pop = new Dictionary<ConfBand, int> { [ConfBand.Dusuk] = 100 };
        var r = GroundTruth.Compute(pop, new List<GroundTruthEntry> { E(0.5f, GroundTruthVerdict.Dogru) });
        Assert.Null(r.AutoOpenAccuracy);   // uydurma sayı üretme
    }
}
