using Anpr.Core.TurkishPlates;

namespace Anpr.Tests;

public class TrConstrainedDecoderTests
{
    private const string Alpha = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
    private const int Slots = 10;

    // Slot dağılımı kur: taban 0.001, verilen (slot, char, prob) noktaları yüksek.
    private static float[] Dist(params (int slot, char ch, float p)[] pts)
    {
        int v = Alpha.Length;
        var a = new float[Slots * v];
        for (int i = 0; i < a.Length; i++) a[i] = 0.001f;
        foreach (var (slot, ch, p) in pts) a[slot * v + Alpha.IndexOf(ch)] = p;
        return a;
    }

    [Fact]
    public void Clean_distribution_decodes_correct_plate()
    {
        // "25 ABC 123" (3 harf, 3 rakam) -> slot 0..7 içerik, 8-9 pad
        var d = Dist(
            (0, '2', 0.95f), (1, '5', 0.95f),
            (2, 'A', 0.95f), (3, 'B', 0.95f), (4, 'C', 0.95f),
            (5, '1', 0.95f), (6, '2', 0.95f), (7, '3', 0.95f),
            (8, '_', 0.95f), (9, '_', 0.95f));

        var r = TrConstrainedDecoder.Decode(d, Alpha, Slots);
        Assert.True(r.Ok);
        Assert.Equal("25 ABC 123", r.Normalized);
        Assert.True(r.Confidence > 0.8f);
    }

    [Fact]
    public void Letter_slot_never_picks_a_digit_even_if_higher()
    {
        // Harf pozisyonunda (slot3) rakam '8' biraz daha olası ama yapı 2-harf ise İZİNLİ değil.
        // Yapı L=1 (slot3=rakam) da mümkün; test: sonuç HER durumda GEÇERLİ TR plakası olmalı.
        var d = Dist(
            (0, '2', 0.95f), (1, '5', 0.95f),
            (2, 'A', 0.95f), (3, '8', 0.55f), (3, 'B', 0.5f),
            (4, '1', 0.95f), (5, '2', 0.95f), (6, '3', 0.95f), (7, '4', 0.95f),
            (8, '_', 0.95f), (9, '_', 0.95f));
        var r = TrConstrainedDecoder.Decode(d, Alpha, Slots);
        Assert.True(r.Ok);
        Assert.Equal("25", r.Province);
        // Harf bloğunda yalnızca A-Z (Q,W,X hariç) olabilir — rakam sızamaz.
        Assert.All(r.Letters, c => Assert.True(c is >= 'A' and <= 'Z' && c is not ('Q' or 'W' or 'X')));
        Assert.All(r.Numbers, c => Assert.True(c is >= '0' and <= '9'));
    }

    [Fact]
    public void Invalid_province_is_forced_into_valid_range()
    {
        // Argmax il "99" (geçersiz, >81). Kısıtlı çözümleme geçerli aralığa (01-81) zorlar.
        var d = Dist(
            (0, '9', 0.5f), (0, '8', 0.45f),
            (1, '9', 0.5f), (1, '1', 0.45f),
            (2, 'A', 0.95f), (3, 'B', 0.95f),
            (4, '3', 0.95f), (5, '4', 0.95f), (6, '5', 0.95f),
            (7, '_', 0.95f), (8, '_', 0.95f), (9, '_', 0.95f));
        var r = TrConstrainedDecoder.Decode(d, Alpha, Slots);
        Assert.True(r.Ok);
        int prov = int.Parse(r.Province);
        Assert.InRange(prov, 1, 81); // "99" değil
    }
}
