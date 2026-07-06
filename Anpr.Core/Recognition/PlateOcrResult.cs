namespace Anpr.Core.Recognition;

/// <summary>
/// OCR sonucu: ham metin + ortalama güven + slot-bazlı TAM olasılık dağılımı (uzunluk = slots × vocab).
/// SlotProbs, karakter oylaması ve TR gramer-kısıtlı çözümleme için kullanılır.
/// </summary>
public sealed record PlateOcrResult(string Text, float MeanConfidence, float[] SlotProbs, string Alphabet);
