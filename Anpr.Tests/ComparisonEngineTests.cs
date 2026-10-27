using Anpr.Service.Panel;
using static Anpr.Service.Panel.ComparisonEngine;

namespace Anpr.Tests;

/// <summary>
/// Karşılaştırma motorunun doğruluğu (2026-07-28 denetimi). Panel ve /analiz ayrı algoritmalar
/// kullanıyordu ve panelinki skoru tek yönlü şişiriyordu; bu testler tek motorun invaryantlarını
/// ve denetimde bulunan kenar durumları sabitler.
/// </summary>
public class ComparisonEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Options O = new();

    private static Obs Our(string plate, double sec, string cam = "giris", string scope = "roi", float ocr = 1f)
        => new(T0.AddSeconds(sec), cam, plate, scope, ocr);
    private static Obs Cam(string plate, double sec, string cam = "giris")
        => new(T0.AddSeconds(sec), cam, plate);

    private static Result Run(IEnumerable<Obs> ours, IEnumerable<Obs> cam)
        => Match(ReducePasses(ours, O), ReducePasses(cam, O), O);

    // ---------------------------------------------------------------- geçişe indirgeme

    [Fact]
    public void Camera_burst_becomes_one_pass_and_never_creates_a_miss()
    {
        // Kenar durum 1: kamera aynı aracı 300 ms içinde 3 kez raporlar (canlı: 25ADP187).
        var cam = new[] { Cam("25ABC123", 0.096), Cam("25ABC123", 0.295), Cam("25ABC123", 0.404) };
        var r = Run(new[] { Our("25ABC123", 0) }, cam);
        Assert.Equal(1, r.CamPasses);
        Assert.Equal(1, r.Exact);
        Assert.Empty(r.CameraOnly);          // fazla kayıtlar "sadece kamera" ÜRETMEZ
    }

    [Fact]
    public void Genuine_second_visit_stays_two_passes()
    {
        // Kenar durum 2: araç gerçekten ikinci kez geçer (pencere dışı) → iki ayrı geçiş.
        var ours = new[] { Our("25ABC123", 0), Our("25ABC123", 165) };
        var cam = new[] { Cam("25ABC123", 1), Cam("25ABC123", 166) };
        var r = Run(ours, cam);
        Assert.Equal(2, r.OursPasses);
        Assert.Equal(2, r.Exact);
    }

    [Fact]
    public void Roi_wins_over_observation_in_same_pass()
    {
        // Kenar durum 3: aynı araç için roi + gozlem okuması → tek geçiş, roi kazanır.
        var ours = new[] { Our("25ABC123", 0, scope: "gozlem"), Our("25ABC123", 0.731) };
        var r = Run(ours, new[] { Cam("25ABC123", 1) });
        Assert.Equal(1, r.OursPasses);
        Assert.Equal(1, r.Exact);            // gözlem olarak sayılsaydı skora hiç girmezdi
    }

    // ---------------------------------------------------------------- eşleştirme doğruluğu

    [Fact]
    public void Dense_traffic_each_vehicle_keeps_its_own_camera_record()
    {
        // Kenar durum 4/16: arka arkaya 5 araç — hiçbiri komşusunun kaydını çalmamalı.
        var ours = Enumerable.Range(0, 5).Select(i => Our($"25ABC12{i}", i * 3)).ToArray();
        var cam = Enumerable.Range(0, 5).Select(i => Cam($"25ABC12{i}", i * 3 + 1)).ToArray();
        var r = Run(ours, cam);
        Assert.Equal(5, r.Exact);
        Assert.Equal(0, r.OursOnly);
        Assert.Empty(r.CameraOnly);
    }

    [Fact]
    public void Result_is_independent_of_input_order()
    {
        // İnvaryant I4: SSE/backfill sırası sonucu DEĞİŞTİREMEZ.
        var ours = new[] { Our("25ABC123", 0), Our("25ABC124", 3), Our("25ABC125", 6) };
        var cam = new[] { Cam("25ABC123", 1), Cam("25ABC124", 4), Cam("25ABC125", 7) };
        var a = Run(ours, cam);
        var b = Run(ours.Reverse(), cam.Reverse());
        Assert.Equal(a.Exact, b.Exact);
        Assert.Equal(a.Near, b.Near);
        Assert.Equal(a.OursOnly, b.OursOnly);
        Assert.Equal(a.MissCandidate, b.MissCandidate);
    }

    [Fact]
    public void One_reading_can_excuse_at_most_one_camera_record()
    {
        // Kenar durum 6 + İnvaryant I3: tek okumamız BİRDEN ÇOK kamera kaydını mazur gösteremez.
        // (Eski panelde bu mümkündü ve paydayı keyfî küçültüyordu.)
        var ours = new[] { Our("25ABC123", 0, cam: "cikis") };
        var cam = new[] { Cam("25ABC123", 5), Cam("25ABC124", 6), Cam("25ABC125", 7) };
        var r = Run(ours, cam);
        Assert.Equal(1, r.Cross);                       // yalnız BİRİ çapraz sayılır
        Assert.Equal(2, r.CameraOnly.Count - r.Cross);  // diğer ikisi kovalarda kalır
    }

    [Fact]
    public void Cross_match_does_not_disappear_from_the_totals()
    {
        // Kenar durum 7: çapraz eşleşme skoru YÜKSELTMEK için kaydı yok edemez; ayrı kovada durur.
        var ours = new[] { Our("25ABC123", 0, cam: "cikis") };
        var cam = new[] { Cam("25ABC123", 5) };
        var r = Run(ours, cam);
        Assert.Equal(1, r.Cross);
        Assert.Equal(0, r.MissCandidate);
        Assert.Equal(1, r.CamPasses);                   // kayıt hâlâ toplamda
    }

    [Theory]
    [InlineData(-8.0, true)]
    [InlineData(8.0, true)]
    [InlineData(-8.1, false)]
    [InlineData(8.1, false)]
    public void Lag_window_boundaries_are_inclusive(double lag, bool shouldMatch)
    {
        // Kenar durum 10: sınır dahil.
        var r = Run(new[] { Our("25ABC123", 0) }, new[] { Cam("25ABC123", lag) });
        Assert.Equal(shouldMatch ? 1 : 0, r.Exact);
    }

    [Fact]
    public void Camera_timestamp_before_ours_is_normal()
    {
        // Kenar durum 11: kamera damgası bizden ÖNCE gelir (ölçülen medyan −1..−3 sn) — eşleşmeli.
        var r = Run(new[] { Our("25ABC123", 5) }, new[] { Cam("25ABC123", 2.7) });
        Assert.Equal(1, r.Exact);
    }

    [Fact]
    public void Distance_two_is_not_a_match_but_is_noted()
    {
        // Kenar durum 12/13: lev=2 eşleşme DEĞİL (farklı araç olabilir); teşhis notu bırakılır.
        // NOT: iki plaka da TR-GEÇERLİ olmalı; aksi halde kamera kaydı "kamera hatası" kovasına düşer
        // (Q/W/X Türk plaka alfabesinde yoktur). lev("25ABC123","25ABD124") = 2.
        var r = Run(new[] { Our("25ABC123", 0) }, new[] { Cam("25ABD124", 1) });
        Assert.Equal(0, r.Exact);
        Assert.Equal(0, r.Near);
        Assert.Equal(1, r.OursOnly);
        Assert.Equal(1, r.MissCandidate);
        Assert.Contains(r.CameraOnly, c => c.SimilarNote != null);   // "benzer: ..." notu
    }

    // ---------------------------------------------------------------- kamera tarafı kovaları

    [Fact]
    public void Camera_invalid_plate_is_not_counted_as_our_miss()
    {
        // Kenar durum 14: kameranın TR-geçersiz okuması (canlı: Kiril harfli) bizim kaçırmamız değildir.
        // Geçersiz kayıt ORTAK PENCERE içinde olmalı; ikinci okumamız pencereyi genişletir.
        var ours = new[] { Our("25ABC123", 0), Our("25ADE456", 40) };
        var cam = new[] { Cam("25ABC123", 1), Cam("22W14027", 20), Cam("25ADE456", 41) };
        var r = Run(ours, cam);
        Assert.Equal(1, r.CamFaulty);
        Assert.Equal(0, r.MissCandidate);
    }

    [Fact]
    public void Street_traffic_seen_by_both_camera_views_is_not_a_miss()
    {
        // Kenar durum 15: kamera ROI kullanmaz; aynı plaka iki görüş alanında yakın zamanda → cadde.
        // Üçüncü okumamız ORTAK PENCEREYİ genişletir; aksi halde ikinci kamera kaydı (T+10) pencere
        // dışında kalır ve "cadde" tespiti için gereken çift oluşmaz.
        var cam = new[] { Cam("34ABZ999", 0, "giris"), Cam("34ABZ999", 10, "cikis") };
        var ours = new[] { Our("25ABC123", 0), Our("25ABC123", 0, "cikis"), Our("25ADE456", 15) };
        var r = Run(ours, cam);
        Assert.Equal(2, r.Street);
        Assert.Equal(0, r.MissCandidate);
    }

    [Fact]
    public void Camera_record_covered_only_by_our_observation_is_labelled_not_missed()
    {
        // Kenar durum: aracı gördük ama ROI DIŞINDA (bariyer kararı değil) → kaçırma sayılmaz.
        var ours = new[] { Our("34ABZ999", 0, scope: "gozlem") };
        var cam = new[] { Cam("34ABZ999", 1) };
        var r = Run(ours, cam);
        Assert.Equal(1, r.InObservation);
        Assert.Equal(0, r.MissCandidate);
        Assert.Equal(0, r.Exact);        // gözlem SKORA girmez
    }

    // ---------------------------------------------------------------- pencere ve boş veri

    [Fact]
    public void Common_window_trims_one_sided_history()
    {
        // Kenar durum 8: kamera 19 saat fazla veriye sahip (servis yeniden başlamış) → şişmemeli.
        var ours = new[] { Our("25ABC123", 0), Our("25ABC124", 10) };
        var cam = new[] { Cam("25OLD001", -68400), Cam("25ABC123", 1), Cam("25ABC124", 11) };
        var r = Run(ours, cam);
        Assert.Equal(2, r.Exact);
        Assert.Equal(0, r.MissCandidate);            // pencere dışı eski kayıt sayılmaz
        Assert.NotNull(r.Window);
    }

    [Fact]
    public void Empty_side_yields_null_metrics_not_zero_or_hundred()
    {
        // Kenar durum 9: bir taraf boşsa oran YOK — "%100" ya da "%0" yazmak yanıltıcı olurdu.
        var r = Run(new[] { Our("25ABC123", 0) }, Array.Empty<Obs>());
        Assert.Null(r.Window);
        Assert.Null(r.ExactAgreement);
        Assert.Null(r.Capture);
    }

    [Fact]
    public void Malformed_input_is_skipped_without_crashing()
    {
        // Kenar durum 17: boş plaka / bozuk kayıt sayaçları etkilemez.
        var ours = new[] { Our("", 0), Our("25ABC123", 1) };
        var r = Run(ours, new[] { Cam("25ABC123", 2) });
        Assert.Equal(1, r.OursPasses);
        Assert.Equal(1, r.Exact);
    }

    // ---------------------------------------------------------------- invaryantlar

    [Fact]
    public void Invariant_I1_our_buckets_sum_to_roi_passes()
    {
        var ours = new[] { Our("25ABC123", 0), Our("25ABC124", 5), Our("25ZBZ999", 10),
                           Our("34AAA111", 12, scope: "gozlem") };
        var cam = new[] { Cam("25ABC123", 1), Cam("25ABC125", 6), Cam("06KKK888", 30) };
        var r = Run(ours, cam);
        int roiPasses = r.Rows.Count;
        Assert.Equal(roiPasses, r.Exact + r.Near + r.OursOnly);
    }

    [Fact]
    public void Invariant_I2_camera_buckets_sum_to_camera_passes()
    {
        var ours = new[] { Our("25ABC123", 0), Our("25ABY777", 20, cam: "cikis"),
                           Our("34AAA111", 12, scope: "gozlem") };
        var cam = new[] { Cam("25ABC123", 1), Cam("34AAA111", 12.5), Cam("25ABY777", 22),
                          Cam("22W14027", 30), Cam("06KKK888", 40) };
        var r = Run(ours, cam);
        int accounted = r.Matched + r.Cross + r.Street + r.CamFaulty + r.InObservation + r.OutOfWindow + r.MissCandidate;
        Assert.Equal(r.CamPasses, accounted);
    }

    [Fact]
    public void Metrics_have_explicit_denominators()
    {
        // ExactAgreement = birebir/eşleşen ; Capture = eşleşen/(eşleşen+kaçırma adayı)
        var ours = new[] { Our("25ABC123", 0), Our("25ABC999", 20), Our("25AFF321", 45) };
        var cam = new[] { Cam("25ABC123", 1), Cam("25ABC998", 21), Cam("06KKK888", 30), Cam("25AFF321", 46) };
        var r = Run(ours, cam);
        Assert.Equal(2, r.Exact);
        Assert.Equal(1, r.Near);
        Assert.Equal(Math.Round(100.0 * r.Exact / r.Matched, 1), r.ExactAgreement);
        Assert.Equal(Math.Round(100.0 * r.Matched / (r.Matched + r.MissCandidate), 1), r.Capture);
    }

    [Fact]
    public void Out_of_window_same_plate_is_not_counted_as_a_miss()
    {
        // Canlı vaka 34SN988: kamera aracı bizden 18 sn ÖNCE raporladı (ROI'ye girmeden görüyor).
        // Aynı plakayı biz de okuduk → KAÇIRMA değil; ayrı kovaya düşmeli, yoksa kaçırma sayısı şişer.
        // Vakayı KUŞATAN kayıtlar: ortak pencere bunlarla açılır, 18 sn'lik fark pencere İÇİNDE ama
        // eşleşme penceresinin (±8 sn) dışında kalır — testin ölçmek istediği tam da bu.
        var ours = new[] { Our("25AAA111", 0), Our("34SN988", 60), Our("25ADE456", 100) };
        var cam = new[] { Cam("25AAA111", 1), Cam("34SN988", 42), Cam("25ADE456", 101) };
        var r = Run(ours, cam);
        Assert.Equal(1, r.OutOfWindow);
        Assert.Equal(0, r.MissCandidate);
        Assert.Equal(2, r.Exact);            // kuşatan iki çift eşleşir; 34SN988 eşleşmez ama kaçırma da sayılmaz
        Assert.Contains(r.CameraOnly, c => c.SimilarNote != null && c.SimilarNote.Contains("pencere dışı"));
    }
}
