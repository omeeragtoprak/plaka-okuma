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
}
