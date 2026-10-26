namespace Anpr.Sdk.Frames;

/// <summary>
/// Bir kameradan en güncel kareyi (<see cref="VideoFrame"/>) sağlayan kaynak soyutlaması.
/// Arka planda sürekli çalışır; biriken eski kareleri atar, yalnızca en günceli tutar.
/// <para>2.0.0: dönüş tipi <c>byte[]</c> (JPEG) yerine <see cref="VideoFrame"/> — kare ham YUV 4:2:0
/// (ffmpeg Y4M yolu, varsayılan), ham BGRA (BMP yolu) ya da JPEG (eski MJPEG yolu) olabilir; tüketici biçime
/// bakmadan <see cref="VideoFrame.ToRgbaBitmap"/> / <see cref="VideoFrame.ToJpeg"/> kullanır.</para>
/// </summary>
public interface IFrameSource : IAsyncDisposable
{
    /// <summary>Arka plan okuma döngüsünü başlatır.</summary>
    void Start(CancellationToken cancellationToken);

    /// <summary>En güncel kareyi döner; henüz kare yoksa null. Her fiziksel kare AYRI bir nesnedir (referans kimliği = kare kimliği).</summary>
    VideoFrame? TryGetLatestFrame();

    /// <summary>
    /// En güncel karenin sıra numarası; bilinmiyorsa -1 (varsayılan). "Yeni kare geldi mi?" sorusunu kareyi
    /// ALMADAN yanıtlar (alınan karenin tamponu geri dönüştürülemez).
    /// </summary>
    long LatestSequence => -1;

    /// <summary>
    /// En güncel kareye onu ALMADAN bakar: <paramref name="inspect"/> çalışırken karenin tamponu değişmez,
    /// kare de "alındı" sayılmaz (tamponu sonraki kare için yeniden kullanılabilir). Kısa iş içindir (hareket
    /// kapısı ~birkaç mikrosaniye); kare dışarı SIZDIRILMAMALIDIR. Desteklenmiyorsa false (varsayılan) —
    /// çağıran <see cref="TryGetLatestFrame"/> yoluna düşer.
    /// </summary>
    bool TryInspectLatest<T>(Func<VideoFrame, T> inspect, out T result)
    {
        result = default!;
        return false;
    }

    /// <summary>
    /// En güncel kareyi KİRALAR: çağıran işi bitince <see cref="ReturnFrame"/> ile geri verir ve kaynak tamponu
    /// yeniden kullanabilir. Kare işlem bittikten sonra da tutulacaksa geri vermeden önce saklanmalıdır
    /// (kütüphane içi: <c>VideoFrame.Keep()</c>). Varsayılan uygulama <see cref="TryGetLatestFrame"/>'dir
    /// (kare "alınır", hiç geri dönüştürülmez) — özel kaynaklar değişiklik yapmadan çalışmaya devam eder.
    /// </summary>
    VideoFrame? TryLeaseLatestFrame() => TryGetLatestFrame();

    /// <summary>Kiralanan kareyi geri verir. Geri verilen kare artık KULLANILMAMALIDIR. Varsayılan: hiçbir şey yapmaz.</summary>
    void ReturnFrame(VideoFrame frame) { }

    bool IsConnected { get; }
    DateTimeOffset? LastFrameUtc { get; }
    long FramesReceived { get; }
    int RestartCount { get; }
}
