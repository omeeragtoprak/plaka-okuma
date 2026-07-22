namespace Anpr.Sdk;

/// <summary>
/// Bir kameranın SALT-OKUNUR durum/telemetri görünümü — PMS kendi sağlık ekranında gösterebilir (M1).
/// <see cref="AnprRuntime.CameraStatus"/> bunu verir; somut <c>CameraWorker</c> (public Start/DisposeAsync
/// dahil, PMS'in yanlış çağırıp runtime'ı bozabileceği lifecycle metotları) yerine bu arayüze bağlanın.
/// </summary>
public interface ICameraStatus
{
    string CameraId { get; }
    string CameraName { get; }
    /// <summary>Süreç canlı VE kare akışı taze (bayat ffmpeg oturumu false verir).</summary>
    bool IsConnected { get; }
    DateTimeOffset? LastFrameUtc { get; }
    long FramesReceived { get; }
    long FramesProcessed { get; }
    /// <summary>ffmpeg yeniden-bağlanma sayısı (kamera/ağ dalgalanması göstergesi).</summary>
    int RestartCount { get; }
    /// <summary>Kayan-pencere kare-işleme süresi p50/p95 (ms) — hedef-PC darboğaz teşhisi.</summary>
    double FrameMsP50 { get; }
    double FrameMsP95 { get; }
    /// <summary>Kalite kapısının OCR öncesi elediği kırpıntı sayısı.</summary>
    long QualityDropped { get; }
    long EventsEmitted { get; }
    DateTimeOffset? LastEventUtc { get; }
}
