using Anpr.Core;
using Anpr.Core.Confirmation;

namespace Anpr.Tests;

/// <summary>
/// `EarlyEmitMinOcr` tutma ölçümü (#70). Tutma bir <b>gecikme–doğruluk takasıdır</b> ve bugüne dek
/// takasın KAZANÇ tarafı yalnız ÇIKARIMLA savunuluyordu (çok-kareli oylamanın ölçülmüş +6,9 puanı).
/// Bu sayaçlar onu doğrudan ölçer; davranış değişmez.
/// <para>Karar ölçütü: <c>EarlyEmitAvgGain</c> ~0 ise tutma yalnız gecikme ekliyordur ve paket
/// varsayılanı 0 (kapalı) KALMALIDIR.</para>
/// </summary>
public class EarlyEmitHoldTests
{
    private static PlateConfirmer Make(float gate) => new(new PlateConfirmerOptions
    {
        DedupWindow = TimeSpan.FromSeconds(8),
        SettleWindow = TimeSpan.FromSeconds(2),
        MinConfirmations = 2,
        CharacterVoting = false,
        ConstrainedDecoding = false,
        EarlyEmitMinOcr = gate,
        CandidateCount = 5,
    });

    private static PlateReading R(string plate, float ocr) => new()
    {
        Text = plate, RawText = plate.Replace(" ", ""),
        Confidence = ocr, DetectorConfidence = 1f, OcrConfidence = ocr,
        Box = new PlateBox(1000, 500, 160, 60), IsValidTr = true,
    };

    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_weak_track_is_held_and_the_gain_is_recorded_when_it_improves()
    {
        // Takasın KAZANDIĞI senaryo: zayıf başlayan iz, araç yaklaştıkça düzeliyor.
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.60f), T0);
        c.Observe(R("25 ABC 123", 0.60f), T0.AddSeconds(0.2));
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.3)));          // TUTULDU
        Assert.Equal(0, c.EarlyEmitHolds);                        // henüz yayınlanmadı → sayılmaz

        c.Observe(R("25 ABC 123", 0.99f), T0.AddSeconds(0.6));    // daha iyi kare geldi
        var ready = c.DrainReady(T0.AddSeconds(0.7));

        Assert.Single(ready);
        Assert.Equal(1, c.EarlyEmitHolds);
        Assert.Equal(1, c.EarlyEmitHoldsResolved);                // eşiğin ÜSTÜNE çıktı
        Assert.True(c.EarlyEmitAvgGain > 0.3, $"kazanç {c.EarlyEmitAvgGain:F3} beklenenden küçük");
    }

    [Fact]
    public void A_track_that_never_improves_is_still_emitted_at_settle_and_counted_as_unresolved()
    {
        // RECALL KORUMASI: tutma aracı ASLA kaybettirmez — settle'da yine yayınlanır.
        // Ama takas KAZANMAMIŞTIR ve sayaç bunu ayırt eder.
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.60f), T0);
        c.Observe(R("25 ABC 123", 0.60f), T0.AddSeconds(0.2));
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.3)));

        var ready = c.DrainReady(T0.AddSeconds(3));               // settle geçti
        Assert.Single(ready);
        Assert.Equal(1, c.EarlyEmitHolds);
        Assert.Equal(0, c.EarlyEmitHoldsResolved);                // eşiğin altında kaldı
        Assert.Equal(0.0, c.EarlyEmitAvgGain, 3);                 // hiç düzelmedi
    }

    [Fact]
    public void A_strong_track_is_never_held_so_the_gate_adds_no_latency()
    {
        // Yüksek güvenli plakalar için tutma GECİKME EKLEMEZ — kapının temel iddiası bu.
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.99f), T0);
        c.Observe(R("25 ABC 123", 0.99f), T0.AddSeconds(0.2));
        Assert.Single(c.DrainReady(T0.AddSeconds(0.3)));
        Assert.Equal(0, c.EarlyEmitHolds);
    }

    [Fact]
    public void With_the_gate_off_nothing_is_ever_held()
    {
        // Paket VARSAYILANI (0 = kapalı): zayıf okuma da hemen yayınlanır, sayaç sessiz kalır.
        var c = Make(0f);
        c.Observe(R("25 ABC 123", 0.40f), T0);
        c.Observe(R("25 ABC 123", 0.40f), T0.AddSeconds(0.2));
        Assert.Single(c.DrainReady(T0.AddSeconds(0.3)));
        Assert.Equal(0, c.EarlyEmitHolds);
        Assert.Equal(0.0, c.EarlyEmitAvgGain, 3);
    }

    // ─── v5 (#70): tutma izi OLAYIN KENDİSİNDE ────────────────────────────────
    // NEDEN: yukarıdaki sayaçlar SÜREÇ düzeyindedir — restart'ta sıfırlanır ve HANGİ olayın
    // tutulduğunu söylemez. O yüzden "beklemek YANLIŞ okumayı DOĞRUYA çevirdi mi?" sorusu
    // yer-gerçeğiyle eşleştirilemiyordu. Aşağıdaki testler izin GERÇEKTEN yazıldığını bağlar;
    // aksi hâlde alan sessizce hep null kalır ve enstrüman var sanılırdı.

    [Fact]
    public void A_held_reading_carries_the_confidence_it_was_FIRST_held_at()
    {
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.60f), T0);
        c.Observe(R("25 ABC 123", 0.60f), T0.AddSeconds(0.2));
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.3)));           // 0,60'ta TUTULDU
        c.Observe(R("25 ABC 123", 0.70f), T0.AddSeconds(0.5));    // biraz düzeldi, hâlâ eşik altı
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.55)));
        c.Observe(R("25 ABC 123", 0.99f), T0.AddSeconds(0.8));    // eşiği geçti
        var ready = c.DrainReady(T0.AddSeconds(0.9));

        var held = Assert.Single(ready).HeldAtOcr;
        Assert.NotNull(held);
        // İLK tutulma anı (0,60) yazılmalı — 0,70 DEĞİL. Aksi hâlde kazanç olduğundan küçük görünür.
        Assert.Equal(0.60f, held!.Value, 2);
        Assert.Equal(ready[0].OcrConfidence - held.Value, (float)c.EarlyEmitAvgGain, 2);
    }

    [Fact]
    public void A_reading_that_was_never_held_carries_no_trace()
    {
        // null "tutulmadı" demektir — bu ayrımı bağlamazsak alan anlamsızlaşır.
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.99f), T0);
        c.Observe(R("25 ABC 123", 0.99f), T0.AddSeconds(0.2));
        Assert.Null(Assert.Single(c.DrainReady(T0.AddSeconds(0.3))).HeldAtOcr);
    }

    [Fact]
    public void An_unresolved_hold_still_carries_the_trace_at_settle()
    {
        // Takas KAYBETTİĞİNDE de iz kalmalı: "beklendi ama düzelmedi" ölçülebilir olmalı,
        // yoksa yalnız kazanan vakalar kaydedilir ve ölçüm KENDİ LEHİNE yanlı olur.
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.60f), T0);
        c.Observe(R("25 ABC 123", 0.60f), T0.AddSeconds(0.2));
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.3)));
        var ready = c.DrainReady(T0.AddSeconds(3));               // settle'da yayınlandı

        var held = Assert.Single(ready).HeldAtOcr;
        Assert.NotNull(held);
        Assert.Equal(0.60f, held!.Value, 2);
        Assert.Equal(0, c.EarlyEmitHoldsResolved);                // düzelmedi ama İZ VAR
    }

    [Fact]
    public void With_the_gate_off_the_trace_is_always_null()
    {
        var c = Make(0f);
        c.Observe(R("25 ABC 123", 0.40f), T0);
        c.Observe(R("25 ABC 123", 0.40f), T0.AddSeconds(0.2));
        Assert.Null(Assert.Single(c.DrainReady(T0.AddSeconds(0.3))).HeldAtOcr);
    }

    // 🔴 KIRMIZI-YEŞİL (2026-08-21, §P.4 (335)): #70'in sorusu "tutmak YANLIŞ okumayı DOĞRUYA
    // çevirdi mi?" — bu soru tutma anındaki METNİ gerektirir. v5 yalnız GÜVENİ kaydediyordu;
    // metin `BuildResult(t)` içinde mevcuttu ve ATILIYORDU. Bu test onu tutar.
    [Fact]
    public void Hold_records_the_text_that_WOULD_have_been_emitted_not_just_its_confidence()
    {
        var c = Make(0.85f);
        // Tutma anında iz "25 ABC 123" okuyordu…
        c.Observe(R("25 ABC 123", 0.60f), T0);
        c.Observe(R("25 ABC 123", 0.60f), T0.AddSeconds(0.2));
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.3)));           // TUTULDU
        // …sonra araç yaklaştı ve okuma DEĞİŞTİ.
        // ⚠️ Değişim HANEDE olmalı: iz kimliği `il + harf bloğu` olduğu için harfi değiştirmek
        //    YENİ BİR İZ açar (§P.4 (280) parçalanması) ve tutma o ize taşınmaz. Bu testin ilk
        //    hâli tam bu yüzden düştü — kusur testte değil, testin kurduğu senaryodaydı.
        c.Observe(R("25 ABC 128", 0.99f), T0.AddSeconds(0.6));
        c.Observe(R("25 ABC 128", 0.99f), T0.AddSeconds(0.8));
        var e = Assert.Single(c.DrainReady(T0.AddSeconds(1.0)));

        Assert.Equal(1, c.EarlyEmitHolds);
        Assert.Equal(0.60f, e.HeldAtOcr!.Value, 2);
        // 🔑 ASIL İDDİA: tutma anındaki metin kayıtlı ve yayınlanandan FARKLI.
        //    Bu olmadan "tutmak sonucu değiştirdi mi?" sorusu YANITLANAMAZ.
        Assert.Equal("25ABC123", e.HeldAtText);
        Assert.Equal("25ABC128", e.RawText);
        Assert.NotEqual(e.HeldAtText, e.RawText);
    }

    // Tutma YOKSA alan null kalır — "tutulmadı" ile "metin bilinmiyor" karışmasın.
    [Fact]
    public void HeldAtText_is_null_when_the_track_was_never_held()
    {
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.99f), T0);
        c.Observe(R("25 ABC 123", 0.99f), T0.AddSeconds(0.2));
        var e = Assert.Single(c.DrainReady(T0.AddSeconds(3)));
        Assert.Null(e.HeldAtOcr);
        Assert.Null(e.HeldAtText);
    }

    // Tutma oldu AMA metin değişmedi → alan DOLU ve yayınlananla AYNI.
    // Bu ayrım kritik: "tutma sonucu değiştirmedi" ile "bilmiyoruz" aynı şey değildir.
    [Fact]
    public void HeldAtText_equals_emitted_text_when_holding_only_added_delay()
    {
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.60f), T0);
        c.Observe(R("25 ABC 123", 0.60f), T0.AddSeconds(0.2));
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.3)));
        c.Observe(R("25 ABC 123", 0.99f), T0.AddSeconds(0.6));
        var e = Assert.Single(c.DrainReady(T0.AddSeconds(3)));
        Assert.Equal("25ABC123", e.HeldAtText);
        Assert.Equal(e.RawText, e.HeldAtText);
    }


    // 🔴 BU TESTİ SABOTAJ BULDU (2026-08-21): "metni sakla" testleri geçiyordu ama İLK/SON
    // ayrımını tutmuyorlardı — `HeldAtText`i her tutmada üzerine yazmak tüm testleri geçiyordu.
    // Doğru semantik İLK'tir: kapı KAPALI olsaydı yayın `confirmed || settled` olan İLK anda
    // yapılırdı, yani karşı-olgusal o andaki dizgidir. Son tutmayı kaydetmek karşı-olgusalı
    // kapının KENDİ etkisiyle kirletir (kapı olmasaydı o kareye hiç gelinmezdi).
    [Fact]
    public void HeldAtText_keeps_the_FIRST_hold_not_the_last()
    {
        var c = Make(0.85f);
        c.Observe(R("25 ABC 123", 0.50f), T0);
        c.Observe(R("25 ABC 123", 0.50f), T0.AddSeconds(0.2));
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.3)));            // 1. tutma → dizgi "25ABC123"
        c.Observe(R("25 ABC 127", 0.70f), T0.AddSeconds(0.5));     // hâlâ eşik altı
        Assert.Empty(c.DrainReady(T0.AddSeconds(0.6)));            // 2. tutma → dizgi "25ABC127"
        c.Observe(R("25 ABC 128", 0.99f), T0.AddSeconds(0.9));
        var e = Assert.Single(c.DrainReady(T0.AddSeconds(1.1)));

        Assert.Equal(0.50f, e.HeldAtOcr!.Value, 2);                // güven de İLK tutmadan
        Assert.Equal("25ABC123", e.HeldAtText);                    // 🔑 İLK, "25ABC127" DEĞİL
        Assert.Equal("25ABC128", e.RawText);
    }

}
