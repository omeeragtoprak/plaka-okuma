namespace Anpr.Core;

/// <summary>
/// Eksenleri görüntü piksel uzayında olan sınırlayıcı kutu (sol-üst köşe + genişlik/yükseklik).
/// </summary>
public readonly record struct PlateBox(int X, int Y, int Width, int Height);


/// <summary>
/// Tek bir plaka okuması. Anpr.Core'un dışarıya verdiği temel sonuç tipidir.
/// </summary>
public sealed record PlateReading
{
    /// <summary>Normalize edilmiş plaka metni (ör. "25 ABC 123"). TR doğrulaması geçmediyse ham okuma döner.</summary>
    public required string Text { get; init; }

    /// <summary>OCR modelinin ürettiği ham metin (normalize/düzeltme öncesi).</summary>
    public required string RawText { get; init; }

    /// <summary>Birleşik güven skoru (dedektör skoru * OCR skoru), 0..1.</summary>
    public required float Confidence { get; init; }

    /// <summary>Dedektörün plaka kutusu için güven skoru, 0..1.</summary>
    public required float DetectorConfidence { get; init; }

    /// <summary>OCR'ın ortalama karakter güveni, 0..1.</summary>
    public required float OcrConfidence { get; init; }

    /// <summary>Plakanın görüntüdeki konumu.</summary>
    public required PlateBox Box { get; init; }

    /// <summary>Türkiye plaka formatına uyup uymadığı.</summary>
    public required bool IsValidTr { get; init; }

    /// <summary>
    /// OCR'ın slot-bazlı TAM olasılık dağılımı (uzunluk = slot × vocab, satır-öncelikli: slot sonra vocab).
    /// Karakter-bazlı oylama + gramer-kısıtlı çözümleme için. Bilinmiyorsa null.
    /// </summary>
    public float[]? SlotProbs { get; init; }

    /// <summary>OCR alfabesi (SlotProbs'taki vocab sırası). SlotProbs doluysa dolu olur.</summary>
    public string? OcrAlphabet { get; init; }

    /// <summary>
    /// Tespit merkezi ROI içinde mi? ROI tanımsızsa daima true. false = "gözlem" okuması:
    /// kapsam dışı (cadde/uzak) trafik — bariyer kararına girmez, karşılaştırma/izleme için yayınlanır.
    /// </summary>
    public bool InZone { get; init; } = true;

    /// <summary>
    /// Uzlaşı çözümlemesinin en iyi K GEÇERLİ adayı (skora göre; ilki Text ile aynı). Whitelist
    /// toleransı + emit-sonrası varyant bastırma için. Yalnız kısıtlı-çözümlemeli uzlaşı emitlerinde dolu.
    /// </summary>
    public IReadOnlyList<PlateCandidate>? Candidates { get; init; }

    /// <summary>
    /// Yön göstergesi: izin ömrü boyunca plaka kutusu ALANININ eğilimi = son-üçte-bir ortalama /
    /// erken-üçte-bir ortalama. &gt;1 aracın YAKLAŞTIĞINI (plaka büyüyor), &lt;1 UZAKLAŞTIĞINI gösterir.
    /// -1 = yeterli kanıt yok (az örnek / kısa iz) → karar verilemez, "yaklaşıyor say". Yalnız uzlaşı
    /// (BuildResult) çıktısında anlamlıdır.
    /// </summary>
    public float SizeTrend { get; init; } = -1f;

    public override string ToString() =>
        $"{Text} (conf={Confidence:F2}, det={DetectorConfidence:F2}, ocr={OcrConfidence:F2}, tr={IsValidTr})";
}

/// <summary>Alternatif plaka okuması adayı (boşluksuz metin + güven).</summary>
public sealed record PlateCandidate(string Compact, float Confidence)
{
    public override string ToString() => $"{Compact}:{Confidence:F2}";
}
