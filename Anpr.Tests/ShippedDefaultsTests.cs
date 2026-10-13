using Anpr.Sdk.Configuration;

namespace Anpr.Tests;

/// <summary>
/// SEVK EDİLEN VARSAYILANLARIN KİLİDİ (2026-07-29 taşınabilirlik denetimi).
///
/// Denetimde ölçülen kusur: paketle gelen varsayılanlar, aylardır ÜRETİMDE DOĞRULANMIŞ
/// yapılandırmadan farklıydı. "Varsayılanlar yeterli" diyen müşteri, hiç doğrulanmamış bir sistem
/// çalıştırıyordu — ve bu farkı yakalayan TEK BİR TEST bile yoktu.
///
/// Bu testler değeri "doğru" diye kanıtlamaz; SESSİZCE DEĞİŞMESİNİ engeller. Bir değeri bilinçli
/// değiştirirken bu testi de güncelleyin ve gerekçeyi §P.4'e yazın.
/// </summary>
public class ShippedDefaultsTests
{
    private static AnprServiceOptions D() => new();

    [Fact]
    public void ManualReviewMinOcr_is_the_production_validated_value()
    {
        // 0,75 → 0,85 (2026-07-29). Ölçüm (1306 canlı roi olayı): 0,75-0,85 bandında 44 olay (%3,4)
        // vardı ve eski varsayılanla operatöre HİÇ düşmeden otomatik bariyer açıyorlardı. Bant, gözle
        // doğrulanmış yanlış okumalar içeriyor (25A48555 = gerçeği "25 A 4855", 25GE508, 25GC188).
        Assert.Equal(0.85f, D().ManualReviewMinOcr);
    }

    [Fact]
    public void KnownPlates_ships_empty_because_whitelist_is_site_specific()
    {
        // Whitelist SAHA verisidir, ürün özelliği değil. Kutudan çıkan üründe etkisi SIFIR olmalı;
        // ayrıca kamera-mutabakatıyla üretilen liste KORELE hatayı kanonlaştırabiliyor
        // (ölçülmüş vaka: 25ACV952 listede, gerçeği 25ACY952).
        Assert.Empty(D().KnownPlates);
    }

    [Fact]
    public void SnapToFrequent_stays_off_measured_net_negative_twice()
    {
        // İki bağımsız ölçümde de NET NEGATİF (2026-07-23: −7 · 2026-07-29: −8).
        Assert.False(D().SnapToFrequent);
    }

    [Fact]
    public void RequireApproaching_stays_off_direction_filter_unprovable_in_this_geometry()
        => Assert.False(D().RequireApproaching);

    [Fact]
    public void ConstrainedDecoding_is_on_but_only_safe_with_an_emit_floor()
    {
        // Gramer çözümleyicisi emit tabanı KAPALIYKEN zararlıdır: boş kırpıntıda plaka UYDURUR
        // (ölçüm: 532 kırpıntıda 32 negatifin 32'sinde hayali plaka, net −31). Emit tabanı açıkken
        // net +1…+3. Yani bu ikisi birbirine bağlıdır — biri açıksa diğeri de anlamlı olmalıdır.
        Assert.True(D().ConstrainedDecoding);
        Assert.True(D().EmitMinOcrConfidence > 0f,
            "ConstrainedDecoding açıkken emit tabanı 0 olamaz — gramer hayaleti geçitlenmez");
    }

    [Fact]
    public void CrossCameraPrefixGuard_ships_ON_at_the_measured_window()
    {
        // ÖLÇÜM (1766 canlı roi olayı): 5 çapraz-kamera kesilme vakası ve 5'i de bariyeri kesik
        // plakayla OTOMATİK açtı (kullanıcının bildirdiği 34ZH764 dahil). Yanlış-pozitif riski 0:
        // 11 ön-ek çiftinin 11'inde de kısa taraf tek seferlik, uzun taraf yerleşik.
        // Pencere: ±8 sn 3 vaka · ±15 sn 5 · ±30 sn yine 5 -> 15 doğal optimum.
        Assert.Equal(15.0, D().CrossCameraPrefixWindowSeconds);

        // AÇIK sevk edilmesinin gerekçesi EYLEMİN FAIL-SAFE olmasıdır: olay bastırılmaz ve plaka
        // DEĞİŞTİRİLMEZ, yalnız LowConfidence=true olur (bariyer otomatik açmaz, operatör karar verir).
        // Yanlış-pozitifin bedeli 1 operatör onayı; yanlış-negatifin bedeli var olmayan plakayla
        // açılan bariyer. 0 yazmak kapıyı tamamen kapatır.
        Assert.True(D().CrossCameraPrefixWindowSeconds > 0);

        // İkame kapısı da AÇIK sevk edilir ama AYRI knob'tur: ölçülmüş isabeti daha düşük
        // (kesilme 8/8 karşı-örneksiz · ikame 17/19 = %89, 2 karşı örnekli). Sahada gürültülü
        // bulunursa yalnız bu kapatılabilmeli.
        Assert.Equal(15.0, D().CrossCameraVariantWindowSeconds);
        Assert.True(D().CrossCameraVariantWindowSeconds > 0);
    }
}
