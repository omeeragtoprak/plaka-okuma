using Anpr.Core;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// Son-hane belirsizliği kapısı (2026-07-29). Kırpıntı son karakteri kestiğinde model "karakter yok"
/// hipotezinden EMİN olur ve OCR güveni ŞİŞER — güven eşiği bu hatayı göremez. Aday farkı görebilir.
/// Kapı plakayı DEĞİŞTİRMEZ, yalnız LowConfidence bayrağı koyar (tek yönlü, asla gevşetmez).
/// </summary>
public class LastCharAmbiguityTests
{
    private const float Gap = 0.10f;

    private static IReadOnlyList<PlateCandidate> C(params (string t, float c)[] xs)
        => xs.Select(x => new PlateCandidate(x.t, x.c)).ToList();

    // --- CANLI VAKALAR: kapının yakaladığı gerçek/şüpheli yanlış okumalar ---
    [Theory]
    [InlineData("34GGE99", 0.884f, "34GGE996", 0.838f)]   // gerçeği 34GGE993
    [InlineData("25AFG45", 0.883f, "25AFG450", 0.849f)]   // gerçeği 25AFG450
    [InlineData("25ABZ03", 0.868f, "25ABZ032", 0.847f)]   // gerçeği 25ABZ039
    [InlineData("25AFJ26", 0.856f, "25AFJ269", 0.798f)]   // gerçeği 25AFJ263
    public void Live_truncation_cases_are_flagged(string a, float ca, string b, float cb)
        => Assert.True(CameraWorker.HasAmbiguousLastChar(C((a, ca), (b, cb)), Gap));

    [Fact]
    public void Zero_gap_between_prefix_pair_is_maximally_ambiguous()
        // Canlı: 25AEJ607(0.908) vs 25AEJ60(0.908) — model tam kararsız.
        => Assert.True(CameraWorker.HasAmbiguousLastChar(C(("25AEJ607", 0.908f), ("25AEJ60", 0.908f)), Gap));

    // --- KORUMALAR: fazla geniş olmamalı ---
    [Fact]
    public void Confident_reading_with_prefix_pair_is_NOT_flagged()
    {
        // Ön-ek çiftinin KENDİSİ ayırt edici değil — olayların %83,8'inde zaten var. Fark büyükse
        // model emindir ve kapı tetiklenmemelidir (canlı karşı örnek: 25AV104 vs 25AV1044 fark 0,515).
        Assert.False(CameraWorker.HasAmbiguousLastChar(C(("25AV104", 0.995f), ("25AV1044", 0.480f)), Gap));
    }

    [Fact]
    public void Non_prefix_candidates_are_NOT_flagged_even_when_close()
    {
        // Ortadaki karakterde farklılık bu kapının konusu DEĞİL (son-hane belirsizliği değil).
        Assert.False(CameraWorker.HasAmbiguousLastChar(C(("25ABC123", 0.90f), ("25ABD123", 0.89f)), Gap));
    }

    [Fact]
    public void Two_character_length_difference_is_NOT_flagged()
    {
        // Yalnız TEK karakterlik son-hane kararsızlığı hedeflenir.
        Assert.False(CameraWorker.HasAmbiguousLastChar(C(("25ABC1", 0.90f), ("25ABC123", 0.89f)), Gap));
    }

    [Fact]
    public void Gate_is_off_when_gap_is_zero()
        => Assert.False(CameraWorker.HasAmbiguousLastChar(C(("34GGE99", 0.884f), ("34GGE996", 0.838f)), 0f));

    [Fact]
    public void Missing_or_single_candidate_is_safe()
    {
        Assert.False(CameraWorker.HasAmbiguousLastChar(null, Gap));
        Assert.False(CameraWorker.HasAmbiguousLastChar(C(("25ABC123", 0.99f)), Gap));
    }

    [Fact]
    public void Known_false_alarm_class_is_documented_not_silently_wrong()
    {
        // "34 AYN 08" GERÇEKTEN 2 haneli bir plaka; kapı onu da işaretler. Bu, kabul edilen bedeldir
        // (ölçüm: 13 yeni işaretin 9'u doğru okuma). Test bunu KİLİTLER ki davranış sürpriz olmasın.
        Assert.True(CameraWorker.HasAmbiguousLastChar(C(("34AYN08", 0.922f), ("34AYN081", 0.830f)), Gap));
    }
}
