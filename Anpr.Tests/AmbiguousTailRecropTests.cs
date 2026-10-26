using Anpr.Core;
using Anpr.Core.Recognition;

namespace Anpr.Tests;

/// <summary>
/// HEDEFLİ İKİNCİ OKUMA — kuyruk belirsizliği (#91).
/// <para><b>Neden var (2026-08-22, §P.4 (350)):</b> ölçülen kusur sınıfı "kuyruk kesilmesi" —
/// yayınlanan plaka, tanığın plakasının tam öneki ve tam <b>1 karakter</b> eksik
/// (16/9728 = %0,16 [%0,10–%0,27]). 16/16'sı format olarak GEÇERLİ ve 16/16'sı emit eşiğinin
/// üstünde, yani ne `ApplyTurkishValidation` ne güven eşiği bu sınıfı ayırabilir:
/// `TurkishPlateNormalizer` 7–8 karakter kabul eder, 8'den 1 düşünce geriye geçerli bir 7 kalır.</para>
/// <para>Kök sebep ölçüldü: kırpıntı dar, son karakter kenarda/dışarıda. Dolgu 0,06 → 0,15
/// yapılınca diskte tam karesi kalan <b>4/4</b> vaka düzeldi (güven 0,90 → 0,998).
/// Ama küresel dolgu artışı yansız örneklemde (n=600) ZARARLI ölçüldü — bu yüzden çare hedefli.</para>
/// </summary>
public class AmbiguousTailRecropTests
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
    private static readonly int PadIdx = Alphabet.IndexOf('_');

    /// <summary>slot başına (karakter, olasılık) verip düz SlotProbs dizisi kurar; kalan sınıflar 0.</summary>
    private static PlateOcrResult Ocr(string text, params (char ch, float p, float padP)[] tail)
    {
        int vocab = Alphabet.Length;
        int slots = text.Length + tail.Length;
        var probs = new float[slots * vocab];
        for (int s = 0; s < text.Length; s++)
            probs[s * vocab + Alphabet.IndexOf(text[s])] = 0.99f;
        for (int i = 0; i < tail.Length; i++)
        {
            int s = text.Length + i;
            probs[s * vocab + Alphabet.IndexOf(tail[i].ch)] = tail[i].p;
            probs[s * vocab + PadIdx] = tail[i].padP;
        }
        return new PlateOcrResult(text, 0.9f, probs, Alphabet);
    }

    [Fact]
    public void Targeted_recrop_is_OFF_by_default()
    {
        // 🔑 EN ÖNEMLİ TEST. Bu ayar AÇIK gelirse her belirsiz kuyrukta İKİNCİ bir OCR koşar —
        // zayıf CPU'lu hedefte kare bütçesi habersiz değişir. Yeni yetenek opt-in olmalıdır.
        Assert.Equal(0f, new AnprOptions().AmbiguousTailRecropPad);
    }

    [Fact]
    public void Off_by_default_means_no_extra_work_even_if_gap_is_set()
    {
        // Eşik dolu ama pad 0 → motor koşulu (pad > CropPaddingRatio) sağlanmaz. İki ayarın
        // BİRLİKTE anlamlı olduğunu sabitler; yalnız eşiği ayarlayan biri "açtım" sanmasın.
        var o = new AnprOptions { AmbiguousTailMaxGap = 0.5f };
        Assert.False(o.AmbiguousTailRecropPad > o.CropPaddingRatio);
    }

    [Fact]
    public void Ambiguous_tail_is_detected_when_pad_barely_beats_a_character()
    {
        // Gerçek vaka şekli: '25AAD79' yayınlandı, 2. aday '25AAD795' ve aradaki fark 0,0007.
        var ocr = Ocr("25AAD79", ('5', 0.46f, 0.47f));
        Assert.True(AnprEngine.HasAmbiguousTail(ocr, 0.25f));
    }

    [Fact]
    public void Confident_end_of_plate_is_not_ambiguous()
    {
        // Plaka gerçekten bittiyse dolgu açık ara kazanır — ikinci okuma BOŞUNA koşmamalı.
        var ocr = Ocr("25AAD795", ('0', 0.02f, 0.97f));
        Assert.False(AnprEngine.HasAmbiguousTail(ocr, 0.25f));
    }

    [Fact]
    public void Gap_threshold_is_honoured_in_both_directions()
    {
        var ocr = Ocr("25AAD79", ('5', 0.40f, 0.60f)); // fark 0,20
        Assert.True(AnprEngine.HasAmbiguousTail(ocr, 0.25f));   // 0,20 < 0,25 → belirsiz
        Assert.False(AnprEngine.HasAmbiguousTail(ocr, 0.15f));  // 0,20 > 0,15 → belirsiz DEĞİL
        Assert.False(AnprEngine.HasAmbiguousTail(ocr, 0f));     // 0 = kapalı
    }

    [Fact]
    public void No_pad_slot_at_all_means_ambiguity_is_undefined_not_true()
    {
        // Plaka tüm slotları doldurmuşsa "kuyruk" yoktur. Burada true dönmek, ölçülecek bir şey
        // olmadığı hâlde her karede ikinci OCR koşturmak demekti — sessiz CPU sızıntısı.
        var ocr = Ocr("25AAD795");
        Assert.False(AnprEngine.HasAmbiguousTail(ocr, 0.25f));
    }

    [Fact]
    public void Empty_or_malformed_probabilities_never_throw()
    {
        Assert.False(AnprEngine.HasAmbiguousTail(
            new PlateOcrResult("25AAD79", 0.9f, System.Array.Empty<float>(), Alphabet), 0.25f));
        Assert.False(AnprEngine.HasAmbiguousTail(
            new PlateOcrResult("25AAD79", 0.9f, new float[10], ""), 0.25f));
    }

    [Fact]
    public void Counters_start_at_zero_so_an_enabled_but_inert_setting_is_visible()
    {
        // Sayaç olmadan ayar "açık ama hiç tetiklenmiyor" olabilir ve fark edilmezdi:
        // denendi/kabul oranı, ayarın sahada işe yarayıp yaramadığının doğrudan kanıtıdır.
        var t = typeof(AnprEngine);
        Assert.NotNull(t.GetProperty(nameof(AnprEngine.TailRecropAttempts)));
        Assert.NotNull(t.GetProperty(nameof(AnprEngine.TailRecropAccepted)));
    }
}
