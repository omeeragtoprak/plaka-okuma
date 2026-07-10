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

public class SkipCharAndTopKTests
{
    private const string Alpha = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
    private const int Slots = 10;

    private static float[] Dist(params (int slot, char ch, float p)[] pts)
    {
        int v = Alpha.Length;
        var a = new float[Slots * v];
        for (int i = 0; i < a.Length; i++) a[i] = 0.001f;
        foreach (var (slot, ch, p) in pts) a[slot * v + Alpha.IndexOf(ch)] = p;
        return a;
    }

    [Fact]
    public void Ghost_trailing_digit_dropped_with_skip_char()
    {
        // 24ACB27_ vakası: slot7'de zayıf hayalet '0' (0.30), pad olasılığı da düşük (kenar artefaktı).
        // SKIP_CHAR kapalıyken hayalet kazanır (24 ACB 270); açıkken kısa okuma (24 ACB 27) kazanmalı.
        var d = Dist(
            (0, '2', 0.95f), (1, '4', 0.95f),
            (2, 'A', 0.95f), (3, 'C', 0.95f), (4, 'B', 0.95f),
            (5, '2', 0.95f), (6, '7', 0.95f),
            (7, '0', 0.30f), // zayıf hayalet hane; pad(slot7) = 0.001
            (8, '_', 0.95f), (9, '_', 0.95f));

        var off = TrConstrainedDecoder.Decode(d, Alpha, Slots, skipCharLevel: 0f);
        var on = TrConstrainedDecoder.Decode(d, Alpha, Slots, skipCharLevel: 0.5f);

        Assert.Equal("24ACB270", off.Compact); // eski davranış: hayalet kazanıyor
        Assert.Equal("24ACB27", on.Compact);   // SKIP_CHAR: "karakter yok" hipotezi kazanır
    }

    [Fact]
    public void Strong_trailing_digit_survives_skip_char()
    {
        // Güçlü son hane (0.95) SKIP_CHAR'dan etkilenmemeli.
        var d = Dist(
            (0, '2', 0.95f), (1, '4', 0.95f),
            (2, 'A', 0.95f), (3, 'C', 0.95f), (4, 'B', 0.95f),
            (5, '2', 0.95f), (6, '7', 0.95f), (7, '0', 0.95f),
            (8, '_', 0.95f), (9, '_', 0.95f));

        var on = TrConstrainedDecoder.Decode(d, Alpha, Slots, skipCharLevel: 0.5f);
        Assert.Equal("24ACB270", on.Compact);
    }

    [Fact]
    public void TopK_contains_both_length_hypotheses()
    {
        var d = Dist(
            (0, '2', 0.95f), (1, '4', 0.95f),
            (2, 'A', 0.95f), (3, 'C', 0.95f), (4, 'B', 0.95f),
            (5, '2', 0.95f), (6, '7', 0.95f), (7, '0', 0.30f),
            (8, '_', 0.95f), (9, '_', 0.95f));

        var top = TrConstrainedDecoder.DecodeTopK(d, Alpha, Slots, 5, skipCharLevel: 0.5f);

        Assert.True(top.Count >= 2);
        var compacts = top.Select(t => t.Compact).ToList();
        Assert.Contains("24ACB27", compacts);   // kazanan
        Assert.Contains("24ACB270", compacts);  // hayalet varyant da adaylarda -> emit-sonrası bastırma köprüsü
        Assert.Equal(top[0].Compact, TrConstrainedDecoder.Decode(d, Alpha, Slots, 0.5f).Compact);
    }
}
