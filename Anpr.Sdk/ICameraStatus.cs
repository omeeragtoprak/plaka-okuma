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

    /// <summary>Geometri kapısında elenen tespit sayısı (plaka olamayacak şekil — dikey/parçalı kutu).
    /// Kapının sahada ne kadar müdahale ettiği bu sayaçtan izlenir; sürekli artıyorsa eşik gözden geçirilir.</summary>
    long AspectDropped { get; }

    /// <summary>TR gramerine uymadığı için atılan okuma sayısı (sessiz kaybın ölçümü).</summary>
    long TrRejected { get; }
    long EventsEmitted { get; }
    DateTimeOffset? LastEventUtc { get; }

    /// <summary>Boyut/mesafe sınırı (<c>MinPlateWidthPx</c>, <c>MaxPlateWidthPx</c>, kalibrasyonlu mesafe) yüzünden
    /// OCR'ı atlanan tespit sayısı. Sınır kapalıyken 0. (2.2.0'da eklendi; varsayılan gövde eski uygulayıcıları kırmaz.)</summary>
    long SizeDropped => 0;

    /// <summary>
    /// Bu kamerada son yayınlanan bariyer (roi) olaylarının plaka genişliği dağılımı (px, dedektör kutusu) —
    /// <c>MinPlateWidthPx</c> eşiğini sitenin KENDİ verisinden seçmek için. Henüz olay yoksa <c>null</c>.
    /// Her olayın değeri, yayınlanan (en iyi) okumanın karesindendir; aracın en yakın anı olmayabilir.
    /// </summary>
    PlateWidthStats? RecentPlateWidths => null;

    /// <summary>Son işlenen karenin genişliği (px) — mesafe kalibrasyonundaki <c>ReferenceFrameWidthPx</c> değeri.
    /// Henüz kare işlenmediyse <c>null</c>. (2.2.0'da eklendi; varsayılan gövde.)</summary>
    int? FrameWidthPx => null;

    /// <summary>Eğik plaka düzeltme (rektifikasyon) bu kamerada ÇALIŞABİLİYOR mu: <c>EnhanceRectify</c> açık VE model
    /// yüklendi. <c>false</c> ise aşama sessizce devre dışıdır (model dosyası yok / yüklenemedi / kapatıldı).
    /// 2.0.0–2.1.0 paketlerinde bu değer pratikte hep false idi. (2.2.0'da eklendi; varsayılan gövde.)</summary>
    bool RectifierAvailable => false;

    /// <summary>Düşük güvenli olup zenginleştirme (rektifikasyon/süper çözünürlük) KIRPINTILARIYLA denenmiş iz sayısı —
    /// araç başına en fazla bir. Aşamanın sahada GERÇEKTEN çalıştığının sayacı. (2.2.0'da eklendi; varsayılan gövde.)</summary>
    long EnhanceTracks => 0;

    /// <summary>Zenginleştirme gereken ama izin kırpıntısı bulunamadığı için ATLANAN iz sayısı. Sürekli artıyorsa aşama
    /// boşa dönüyordur (2.2.0 öncesinde kanıt kaydı kapalı kurulumlarda tam olarak bu oluyordu). (2.2.0; varsayılan gövde.)</summary>
    long EnhanceNoCrops => 0;

    /// <summary>Zenginleştirmenin izlere eklediği geçerli okuma sayısı (oylamaya giren ek gözlem).
    /// <see cref="EnhanceTracks"/> artıp bu artmıyorsa düzeltilmiş kırpıntılar okunamıyor demektir. (2.2.0; varsayılan gövde.)</summary>
    long EnhanceInjected => 0;
}

/// <summary>Plaka genişliği dağılımı (px): kaç olaydan ve 10./50./90. yüzdelikler.</summary>
public readonly record struct PlateWidthStats(int Count, int P10, int P50, int P90)
{
    /// <summary>Sıralı dizide <c>round(q·(n−1))</c> konumu (<c>scripts/plate_size_sim.py</c> ile aynı kural).
    /// Boş girdide <c>null</c>.</summary>
    public static PlateWidthStats? From(IEnumerable<int> widths)
    {
        var s = widths.Where(w => w > 0).OrderBy(w => w).ToArray();
        if (s.Length == 0) return null;
        int At(double q) => s[(int)Math.Round(q * (s.Length - 1), MidpointRounding.AwayFromZero)];
        return new PlateWidthStats(s.Length, At(0.10), At(0.50), At(0.90));
    }
}
