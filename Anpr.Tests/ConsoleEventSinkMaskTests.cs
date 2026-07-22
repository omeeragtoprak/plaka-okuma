using Anpr.Sdk.Events;

namespace Anpr.Tests;

/// <summary>M8: KVKK log-plaka maskeleme (il + son 3 kalır); worker/runtime log yolları buradan geçer.</summary>
public class ConsoleEventSinkMaskTests
{
    [Theory]
    [InlineData("25ABC123", "25***123")]
    [InlineData("25 ABC 123", "25***123")]  // boşluklar temizlenir
    [InlineData("34XY99", "34***Y99")]       // 6 karakter -> maskelenir
    [InlineData("ABCD", "ABCD")]             // <=5 -> değişmez
    public void Mask_keeps_province_and_last3(string input, string expected)
        => Assert.Equal(expected, ConsoleEventSink.Mask(input));

    [Fact]
    public void MaskIf_respects_flag()
    {
        Assert.Equal("25***123", ConsoleEventSink.MaskIf(true, "25ABC123"));   // açık -> maskeli
        Assert.Equal("25ABC123", ConsoleEventSink.MaskIf(false, "25ABC123"));  // kapalı -> aynen
    }
}
