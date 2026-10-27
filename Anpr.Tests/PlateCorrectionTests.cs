using Anpr.Core;
using Anpr.Sdk.Configuration;
using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// TAM-DİZGİ DÜZELTME HARİTASI. Neden var (2026-07-31, canlı ölçüm):
/// <para><c>25 ACY 952</c> plakalı araç sistematik olarak <c>25ACV952</c> okunuyor — <b>39 olay,
/// 39'u da bariyeri OTOMATİK açtı</b>, güvenler 0,852–0,999 (27'si ≥0,99). Var olmayan bir plaka.
/// Mevcut hiçbir koruma yakalayamıyor:</para>
/// <list type="number">
/// <item>GÜVEN yakalayamaz — hata 0,999 güvenle geliyor (korele hata; model emin ve YANLIŞ).</item>
/// <item>KAMERA ÇAPRAZI yakalayamaz — Hikvision da AYNI yönde yanılıyor (mutabakat %100, doğruluk %0).</item>
/// <item>BULANIK SNAP yakalayamaz — <c>SnapMaxOcrConfidence=0,99</c> kapısı tam da bu okumaları geçiriyor,
///       ve kapıyı kaldırmak ÖLÇÜLDÜ: lev1 komşuya yanlış snap 1→11 (2026-07-23).</item>
/// </list>
/// <para>ÇÖZÜM ve NEDEN GÜVENLİ: bulanık snap'in riski "lev1 komşusuna kayma"dır; TAM-DİZGİ eşleşme
/// bu riski yapısal olarak taşımaz — yalnız küratörlü, kanıt görselinden doğrulanmış tek bir dizgide
/// tetiklenir. Bu yüzden güven kapısı UYGULANMAZ (kapı yanlış aracın korumasıydı).</para>
/// </summary>
public class PlateCorrectionTests
{
    private static AnprServiceOptions O(params (string From, string To)[] pairs)
    {
        var o = new AnprServiceOptions();
        foreach (var p in pairs) o.PlateCorrections[p.From] = p.To;
        return o;
    }

    [Fact]
    public void Shipped_default_is_EMPTY_so_the_product_behaves_identically_out_of_the_box()
        => Assert.Empty(new AnprServiceOptions().PlateCorrections);

    [Fact]
    public void The_measured_live_case_is_corrected_regardless_of_confidence()
    {
        // 0,999 = canlıda ölçülen en yüksek yanlış güven. Güven kapısı olsaydı bu düzeltilemezdi.
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25ACY952")), out _);
        Assert.Equal("25ACY952", map.Apply("25ACV952"));
    }

    [Fact]
    public void Only_the_EXACT_string_is_corrected_neighbours_are_untouched()
    {
        // Bulanık snap'ten farkı budur: lev1 komşuya KAYMAZ. 25ACV953 başka bir araç olabilir.
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25ACY952")), out _);
        Assert.Equal("25ACV953", map.Apply("25ACV953"));
        Assert.Equal("25ACV95", map.Apply("25ACV95"));
        Assert.Equal("5ACV952", map.Apply("5ACV952"));
    }

    [Fact]
    public void Input_is_normalised_so_config_may_be_written_with_spaces_or_lowercase()
    {
        var map = PlateCorrectionMap.Build(O(("25 acv 952", "25 ACY 952")), out _);
        Assert.Equal("25ACY952", map.Apply("25ACV952"));
    }

    [Fact]
    public void A_target_that_is_not_a_valid_TR_plate_is_REFUSED_not_silently_applied()
    {
        // Aksi hâlde config'e yazılan bir yazım hatası bariyeri geçersiz plakayla açardı.
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25QQQ952")), out var problems);  // Q TR'de yok
        Assert.Equal("25ACV952", map.Apply("25ACV952"));
        Assert.Contains(problems, p => p.Contains("geçerli TR plakası değil", StringComparison.Ordinal));
    }

    [Fact]
    public void A_correction_that_contradicts_the_whitelist_is_REFUSED_and_reported()
    {
        // Aynı dizgi hem "bilinen doğru plaka" hem "kanıtlanmış yanlış okuma" olamaz. Sessizce
        // birini seçmek, hangisinin kazandığını config'i yazan kişiden GİZLERDİ.
        var o = O(("25ACV952", "25ACY952"));
        o.KnownPlates.Add("25 ACV 952");
        var map = PlateCorrectionMap.Build(o, out var problems);
        Assert.Equal("25ACV952", map.Apply("25ACV952"));
        Assert.Contains(problems, p => p.Contains("whitelist ile ÇELİŞİYOR", StringComparison.Ordinal));
    }

    [Fact]
    public void Self_mapping_is_refused_it_would_be_a_silent_no_op()
    {
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25 ACV 952")), out var problems);
        Assert.Empty(map.Entries);
        Assert.Contains(problems, p => p.Contains("kendine", StringComparison.Ordinal));
    }

    [Fact]
    public void A_chain_is_refused_because_apply_order_would_be_undefined()
    {
        // A→B ve B→C: tek geçişte hangi sonucun çıkacağı sözlük sırasına bağlı olurdu.
        var map = PlateCorrectionMap.Build(O(("25ABC123", "25ABC124"), ("25ABC124", "25ABC125")), out var problems);
        Assert.Contains(problems, p => p.Contains("zincir", StringComparison.Ordinal));
        Assert.Empty(map.Entries);
    }

    [Fact]
    public void Empty_map_is_a_pure_pass_through()
    {
        var map = PlateCorrectionMap.Build(new AnprServiceOptions(), out var problems);
        Assert.Empty(problems);
        Assert.Equal("25ABC123", map.Apply("25ABC123"));
        Assert.False(map.HasEntries);
    }

    [Fact]
    public void Blank_entries_are_ignored_without_throwing()
    {
        var o = new AnprServiceOptions();
        o.PlateCorrections["  "] = "25ABC123";
        o.PlateCorrections["25ABC124"] = "";
        var map = PlateCorrectionMap.Build(o, out _);
        Assert.Empty(map.Entries);
    }

    // --- MOTORA BAĞLANMA (yalnız harita mantığı değil, EMIT yolunda uygulanması) ---
    private static PlateReading R(string text, float ocr = 0.997f, params string[] cands)
        => new()
        {
            Text = text, RawText = text.Replace(" ", ""), Confidence = ocr * 0.8f,
            DetectorConfidence = 0.8f, OcrConfidence = ocr,
            Box = new PlateBox(10, 10, 140, 55), IsValidTr = true,
            Candidates = cands.Length == 0 ? null
                       : cands.Select(c => new PlateCandidate(c, ocr)).ToList(),
        };

    [Fact]
    public void Emit_path_applies_the_correction_and_leaves_an_audit_trail()
    {
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25 ACY 952")), out _);
        var (applied, reading, compact, from) =
            CameraWorker.ApplyCuratedCorrection(R("25 ACV 952"), "25ACV952", map);

        Assert.True(applied);
        Assert.Equal("25 ACY 952", reading.Text);
        Assert.Equal("25ACY952", compact);
        // SnappedFrom olmadan düzeltme olayda İZSİZ kalır — canlı denetim imkânsızlaşır.
        Assert.Equal("25ACV952", from);
    }

    [Fact]
    public void Emit_path_does_NOT_apply_a_confidence_gate()
    {
        // Bütün mesele bu: canlıda hata 0,999 güvenle geliyordu ve her güven kapısı onu geçiriyordu.
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25 ACY 952")), out _);
        foreach (float ocr in new[] { 0.50f, 0.85f, 0.99f, 0.999f, 1.0f })
            Assert.True(CameraWorker.ApplyCuratedCorrection(R("25 ACV 952", ocr), "25ACV952", map).Applied,
                        $"ocr={ocr} düzeltilmedi");
    }

    [Fact]
    public void Emit_path_keeps_the_documented_invariant_Candidates0_equals_the_emitted_plate()
    {
        // PMS aday kümesini bu sözleşmeye göre okur (README'de belgeli, canlı 505/505 doğrulandı).
        // Düzeltilmiş plaka başa alınmazsa olay KENDİ aday listesiyle çelişirdi.
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25 ACY 952")), out _);
        var (_, reading, compact, _) = CameraWorker.ApplyCuratedCorrection(
            R("25 ACV 952", 0.997f, "25ACV952", "25ACV953"), "25ACV952", map);

        Assert.Equal(compact, reading.Candidates![0].Compact);
        Assert.DoesNotContain(reading.Candidates, c => c.Compact == "25ACY952" && c != reading.Candidates[0]);
        Assert.Contains(reading.Candidates, c => c.Compact == "25ACV953");   // diğer adaylar korunur
    }

    [Fact]
    public void Emit_path_LEAVES_confidence_untouched_so_the_score_describes_the_PRE_snap_string()
    {
        // 🔴 SÖZLEŞMEYİ KİLİTLEYEN TEST (§P.4 (365)). Snap metni değiştirir ama OcrConfidence'a
        // DOKUNMAZ — model snap edilmiş dizgiyi hiç skorlamamıştır. Bu davranışın kendisi
        // savunulabilir (elde başka skor yok), ama BELGESİ yanlıştı: alan "OKUNAN METNİN güveni"
        // diyordu ve tüketici onu yayınlanan plakaya duyulan güven sanıyordu.
        // Test davranışı sabitler ki belge ile kod bir daha ayrışmasın.
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25 ACY 952")), out _);
        var (_, reading, compact, from) =
            CameraWorker.ApplyCuratedCorrection(R("25 ACV 952", 0.997f), "25ACV952", map);

        Assert.Equal("25ACY952", compact);
        Assert.Equal("25ACV952", from);
        Assert.Equal(0.997f, reading.OcrConfidence);          // DEĞİŞMEDİ: skor '25ACV952'ye ait
        // ve aynı ödünç güven aday listesinin başına da yazılır
        Assert.Equal(0.997f, reading.Candidates![0].Confidence);
    }

    [Fact]
    public void Emit_path_DROPS_the_pre_snap_candidate_equal_to_the_target_so_support_is_unmeasurable()
    {
        // Ölçüldü (2026-08-22): 536 canlı snap olayının 536'sında hedef 1., kaynak 2. sırada.
        // Sebep burada: hedefe EŞİT olan özgün aday eleniyor → "OCR'ın kendisi bu düzeltmeyi
        // destekliyor muydu, kaçıncı sıradaydı?" sorusu olaydan ARTIK yanıtlanamaz.
        // Bu bir hata değil, ÖLÇÜM BOŞLUĞU; #78 kararı için önemli olduğu için teste yazıldı.
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25 ACY 952")), out _);
        var (_, reading, _, _) = CameraWorker.ApplyCuratedCorrection(
            R("25 ACV 952", 0.90f, "25ACV952", "25ACY952", "25ACV953"), "25ACV952", map);

        // hedef listede TEK KEZ ve yalnız 0. sırada var; özgün kopyası elenmiş
        Assert.Single(reading.Candidates!, c => c.Compact == "25ACY952");
        Assert.Equal("25ACY952", reading.Candidates![0].Compact);
    }

    [Fact]
    public void Emit_path_is_a_no_op_with_the_shipped_empty_map()
    {
        var (applied, reading, compact, from) = CameraWorker.ApplyCuratedCorrection(
            R("25 ABC 123"), "25ABC123", PlateCorrectionMap.Empty);
        Assert.False(applied);
        Assert.Equal("25 ABC 123", reading.Text);
        Assert.Equal("25ABC123", compact);
        Assert.Null(from);
    }

    [Fact]
    public void Emit_path_leaves_every_plate_that_is_not_in_the_map_untouched()
    {
        // Haritada olmayan HİÇBİR okuma etkilenmez — düzeltme yalnız küratörlü dizgide tetiklenir.
        // (Geçersiz HEDEF senaryosu burada test EDİLEMEZ: Build() onu zaten reddediyor, dolayısıyla
        //  ApplyCuratedCorrection içindeki ikinci Normalize kontrolü erişilemez bir derinlik savunması.)
        var map = PlateCorrectionMap.Build(O(("25ACV952", "25 ACY 952")), out _);
        foreach (string p in new[] { "34XYZ111", "25ACV953", "25ACY952", "25ACV95" })
            Assert.False(CameraWorker.ApplyCuratedCorrection(R("x"), p, map).Applied, $"{p} değişti");
    }

    [Fact]
    public void Two_wrong_readings_may_map_to_the_SAME_true_plate()
    {
        // Canlı: aynı araç hem 25ZCG011 hem 25ZG924 okunmuştu (ikisi de 25TGG001).
        var map = PlateCorrectionMap.Build(O(("25ZCG011", "25TGG001"), ("25ZG924", "25TGG001")), out var problems);
        Assert.Empty(problems);
        Assert.Equal("25TGG001", map.Apply("25ZCG011"));
        Assert.Equal("25TGG001", map.Apply("25ZG924"));
    }
}
