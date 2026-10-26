using Anpr.Core.TurkishPlates;

namespace Anpr.Tests;

public class TurkishPlateNormalizerTests
{
    [Theory]
    // Temiz, doğru okumalar (3 kombinasyon tipi)
    [InlineData("34A1234", "34 A 1234")]     // 1 harf -> 4 rakam
    [InlineData("34A12345", "34 A 12345")]   // 1 harf -> 5 rakam
    [InlineData("34AB123", "34 AB 123")]     // 2 harf -> 3 rakam
    [InlineData("34AB1234", "34 AB 1234")]   // 2 harf -> 4 rakam
    [InlineData("34ABC12", "34 ABC 12")]     // 3 harf -> 2 rakam
    [InlineData("34ABC123", "34 ABC 123")]   // 3 harf -> 3 rakam
    [InlineData("06BC345", "06 BC 345")]     // Ankara
    [InlineData("01K1234", "01 K 1234")]     // il 01
    [InlineData("81Z9999", "81 Z 9999")]     // il 81 (son geçerli il)
    // Boşluk/biçim toleransı
    [InlineData("34 abc 123", "34 ABC 123")]
    [InlineData("34-AB-123", "34 AB 123")]
    public void Valid_plates_are_normalized(string raw, string expected)
    {
        var r = TurkishPlateNormalizer.Normalize(raw);
        Assert.True(r.IsValid, $"'{raw}' geçerli olmalıydı");
        Assert.Equal(expected, r.Normalized);
    }

    [Theory]
    // İl kodu pozisyonunda harf->rakam düzeltmesi (O->0)
    [InlineData("O6BC345", "06 BC 345")]
    // Rakam bölgesinde harf->rakam (O->0)
    [InlineData("34AB12O", "34 AB 120")]
    // Harf bölgesinde rakam->harf (0->O)
    [InlineData("3400C12", "34 OOC 12")]
    public void Ocr_confusions_are_corrected_in_position(string raw, string expected)
    {
        var r = TurkishPlateNormalizer.Normalize(raw);
        Assert.True(r.IsValid, $"'{raw}' düzeltme ile geçerli olmalıydı");
        Assert.Equal(expected, r.Normalized);
        Assert.True(r.Corrections > 0);
    }

    [Theory]
    [InlineData("")]            // boş
    [InlineData("ABC")]         // çok kısa
    [InlineData("UD1234")]      // il kodu yok (örnek araç plakası - TR değil)
    [InlineData("00ABC12")]     // il 00 geçersiz
    [InlineData("82ABC12")]     // il 82 geçersiz (>81)
    [InlineData("99XY123")]     // il 99 geçersiz
    [InlineData("34QWE12")]     // W harfi TR'de yok
    [InlineData("34ABX12")]     // X harfi TR'de yok
    [InlineData("ZZZZZZZ")]     // çöp
    [InlineData("341234567")]   // çok uzun / harf yok
    [InlineData("3412345")]     // tamamen sayısal (harf bloğu uydurulamaz)
    [InlineData("1111111")]     // tamamen sayısal
    [InlineData("34123456")]    // tamamen sayısal (8 hane)
    [InlineData("3403456")]     // tamamen sayısal (0->O ile harf uydurulamaz)
    public void Invalid_or_garbage_is_rejected(string raw)
    {
        var r = TurkishPlateNormalizer.Normalize(raw);
        Assert.False(r.IsValid, $"'{raw}' reddedilmeliydi ama '{r.Normalized}' döndü");
    }

    [Fact]
    public void Q_is_corrected_to_O_in_letter_block()
    {
        // Q TR'de yok; neredeyse her zaman yanlış okunan O'dur.
        var r = TurkishPlateNormalizer.Normalize("34QBC12");
        Assert.True(r.IsValid);
        Assert.Equal("34 OBC 12", r.Normalized);
    }

    [Fact]
    public void Compact_form_has_no_spaces()
    {
        var r = TurkishPlateNormalizer.Normalize("34 ABC 123");
        Assert.True(r.IsValid);
        Assert.Equal("34ABC123", r.Compact);
        Assert.Equal(34, r.Province);
        Assert.Equal("ABC", r.Letters);
        Assert.Equal("123", r.Numbers);
    }
}
