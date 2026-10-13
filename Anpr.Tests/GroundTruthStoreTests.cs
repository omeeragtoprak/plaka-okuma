using Anpr.Service.Panel;

namespace Anpr.Tests;

/// <summary>
/// Yer-gerçeği kalıcılığı + KANIT KORUMASI. Kanıt dizini EvidenceMaxFiles(6000) ile budanıyor ve
/// ölçülen efektif saklama ~2,6 gün — yani doğrulanan kaydın görseli silinir ve insan emeği
/// buharlaşır. Verdict yazılırken görsel korumalı dizine kopyalanır; bu dizin aynı zamanda kalıcı
/// ETİKETLİ TEST SETİ olur (eski dataset/{live1h,s1,s2} setlerinin silinmesi tekrarlanmasın).
/// </summary>
public class GroundTruthStoreTests : IDisposable
{
    private readonly string _dir;

    public GroundTruthStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "anpr-gt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_dir, "evidence"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string MakeEvidence(string name)
    {
        string rel = Path.Combine("evidence", name);
        File.WriteAllBytes(Path.Combine(_dir, rel), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        return rel.Replace('\\', '/');
    }

    private static GroundTruthEntry Entry(string? evidence, string ours, GroundTruthVerdict v,
                                          string? truth = null, float ocr = 0.99f)
        => new(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "giris", evidence, ours, truth, v, ocr);

    [Fact]
    public void Record_persists_across_reload()
    {
        var e = Entry(null, "25ABC123", GroundTruthVerdict.Dogru);
        new GroundTruthStore(_dir).Record(e);

        var reopened = new GroundTruthStore(_dir);
        Assert.True(reopened.IsVerified(e.EventId));
        Assert.Single(reopened.All());
    }

    [Fact]
    public void Re_verifying_the_same_event_overwrites_instead_of_double_counting()
    {
        var s = new GroundTruthStore(_dir);
        var id = Guid.NewGuid();
        s.Record(new GroundTruthEntry(DateTimeOffset.UnixEpoch, id, "giris", null, "25ABC123", null,
                                      GroundTruthVerdict.Dogru, 0.99f));
        s.Record(new GroundTruthEntry(DateTimeOffset.UnixEpoch, id, "giris", null, "25ABC123", "25ABC124",
                                      GroundTruthVerdict.Yanlis, 0.99f));
        // Aksi hâlde aynı olay hem doğru hem yanlış sayılıp doğruluk oranını bozardı.
        Assert.Single(s.All());
        Assert.Equal(GroundTruthVerdict.Yanlis, s.All()[0].Verdict);
    }

    [Fact]
    public void Correct_verdict_archives_evidence_under_our_reading()
    {
        string ev = MakeEvidence("20260730-100000_giris_25ABC123.jpg");
        var s = new GroundTruthStore(_dir);
        s.Record(Entry(ev, "25ABC123", GroundTruthVerdict.Dogru));

        string expect = Path.Combine(s.ArchiveRoot, "25ABC123", "20260730-100000_giris_25ABC123.jpg");
        Assert.True(File.Exists(expect), "doğru okuma kendi etiketi altına arşivlenmeli");
    }

    [Fact]
    public void Wrong_verdict_archives_under_the_TRUE_plate_not_our_misread()
    {
        // Etiketli set için kritik: dosya GERÇEK plakanın altında olmalı, yoksa set yanlış etiketlenir
        // ve gelecekteki A/B ölçümleri hatayı "doğru" öğrenir.
        string ev = MakeEvidence("20260730-101000_giris_25ACV952.jpg");
        var s = new GroundTruthStore(_dir);
        s.Record(Entry(ev, "25ACV952", GroundTruthVerdict.Yanlis, truth: "25ACY952"));

        Assert.True(File.Exists(Path.Combine(s.ArchiveRoot, "25ACY952", "20260730-101000_giris_25ACV952.jpg")));
        Assert.False(Directory.Exists(Path.Combine(s.ArchiveRoot, "25ACV952")));
    }

    [Fact]
    public void Unreadable_goes_to_its_own_bucket_not_a_fake_plate_label()
    {
        string ev = MakeEvidence("20260730-102000_giris_25AV104.jpg");
        var s = new GroundTruthStore(_dir);
        s.Record(Entry(ev, "25AV104", GroundTruthVerdict.Okunamadi, ocr: 0.81f));

        Assert.True(File.Exists(Path.Combine(s.ArchiveRoot, "_okunamadi", "20260730-102000_giris_25AV104.jpg")));
        Assert.False(Directory.Exists(Path.Combine(s.ArchiveRoot, "25AV104")),
            "okunamayan kayıt plaka etiketi altına konulmamalı — set kirlenir");
    }

    [Fact]
    public void Full_frame_is_archived_too_because_crop_bugs_need_it()
    {
        // 34ZH7641 kesilme vakası YALNIZ tam kareden çözülebildi; kırpıntı tek başına yetmez.
        string ev = MakeEvidence("20260730-103000_giris_34ZH7641.jpg");
        MakeEvidence("20260730-103000_giris_34ZH7641_full.jpg");
        var s = new GroundTruthStore(_dir);
        s.Record(Entry(ev, "34ZH7641", GroundTruthVerdict.Dogru));

        var dir = Path.Combine(s.ArchiveRoot, "34ZH7641");
        Assert.True(File.Exists(Path.Combine(dir, "20260730-103000_giris_34ZH7641.jpg")));
        Assert.True(File.Exists(Path.Combine(dir, "20260730-103000_giris_34ZH7641_full.jpg")));
    }

    [Fact]
    public void Verdict_survives_even_when_the_evidence_file_is_already_gone()
    {
        // Budayıcı görseli silmiş olabilir (yarış). Arşivleme başarısızlığı verdict'i KAYBETTİRMEMELİ.
        var s = new GroundTruthStore(_dir);
        var e = Entry("evidence/silinmis.jpg", "25ABC123", GroundTruthVerdict.Dogru);
        s.Record(e);
        Assert.True(s.IsVerified(e.EventId));
        Assert.Single(new GroundTruthStore(_dir).All());
    }

    [Fact]
    public void VerifiedPerBand_counts_by_confidence_band_for_stratified_sampling()
    {
        var s = new GroundTruthStore(_dir);
        s.Record(Entry(null, "25ABC123", GroundTruthVerdict.Dogru, ocr: 0.999f));
        s.Record(Entry(null, "25ABC124", GroundTruthVerdict.Dogru, ocr: 0.999f));
        s.Record(Entry(null, "25ABC125", GroundTruthVerdict.Dogru, ocr: 0.80f));

        var per = s.VerifiedPerBand();
        Assert.Equal(2, per[ConfBand.CokYuksek]);
        Assert.Equal(1, per[ConfBand.Dusuk]);
    }

    [Fact]
    public void Corrupt_line_does_not_destroy_the_rest_of_the_history()
    {
        var s = new GroundTruthStore(_dir);
        s.Record(Entry(null, "25ABC123", GroundTruthVerdict.Dogru));
        File.AppendAllText(Path.Combine(_dir, "ground-truth.jsonl"), "{bozuk json" + Environment.NewLine);
        s.Record(Entry(null, "25ABC124", GroundTruthVerdict.Dogru));

        Assert.Equal(2, new GroundTruthStore(_dir).All().Count);
    }
}
