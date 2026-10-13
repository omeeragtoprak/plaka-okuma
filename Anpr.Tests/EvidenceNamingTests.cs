using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// Kanıt dosya adı hizalaması (2026-07-28 denetiminde bulundu): SaveEvidence füzyon/snap'ten ÖNCE
/// çağrıldığı için dosya adı DÜZELTME ÖNCESİ plakayı taşıyordu — canlı ölçüm: snap uygulanan 10 olayın
/// 10'unda dosya adı ≠ olay plakası. Operatör dizinde plakaya göre arayınca kaydı bulamıyordu.
/// </summary>
public class EvidenceNamingTests
{
    [Fact]
    public void Renames_when_snap_changed_the_plate()
    {
        // Canlı vaka: dosya 25EB648 ile yazıldı, snap 25EB646'ya düzeltti.
        var stem = CameraWorker.AlignedStem("evidence/20260727-172028_giris_25EB648.jpg", "25EB646");
        Assert.Equal("20260727-172028_giris_25EB646", stem);
    }

    [Fact]
    public void Returns_null_when_already_aligned()
    {
        // Snap yoksa ad zaten doğru → dosya taşıma YAPILMAMALI (gereksiz I/O + çakışma riski).
        Assert.Null(CameraWorker.AlignedStem("evidence/20260727-170132_giris_25ADT274.jpg", "25ADT274"));
    }

    [Fact]
    public void Preserves_timestamp_and_camera_parts()
    {
        // Yalnız SON alt çizgiden sonrası (plaka) değişmeli; zaman damgası + kamera kimliği korunmalı.
        var stem = CameraWorker.AlignedStem("evidence/20260728-063551_cikis_25DL602.jpg", "25DL502");
        Assert.StartsWith("20260728-063551_cikis_", stem);
        Assert.EndsWith("_25DL502", stem);
    }

    [Theory]
    [InlineData("evidence/bozukisim.jpg", "25ABC123")]   // alt çizgi yok → dokunma
    [InlineData("", "25ABC123")]
    [InlineData("evidence/20260727-170132_giris_25ADT274.jpg", "")]   // nihai plaka boş → dokunma
    public void Refuses_to_rename_on_unexpected_input(string path, string final)
    {
        Assert.Null(CameraWorker.AlignedStem(path, final));
    }

    [Fact]
    public void Handles_shorter_snapped_plate()
    {
        // Snap karakter EKLEYEBİLİR de (06DKM58 -> 06DKM581, canlı vaka) — uzunluk değişimi sorun olmamalı.
        var stem = CameraWorker.AlignedStem("evidence/20260727-170818_giris_06DKM58.jpg", "06DKM581");
        Assert.Equal("20260727-170818_giris_06DKM581", stem);
    }
}
