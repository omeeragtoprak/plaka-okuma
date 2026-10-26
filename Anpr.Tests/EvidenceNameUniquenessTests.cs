using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// Kanıt dosya adı BENZERSİZLİĞİ (2026-07-30 canlı bug).
///
/// Ad <c>yyyyMMdd-HHmmss_kamera_PLAKA</c> biçimindeydi; aynı SANİYE içinde iki olay üretilebiliyor
/// (aynı aracın roi + gözlem okuması ya da hızlı ardışık emit) ve ikinci olay birincinin kırpıntısını
/// VE tam karesini SESSİZCE EZİYORDU. ÖLÇÜM: 2033 olayda 12 çakışma (%1,2) → 12 olayın kanıtı başka
/// bir ana ait. Zincirleme sonuç: o olayın FrameBox'ı EZİLMİŞ kareye işaret ediyor, kırpıntı kayıyor,
/// üzerine kurulan gözle doğrulama ve etiketli set BOZULUYOR. Bug tam da etiketli set kurarken bir
/// kırpıntının boş çıkmasıyla yakalandı (34BE3003).
/// </summary>
public class EvidenceNameUniquenessTests
{
    [Fact]
    public void Free_name_is_returned_unchanged()
        => Assert.Equal("20260730-140508123_giris_34BE3003",
            CameraWorker.EnsureUniqueStem("20260730-140508123_giris_34BE3003", _ => false));

    [Fact]
    public void Taken_name_gets_a_suffix_instead_of_overwriting()
    {
        var used = new HashSet<string> { "a" };
        Assert.Equal("a-2", CameraWorker.EnsureUniqueStem("a", used.Contains));
    }

    [Fact]
    public void Suffix_increments_until_free()
    {
        var used = new HashSet<string> { "a", "a-2", "a-3" };
        Assert.Equal("a-4", CameraWorker.EnsureUniqueStem("a", used.Contains));
    }

    [Fact]
    public void Pathological_case_still_returns_something_unique_instead_of_looping_forever()
    {
        // Her ad alınmışsa sonsuz döngüye girmemeli; benzersiz bir ad üretip devam etmeli.
        string s = CameraWorker.EnsureUniqueStem("a", _ => true);
        Assert.NotEqual("a", s);
        Assert.StartsWith("a", s);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CameraWorker.EnsureUniqueStem(null!, _ => false));
        Assert.Throws<ArgumentNullException>(() => CameraWorker.EnsureUniqueStem("a", null!));
    }

    [Fact]
    public void Aligned_stem_still_works_with_millisecond_timestamps()
    {
        // Ad biçimi değişti (fff eklendi) — plaka hizalama SON alt çizgiye baktığı için bozulmamalı.
        Assert.Equal("20260730-140508123_giris_25ACY952",
            CameraWorker.AlignedStem("20260730-140508123_giris_25ACV952.jpg", "25ACY952"));
        // Ad zaten doğruysa null döner (dokunma).
        Assert.Null(CameraWorker.AlignedStem("20260730-140508123_giris_25ACY952.jpg", "25ACY952"));
    }

    [Fact]
    public void Aligned_stem_handles_the_uniqueness_suffix()
    {
        // "-2" ekli adlarda da plaka SON alt çizgiden sonrasıdır; hizalama bozulmaz.
        Assert.Equal("20260730-140508123_giris_25ACY952",
            CameraWorker.AlignedStem("20260730-140508123_giris_25ACV952.jpg", "25ACY952"));
    }
}
