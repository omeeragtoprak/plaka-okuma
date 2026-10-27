namespace Anpr.Sdk.Cameras;

/// <summary>
/// Kameranın akış (video kodlayıcı) ayarlarının SALT-OKUNUR anlık görüntüsü. Bilinmeyen / okunamayan alan <c>null</c>'dır —
/// <c>null</c> "kapalı" ya da "sıfır" DEMEK DEĞİLDİR, "bilinmiyor" demektir.
/// </summary>
public sealed record CameraStreamSettings
{
    /// <summary>Kodek (ör. "H.265", "H.264"; ffprobe'dan geliyorsa "hevc", "h264").</summary>
    public string? Codec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Fps { get; init; }
    /// <summary>Bit hızı (kbps): CBR'de sabit değer, VBR'de üst sınır.</summary>
    public int? BitrateKbps { get; init; }
    /// <summary>"CBR" / "VBR".</summary>
    public string? BitrateMode { get; init; }
    /// <summary>Anahtar kare aralığı (kare sayısı).</summary>
    public int? GopFrames { get; init; }
    /// <summary>Üreticinin "akıllı kodek" özelliği (Hikvision H.264+/H.265+) açık mı.</summary>
    public bool? SmartCodec { get; init; }
    /// <summary>Kameranın desteklediği kare hızları (yetenek listesi; okunamadıysa <c>null</c>).</summary>
    public IReadOnlyList<double>? FpsOptions { get; init; }
    /// <summary>Kameranın izin verdiği en yüksek bit hızı (kbps).</summary>
    public int? MaxBitrateKbps { get; init; }
    public IReadOnlyList<string>? CodecOptions { get; init; }
}

/// <summary>Kameranın görüntü (pozlama/işleme) ayarlarının SALT-OKUNUR anlık görüntüsü. Bilinmeyen alan <c>null</c>.</summary>
public sealed record CameraImageSettings
{
    /// <summary>Pozlama kipi (ör. "auto", "manual", "ShutterFirst").</summary>
    public string? ExposureMode { get; init; }
    /// <summary>Deklanşör, kameranın yazdığı biçimde (ör. "1/150"). Otomatik kipte çoğu kamerada ÜST SINIRDIR.</summary>
    public string? Shutter { get; init; }
    public double? ShutterSeconds { get; init; }
    /// <summary>Kazanç, ÜRETİCİ ölçeğinde (Hikvision 0–100; dB değildir).</summary>
    public int? GainLevel { get; init; }
    /// <summary>WDR açık mı: "open" → true, "close" → false, "auto" ya da bilinmeyen → <c>null</c> (kip için <see cref="WdrMode"/>).</summary>
    public bool? Wdr { get; init; }
    /// <summary>WDR kipi kameranın yazdığı biçimde ("open" / "close" / "auto").</summary>
    public string? WdrMode { get; init; }
    public bool? Blc { get; init; }
    public bool? Hlc { get; init; }
    public string? NoiseReductionMode { get; init; }
    /// <summary>Zamansal (3D / kareler arası) gürültü azaltma seviyesi, üretici ölçeğinde.</summary>
    public int? TemporalNoiseReductionLevel { get; init; }
    /// <summary>Gece/gündüz (IR-cut) kipi.</summary>
    public string? DayNightMode { get; init; }
    public string? FocusMode { get; init; }
    /// <summary>Ek aydınlatma kipi (ör. "irLight").</summary>
    public string? Illumination { get; init; }
    /// <summary>Kameranın desteklediği deklanşör değerleri (saniye; yetenek listesi).</summary>
    public IReadOnlyList<double>? ShutterOptionsSeconds { get; init; }
}

/// <summary>Bir kaynaktan (ör. Hikvision ISAPI) okunmuş ayarlar ve okuma sırasında karşılaşılan sorunlar.</summary>
public sealed record CameraSettingsSnapshot
{
    public required string CameraId { get; init; }
    /// <summary>"hikvision-isapi" gibi kaynak etiketi.</summary>
    public required string Source { get; init; }
    public required DateTimeOffset CapturedUtc { get; init; }
    public CameraStreamSettings? Stream { get; init; }
    public CameraImageSettings? Image { get; init; }
    /// <summary>Okunamayan uçlar, kimlik doğrulama hatası vb. (insan okur; şifre/URL İÇERMEZ).</summary>
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    /// <summary>Kamera kimlik bilgisini reddetti (401/403). Çağıran bu durumda TEKRAR DENEMEMELİDİR (kilitlenme riski).</summary>
    public bool AuthRejected { get; init; }
}
