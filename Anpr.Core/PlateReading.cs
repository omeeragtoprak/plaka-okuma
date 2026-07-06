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

    public override string ToString() =>
        $"{Text} (conf={Confidence:F2}, det={DetectorConfidence:F2}, ocr={OcrConfidence:F2}, tr={IsValidTr})";
}
