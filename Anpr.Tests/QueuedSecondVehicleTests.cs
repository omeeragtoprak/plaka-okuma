using Anpr.Core;
using Anpr.Core.Confirmation;

namespace Anpr.Tests;

/// <summary>
/// Kuyruktaki İKİNCİ ARAÇ (2026-07-28 derin denetim bulgusu).
/// <para>FindFuzzyMatches, iz ZATEN YAYINLANMIŞ olsa bile yeni okumayı ona yapıştırıyordu; oysa
/// tam-eşleşme yolunda yayınlanmış iz için yeni iz açılır. Sonuç: "25 ABC 123" yayınlandıktan
/// saniyeler sonra gelen "25 ABC 124" (lev=1, FARKLI araç, farklı konum) hiç yayınlanmıyordu.
/// Whitelist'in %16'sı ardışık seri plaka olduğundan (aynı filo) bu gerçekçi bir senaryo.</para>
/// </summary>
public class QueuedSecondVehicleTests
{
    // Canlı yapılandırma
    private static PlateConfirmer Make() => new(new PlateConfirmerOptions
    {
        DedupWindow = TimeSpan.FromSeconds(8),
        SettleWindow = TimeSpan.FromSeconds(2),
        MinConfirmations = 2,
        CharacterVoting = false,      // oylama kapalı: testi metin/iz mantığına odakla
        ConstrainedDecoding = false,
        FuzzyMergeDistance = 1,
        SpatialMergeIou = 0.35f,
        EarlyEmitMinOcr = 0.85f,
        CandidateCount = 5,
    });

    private static PlateReading R(string plate, float ocr, int x) => new()
    {
        Text = plate,
        RawText = plate.Replace(" ", ""),
        Confidence = ocr,
        DetectorConfidence = 1f,
        OcrConfidence = ocr,
        Box = new PlateBox(x, 500, 160, 60),   // x farklıysa kutular ÇAKIŞMAZ (IoU = 0)
        IsValidTr = true,
    };

    private static readonly DateTimeOffset T0 = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Second_vehicle_with_neighbouring_plate_is_still_emitted()
    {
        var c = Make();
        var emitted = new List<string>();

        // 1. araç: iki okuma → onaylanır ve yayınlanır
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0);
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0.AddSeconds(0.2));
        emitted.AddRange(c.DrainReady(T0.AddSeconds(0.3)).Select(r => r.Text.Replace(" ", "")));

        // 2. araç: 3 sn sonra, plakası 1 karakter farklı, KUTUSU AYRIK, okuma güçlü
        c.Observe(R("25 ABC 124", 0.99f, x: 1400), T0.AddSeconds(3.0));
        c.Observe(R("25 ABC 124", 0.99f, x: 1400), T0.AddSeconds(3.2));
        emitted.AddRange(c.DrainReady(T0.AddSeconds(3.3)).Select(r => r.Text.Replace(" ", "")));

        Assert.Contains("25ABC123", emitted);
        Assert.Contains("25ABC124", emitted);   // REGRESYON: eskiden yutuluyordu
    }

    [Fact]
    public void Ghost_variant_at_same_position_is_still_merged()
    {
        // KORUMA: hayalet varyant (aynı konum, zayıf okuma) ESKİSİ GİBİ birleşmeli — aksi halde
        // düzeltme, varyant bastırmayı bozup çift emit üretirdi.
        var c = Make();
        var emitted = new List<string>();
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0);
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0.AddSeconds(0.2));
        emitted.AddRange(c.DrainReady(T0.AddSeconds(0.3)).Select(r => r.Text.Replace(" ", "")));

        // aynı kutu + DÜŞÜK güven = aynı aracın hayalet okuması
        c.Observe(R("25 ABC 128", 0.70f, x: 1000), T0.AddSeconds(1.0));
        c.Observe(R("25 ABC 128", 0.70f, x: 1000), T0.AddSeconds(1.2));
        emitted.AddRange(c.DrainReady(T0.AddSeconds(1.3)).Select(r => r.Text.Replace(" ", "")));

        Assert.Single(emitted);
        Assert.Equal("25ABC123", emitted[0]);
    }

    [Fact]
    public void Absorbed_counter_fires_exactly_when_a_different_reading_joins_an_emitted_track()
    {
        // H1 GÖZLENEBİLİRLİĞİ (2026-08-02). Bastırılan okuma HİÇBİR olay üretmez; dolayısıyla
        // dışarıdan "hiç tespit edilmedi"den ayırt edilemez. Sayaç tam bu boşluğu kapatır.
        var c = Make();
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0);
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0.AddSeconds(0.2));
        c.DrainReady(T0.AddSeconds(0.3));
        Assert.Equal(0, c.AbsorbedAfterEmit);          // yayına kadar absorpsiyon yok

        // zayıf + aynı konum → ayırma kapısı açılmaz, okuma yutulur = H1 imzası
        c.Observe(R("25 ABC 128", 0.70f, x: 1000), T0.AddSeconds(1.0));
        Assert.Equal(1, c.AbsorbedAfterEmit);
        Assert.Contains(c.RecentAbsorbed, s => s.EndsWith("25ABC128→25ABC123", StringComparison.Ordinal));
        Assert.Contains(c.RecentAbsorbed, s => s.StartsWith("12:00:01", StringComparison.Ordinal));  // zaman damgası ŞART
    }

    [Fact]
    public void A_lingering_vehicle_cannot_flood_the_diagnostic_ring()
    {
        // ÖLÇÜLMÜŞ VAKA (2026-08-03): tek bir araç 20 saniyede 13 absorpsiyon üretip 16 elemanlı
        // halkanın TAMAMINI kendi varyantlarıyla doldurdu; diğer tüm çiftler tahliye oldu.
        // Gürültülü bir teşhis, teşhis DEĞİLDİR — aynı ize 3+ absorpsiyon tek satıra katlanır.
        var c = Make();
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0);
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0.AddSeconds(0.2));
        c.DrainReady(T0.AddSeconds(0.3));

        for (int i = 0; i < 10; i++)                       // oyalanan araç: 10 çöp okuma
            c.Observe(R($"25 ABC 12{i}", 0.70f, x: 1000), T0.AddSeconds(1 + i * 0.3));

        // i=3 tam olarak YAYINLANAN plakayı (25ABC123) üretir → lev=0 → absorpsiyon DEĞİL,
        // normal dedup. Sayaç bu yüzden 10 değil 9'dur; bu, "tekrar absorpsiyon sayılmaz"
        // kuralının bu testteki bedava doğrulamasıdır.
        Assert.Equal(9, c.AbsorbedAfterEmit);              // SAYAÇ hepsini sayar (tekrar hariç)
        Assert.True(c.RecentAbsorbed.Count <= 3,
            $"halka {c.RecentAbsorbed.Count} kayıtla boğuldu — katlama çalışmıyor");
        Assert.Contains(c.RecentAbsorbed, s => s.Contains("oyalanan araç", StringComparison.Ordinal));
    }

    [Fact]
    public void The_absorption_target_is_the_EMITTED_plate_not_the_drifting_best()
    {
        // Best emitten SONRA kayabilir; kayıt Best'i yazarsa çift hiçbir OLAYA bağlanamaz.
        // Canlı vaka (2026-08-03): "25ADL990→25ADL991" — iki plaka da o pencerede HİÇ geçmemişti.
        var c = Make();
        c.Observe(R("25 ABC 123", 0.90f, x: 1000), T0);
        c.Observe(R("25 ABC 123", 0.90f, x: 1000), T0.AddSeconds(0.2));
        c.DrainReady(T0.AddSeconds(0.3));                  // 25ABC123 olarak YAYINLANDI

        // aynı ize daha güvenli bir varyant girer → Best KAYAR ama yayınlanan plaka değişmez
        c.Observe(R("25 ABC 124", 0.98f, x: 1000), T0.AddSeconds(1.0));
        Assert.Contains(c.RecentAbsorbed, s => s.EndsWith("25ABC124→25ABC123", StringComparison.Ordinal));
    }

    [Fact]
    public void Absorbed_counter_stays_silent_for_repeats_and_for_genuine_splits()
    {
        // İki YANLIŞ POZİTİF kapatılıyor — sayaç gürültülü olursa hiç kimse ona bakmaz:
        //   (a) AYNI plakanın tekrarı absorpsiyon değildir (lev = 0, normal dedup),
        //   (b) ayırma kapısı açıldığında okuma YENİ ize gider, yutulmaz.
        var c = Make();
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0);
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0.AddSeconds(0.2));
        c.DrainReady(T0.AddSeconds(0.3));

        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0.AddSeconds(1.0));   // (a) tekrar
        Assert.Equal(0, c.AbsorbedAfterEmit);

        c.Observe(R("25 ABC 124", 0.99f, x: 1400), T0.AddSeconds(3.0));  // (b) gerçek ayrılma
        c.Observe(R("25 ABC 124", 0.99f, x: 1400), T0.AddSeconds(3.2));
        Assert.Equal(0, c.AbsorbedAfterEmit);
        Assert.Contains("25ABC124", c.DrainReady(T0.AddSeconds(3.3)).Select(r => r.Text.Replace(" ", "")));
    }

    [Fact]
    public void Same_plate_seen_again_within_dedup_window_is_not_re_emitted()
    {
        // Dedup davranışı korunmalı: aynı araç 8 sn içinde tekrar okunursa ikinci olay üretilmez.
        var c = Make();
        var emitted = new List<string>();
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0);
        c.Observe(R("25 ABC 123", 1.0f, x: 1000), T0.AddSeconds(0.2));
        emitted.AddRange(c.DrainReady(T0.AddSeconds(0.3)).Select(r => r.Text));
        c.Observe(R("25 ABC 123", 1.0f, x: 1400), T0.AddSeconds(3.0));
        c.Observe(R("25 ABC 123", 1.0f, x: 1400), T0.AddSeconds(3.2));
        emitted.AddRange(c.DrainReady(T0.AddSeconds(3.3)).Select(r => r.Text));

        Assert.Single(emitted);
    }
}
