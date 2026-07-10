using Anpr.Service.Panel;

namespace Anpr.Tests;

public class PanelTests
{
    [Fact]
    public void RoiStore_roundtrip_and_fallback()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var store = new RoiStore(dir);
        Assert.Equal("varsayilan", store.LoadOrDefault("giris", "varsayilan")); // dosya yok -> fallback
        store.Save("giris", "10,10;990,10;500,900");
        Assert.Equal("10,10;990,10;500,900", store.LoadOrDefault("giris", "x"));
        store.Save("giris", ""); // temizleme kalıcı: fallback'e DÖNMEMELİ
        Assert.Equal("", store.LoadOrDefault("giris", "x"));
    }

    [Fact]
    public void Poller_ParseRecords_extracts_valid_plates_with_time()
    {
        string xml = """
            <Plates><Plate><captureTime>20260706T152030+0300</captureTime><plateNumber>25ABC123</plateNumber></Plate>
            <Plate><captureTime>20260706T152031+0300</captureTime><plateNumber>unknown</plateNumber></Plate>
            <Plate><captureTime>bozukzaman</captureTime><plateNumber>34XYZ99</plateNumber></Plate></Plates>
            """;
        var recs = CameraRecordPoller.ParseRecords(xml);
        Assert.Single(recs); // unknown ve bozuk-zaman elendi
        Assert.Equal("25ABC123", recs[0].Plate);
        Assert.Equal(new DateTimeOffset(2026, 7, 6, 15, 20, 30, TimeSpan.FromHours(3)), recs[0].CaptureTime);
    }
}

public class FrequentPlateRegistryTests
{
    [Fact]
    public void Becomes_frequent_after_min_sightings_high_conf_only()
    {
        var r = new Anpr.Service.Events.FrequentPlateRegistry { MinSightings = 3 };
        for (int i = 0; i < 2; i++) r.Record("25FZ069", 0.99f);
        r.Record("25FZ069", 0.80f);            // düşük güven sayılmaz
        Assert.False(r.IsFrequent("25FZ069"));
        r.Record("25FZ069", 1.0f);
        Assert.True(r.IsFrequent("25FZ069"));
        Assert.False(r.IsFrequent("25FZ289")); // varyant müdavim değil
    }
}
