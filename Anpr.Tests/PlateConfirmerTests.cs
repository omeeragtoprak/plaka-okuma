using Anpr.Core;
using Anpr.Core.Confirmation;

namespace Anpr.Tests;

public class PlateConfirmerTests
{
    private static PlateReading Reading(string text, float conf, bool valid = true) => new()
    {
        Text = text,
        RawText = text.Replace(" ", ""),
        Confidence = conf,
        DetectorConfidence = conf,
        OcrConfidence = conf,
        Box = new PlateBox(0, 0, 10, 10),
        IsValidTr = valid,
    };

    private static PlateConfirmer New(int minConf = 2, double settleSec = 3) => new(new PlateConfirmerOptions
    {
        DedupWindow = TimeSpan.FromSeconds(8),
        SettleWindow = TimeSpan.FromSeconds(settleSec),
        MinConfirmations = minConf,
    });

    [Fact]
    public void Confirmed_plate_wins_over_one_off_number_misread()
    {
        // Kimlik artık HARF|RAKAM. Tek seferlik rakam-yanlışı "25 AFH 11" AYRI kimliktir; onaylanan
        // "25 AFH 149" yayınlanır, tek-seferlik yanlış (onaysız, settle dolmadı) yayınlanmaz.
        // (Üretimde düşük güvenli tek okuma zaten OCR tabanıyla elenir.)
        var c = New(minConf: 2, settleSec: 3);
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("25 AFH 149", 0.95f), t);
        c.Observe(Reading("25 AFH 11", 0.40f), t.AddSeconds(1)); // farklı rakam -> ayrı kimlik
        c.Observe(Reading("25 AFH 149", 0.96f), t.AddSeconds(2)); // 149 onaylanır (count 2)

        var ready = c.DrainReady(t.AddSeconds(2));

        Assert.Single(ready);
        Assert.Equal("25 AFH 149", ready[0].Text);
    }

    [Fact]
    public void Single_frame_car_still_emitted_after_settle()
    {
        var c = New(minConf: 2, settleSec: 3);
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("34 ABC 123", 0.9f), t);

        Assert.Empty(c.DrainReady(t.AddSeconds(1)));        // henüz onaylanmadı, settle dolmadı
        var ready = c.DrainReady(t.AddSeconds(4));          // settle doldu -> yine de yayınla
        Assert.Single(ready);
        Assert.Equal("34 ABC 123", ready[0].Text);
    }

    [Fact]
    public void Confirmed_car_emitted_once_then_deduped()
    {
        var c = New(minConf: 2);
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("06 BC 345", 0.8f), t);
        c.Observe(Reading("06 BC 345", 0.85f), t.AddSeconds(1));
        Assert.Single(c.DrainReady(t.AddSeconds(1)));       // onaylandı, yayınlandı

        // Aynı araç görülmeye devam etse de dedup penceresinde tekrar yayınlanmaz.
        c.Observe(Reading("06 BC 345", 0.9f), t.AddSeconds(2));
        Assert.Empty(c.DrainReady(t.AddSeconds(2)));
    }

    [Fact]
    public void Different_cars_emitted_separately()
    {
        var c = New(minConf: 1); // onay kapalı -> ilk görüşte yayınla
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("25 GC 147", 0.9f), t);
        c.Observe(Reading("25 AG 368", 0.9f), t);
        var ready = c.DrainReady(t);
        Assert.Equal(2, ready.Count);
    }

    [Fact]
    public void Province_variants_merge_and_majority_wins()
    {
        // Aynı harf+rakam, il ilk-hanesi yanlış okunmuş varyant (35) -> aynı ize düşmeli,
        // yüksek güvenli (çoğunluk) 25 kazanmalı, TEK olay çıkmalı. (MergeAcrossProvince açık gerekir)
        var c = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 2,
            MergeAcrossProvince = true,
        });
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("25 AFG 450", 0.95f), t);
        c.Observe(Reading("35 AFG 450", 0.50f), t.AddSeconds(1));
        var ready = c.DrainReady(t.AddSeconds(1));
        Assert.Single(ready);
        Assert.Equal("25 AFG 450", ready[0].Text);
    }

    [Fact]
    public void Fuzzy_clustering_collapses_passage_variants_to_one()
    {
        // Aynı geçişin il/rakam yanlış-okuma varyantları (edit-mesafesi ≤2) TEK olaya çökmeli (kamera davranışı).
        var c = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 2,
            FuzzyMergeDistance = 2,
        });
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("25 ACR 626", 0.90f), t);
        c.Observe(Reading("75 ACR 626", 0.60f), t.AddSeconds(1)); // il varyantı (dist 1)
        c.Observe(Reading("25 ACR 628", 0.55f), t.AddSeconds(2)); // rakam varyantı (dist 1)
        var ready = c.DrainReady(t.AddSeconds(2));
        Assert.Single(ready);
        Assert.Equal("25 ACR 626", ready[0].Text); // en güvenli okuma kazanır
    }

    [Fact]
    public void Fuzzy_clustering_keeps_distinct_cars_separate()
    {
        // Uzak plakalar (edit-mesafesi > eşik) birleşmemeli.
        var c = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 1,
            FuzzyMergeDistance = 2,
        });
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("25 ACR 626", 0.9f), t);
        c.Observe(Reading("34 BFG 100", 0.9f), t); // tamamen farklı araç
        var ready = c.DrainReady(t);
        Assert.Equal(2, ready.Count);
    }

    [Fact]
    public void Bridge_reading_merges_split_tracks_single_emit()
    {
        // diag4'te ÖLÇÜLEN gerçek vaka (trace): hızlı araç 3 karede 3 farklı okuma verdi:
        //   25AFB625 (izA) -> 25AFB52 (izA'ya lev2 -> ayrı iz B!) -> 25AFB525 (HEM A'ya HEM B'ye lev1 = KÖPRÜ)
        // Köprü okuma izleri birleştirmeli -> TEK emit (eski kod: B ayrı kalıp settle'da 2. emit üretiyordu).
        var c = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 2,
            FuzzyMergeDistance = 1,
        });
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("25 AFB 625", 0.81f), t);
        c.Observe(Reading("25 AFB 52", 0.72f), t.AddSeconds(1));   // A'ya lev2 -> ayrı iz
        c.Observe(Reading("25 AFB 525", 0.95f), t.AddSeconds(2));  // köprü: A ve B birleşir + onay
        var ready = c.DrainReady(t.AddSeconds(2));
        Assert.Single(ready);
        Assert.Equal("25 AFB 525", ready[0].Text);                 // en yüksek güvenli okuma
        // settle sonrasında da İKİNCİ emit GELMEMELİ (eski hata buydu):
        Assert.Empty(c.DrainReady(t.AddSeconds(10)));
    }

    [Fact]
    public void Fuzzy_matches_any_seen_variant_not_just_best()
    {
        // Best drift vakası (diag4'te ölçülen gerçek çift-emit): iz "25 AFB 52" ile başlar, Best daha
        // yüksek güvenli "25 AFB 92"ye kayar; tam okuma "25 AFB 525" Best'e lev=2 (eski kod: yeni iz →
        // ÇİFT emit) ama görülen varyant "25AFB52"ye lev=1 → AYNI ize düşmeli, TEK emit.
        var c = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 3,
            FuzzyMergeDistance = 1,
        });
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("25 AFB 52", 0.60f), t);
        c.Observe(Reading("25 AFB 92", 0.95f), t.AddSeconds(1));   // lev1 -> merge, Best drift eder
        c.Observe(Reading("25 AFB 525", 0.90f), t.AddSeconds(2));  // Best'e lev2, varyanta lev1 -> merge
        var ready = c.DrainReady(t.AddSeconds(2));
        Assert.Single(ready); // tek iz, tek emit (onay 3'e ulaştı)
    }

    [Fact]
    public void Early_emit_gate_holds_low_confidence_until_better_read()
    {
        // Uzaktan gelen 2 kırpık/düşük-güven okuma onayı doldursa da EMIT edilmemeli (kapı);
        // araç yaklaşınca gelen iyi okuma izi günceller ve o anda yayınlanır.
        var c = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 2,
            EarlyEmitMinOcr = 0.8f,
        });
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("25 AFB 52", 0.55f), t);
        c.Observe(Reading("25 AFB 52", 0.60f), t.AddSeconds(1));
        Assert.Empty(c.DrainReady(t.AddSeconds(1)));               // onaylı ama güven < 0.8 -> bekle
        c.Observe(Reading("25 AFB 525", 0.95f), t.AddSeconds(2));  // yakın-mesafe iyi okuma (aynı il|harf izi)
        var ready = c.DrainReady(t.AddSeconds(2));
        Assert.Single(ready);
        Assert.Equal("25 AFB 525", ready[0].Text);                 // yanlış/kırpık erken emit YOK
    }

    [Fact]
    public void Early_emit_gate_still_emits_at_settle()
    {
        // İyi okuma hiç gelmezse settle'da yine de yayınla (aracı kaçırma).
        var c = new PlateConfirmer(new PlateConfirmerOptions
        {
            DedupWindow = TimeSpan.FromSeconds(8),
            SettleWindow = TimeSpan.FromSeconds(3),
            MinConfirmations = 2,
            EarlyEmitMinOcr = 0.8f,
        });
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("06 ABC 123", 0.55f), t);
        c.Observe(Reading("06 ABC 123", 0.60f), t.AddSeconds(1));
        Assert.Empty(c.DrainReady(t.AddSeconds(1)));
        var ready = c.DrainReady(t.AddSeconds(5)); // settle doldu
        Assert.Single(ready);
        Assert.Equal("06 ABC 123", ready[0].Text);
    }

    [Fact]
    public void Invalid_readings_are_ignored()
    {
        var c = New(minConf: 1);
        var t = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        c.Observe(Reading("GARBAGE", 0.9f, valid: false), t);
        Assert.Empty(c.DrainReady(t));
    }
}

public class SpatialMergeTests
{
    private static PlateReading Reading(string text, float conf, PlateBox box) => new()
    {
        Text = text,
        RawText = text.Replace(" ", ""),
        Confidence = conf,
        DetectorConfidence = conf,
        OcrConfidence = conf,
        Box = box,
        IsValidTr = true,
    };

    private static PlateConfirmer New(float spatialIou) => new(new PlateConfirmerOptions
    {
        DedupWindow = TimeSpan.FromSeconds(8),
        SettleWindow = TimeSpan.FromSeconds(3),
        MinConfirmations = 2,
        FuzzyMergeDistance = 1,
        SpatialMergeIou = spatialIou,
    });

    [Fact]
    public void Heavy_variants_of_same_physical_plate_merge_into_single_emit()
    {
        // GERÇEK VAKA (2026-07-08 16:12, çıkış): aynı araç 4 ayrı emit üretti çünkü varyantlar arası
        // lev>1: 26ADD884 / 26ADJ888 / 25DDS84 / 25DUS88. Kutular ardışık karelerde çakışıyor ->
        // uzamsal birleşme hepsini TEK ize toplamalı; tek emit çıkmalı.
        var c = New(spatialIou: 0.35f);
        var t = new DateTimeOffset(2026, 7, 8, 13, 12, 43, TimeSpan.Zero);

        c.Observe(Reading("26 ADD 884", 0.86f, new PlateBox(870, 703, 134, 51)), t);
        c.Observe(Reading("26 ADJ 888", 0.68f, new PlateBox(880, 700, 132, 52)), t.AddSeconds(0.2));
        c.Observe(Reading("25 DDS 84", 0.94f, new PlateBox(892, 698, 130, 52)), t.AddSeconds(0.4));
        c.Observe(Reading("25 DUS 88", 0.60f, new PlateBox(905, 695, 128, 53)), t.AddSeconds(0.6));
        c.Observe(Reading("25 DDS 84", 0.95f, new PlateBox(918, 692, 127, 53)), t.AddSeconds(0.8));

        var ready = c.DrainReady(t.AddSeconds(1));

        Assert.Single(ready); // TEK olay (uzamsal birleşme olmadan 3-4 ayrı iz olurdu)
    }

    [Fact]
    public void Spatially_disjoint_plates_stay_separate()
    {
        // Aynı anda iki FARKLI araç (kutular ayrık) birleşmemeli.
        var c = New(spatialIou: 0.35f);
        var t = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("25 ABC 123", 0.9f, new PlateBox(100, 100, 120, 50)), t);
        c.Observe(Reading("34 XYD 456", 0.9f, new PlateBox(900, 700, 120, 50)), t.AddSeconds(0.2));
        c.Observe(Reading("25 ABC 123", 0.9f, new PlateBox(105, 102, 120, 50)), t.AddSeconds(0.4));
        c.Observe(Reading("34 XYD 456", 0.9f, new PlateBox(905, 702, 120, 50)), t.AddSeconds(0.6));

        var ready = c.DrainReady(t.AddSeconds(1));
        Assert.Equal(2, ready.Count);
    }

    [Fact]
    public void Stale_track_not_spatially_chained()
    {
        // Kapıda aynı NOKTAYA 3 sn sonra gelen FARKLI araç (kuyruk) zincirlenmemeli (MaxGap=1.5s).
        var c = New(spatialIou: 0.35f);
        var t = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

        c.Observe(Reading("25 ABC 123", 0.9f, new PlateBox(500, 500, 120, 50)), t);
        c.Observe(Reading("25 ABC 123", 0.9f, new PlateBox(505, 500, 120, 50)), t.AddSeconds(0.2));
        // 3 sn sonra aynı yerde bambaşka plaka (yeni araç)
        c.Observe(Reading("34 KLM 789", 0.9f, new PlateBox(500, 500, 120, 50)), t.AddSeconds(3.5));
        c.Observe(Reading("34 KLM 789", 0.9f, new PlateBox(505, 500, 120, 50)), t.AddSeconds(3.7));

        var ready = c.DrainReady(t.AddSeconds(4));
        Assert.Equal(2, ready.Count);
        Assert.Contains(ready, r => r.Text == "25 ABC 123");
        Assert.Contains(ready, r => r.Text == "34 KLM 789");
    }
}
