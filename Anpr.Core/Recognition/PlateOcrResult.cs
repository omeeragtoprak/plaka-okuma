namespace Anpr.Core.Recognition;

/// <summary>OCR sonucu: ham metin + ortalama güven + slot-bazlı tahminler (karakter oylaması için).</summary>
public sealed record PlateOcrResult(string Text, float MeanConfidence, IReadOnlyList<SlotPrediction> Slots);
