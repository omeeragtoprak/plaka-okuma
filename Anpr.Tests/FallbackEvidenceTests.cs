using Anpr.Core;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// KANITSIZ OLAY KUSURU (2026-08-01, canlı ölçümde bulundu).
///
/// <para><b>Mekanizma:</b> kırpıntılar yalnız <c>IsValidTr</c> okumalarda saklanıyordu. Çok-kareli
/// uzlaşı, tek tek kareleri TR-geçersiz okunmuş bir araçtan GEÇERLİ plaka üretebilir; o zaman hiç
/// kova oluşmaz ve <c>SaveEvidence</c> sessizce boş döner.</para>
///
/// <para><b>Ölçüm:</b> 2844 olayın 8'inde (roi) ne kırpıntı ne tam kare vardı — ve <b>sekizinin de
/// <c>LowConfidence=true</c></b> olduğu görüldü (ocr 0,46–0,77). Yani görüntüsüz kalan olaylar tam
/// da operatörün GÖZLE bakması gereken olaylardı; operatör kuyruğunun %4,3'ü doğrulanamıyordu.</para>
///
/// <para><b>Yedek yol METİNLE DEĞİL, UZAMSAL ÖRTÜŞMEYLE eşleşir.</b> Kova mekanizmasını (lev≤1 metin
/// benzerliği) geçersiz okumalara açmak, çöp bir dizginin başka bir aracın uzlaşı metnine düşüp ona
/// YANLIŞ kanıt vermesi riskini büyütürdü. <b>Yanlış kanıt, kanıtsızlıktan kötüdür</b> — yer-gerçeğini
/// zehirler (§P.4 2026-07-30 (15)'te dosya adı çakışması tam bunu yapmıştı).</para>
/// </summary>
public class FallbackEvidenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan W = TimeSpan.FromSeconds(6);

    private static (PlateBox, float, DateTimeOffset) C(int x, int y, int w, int h, float ocr, double dt)
        => (new PlateBox(x, y, w, h), ocr, T0.AddSeconds(dt));

    // ---- KİMLİK ÖLÇÜTÜ: KESİŞİM ----------------------------------------------------------------

    [Fact]
    public void An_overlapping_recent_crop_is_used_as_evidence()
    {
        var cands = new[] { C(100, 100, 120, 40, 0.62f, -1) };
        Assert.Equal(0, CameraWorker.PickFallbackCrop(cands, new PlateBox(105, 102, 120, 40), T0, W));
    }

    [Fact]
    public void A_disjoint_box_is_rejected_even_when_it_is_the_only_candidate()
    {
        // "Yakındı" yeterli değildir: kesişmiyorsa BAŞKA bir araç olabilir ve yanlış kanıt,
        // kanıtsızlıktan kötüdür.
        var cands = new[] { C(100, 100, 120, 40, 0.99f, 0) };
        Assert.Equal(-1, CameraWorker.PickFallbackCrop(cands, new PlateBox(900, 100, 120, 40), T0, W));
    }

    [Fact]
    public void Touching_edges_do_not_count_as_intersection()
    {
        Assert.False(CameraWorker.BoxesIntersect(new PlateBox(0, 0, 10, 10), new PlateBox(10, 0, 10, 10)));
        Assert.True(CameraWorker.BoxesIntersect(new PlateBox(0, 0, 10, 10), new PlateBox(9, 0, 10, 10)));
    }

    [Fact]
    public void Degenerate_boxes_never_match()
    {
        Assert.False(CameraWorker.BoxesIntersect(new PlateBox(0, 0, 0, 10), new PlateBox(0, 0, 10, 10)));
        var cands = new[] { C(0, 0, 10, 0, 0.9f, 0) };
        Assert.Equal(-1, CameraWorker.PickFallbackCrop(cands, new PlateBox(0, 0, 10, 10), T0, W));
    }

    // ---- SEÇİM ---------------------------------------------------------------------------------

    [Fact]
    public void Among_overlapping_candidates_the_temporally_nearest_wins_not_the_most_confident()
    {
        // DÜZELTİLDİ (2026-08-01): ilk sürüm EN GÜVENLİYİ seçiyordu. Güven bir KİMLİK ölçütü
        // değildir. Halka emit ANINDA sorgulanır, yani içinde yalnız GEÇMİŞ kırpıntılar vardır;
        // olayı üreten aracın kırpıntıları zorunlu olarak EN YENİLERDİR. Daha eski bir kayıt
        // ancak ÖNCEKİ bir araca ait olabilir — ölçüm: 8 sn içinde aynı kameraya başka araç
        // gelme oranı ~%13.
        var cands = new[]
        {
            C(100, 100, 120, 40, 0.41f, -2),
            C(102, 101, 120, 40, 0.77f, -1),   // EN GÜVENLİ ama daha eski
            C(104, 100, 120, 40, 0.55f, 0),    // ZAMANCA EN YAKIN → seçilmeli
        };
        Assert.Equal(2, CameraWorker.PickFallbackCrop(cands, new PlateBox(103, 100, 120, 40), T0, W));
    }

    [Fact]
    public void An_older_high_confidence_crop_of_a_previous_vehicle_does_not_win()
    {
        // Somut zarar senaryosu: 6 sn önce aynı şeritten geçen BAŞKA bir araç, çöp okumasını
        // 0,95 güvenle bırakmış. Bu olayın kendi (zayıf) kırpıntısı 0,5 sn önce. Zaman ölçütü
        // doğru olanı seçer; güven ölçütü YANLIŞ ARACIN görüntüsünü iliştirirdi.
        var cands = new[]
        {
            C(100, 100, 120, 40, 0.95f, -6.0),
            C(101, 100, 120, 40, 0.33f, -0.5),
        };
        Assert.Equal(1, CameraWorker.PickFallbackCrop(cands, new PlateBox(100, 100, 120, 40), T0, W));
    }

    [Fact]
    public void A_more_confident_but_disjoint_candidate_never_beats_an_overlapping_one()
    {
        // Güven bir KİMLİK ölçütü değildir; kesişim öyledir.
        var cands = new[]
        {
            C(900, 400, 120, 40, 0.99f, 0),    // güvenli AMA başka yerde
            C(100, 100, 120, 40, 0.44f, 0),    // zayıf AMA aynı araç
        };
        Assert.Equal(1, CameraWorker.PickFallbackCrop(cands, new PlateBox(100, 100, 120, 40), T0, W));
    }

    // ---- PENCERE -------------------------------------------------------------------------------

    [Fact]
    public void Candidates_outside_the_window_are_ignored_in_both_directions()
    {
        var eventBox = new PlateBox(100, 100, 120, 40);
        Assert.Equal(-1, CameraWorker.PickFallbackCrop(new[] { C(100, 100, 120, 40, 0.9f, -20) }, eventBox, T0, W));
        Assert.Equal(-1, CameraWorker.PickFallbackCrop(new[] { C(100, 100, 120, 40, 0.9f, 20) }, eventBox, T0, W));
        Assert.Equal(0, CameraWorker.PickFallbackCrop(new[] { C(100, 100, 120, 40, 0.9f, -5) }, eventBox, T0, W));
    }

    [Fact]
    public void No_candidates_means_no_evidence_and_the_caller_must_warn()
    {
        Assert.Equal(-1, CameraWorker.PickFallbackCrop(
            Array.Empty<(PlateBox, float, DateTimeOffset)>(), new PlateBox(100, 100, 120, 40), T0, W));
    }

    // ---- CANLI VAKA ----------------------------------------------------------------------------

    [Fact]
    public void The_measured_live_case_would_now_produce_evidence()
    {
        // 2026-07-31 10:59:40 giris, '25A4649' ocr=0,491, LowConfidence=true, kanıt YOKTU.
        // Aynı araç aynı saniyelerde TR-geçersiz okunmuş kırpıntılar bırakır; kutular kesişir.
        var cands = new[]
        {
            C(1180, 520, 132, 46, 0.38f, -2.0),
            C(1194, 524, 130, 45, 0.47f, -0.8),
        };
        int i = CameraWorker.PickFallbackCrop(cands, new PlateBox(1188, 522, 131, 46), T0, W);
        Assert.Equal(1, i);
    }
}
