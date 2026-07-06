using Anpr.Core;

namespace Anpr.Service.Events;

/// <summary>
/// Geçerli ve yeni bir plaka okumasından üretilen olay. Web app / veritabanı bu modeli tüketir.
/// </summary>
public sealed record PlateEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string CameraId { get; init; }
    public required string CameraName { get; init; }

    /// <summary>Normalize edilmiş plaka, ör. "25 ABC 123".</summary>
    public required string Plate { get; init; }

    /// <summary>Boşluksuz plaka, ör. "25ABC123" (eşleştirme/sorgu için).</summary>
    public required string PlateCompact { get; init; }

    /// <summary>OCR'ın ham çıktısı (normalizasyon öncesi).</summary>
    public required string RawText { get; init; }

    public required float Confidence { get; init; }
    public required float DetectorConfidence { get; init; }
    public required float OcrConfidence { get; init; }
    public required PlateBox Box { get; init; }
}
