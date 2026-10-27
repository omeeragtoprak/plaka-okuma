using Anpr.Core;
using Anpr.Sdk.Events;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// `LowConfidenceReasons` (şema v4) — operatör onayı bayrağının GEREKÇESİ.
/// <para>
/// <b>Neden eklendi:</b> tek bir bool, isabetleri <b>%13 ile %89</b> arasında değişen üç ayrı nedeni
/// katlıyordu; operatör en verimli işi önce yapamıyordu. Ve gerekçe geriye dönük HESAPLANAMIYOR:
/// <c>Candidates</c> sırasız olduğu için <c>last-char-ambiguity</c> kuralı (aday[0] vs aday[1]) olay
/// günlüğünden yeniden üretilemez — kayıt anında yazılmazsa bilgi kalıcı olarak kaybolur.
/// </para>
/// <para>
/// <b>Geriye dönük uyumluluk kilidi:</b> aşağıdaki testler <c>LowConfidence</c> bool'unun anlamının
/// v4'te DEĞİŞMEDİĞİNİ de tutar — mevcut PMS entegrasyonları o alana bakıyor.
/// </para>
/// </summary>
public class LowConfidenceReasonTests
{
    private static PlateCandidate C(string p, float c) => new(p, c);

    // --- Kuralın kendisi (CameraWorker.HasAmbiguousLastChar) ---

    [Fact]
    public void Last_char_ambiguity_fires_only_on_a_one_char_prefix_pair_within_the_gap()
    {
        // "son karakter var mı yok mu": bir aday diğerinin TAM ön-eki ve tek karakter kısa olmalı.
        Assert.True(CameraWorker.HasAmbiguousLastChar(
            [C("25ABC12", 0.86f), C("25ABC120", 0.80f)], 0.10f));
        Assert.True(CameraWorker.HasAmbiguousLastChar(
            [C("25ABC120", 0.86f), C("25ABC12", 0.80f)], 0.10f));   // ters yön de
    }

    [Fact]
    public void Last_char_ambiguity_stays_silent_when_the_gap_is_wide_or_the_pair_is_not_a_prefix()
    {
        // Güven farkı büyükse kararsızlık YOKTUR — sistem seçimini biliyor.
        Assert.False(CameraWorker.HasAmbiguousLastChar(
            [C("25ABC12", 0.95f), C("25ABC120", 0.10f)], 0.10f));
        // İKAME (aynı uzunluk, farklı karakter) bu kuralın işi DEĞİL — çapraz-kamera kapısının işi.
        Assert.False(CameraWorker.HasAmbiguousLastChar(
            [C("25ABC123", 0.86f), C("25ABC124", 0.80f)], 0.10f));
        // İki karakter fark: ön-ek değil.
        Assert.False(CameraWorker.HasAmbiguousLastChar(
            [C("25ABC1", 0.86f), C("25ABC123", 0.80f)], 0.10f));
        // maxGap = 0 → kural kapalı (saha bunu kapatabilmeli).
        Assert.False(CameraWorker.HasAmbiguousLastChar(
            [C("25ABC12", 0.86f), C("25ABC120", 0.85f)], 0f));
    }

    [Fact]
    public void Rule_needs_at_least_two_candidates()
    {
        Assert.False(CameraWorker.HasAmbiguousLastChar(null, 0.10f));
        Assert.False(CameraWorker.HasAmbiguousLastChar([C("25ABC123", 0.9f)], 0.10f));
    }

    // --- Sözleşme: bayrak ile gerekçe listesi AYRILAMAZ ---

    /// <summary>ÜRETİLEN olayı temsil eder — üretim yolu gibi sürümü AÇIKÇA yazar (2026-08-10).</summary>
    private static PlateEvent Ev(params string[] reasons) => new()
    {
        SchemaVersion = PlateEvent.CurrentSchemaVersion,
        Timestamp = DateTimeOffset.UnixEpoch,
        CameraId = "giris", CameraName = "Giriş",
        Plate = "25 ABC 123", PlateCompact = "25ABC123", RawText = "25ABC123",
        Confidence = 0.9f, DetectorConfidence = 0.9f, OcrConfidence = 0.9f,
        Box = new PlateBox(0, 0, 10, 10),
        LowConfidence = reasons.Length > 0,
        LowConfidenceReasons = reasons,
    };

    [Fact]
    public void Flag_and_reasons_always_agree_for_v4_events()
    {
        // GERİYE DÖNÜK UYUMLULUK: mevcut PMS'ler LowConfidence bool'una bakıyor. v4 onun anlamını
        // DEĞİŞTİRMEZ; yalnız gerekçesini açar. Bu ilişki bozulursa entegrasyonlar sessizce sapar.
        Assert.False(Ev().LowConfidence);
        Assert.Empty(Ev().LowConfidenceReasons);

        foreach (var r in new[] { PlateEvent.ReasonWeakReading, PlateEvent.ReasonLastCharAmbiguity,
                                  PlateEvent.ReasonCrossCameraTruncation,
                                  PlateEvent.ReasonCrossCameraSubstitution })
        {
            var e = Ev(r);
            Assert.True(e.LowConfidence);
            Assert.Equal(e.LowConfidence, e.LowConfidenceReasons.Count > 0);
        }
    }

    [Fact]
    public void A_v3_event_may_carry_the_flag_with_an_empty_reason_list()
    {
        // SÖZLEŞME SINIRI — canlı doğrulamada bulundu (2026-08-03). Diskteki 3041 v3 olayın 270'i
        // LowConfidence=true taşıyor ama alan o dönemde yazılmadığı için listesi BOŞ gelir.
        // Geçmişi de okuyan bir PMS "bayrak sebepsiz" sonucuna varmamalı; SchemaVersion'a bakmalı.
        var v3 = Ev() with { SchemaVersion = 3, LowConfidence = true };
        Assert.True(v3.LowConfidence);
        Assert.Empty(v3.LowConfidenceReasons);          // eşitlik v3'te GEÇERLİ DEĞİL — bilinçli
        Assert.Equal(3, v3.SchemaVersion);              // ayırt edici alan bu
    }

    [Fact]
    public void Reasons_default_to_empty_so_old_construction_sites_stay_valid()
    {
        // Alan opsiyoneldir: v3 kodu derlenmeye ve çalışmaya devam eder.
        var e = new PlateEvent
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            CameraId = "c", CameraName = "C",
            Plate = "25 ABC 123", PlateCompact = "25ABC123", RawText = "25ABC123",
            Confidence = 1f, DetectorConfidence = 1f, OcrConfidence = 1f,
            Box = new PlateBox(0, 0, 1, 1),
        };
        Assert.NotNull(e.LowConfidenceReasons);
        Assert.Empty(e.LowConfidenceReasons);
        Assert.False(e.LowConfidence);
    }

    [Fact]
    public void Schema_version_was_bumped_for_the_new_field()
    {
        // Tüketici sürüme bakıp uyum sağlayabilsin (M5). Alan eklendi → sürüm artmalı.
        // Alan v4'te geldi; tam sürüm PlateEventTests'te kilitli (her artışta burayı kırmasın).
        Assert.True(PlateEvent.CurrentSchemaVersion >= 4);
        Assert.Equal(PlateEvent.CurrentSchemaVersion, Ev().SchemaVersion);
    }

    [Fact]
    public void Cross_camera_reason_is_listed_first_so_the_queue_can_be_sorted_by_value()
    {
        // Ölçülen isabet: çapraz-kamera ~%89, zayıf okuma ~%56, son-karakter %13.
        // Operatör listenin BAŞINA bakarak en verimli işi seçebilmeli.
        var e = Ev(PlateEvent.ReasonCrossCameraSubstitution, PlateEvent.ReasonWeakReading,
                   PlateEvent.ReasonLastCharAmbiguity);
        Assert.Equal(PlateEvent.ReasonCrossCameraSubstitution, e.LowConfidenceReasons[0]);
    }
}
