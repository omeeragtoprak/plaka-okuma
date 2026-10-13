using Anpr.Core;
using Anpr.Core.Confirmation;
using Anpr.Sdk.Events;

namespace Anpr.Tests;

/// <summary>
/// GEÇİŞ BAŞINA KARE ENSTRÜMANI (şema v8, §P.4 (425)).
/// <para><b>Neden var:</b> #69'un kadraj tavsiyesi bir TAKAS taşıyor — dar kadraj görüş süresini
/// kısaltır ve kare bütçesi 20 km/s'te 3,7'ye iner. Ama o tablo <b>varsayılan hızlarla</b>
/// hesaplandı; sahada geçiş başına gerçekte kaç kare işlendiği hiç ölçülmemişti ve
/// <c>docs/saha-fiziksel-gercekler.md</c> §4'te açıkça "ÖLÇÜLMEDİ" diye kayıtlıydı.</para>
/// <para>Sayı <c>PlateConfirmer.Track.Count</c> içinde ZATEN tutuluyordu; yalnız olaya
/// yazılmıyordu. Bu, yeni bir hesaplama değil, var olan bir sayacın görünür kılınmasıdır —
/// davranış DEĞİŞMEZ.</para>
/// </summary>
public class TrackFrameInstrumentTests
{
    private static PlateReading R(string text, float conf) => new()
    {
        Text = text,
        RawText = text.Replace(" ", ""),
        Confidence = conf,
        DetectorConfidence = conf,
        OcrConfidence = conf,
        Box = new PlateBox(0, 0, 10, 10),
        IsValidTr = true,
    };

    private static PlateConfirmer New(int minConf = 2, bool voting = false) => new(new PlateConfirmerOptions
    {
        DedupWindow = TimeSpan.FromSeconds(8),
        SettleWindow = TimeSpan.FromSeconds(3),
        MinConfirmations = minConf,
        CharacterVoting = voting,
    });

    [Fact]
    public void Frame_count_reports_how_many_times_the_plate_was_actually_seen()
    {
        var c = New(minConf: 2);
        var t = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        c.Observe(R("25 AFH 149", 0.90f), t);
        c.Observe(R("25 AFH 149", 0.92f), t.AddSeconds(1));
        c.Observe(R("25 AFH 149", 0.91f), t.AddSeconds(2));

        var ready = c.DrainReady(t.AddSeconds(2));

        var e = Assert.Single(ready);
        Assert.Equal(3, e.TrackFrames);
    }

    [Fact]
    public void Single_frame_pass_reports_one_not_null()
    {
        // 🔑 AYRIM: 1 ile null AYNI ŞEY DEĞİL. null = "alanı taşımayan eski şema", 1 = "gerçekten
        // tek kare". Bu ayrım kaybolursa "geçişlerin %X'i tek kare" ölçümü sessizce bozulur.
        var c = New(minConf: 1);
        var t = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        c.Observe(R("25 AFH 149", 0.90f), t);

        var e = Assert.Single(c.DrainReady(t));
        Assert.Equal(1, e.TrackFrames);
        Assert.NotNull(e.TrackFrames);
    }

    [Fact]
    public void Instrument_survives_the_NON_voting_path_too()
    {
        // 🔴 BU TESTİN VAR OLMA SEBEBİ: BuildResultCore'da ÜÇ ayrı `return` yolu var
        // (oylama kapalı · uzlaşı geçersiz · uzlaşı geçerli). v7'de tam bu desende ikinci snap
        // yolu enstrümansız kalabiliyordu ve HİÇBİR test düşmüyordu (SnapSupportInstrumentTests).
        // Bu yüzden alanlar tek tek değil, BuildResult SARMALAYICISINDA iliştiriliyor.
        var c = New(minConf: 2, voting: false);   // oylama KAPALI → erken return yolu
        var t = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        c.Observe(R("25 AFH 149", 0.90f), t);
        c.Observe(R("25 AFH 149", 0.95f), t.AddSeconds(1));

        var e = Assert.Single(c.DrainReady(t.AddSeconds(1)));
        Assert.Equal(2, e.TrackFrames);
    }

    [Fact]
    public void Absorbed_readings_are_counted_SEPARATELY_from_frames()
    {
        // İki sayaç ayrı tutulur: TrackFrames = plaka kaç kare görüldü, TrackAbsorbed = kaç AYRI
        // okuma bu ize soğuruldu. Birleştirilirse "3 kare mi, 1 kare + 2 soğurma mı?" ayırt edilemez.
        var c = New(minConf: 2);
        var t = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        c.Observe(R("25 AFH 149", 0.90f), t);
        c.Observe(R("25 AFH 149", 0.92f), t.AddSeconds(1));

        var e = Assert.Single(c.DrainReady(t.AddSeconds(1)));
        Assert.Equal(2, e.TrackFrames);
        Assert.NotNull(e.TrackAbsorbed);          // alan TAŞINIYOR (0 olabilir, null olmamalı)
    }

    [Fact]
    public void Schema_version_was_bumped_because_fields_were_ADDED()
    {
        // Eklemeli değişiklik → sürüm artar. Artırılmazsa PMS v7 sanıp yeni alanları hiç aramaz.
        Assert.Equal(8, PlateEvent.CurrentSchemaVersion);
    }

    [Fact]
    public void Schema_default_is_still_the_OLDEST_not_the_newest()
    {
        // §P.4 (2026-08-10) dersi: varsayılan "en yeni" olursa, alanı HİÇ taşımayan eski JSON
        // sessizce "en yeni şema" diye okunur. v8'de de varsayılan EN ESKİ kalmalı.
        Assert.Equal(1, new PlateEvent
        {
            Timestamp = DateTimeOffset.UnixEpoch, CameraId = "c", CameraName = "c",
            Plate = "25 ABC 123", PlateCompact = "25ABC123", RawText = "25ABC123",
            Confidence = 1, DetectorConfidence = 1, OcrConfidence = 1,
            Box = new PlateBox(0, 0, 1, 1),
        }.SchemaVersion);
    }
}
