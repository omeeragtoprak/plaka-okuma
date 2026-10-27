using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>H1: varyant-bastırma kuyrukta GERÇEK ikinci aracı düşürmemeli, ama hayalet varyantı bastırmalı.</summary>
public class VariantSuppressionTests
{
    private static HashSet<string> S(params string[] x) => new(x);
    private const float MR = 0.85f;  // ManualReviewMinOcr

    // --- H1 DÜZELTMESİ: yüksek-güvenli (≥0.99) aday-eşleşmesi = GERÇEK 2. araç → bastırMA ---
    [Fact]
    public void High_conf_candidate_match_is_NOT_suppressed_real_second_vehicle()
    {
        // Kuyrukta: 1. araç 25ADM123 (top-K aday: 25ADM124); 2. araç 25ADM124'ü 1.0 güvenle okuyoruz.
        bool sup = CameraWorker.IsVariantOfRecentEmit(
            "25ADM124", S(), 1.00f, false,
            "25ADM123", S("25ADM124"), 0.98f, false, MR);
        Assert.False(sup);   // gerçek 2. araç emit edilmeli (bariyer açılır)
    }

    // --- Hayalet varyant KORUNDU: belirsiz (<0.99) aday-eşleşmesi bastırılır ---
    [Fact]
    public void Low_conf_candidate_match_is_suppressed_ghost_variant()
    {
        bool sup = CameraWorker.IsVariantOfRecentEmit(
            "25ADM124", S(), 0.90f, false,
            "25ADM123", S("25ADM124"), 0.98f, false, MR);
        Assert.True(sup);
    }

    [Fact]
    public void Exact_reemit_is_suppressed_dedup()
        => Assert.True(CameraWorker.IsVariantOfRecentEmit(
            "25ADM123", S(), 1.00f, false, "25ADM123", S(), 0.98f, false, MR));

    [Fact]
    public void Rule2_low_conf_near_variant_suppressed()
        => Assert.True(CameraWorker.IsVariantOfRecentEmit(
            "25ADM124", S(), 0.80f, false, "25ADM123", S(), 0.95f, false, MR));  // <MR + lev1 + recent≥0.9

    [Fact]
    public void RuleD_frequent_variant_suppressed()
        => Assert.True(CameraWorker.IsVariantOfRecentEmit(
            "25FZ289", S(), 0.89f, false, "25FZ069", S(), 0.97f, /*recentIsFrequent*/ true, MR));  // lev2, müdavimin varyantı

    [Fact]
    public void Far_ghost_via_candidate_containment_suppressed()
        // 25DDS84-kümesi: lev>2 hayalet, YALNIZ aday-eşleşmesiyle yakalanır (Kural 2/D lev≤2 ister); <0.99 → bastır.
        => Assert.True(CameraWorker.IsVariantOfRecentEmit(
            "26ADJ888", S(), 0.68f, false, "25DDS84", S("26ADJ888"), 0.95f, false, MR));

    [Fact]
    public void Distinct_high_conf_plate_not_suppressed()
        => Assert.False(CameraWorker.IsVariantOfRecentEmit(
            "34XYZ999", S(), 1.00f, false, "25ABC123", S(), 0.98f, false, MR));

    // --- Kural 2 yeniden kalibrasyonu (2026-07-28): canlı hayalet vakaları ---
    // Aynı araç 2-4 sn arayla iki olay üretiyor, ikincisi var olmayan bir plaka. Ölçüm: eski kural
    // (oc<0.85 && roc>=0.90) 4 canlı vakanın 0'ını yakalıyordu; yeni kural (oc<0.95 && roc>oc) 4/4
    // yakalıyor ve gerçek araç kaybı 0.

    [Theory]
    [InlineData("20FJV789", 0.853f, "26FLV789", 0.803f)]   // 27.07 16:45 cikis
    [InlineData("25ACL560", 0.867f, "25ACL860", 0.703f)]   // 27.07 17:18 giris (kanıt görselinde plaka 25 ACL 560)
    [InlineData("25ADA743", 0.863f, "25ADA33", 0.853f)]    // 27.07 17:29 giris
    [InlineData("25AEU557", 0.998f, "25AEU337", 0.864f)]   // 28.07 08:11 giris — LowConfidence=false idi, OTOMATİK AÇIYORDU
    public void Ghost_variant_after_real_emit_is_suppressed(string firstPlate, float firstOcr, string ghost, float ghostOcr)
    {
        bool suppressed = CameraWorker.IsVariantOfRecentEmit(
            compact: ghost, candSet: new HashSet<string>(), ocrConf: ghostOcr, isFrequent: false,
            recentCompact: firstPlate, recentCandidates: new HashSet<string>(), recentOcrConf: firstOcr,
            recentIsFrequent: false, manualReviewMinOcr: 0.85f);
        Assert.True(suppressed, $"{firstPlate}({firstOcr:F2}) sonrası {ghost}({ghostOcr:F2}) hayalet sayılmalıydı");
    }

    [Fact]
    public void Stronger_reading_after_weak_emit_is_not_suppressed()
    {
        // Ters yön: ikinci okuma DAHA GÜÇLÜyse hayalet değildir (gerçek araç ya da düzelen okuma).
        bool suppressed = CameraWorker.IsVariantOfRecentEmit(
            compact: "25ABC124", candSet: new HashSet<string>(), ocrConf: 0.97f, isFrequent: false,
            recentCompact: "25ABC123", recentCandidates: new HashSet<string>(), recentOcrConf: 0.86f,
            recentIsFrequent: false, manualReviewMinOcr: 0.85f);
        Assert.False(suppressed);
    }

    [Fact]
    public void High_confidence_second_vehicle_is_not_suppressed()
    {
        // Kuyruktaki gerçek ikinci araç (0.95 üstü) korunmalı — 0.95 tavanının varlık sebebi budur.
        bool suppressed = CameraWorker.IsVariantOfRecentEmit(
            compact: "25ABC124", candSet: new HashSet<string>(), ocrConf: 0.96f, isFrequent: false,
            recentCompact: "25ABC123", recentCandidates: new HashSet<string>(), recentOcrConf: 0.99f,
            recentIsFrequent: false, manualReviewMinOcr: 0.85f);
        Assert.False(suppressed);
    }

    // --- KESİK VARYANT (2026-07-29): ön-ek kanıtı güven tavanını DELMELİ ---
    // Canlı vaka (kullanıcı bildirdi): araç 34 ZH 7641. 11:07:26 giriş kamerasından DOĞRU okundu
    // (ocr=0.9947, adaylar arasında "34ZH764" 0.648 ile VAR). 3 saniye sonra aynı kameradan kutu
    // küçüldü (141x96 -> 119x43), kırpıntı son haneyi kesti ve "34ZH764" ocr=0.9977 ile YAYINLANDI.
    // Eksik karakterin "yok" olasılığı KESİN olduğu için güven şişti (0.9977 > 0.99) ve üç korumayı
    // birden devre dışı bıraktı: aday-eşleşmesi 0.99 tavanı, Kural 2'nin 0.95 tavanı, snap kapısının
    // 0.99 tavanı. Sonuç: VAR OLMAYAN bir plakayla LowConfidence=false, otomatik bariyer açılması.
    // Fiziksel gerekçe: yeni okuma önceki emitin KESİN ÖN-EKİ ise (son karakter(ler)i düşmüş) ve önceki
    // emit bu dizgiyi ADAY olarak zaten taşıyorsa, bu yeni bir araç değil aynı geçişin kesik kırpıntısıdır.
    [Fact]
    public void Truncated_prefix_of_recent_emit_is_suppressed_even_above_conf_ceiling()
    {
        bool suppressed = CameraWorker.IsVariantOfRecentEmit(
            compact: "34ZH764", candSet: S("34ZH764", "34ZH7644", "34ZHT64"), ocrConf: 0.9977f, isFrequent: false,
            recentCompact: "34ZH7641", recentCandidates: S("34ZH7641", "34ZH764", "34ZHT641"),
            recentOcrConf: 0.9947f, recentIsFrequent: false, manualReviewMinOcr: 0.85f);
        Assert.True(suppressed, "kesik ön-ek (34ZH764 ⊂ 34ZH7641) şişmiş güvene rağmen bastırılmalıydı");
    }

    [Fact]
    public void Longer_reading_after_shorter_emit_is_NOT_suppressed()
    {
        // TERS YÖN korunmalı: önce kısa yayınlandıysa ve şimdi UZUN okuma geldiyse, uzun olan
        // düzelmiş okuma olabilir. Ön-ek muafiyeti YALNIZ yeni okuma KISA olduğunda geçerlidir.
        bool suppressed = CameraWorker.IsVariantOfRecentEmit(
            compact: "34ZH7641", candSet: S("34ZH7641", "34ZH764"), ocrConf: 0.9977f, isFrequent: false,
            recentCompact: "34ZH764", recentCandidates: S("34ZH764", "34ZH7641"),
            recentOcrConf: 0.9947f, recentIsFrequent: false, manualReviewMinOcr: 0.85f);
        Assert.False(suppressed);
    }

    [Fact]
    public void Prefix_without_candidate_evidence_is_NOT_suppressed()
    {
        // Ön-ek TEK BAŞINA yetmez: önceki emitin aday listesinde bu dizgi yoksa, gerçekten kısa
        // plakalı ayrı bir araç olabilir (ör. "34 AYN 08" gibi 2 haneli gerçek plakalar mevcut).
        bool suppressed = CameraWorker.IsVariantOfRecentEmit(
            compact: "34AYN08", candSet: S("34AYN08"), ocrConf: 0.9977f, isFrequent: false,
            recentCompact: "34AYN081", recentCandidates: S("34AYN081"),
            recentOcrConf: 0.9947f, recentIsFrequent: false, manualReviewMinOcr: 0.85f);
        Assert.False(suppressed);
    }
}
