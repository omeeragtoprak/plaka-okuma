using Anpr.Sdk.Configuration;
using Anpr.Core;
using Anpr.Sdk.Events;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// SNAP DESTEK ENSTRÜMANI (şema v7, §P.4 (366)).
/// <para><b>Neden var:</b> snap, aday listesini yeniden yazıp hedefi başa koyar ve hedefe EŞİT olan
/// özgün adayı <b>eler</b>. Bu belgelenmiş bir sözleşmedir (<c>Candidates[0] == PlateCompact</c>,
/// PMS böyle okur) ama bir ÖLÇÜM BOŞLUĞU doğuruyordu: *"OCR'ın kendisi bu düzeltmeyi destekliyor
/// muydu, kaçıncı sıradaydı?"* sorusu olaydan yanıtlanamıyordu.</para>
/// <para>Ölçüldü (2026-08-22): 536 canlı snap olayının <b>536'sında</b> hedef 1., kaynak 2. sırada —
/// yani saklanan liste snap SONRASI durumu yansıtıyor ve snap kararı için bağımsız kanıt DEĞİL.
/// Bu, #78'in (mali sınıfta net −21) tam ortasındaki bir körlüktü.</para>
/// </summary>
public class SnapSupportInstrumentTests
{
    private static List<PlateCandidate> C(params (string p, float c)[] xs) =>
        xs.Select(x => new PlateCandidate(x.p, x.c)).ToList();

    [Fact]
    public void Target_that_the_model_itself_ranked_is_reported_with_rank_and_its_OWN_score()
    {
        // Model hedefi 2. sırada 0,81 ile düşünmüş → snap modele AYKIRI değil.
        var (cands, rank, ocr) = CameraWorker.RebuildCandidatesForSnap(
            C(("25ACV952", 0.90f), ("25ACY952", 0.81f), ("25ACV953", 0.30f)), "25ACY952", 0.90f);

        Assert.Equal(2, rank);
        Assert.Equal(0.81f, ocr);
        // sözleşme yine korunur: hedef başta ve ÖDÜNÇ güvenle
        Assert.Equal("25ACY952", cands[0].Compact);
        Assert.Equal(0.90f, cands[0].Confidence);
        // ve özgün kopya elenir (liste tek kez içerir)
        Assert.Single(cands, c => c.Compact == "25ACY952");
    }

    [Fact]
    public void Target_outside_the_models_topK_reports_null_which_means_UNSEEN_not_impossible()
    {
        // 🔑 Bu ayrım belgeye de yazıldı: null = "ilk-K içinde değildi", "olasılık sıfır" DEĞİL.
        // Yokluk kanıtı değil, kapsam sınırı — aksi hâlde bu alan yanlış okunur.
        var (cands, rank, ocr) = CameraWorker.RebuildCandidatesForSnap(
            C(("25ACV952", 0.90f), ("25ACV953", 0.30f)), "25ACY952", 0.90f);

        Assert.Null(rank);
        Assert.Null(ocr);
        Assert.Equal("25ACY952", cands[0].Compact);
        Assert.Equal(3, cands.Count);           // hedef + iki özgün aday
    }

    [Fact]
    public void Borrowed_confidence_and_the_models_own_score_are_kept_SEPARATE()
    {
        // (365)'in çekirdeği: Candidates[0].Confidence ÖDÜNÇTÜR (ham okumanın skoru).
        // Enstrümanın değeri, ikisinin FARKINI görünür kılmasında — 0,99 ödünç güvenle
        // yayınlanan bir snap'in model skoru 0,05 olabilir ve bunu ancak burada görürüz.
        var (cands, _, ocr) = CameraWorker.RebuildCandidatesForSnap(
            C(("25ACV952", 0.99f), ("25ACY952", 0.05f)), "25ACY952", 0.99f);

        Assert.Equal(0.99f, cands[0].Confidence);   // ödünç
        Assert.Equal(0.05f, ocr);                   // modelin kendi inancı
        Assert.NotEqual(cands[0].Confidence, ocr!.Value);
    }

    [Fact]
    public void No_candidate_list_does_not_throw_and_reports_nothing()
    {
        var (cands, rank, ocr) = CameraWorker.RebuildCandidatesForSnap(null, "25ACY952", 0.7f);
        Assert.Single(cands);
        Assert.Equal("25ACY952", cands[0].Compact);
        Assert.Null(rank);
        Assert.Null(ocr);
    }

    [Fact]
    public void Curated_correction_path_carries_the_instrument_onto_the_reading()
    {
        // İki snap yolu var (küratörlü tam-eşleşme + whitelist/müdavim) ve desen eskiden
        // İKİ YERDE KOPYALIYDI. Ortak yardımcıya çekildi; bu test küratörlü yolun
        // enstrümanı gerçekten taşıdığını sabitler.
        var o = new AnprServiceOptions();
        o.PlateCorrections["25ACV952"] = "25 ACY 952";
        var map = PlateCorrectionMap.Build(o, out _);
        var reading = new PlateReading
        {
            Text = "25 ACV 952", RawText = "25ACV952", Confidence = 0.9f,
            DetectorConfidence = 0.9f, OcrConfidence = 0.93f,
            Box = new PlateBox(0, 0, 10, 4), IsValidTr = true,
            Candidates = C(("25ACV952", 0.93f), ("25ACY952", 0.44f)),
        };

        var fix = CameraWorker.ApplyCuratedCorrection(reading, "25ACV952", map);

        Assert.True(fix.Applied);
        Assert.Equal(2, fix.Reading.SnapTargetRank);
        Assert.Equal(0.44f, fix.Reading.SnapTargetOcr);
        Assert.Equal(0.93f, fix.Reading.OcrConfidence);   // ödünç skor DEĞİŞMEDİ (365)
    }

    [Fact]
    public void Whitelist_snap_path_ALSO_carries_the_instrument()
    {
        // 🔴 BU TESTİN VAR OLMA SEBEBİ: kırmızı-yeşil denemesinde bu yolda enstrümanı null'a
        // sabitlemek HİÇBİR testi düşürmedi — yani ikinci snap yolu ölçümsüz kalabilir ve kimse
        // fark etmezdi. Mantık satır-içiydi ve kopyalanmış bir desendi; saf fonksiyona çıkarıldı.
        var reg = new FrequentPlateRegistry
        {
            KnownPlates = FrequentPlateRegistry.NormalizeKnownPlates(new[] { "25 ACY 952" }),
        };
        var reading = new PlateReading
        {
            Text = "25 ACV 952", RawText = "25ACV952", Confidence = 0.7f,
            DetectorConfidence = 0.8f, OcrConfidence = 0.70f,
            Box = new PlateBox(0, 0, 10, 4), IsValidTr = true,
            Candidates = C(("25ACV952", 0.70f), ("25ACX952", 0.31f), ("25ACY952", 0.22f)),
        };

        var snap = CameraWorker.ApplyFrequentSnap(reading, "25ACV952", reg, maxLev: 1, snapToFrequent: false);

        Assert.True(snap.Applied);
        Assert.Equal("25ACY952", snap.Compact);
        Assert.Equal("25ACV952", snap.SnappedFrom);
        Assert.Equal(3, snap.Reading.SnapTargetRank);      // model hedefi 3. sırada düşünmüş
        Assert.Equal(0.22f, snap.Reading.SnapTargetOcr);   // ve yalnız 0,22 vermiş
        Assert.Equal(0.70f, snap.Reading.OcrConfidence);   // yayınlanan skor ÖDÜNÇ, değişmedi
        Assert.Equal("25ACY952", snap.Reading.Candidates![0].Compact);
    }

    [Fact]
    public void Whitelist_snap_is_a_no_op_when_nothing_is_near()
    {
        var reg = new FrequentPlateRegistry
        {
            KnownPlates = FrequentPlateRegistry.NormalizeKnownPlates(new[] { "34 XYZ 999" }),
        };
        var reading = new PlateReading
        {
            Text = "25 ABC 123", RawText = "25ABC123", Confidence = 0.9f,
            DetectorConfidence = 0.9f, OcrConfidence = 0.6f,
            Box = new PlateBox(0, 0, 10, 4), IsValidTr = true,
        };
        var snap = CameraWorker.ApplyFrequentSnap(reading, "25ABC123", reg, maxLev: 1, snapToFrequent: false);
        Assert.False(snap.Applied);
        Assert.Null(snap.SnappedFrom);
        Assert.Null(snap.Reading.SnapTargetRank);
    }

    [Fact]
    public void Unsnapped_reading_carries_no_instrument()
    {
        // Alanlar SNAP'e özgüdür; snapsız olayda null kalmalı, yoksa tüketici "snap oldu" sanır.
        var reading = new PlateReading
        {
            Text = "25 ABC 123", RawText = "25ABC123", Confidence = 0.9f,
            DetectorConfidence = 0.9f, OcrConfidence = 0.99f,
            Box = new PlateBox(0, 0, 10, 4), IsValidTr = true,
        };
        Assert.Null(reading.SnapTargetRank);
        Assert.Null(reading.SnapTargetOcr);

        var noop = CameraWorker.ApplyCuratedCorrection(reading, "25ABC123", PlateCorrectionMap.Empty);
        Assert.False(noop.Applied);
        Assert.Null(noop.Reading.SnapTargetRank);
    }

    [Fact]
    public void Schema_version_was_bumped_because_fields_were_ADDED()
    {
        // Eklemeli değişiklik → sürüm artar. Artırılmazsa tüketici v6 sanıp yeni alanları
        // hiç aramaz; sürüm alanının varlık sebebi tam olarak bu sessiz kırılmayı önlemektir.
        // Alanlar v7'de geldi; tam sürüm PlateEventTests'te kilitli.
        Assert.True(PlateEvent.CurrentSchemaVersion >= 7);
    }

    [Fact]
    public void Schema_default_is_still_the_OLDEST_not_the_newest()
    {
        // §P.4 (2026-08-10) dersi: varsayılan "en yeni" olursa, alanı HİÇ taşımayan eski JSON
        // sessizce "en yeni şema" diye okunur. Varsayılan en eski kalmalı.
        Assert.Equal(1, new PlateEvent
        {
            Timestamp = DateTimeOffset.UnixEpoch, CameraId = "c", CameraName = "c",
            Plate = "25 ABC 123", PlateCompact = "25ABC123", RawText = "25ABC123",
            Confidence = 1, DetectorConfidence = 1, OcrConfidence = 1,
            Box = new PlateBox(0, 0, 1, 1),
        }.SchemaVersion);
    }
}
