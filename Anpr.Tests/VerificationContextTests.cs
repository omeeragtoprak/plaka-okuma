using Anpr.Service.Panel;

namespace Anpr.Tests;

/// <summary>
/// Çapraz kanıt paneli. ÖLÇÜLMÜŞ İNSAN HATASI RİSKİ: 2026-07-30 doğrulama kampanyasında bulanık tek
/// kırpıntı doğrulayıcıyı iki kez yanılttı (25ADL989→"26…", 25ADL991→"…981") ve her ikisinde de
/// yanlış bir "Yanlış" verdict'i yer-gerçeğini ZEHİRLEYECEKTİ. Aynı plakanın EN NET kanıtına bakmak
/// ikisini de düzeltti; bu panel o adımı otomatikleştirir.
/// </summary>
public class VerificationContextTests
{
    private static AnalyticsService.OurEvent E(string plate, float ocr, int w, int h,
                                               Guid? id = null, string? ev = "evidence/x.jpg")
        => new(DateTimeOffset.UnixEpoch, "giris", plate, ocr, ocr, false, "roi", null, ev, null,
               Array.Empty<string>(), id ?? Guid.NewGuid(), 0.8f, w, h);

    [Fact]
    public void Clearest_evidence_comes_first_ranked_by_pixels_not_confidence()
    {
        // KRİTİK: güvene göre sıralamak yanıltıcı olurdu — güven kalibre DEĞİL (kesik okuma 0,9977
        // alabiliyor). Kutu alanı kaç gerçek piksel olduğunun doğrudan vekilidir.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            E("25ADL991", 0.95f, 172, 84, target),          // doğrulanan olay
            E("25ADL991", 0.999f, 60, 20),                  // yüksek güven ama KÜÇÜK kutu
            E("25ADL991", 0.80f, 220, 90),                  // düşük güven ama BÜYÜK kutu
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Equal(2, ctx.Count);
        Assert.Equal(220 * 90, ctx[0].BoxArea);   // en çok piksel başta
    }

    [Fact]
    public void Same_plate_ranks_above_lev1_neighbour_even_if_neighbour_is_bigger()
    {
        // Aynı dizgi en güvenilir çapraz kanıttır; komşu BAŞKA bir araç olabilir.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            E("25ADL991", 0.95f, 100, 40, target),
            E("25ADL990", 0.99f, 500, 200),   // komşu, çok daha büyük
            E("25ADL991", 0.90f, 110, 45),    // aynı plaka, küçük
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Equal("ayni", ctx[0].Relation);
        Assert.Equal("komsu", ctx[1].Relation);
    }

    [Fact]
    public void Fleet_neighbours_are_never_labelled_as_the_SAME_plate()
    {
        // Bu sahada 25ADL989/990/991/992 HEPSİ gerçek araç. Korunması gereken değişmez:
        // bunlar "ayni" ETİKETİNİ ALMAMALI — aksi hâlde operatör iki farklı aracı tek araç sanar.
        // "komsu" (lev1) ve "benzer" (konum≥0,75) ayrımı bilgilendiricidir, hüküm değildir:
        // 25ADL989 ↔ 25ADL991 lev=2 / benzerlik 0,75 olduğu için "benzer" gelir. Bu KABUL EDİLEN
        // bedeldir — ardışık filo plakaları bazen "benzer" görünür; karşılığında ağır bozulmuş
        // okumanın GERÇEK plakası yüzeye çıkar (25ATE548 → 25AFL548 vakası).
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            E("25ADL991", 0.95f, 100, 40, target),
            E("25ADL989", 0.99f, 200, 80),
            E("25ADL992", 0.99f, 150, 60),
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Equal(2, ctx.Count);
        Assert.DoesNotContain(ctx, c => c.Relation == "ayni");
    }

    // --- KONUM-BENZERLİĞİ (2026-07-30): lev≤1 yetmiyordu ---
    [Fact]
    public void Badly_mangled_read_surfaces_its_true_plate_via_positional_similarity()
    {
        // CANLI VAKA: 25ATE548 ile gerçek plaka 25AFL548 arası lev=2 → eski lev≤1 filtresi bu çifti
        // HİÇ göstermiyordu ve ben bulanık kırpıntıya bakıp yanlış okumayı "DOĞRU" onayladım
        // (yer-gerçeğini şişiren en tehlikeli hata). Konum-benzerliği 0,75 bunu yakalar.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            E("25ATE548", 0.785f, 147, 51, target),
            E("25AFL548", 0.998f, 164, 100),
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Single(ctx);
        Assert.Equal("25AFL548", ctx[0].Plate);
        Assert.Equal("benzer", ctx[0].Relation);
    }

    [Fact]
    public void PositionalSimilarity_matches_the_measured_cases()
    {
        Assert.Equal(0.75, AnalyticsService.PositionalSimilarity("25ATE548", "25AFL548"), 3);
        Assert.Equal(0.75, AnalyticsService.PositionalSimilarity("34ME1156", "34PE8156"), 3);
        // Farklı uzunluk → konum karşılaştırması anlamsız, 0 döner.
        Assert.Equal(0, AnalyticsService.PositionalSimilarity("22DL911", "25ADL991"));
        Assert.Equal(1.0, AnalyticsService.PositionalSimilarity("25ABC123", "25ABC123"));
    }

    [Fact]
    public void Same_length_but_too_different_is_still_excluded()
    {
        // 8 karakterin yalnız 4'ü aynı (0,50) → eşiğin (0,75) altında, gösterilmez.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            E("25ABC123", 0.9f, 100, 40, target),
            E("25XYZ923", 0.9f, 300, 120),
        };
        Assert.Empty(AnalyticsService.BuildVerificationContext(evs, target));
    }

    [Fact]
    public void Ranking_puts_same_then_neighbour_then_similar()
    {
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            E("25ATE548", 0.8f, 100, 40, target),
            E("25AFL548", 0.99f, 900, 400),   // benzer (lev2) ama ÇOK büyük kutu
            E("25ATE549", 0.99f, 100, 40),    // komşu (lev1)
            E("25ATE548", 0.99f, 100, 40),    // aynı
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Equal("ayni", ctx[0].Relation);
        Assert.Equal("komsu", ctx[1].Relation);
        Assert.Equal("benzer", ctx[2].Relation);
    }

    [Fact]
    public void Distant_plates_are_never_presented_as_TEXT_evidence()
    {
        // lev>1 ve benzerlik eşiğin altında → metin kanıtı DEĞİLDİR. (2026-07-31'den beri aynı
        // kayıt, zamansal olarak yakınsa "es-zamanli" ADAYI olarak çıkabilir — ayrı ve daha zayıf
        // bir iddia. Burada kilitlenen şey: metin etiketlerinden birini ASLA almaması.)
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            E("25ADL991", 0.95f, 100, 40, target),
            E("34XYZ123", 0.99f, 300, 120),   // lev > 1, benzerlik < 0,75
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.DoesNotContain(ctx, c => c.Relation is "ayni" or "komsu" or "benzer");

        // Zaman penceresinin de dışındaysa hiç görünmez.
        var far = new List<AnalyticsService.OurEvent>
        {
            T("25ADL991", 0.95f, 100, 40, 0, target),
            T("34XYZ123", 0.99f, 300, 120, 60),
        };
        Assert.Empty(AnalyticsService.BuildVerificationContext(far, target));
    }

    [Fact]
    public void The_event_being_verified_is_not_shown_as_its_own_evidence()
    {
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent> { E("25ADL991", 0.95f, 100, 40, target) };
        Assert.Empty(AnalyticsService.BuildVerificationContext(evs, target));
    }

    [Fact]
    public void Events_without_evidence_are_skipped_they_show_nothing_useful()
    {
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            E("25ADL991", 0.95f, 100, 40, target),
            E("25ADL991", 0.99f, 300, 120, ev: null),
        };
        Assert.Empty(AnalyticsService.BuildVerificationContext(evs, target));
    }

    [Fact]
    public void Unknown_event_id_returns_empty_instead_of_throwing()
        => Assert.Empty(AnalyticsService.BuildVerificationContext(
            new List<AnalyticsService.OurEvent> { E("25ABC123", 0.9f, 100, 40) }, Guid.NewGuid()));

    // --- EŞ-ZAMANLI KANIT (2026-07-31): metin benzerliği ÖLÇÜLDÜ ve %27'de kaldı ---
    private static AnalyticsService.OurEvent T(string plate, float ocr, int w, int h,
                                               double secs, Guid? id = null)
        => new(DateTimeOffset.UnixEpoch.AddSeconds(secs), "giris", plate, ocr, ocr, false, "roi",
               null, "evidence/x.jpg", null, Array.Empty<string>(), id ?? Guid.NewGuid(), 0.8f, w, h);

    [Theory]
    // Dördü de CANLI doğrulanmış hata; dördünde de gerçek plaka veride ≥0,95 güvenle VARDI ama
    // metin paneli hiçbirini göstermedi (benzerlik sırasıyla 0,625 / 0,571 / 0 / 0,25).
    [InlineData("26FFV779", "06FJV769", 3.0)]    // benzerlik 0,625
    [InlineData("07HHS00", "07MMS01", 2.0)]      // benzerlik 0,571
    [InlineData("27BH8600", "77BH860", 10.0)]    // uzunluk farklı → benzerlik 0
    [InlineData("25ZG924", "25TGG001", 5.0)]     // uzunluk farklı → benzerlik 0
    public void Strong_reading_seconds_away_surfaces_even_when_text_is_far(
        string ours, string truth, double gapSeconds)
    {
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            T(ours, 0.60f, 150, 50, 0, target),
            T(truth, 0.99f, 200, 80, gapSeconds),
        };

        // Ön koşul: metin yolu bu çifti GERÇEKTEN kaçırıyor (test kendi gerekçesini doğrular).
        Assert.True(AnalyticsService.PositionalSimilarity(ours, truth) < AnalyticsService.SimilarityThreshold);

        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Single(ctx);
        Assert.Equal(truth, ctx[0].Plate);
        Assert.Equal("es-zamanli", ctx[0].Relation);
    }

    [Fact]
    public void A_time_neighbour_is_never_labelled_as_the_SAME_plate()
    {
        // KORUNAN DEĞİŞMEZ: eş-zamanlılık AYNI ARAÇ OLDUĞUNU KANITLAMAZ — yoğun trafikte 15 sn
        // içinde başka araç da geçer. Panel ADAY sunar, hüküm insanındır. "ayni" etiketi verilirse
        // operatör iki farklı aracı tek araç sanar.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            T("25ABC123", 0.60f, 150, 50, 0, target),
            T("34XYZ789", 0.99f, 300, 120, 4),
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Equal("es-zamanli", Assert.Single(ctx).Relation);
    }

    [Fact]
    public void Weak_reading_nearby_is_noise_not_evidence()
    {
        // Zayıf bir okumayı başka bir zayıf okumayla açıklamak kanıt değil. ÖLÇÜLDÜ: eşiği 0,99'a
        // çekmek yakalamayı %77→%50 düşürüyor, 0,95 optimum.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            T("25ABC123", 0.60f, 150, 50, 0, target),
            T("34XYZ789", 0.94f, 300, 120, 2),   // eşiğin ALTINDA
        };
        Assert.Empty(AnalyticsService.BuildVerificationContext(evs, target));
    }

    [Fact]
    public void Outside_the_measured_window_it_is_not_shown()
    {
        // ±15 sn ÖLÇÜLMÜŞ optimumdur: büyütmek yakalamayı ARTIRMIYOR, yalnız aday sayısını büyütüyor.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            T("25ABC123", 0.60f, 150, 50, 0, target),
            T("34XYZ789", 0.99f, 300, 120, 16),
        };
        Assert.Empty(AnalyticsService.BuildVerificationContext(evs, target));
        // Simetrik: geçmişteki okuma da sayılır (araç önce diğer kameradan geçmiş olabilir).
        var past = new List<AnalyticsService.OurEvent>
        {
            T("25ABC123", 0.60f, 150, 50, 20, target),
            T("34XYZ789", 0.99f, 300, 120, 8),
        };
        Assert.Single(AnalyticsService.BuildVerificationContext(past, target));
    }

    [Fact]
    public void Text_matches_cannot_crowd_out_the_time_signal()
    {
        // CANLI KUSUR (2026-07-31, ilk sürümde yakalandı): `25ZCG011` olayında gerçek plaka
        // (`25TGG001`) eş-zamanlı olarak MEVCUTTU ama 6 metin-benzeri aday şeridi doldurup onu
        // dışarı itti — panel yanlış cevabı gösterip doğrusunu sakladı. Metin sinyalinin ölçülmüş
        // yakalaması yalnız %27; tüm slotları ona vermek panelin amacını yok eder.
        // DEĞİŞMEZ: eş-zamanlı aday varsa şeritte kendine YER AYRILIR.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent> { T("25ZCG011", 0.58f, 100, 40, 0, target) };
        for (int i = 0; i < 8; i++)                              // şeridi dolduran metin-benzerleri
            evs.Add(T("25ZCG01" + i, 0.99f, 900 - i, 400, 3));
        evs.Add(T("25TGG001", 0.97f, 150, 45, 5));               // GERÇEK plaka, küçük kutu

        var ctx = AnalyticsService.BuildVerificationContext(evs, target);

        Assert.Equal(6, ctx.Count);
        Assert.Contains(ctx, c => c.Plate == "25TGG001" && c.Relation == "es-zamanli");
    }

    [Fact]
    public void Copies_of_the_same_string_cannot_fill_the_strip()
    {
        // CANLI KUSUR (2026-07-31, üçüncü tur): `25ACV952` olayında şerit AYNI DİZGİNİN 6 kopyasıyla
        // doldu ve gerçek plaka `25ACY952` (lev=1 komşu!) dışarı itildi — panel altı kez "kendini"
        // gösterip tek işe yarar adayı sakladı. Aynı dizginin 3. kırpıntısı bilgi KATMAZ; farklı bir
        // aday katar. Bu yüzden dizgi başına tavan var.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent> { E("25ACV952", 0.97f, 100, 40, target) };
        for (int i = 0; i < 8; i++) evs.Add(E("25ACV952", 0.99f, 900 - i, 400));   // aynı dizgi ×8
        evs.Add(E("25ACY952", 0.888f, 120, 45));                                   // lev1 GERÇEK plaka

        var ctx = AnalyticsService.BuildVerificationContext(evs, target);

        Assert.Contains(ctx, c => c.Plate == "25ACY952");
        Assert.True(ctx.Count(c => c.Plate == "25ACV952") <= 2,
                    "aynı dizgi şeridi tekelleştiremez");
    }

    [Fact]
    public void Time_neighbours_rank_by_closeness_in_TIME_not_by_box_size()
    {
        // CANLI KUSUR (2026-07-31, ikinci tur): eş-zamanlıları da kutu alanına göre sıralıyordum.
        // `25ZCG011` olayında gerçek plaka (`25TGG001`, −5,2 sn, 6235px) +11,5 sn'deki BAŞKA bir
        // aracın DAHA BÜYÜK kutusu tarafından şeritten atıldı. Metin kanıdında "en net kırpıntı"
        // doğru ölçüttür; eş-zamanlıda soru "hangisi AYNI ARAÇ" ve onun vekili ZAMAN yakınlığıdır.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            T("25ZCG011", 0.58f, 100, 40, 0, target),
            T("06HB6654", 0.999f, 900, 400, 11.5),   // uzak ama DEV kutu → başka araç
            T("25TGG001", 0.971f, 120, 52, -5.2),    // yakın ama küçük kutu → gerçek plaka
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Equal("25TGG001", ctx[0].Plate);
    }

    [Fact]
    public void Text_evidence_still_outranks_time_evidence()
    {
        // Eş-zamanlılık EN ZAYIF iddiadır — aynı dizgi ve lev1 komşu ondan ÖNCE gelmeli, kutu
        // ne kadar büyük olursa olsun.
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent>
        {
            T("25ABC123", 0.60f, 100, 40, 0, target),
            T("99ZZZ999", 0.99f, 900, 400, 3),   // eş-zamanlı, DEV kutu
            T("25ABC124", 0.99f, 100, 40, 3),    // komşu (lev1)
            T("25ABC123", 0.99f, 100, 40, 3),    // aynı
        };
        var ctx = AnalyticsService.BuildVerificationContext(evs, target);
        Assert.Equal(new[] { "ayni", "komsu", "es-zamanli" }, ctx.Select(c => c.Relation).ToArray());
    }

    [Fact]
    public void Result_is_capped_so_the_page_stays_usable()
    {
        // İKİ tavan birlikte çalışır: toplam `max` VE dizgi başına 2 (bkz.
        // Copies_of_the_same_string_cannot_fill_the_strip — canlıda aynı dizginin 6 kopyası
        // gerçek plakayı şeritten atmıştı).
        var target = Guid.NewGuid();
        var evs = new List<AnalyticsService.OurEvent> { E("25ADL991", 0.95f, 100, 40, target) };
        for (int i = 0; i < 20; i++) evs.Add(E("25ADL991", 0.9f, 100 + i, 40));
        // Hepsi AYNI dizgi → dizgi tavanı bağlar: 20 değil 2.
        Assert.Equal(2, AnalyticsService.BuildVerificationContext(evs, target).Count);

        // Farklı dizgiler olduğunda toplam tavan bağlar.
        for (int i = 0; i < 20; i++) evs.Add(E("25ADL99" + (i % 10), 0.9f, 200 + i, 40));
        Assert.Equal(6, AnalyticsService.BuildVerificationContext(evs, target).Count);
        Assert.Equal(3, AnalyticsService.BuildVerificationContext(evs, target, max: 3).Count);
    }
}
