namespace Anpr.Sdk.Frames;

/// <summary>
/// Bir kameradan en güncel kareyi (JPEG byte[]) sağlayan kaynak soyutlaması.
/// Arka planda sürekli çalışır; biriken eski kareleri atar, yalnızca en günceli tutar.
/// </summary>
public interface IFrameSource : IAsyncDisposable
{
    /// <summary>Arka plan okuma döngüsünü başlatır.</summary>
    void Start(CancellationToken cancellationToken);

    /// <summary>En güncel kareyi (JPEG) döner; henüz kare yoksa null.</summary>
    byte[]? TryGetLatestFrame();

    bool IsConnected { get; }
    DateTimeOffset? LastFrameUtc { get; }
    long FramesReceived { get; }
    int RestartCount { get; }
}
