using System.Text.Json;
using Anpr.Core;
using Anpr.Sdk.Events;

namespace Anpr.Tests;

/// <summary>M5: olay şema sürümü (webhook/jsonl tüketicileri sürümü kontrol edebilsin) + snap denetim alanı.</summary>
public class PlateEventTests
{
    /// <summary>ÜRETİLEN bir olayı temsil eder — üretim yolu gibi sürümü AÇIKÇA yazar.
    /// (Varsayılana bırakmak artık 1 verir; bu bilinçli, bkz. <c>Absent_SchemaVersion…</c> testi.)</summary>
    private static PlateEvent Base() => new()
    {
        SchemaVersion = PlateEvent.CurrentSchemaVersion,
        Timestamp = DateTimeOffset.UnixEpoch,
        CameraId = "giris", CameraName = "Giriş",
        Plate = "25 ABC 123", PlateCompact = "25ABC123", RawText = "25ABC123",
        Confidence = 0.9f, DetectorConfidence = 0.9f, OcrConfidence = 0.9f,
        Box = new PlateBox(0, 0, 10, 10),
    };

    [Fact]
    public void SchemaVersion_is_7_after_SnapTarget_fields_added()
    {
        // Şema değişince bump edilir (BİLİNÇLİ KIRILMA): bu testin kırmızıya dönmesi, alan
        // ekleyen kişiye sürümü unutmadığını hatırlatır. v2 SnappedFrom · v3 FrameBox ·
        // v4 LowConfidenceReasons · v5 EarlyEmitHeldAtOcr · v6 EarlyEmitHeldAs ·
        // v7 SnapTargetRank + SnapTargetOcr. Hiçbir alan kaldırılmadı, hiçbir anlam değişmedi.
        Assert.Equal(8, PlateEvent.CurrentSchemaVersion);
        Assert.Equal(8, Base().SchemaVersion);          // üretim yolu bu sabiti yazar
    }

    [Fact]
    public void Absent_SchemaVersion_reads_as_oldest_not_newest()
    {
        // 🔴 DÜZELTİLEN KUSUR (2026-08-10): alan M5'te eklendi; ondan ÖNCEKİ JSON'da (yayınlanmış
        // v1.0.1 dönemi) hiç YOK. Varsayılan 5 iken böyle bir belge sessizce "EN YENİ şema" diye
        // okunuyordu — oysa LowConfidenceReasons gibi alanları hiç taşımıyor. Tüketicinin
        // `SchemaVersion >= 4` kontrolü YANLIŞ POZİTİF veriyordu: tam olarak sürüm alanının
        // önlemesi gereken sessiz kırılmanın ta kendisi.
        //
        // ⚠️ Bu test SADECE varsayılanı değil, DESERİALİZASYONU sınar: derleme zamanı varsayılanı
        // doğru olup JSON yolu farklı davranabilirdi (alan yoksa init'in çalışıp çalışmaması).
        string legacy = System.Text.Json.JsonSerializer.Serialize(Base());
        legacy = System.Text.Json.Nodes.JsonNode.Parse(legacy)!.AsObject() is var o
                 && o.Remove("SchemaVersion") ? o.ToJsonString() : legacy;
        Assert.DoesNotContain("SchemaVersion", legacy);

        var back = System.Text.Json.JsonSerializer.Deserialize<PlateEvent>(legacy)!;
        Assert.Equal(1, back.SchemaVersion);                       // EN ESKİ, en yeni DEĞİL
        Assert.True(back.SchemaVersion < 4);                       // tüketicinin muhafazakâr dalı
    }

    [Fact]
    public void LowConfidenceReasons_survive_the_json_wire_format()
    {
        // KABLOLAMA DOĞRULAMASI: yeni alan events.jsonl ve webhook'a GERÇEKTEN yazılıyor mu?
        // Her ikisi de düz JsonSerializer.Serialize(e) kullanıyor; alan sessizce düşerse gerekçe
        // kaybolur ve bunu kimse fark etmez (v4'ün TÜM değeri bu alanın kalıcı olmasında).
        var e = Base() with
        {
            LowConfidence = true,
            LowConfidenceReasons = new[] { PlateEvent.ReasonCrossCameraSubstitution,
                                           PlateEvent.ReasonWeakReading },
        };
        string json = System.Text.Json.JsonSerializer.Serialize(e);

        Assert.Contains("cross-camera-substitution", json);      // ham telde okunabilir dizgi
        Assert.Contains("\"SchemaVersion\":8", json.Replace(" ", ""));

        var back = System.Text.Json.JsonSerializer.Deserialize<PlateEvent>(json)!;
        Assert.Equal(2, back.LowConfidenceReasons.Count);
        Assert.Equal(PlateEvent.ReasonCrossCameraSubstitution, back.LowConfidenceReasons[0]);
        Assert.True(back.LowConfidence);
    }

    [Fact]
    public void A_v3_json_without_the_new_field_still_deserialises()
    {
        // GERİYE DÖNÜK: diskte v3 döneminden 3000+ satır var; okuyucu onları kırılmadan almalı.
        // ⚠️ 2026-08-10'da düzeltildi: bu test "v3 belgesi" diyor ama belgeyi Base()'ten üretiyordu
        // ve Base() sürümü 5 yazıyor — yani sınanan şey v3 DEĞİL, "alanı silinmiş bir v5"ti.
        // Sürüm de 3'e çekildi ki test adı ile sınadığı belge AYNI şeyi anlatsın.
        string v3 = System.Text.Json.JsonSerializer.Serialize(
            Base() with { SchemaVersion = 3, LowConfidence = true });
        v3 = v3.Replace(",\"LowConfidenceReasons\":[]", "");
        Assert.DoesNotContain("LowConfidenceReasons", v3);
        Assert.Contains("\"SchemaVersion\":3", v3.Replace(" ", ""));

        var back = System.Text.Json.JsonSerializer.Deserialize<PlateEvent>(v3)!;
        Assert.Equal(3, back.SchemaVersion);
        Assert.NotNull(back.LowConfidenceReasons);               // null DEĞİL, boş
        Assert.Empty(back.LowConfidenceReasons);
        Assert.True(back.LowConfidence);                         // eski bayrak hâlâ okunuyor
    }

    [Fact]
    public void SnappedFrom_is_null_when_no_snap_applied()
    {
        // Snap uygulanmayan olayda alan null olmalı — "snap oldu mu" sorusu bu alanla yanıtlanır.
        Assert.Null(Base().SnappedFrom);
    }

    [Fact]
    public void SnappedFrom_survives_json_roundtrip_for_pms_consumers()
    {
        // PMS/denetim events.jsonl + webhook üzerinden okur: alan serileşmezse denetim kanıtı kaybolur.
        var e = Base() with { PlateCompact = "25TGG001", SnappedFrom = "25ZGG001" };
        var back = JsonSerializer.Deserialize<PlateEvent>(JsonSerializer.Serialize(e))!;
        Assert.Equal("25ZGG001", back.SnappedFrom);
        Assert.Equal("25TGG001", back.PlateCompact);
        Assert.Equal(8, back.SchemaVersion);
    }

    [Fact]
    public void SnappedFrom_differs_from_final_plate_so_correction_is_auditable()
    {
        // Alanın tek işlevi: düzeltmenin İKİ ucunu da görünür kılmak (öncesi + sonrası).
        var e = Base() with { PlateCompact = "25AFH151", SnappedFrom = "25AFH150" };
        Assert.NotEqual(e.SnappedFrom, e.PlateCompact);
    }

    // --- v3: FrameBox (2026-07-29) — kaydedilen kareye AİT kutu ---
    // Ölçüm: olayların %24,3'ünde Box, FramePath'teki kareye ait değildi (Box=best.Box farklı andan,
    // FramePath=bucket.BestFrameJpeg). Bu yüzden "kırpıntı plakayı kesiyor mu" sorusu olay kaydından
    // yanıtlanamıyordu — tam da 34ZH7641 kesilme vakasını teşhis etmeyi zorlaştıran şey buydu.

    [Fact]
    public void FrameBox_is_null_when_no_frame_saved()
        => Assert.Null(Base().FrameBox);

    [Fact]
    public void FrameBox_survives_json_roundtrip_for_field_auditing()
    {
        var e = Base() with { FramePath = "evidence/x_full.jpg", FrameBox = new PlateBox(1276, 558, 119, 43) };
        var back = JsonSerializer.Deserialize<PlateEvent>(JsonSerializer.Serialize(e))!;
        Assert.NotNull(back.FrameBox);
        Assert.Equal(1276, back.FrameBox!.Value.X);
        Assert.Equal(119, back.FrameBox!.Value.Width);
    }

    [Fact]
    public void FrameBox_may_differ_from_Box_that_is_the_whole_point()
    {
        // İkisi AYNI olmak zorunda değil; ayrı olmaları kusur değil, alanın varlık sebebidir.
        var e = Base() with { Box = new PlateBox(100, 100, 150, 60), FrameBox = new PlateBox(1276, 558, 119, 43) };
        Assert.NotEqual(e.Box, e.FrameBox!.Value);
    }
}
