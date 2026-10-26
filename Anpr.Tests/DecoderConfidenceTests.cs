using Anpr.Core.TurkishPlates;

namespace Anpr.Tests;

/// <summary>
/// Güvenin UZUNLUKTAN BAĞIMSIZ olması (2026-07-28 CRITICAL bulgu).
/// <para>Skip-char mekanizması zayıf son slotu "pad" sayıp haneyi düşürebiliyor. Eskiden güven yalnız
/// pad-DIŞI slotların ortalamasıydı; zayıf slot ortalamadan çıkınca güven YÜKSELİYORDU. Sonuç:
/// kesik plaka (25AFH27 ← gerçek 25 AFH 277) ocr≈0.998 ile, LowConfidence=false olarak yayınlanıp
/// bariyeri var olmayan plakayla açıyordu; üstelik doğrusu whitelist'te olmasına rağmen snap kapısı
/// (ocr &lt; 0.99) şişmiş güven yüzünden hiç çalışmıyordu.</para>
/// </summary>
public class DecoderConfidenceTests
{
    // Model alfabesi: rakamlar + harfler + pad ('_').
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPRSTUVYZ_";
    private const int Slots = 8;

    /// <summary>Tek bir slotun dağılımı; padProb verilmezse kalan kütle pad'e gider.</summary>
    private static void SetSlot(float[] p, int slot, (char Ch, float Prob)[] chars, float? padProb = null)
    {
        int v = Alphabet.Length, pad = Alphabet.IndexOf('_');
        float used = 0;
        foreach (var (ch, pr) in chars) { p[slot * v + Alphabet.IndexOf(ch)] = pr; used += pr; }
        if (padProb is { } pp)
        {
            p[slot * v + pad] = pp;
            // Kalan kütleyi "gürültü" olarak dağıt: skip-char kapısı hem pad'in hem EN GÜÇLÜ pad-dışı
            // karakterin eşik altında olmasını ister; gerçek vakada dağılım işte böyle parçalıdır.
            float rest = Math.Max(0f, 1f - used - pp);
            int spread = 6;
            for (int k = 0; k < spread; k++) p[slot * v + k] += rest / spread;
        }
        else p[slot * v + pad] = Math.Max(0f, 1f - used);
    }

    /// <summary>"25 ACU 92|7" — son hanede gerçek '7' var ama ZAYIF (0.23), pad ise 0.12.</summary>
    private static float[] WeakLastDigitPlate()
    {
        var p = new float[Slots * Alphabet.Length];
        SetSlot(p, 0, new[] { ('2', 0.99f) });
        SetSlot(p, 1, new[] { ('5', 0.99f) });
        SetSlot(p, 2, new[] { ('A', 0.99f) });
        SetSlot(p, 3, new[] { ('C', 0.98f) });
        SetSlot(p, 4, new[] { ('U', 0.98f) });
        SetSlot(p, 5, new[] { ('9', 0.99f) });
        SetSlot(p, 6, new[] { ('2', 0.99f) });
        SetSlot(p, 7, new[] { ('7', 0.23f) }, padProb: 0.12f);   // CANLI VAKA: hem hane hem pad eşik (0.3) ALTINDA
        return p;
    }

    [Fact]
    public void Dropping_a_weak_digit_must_not_inflate_confidence()
    {
        var probs = WeakLastDigitPlate();
        var full = TrConstrainedDecoder.Decode(probs, Alphabet, Slots, skipCharLevel: 0f);      // hane korunur
        var cut  = TrConstrainedDecoder.Decode(probs, Alphabet, Slots, skipCharLevel: 0.3f);   // hane düşer
        Assert.True(full.Ok && cut.Ok);
        Assert.Equal("25ACU927", full.Compact);
        Assert.Equal("25ACU92", cut.Compact);

        // ASIL İNVARYANT: bir haneyi DÜŞÜREN çözüm, o haneyi OKUYAN çözümden belirgin şekilde daha
        // güvenli görünemez. Eski hesapta zayıf slot ortalamadan çıkarıldığı için tam tersi oluyordu:
        // tam okuma 0.8228 iken kesik okuma 0.9871 (+0.164) — yani hatanın kendisi güven üretiyordu.
        // Bu şişme hem LowConfidence korumasını hem whitelist snap kapısını (0.99) devre dışı bırakıyordu.
        float inflation = cut.Confidence - full.Confidence;
        Assert.True(inflation < 0.10f,
            $"hane düşürmek güveni şişirmemeli: tam={full.Confidence:F4} kesik={cut.Confidence:F4} (+{inflation:F4})");
    }

    [Fact]
    public void Clean_full_length_plate_keeps_high_confidence()
    {
        // Tüm slotlar güçlü ve pad kütlesi yok → güven yüksek kalmalı (düzeltme temiz okumayı cezalandırmaz).
        var p = new float[Slots * Alphabet.Length];
        SetSlot(p, 0, new[] { ('3', 0.99f) });
        SetSlot(p, 1, new[] { ('4', 0.99f) });
        SetSlot(p, 2, new[] { ('A', 0.99f) });
        SetSlot(p, 3, new[] { ('B', 0.99f) });
        SetSlot(p, 4, new[] { ('C', 0.99f) });
        SetSlot(p, 5, new[] { ('1', 0.99f) });
        SetSlot(p, 6, new[] { ('2', 0.99f) });
        SetSlot(p, 7, new[] { ('3', 0.99f) });

        var r = TrConstrainedDecoder.Decode(p, Alphabet, Slots, skipCharLevel: 0.3f);
        Assert.True(r.Ok);
        Assert.Equal("34ABC123", r.Compact);
        Assert.True(r.Confidence > 0.95f, $"temiz plaka yüksek güvenli kalmalı, gelen: {r.Confidence:F4}");
    }

    [Fact]
    public void Genuine_short_plate_with_confident_pad_keeps_high_confidence()
    {
        // 7 haneli GERÇEK plaka: son slot kesin pad (model emin) → güven düşmemeli.
        var p = new float[Slots * Alphabet.Length];
        SetSlot(p, 0, new[] { ('2', 0.99f) });
        SetSlot(p, 1, new[] { ('5', 0.99f) });
        SetSlot(p, 2, new[] { ('A', 0.99f) });
        SetSlot(p, 3, new[] { ('B', 0.99f) });
        SetSlot(p, 4, new[] { ('1', 0.99f) });
        SetSlot(p, 5, new[] { ('2', 0.99f) });
        SetSlot(p, 6, new[] { ('3', 0.99f) });
        SetSlot(p, 7, Array.Empty<(char, float)>());   // tüm kütle pad'de → gerçekten 7 hane

        var r = TrConstrainedDecoder.Decode(p, Alphabet, Slots, skipCharLevel: 0.3f);
        Assert.True(r.Ok);
        Assert.Equal("25AB123", r.Compact);
        Assert.True(r.Confidence > 0.95f, $"pad'den emin kısa plaka cezalanmamalı, gelen: {r.Confidence:F4}");
    }
}
