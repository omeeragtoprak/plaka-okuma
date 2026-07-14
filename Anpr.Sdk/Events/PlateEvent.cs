using Anpr.Core;

namespace Anpr.Sdk.Events;

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

    /// <summary>
    /// Olay kapsamı: "roi" = tetik bölgesi içi (bariyer/otopark yazılımına gider);
    /// "gozlem" = ROI dışı cadde/uzak okuma (yalnız izleme/karşılaştırma — karar üretmez).
    /// </summary>
    public string Scope { get; init; } = "roi";

    /// <summary>Kanıt kırpıntısının (yalnız PLAKA) servis-köküne göre yolu (ör. "evidence/xxx.jpg"). Yoksa null.</summary>
    public string? EvidencePath { get; init; }

    /// <summary>
    /// En iyi okumanın geldiği TAM KARE (aracın da göründüğü bütün sahne) yolu, ör.
    /// "evidence/xxx_full.jpg". Otopark yazılımı "son okunan aracı" görselleyebilsin diye plaka
    /// kırpıntısının yanında verilir. SaveFullFrame kapalıysa ya da kare yoksa null.
    /// </summary>
    public string? FramePath { get; init; }

    /// <summary>Tekil olay kimliği — webhook/PMS tarafında idempotent işleme ve düzeltme geri-bildirimi için.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Uzlaşı güveni ManualReviewMinOcr eşiğinin altında: otopark yazılımı bariyeri otomatik açmak
    /// yerine operatör onayına düşürmelidir (kirli/karlı/bulanık plaka akışı).
    /// </summary>
    public bool LowConfidence { get; init; }

    /// <summary>Top-K alternatif okuma adayı (whitelist toleransı; ilki Plate ile aynıdır). Yoksa null.</summary>
    public IReadOnlyList<PlateCandidate>? Candidates { get; init; }

    /// <summary>
    /// Yön göstergesi (denetim/ayar): izin plaka GENİŞLİK eğilimi = son-yarı-max / erken-yarı-max
    /// (DirectionTracker, OCR'dan bağımsız her tespit kutusundan). &gt;1 araç yaklaşıyordu, &lt;1
    /// uzaklaşıyordu, -1 karar verilemedi (kısa iz). RequireApproaching açıkken belirgin uzaklaşan
    /// (bu değer &lt; ApproachDepartRatio) roi olayları zaten yayınlanmaz; bu alan, yayınlananların yönünü
    /// tam-kare kanıtla karşılaştırıp eşiği ayarlamak için taşınır.
    /// </summary>
    public float SizeTrend { get; init; } = -1f;
}
