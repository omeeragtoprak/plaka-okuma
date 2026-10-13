using Anpr.Sdk.Configuration;

namespace Anpr.Tests;

/// <summary>
/// SEÇİCİ KORUMA (#75). Bayraklı olayın kanıtı, ADET tavanının erken tahliyesinden muaf tutulur.
/// <para><b>Neden var — bedeli ölçüldü (2026-08-07):</b> bir kapı kararına (#76) doğrudan bağlı
/// 91 olayın <b>20'si (%19,8)</b> kanıtı silindiği için kalıcı olarak doğrulanamaz hâle gelmişti;
/// silinenler rastgele değil popülasyonun <b>en eskileriydi</b>. Kaybın nedeni gün sınırı DEĞİL
/// adet tavanıydı (30 gün ayarlıyken efektif saklama 4,5 gün ölçüldü).</para>
/// </summary>
public class EvidencePreservationTests
{
    [Fact]
    public void Preservation_is_OFF_by_default()
    {
        // 🔑 EN ÖNEMLİ TEST. Koruma varsayılan olarak AÇILIRSA, mevcut kurulumlarda disk kullanımı
        // ve KVKK yüzeyi HABERSİZ değişir. Yeni yetenek opt-in olmalıdır.
        Assert.False(new AnprServiceOptions().EvidencePreserveFlagged);
    }

    [Fact]
    public void Preserved_area_has_a_bounded_default_quota()
    {
        // Koruma "sonsuz sakla" DEĞİLDİR: korunan alanın da kendi tavanı olmalı, yoksa KVKK
        // açısından sınırsız birikim doğar.
        Assert.True(new AnprServiceOptions().EvidencePreserveMaxFiles > 0);
    }

    [Fact]
    public void A_zero_quota_with_preservation_ON_is_rejected()
    {
        // Sessiz başarısızlık tuzağı: bayrak açık ama tavan 0 → hiçbir şey korunmaz, oysa
        // yapılandırmaya bakan "koruma var" sanır. Doğrulama bunu YAKALAMALI.
        var o = new AnprServiceOptions { EvidencePreserveFlagged = true, EvidencePreserveMaxFiles = 0 };
        o.Cameras.Add(new CameraOptions { Id = "a", Name = "A", RtspUrl = "rtsp://x" });
        var ex = Assert.Throws<InvalidOperationException>(() => o.Validate());
        Assert.Contains("EvidencePreserveMaxFiles", ex.Message);
    }

    [Fact]
    public void A_zero_quota_with_preservation_OFF_is_fine()
    {
        // Kapalıyken tavan anlamsızdır; gereksiz yere hata vermek yapılandırmayı zorlaştırır.
        var o = new AnprServiceOptions { EvidencePreserveFlagged = false, EvidencePreserveMaxFiles = 0 };
        o.Cameras.Add(new CameraOptions { Id = "a", Name = "A", RtspUrl = "rtsp://x" });
        o.Validate();
    }

    [Fact]
    public void Preservation_does_NOT_extend_the_KVKK_day_limit()
    {
        // SÖZLEŞME: koruma yalnız ADET tavanından muafiyettir. Gün kesimi tek bir ayardan gelir
        // (EvidenceRetentionDays) ve korunan alana da AYNEN uygulanır — ayrı bir "koruma günü"
        // ayarı YOKTUR. Böyle bir ayar eklenirse bu test onu görünür kılar ve karar bilinçli olur.
        var t = typeof(AnprServiceOptions);
        Assert.Null(t.GetProperty("EvidencePreserveRetentionDays"));
        Assert.NotNull(t.GetProperty("EvidenceRetentionDays"));
    }
}
