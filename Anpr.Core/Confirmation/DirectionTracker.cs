namespace Anpr.Core.Confirmation;

public sealed class DirectionTrackerOptions
{
    /// <summary>Yön kararı için gereken asgari genişlik örneği (yarı-max için ≥4). Altında -1 (kararsız).</summary>
    public int MinSamples { get; set; } = 4;

    /// <summary>Yön kararı için izin kapsaması gereken asgari süre (ilk-son örnek). Altında -1.</summary>
    public TimeSpan MinSpan { get; set; } = TimeSpan.FromSeconds(0.6);

    /// <summary>İz bu süre görünmezse düşer (farklı araç aynı bölgeye gelince zincirlenmesin).</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(2.5);

    /// <summary>Centroid eşleşme kapısı: mesafe ≤ MatchGate × ortalama kutu boyutu ise aynı iz. IoU DEĞİL
    /// (2 fps'te kutular örtüşmeyebilir). CÖMERT (4×): 2 fps'te araç kareler arası çok yer değiştirdiğinden
    /// dar kapı izi parçalar (her kare ayrı iz -> 1 örnek -> kararsız). Tek-şerit bariyerde tek araç
    /// olduğundan geniş kapı güvenli; farklı yön/uzaklık araçları TTL + konumla ayrışır.</summary>
    public float MatchGate { get; set; } = 4.0f;

    public int MaxSamples { get; set; } = 48;
}

/// <summary>
/// OCR'dan BAĞIMSIZ hareket-yönü izleyici (yaklaşan/uzaklaşan). Her karede GÖRÜLEN her plaka tespit
/// kutusunu (geçerli-TR olsun olmasın) alır; centroid yakınlığıyla izlere böler ve iz başına plaka
/// GENİŞLİĞİ zaman serisini tutar. Yön = son-yarı-MAX / erken-yarı-MAX genişlik oranı (uzaklaşan
/// arka plaka OCR'lanamasa bile kutusu görünür → yoğun veri; MAX tek kırpık/bulanık kareyi yutar).
///
/// Neden genişlik: TR plakası gerçek dünyada ~52 cm sabit genişlikte; plaka genişliği, araç kutusundan
/// (tipe göre değişir) ve plaka yüksekliğinden (eğim/pitch etkiler) daha kararlı bir ölçek göstergesidir.
/// Thread-safe değildir; tek kamera döngüsünden sıralı kullanılır (PlateConfirmer gibi).
/// </summary>
public sealed class DirectionTracker
{
    private sealed class Track
    {
        public readonly List<(DateTimeOffset At, int Width)> Widths = new();
        public float Cx, Cy;
        public float Dim;          // son kutunun ortalama boyutu (eşleşme kapısı için)
        public DateTimeOffset Last;
    }

    private readonly List<Track> _tracks = new();
    private readonly DirectionTrackerOptions _o;

    public DirectionTracker(DirectionTrackerOptions options) => _o = options;

    /// <summary>Bir kareden görülen plaka tespit kutusunu izle (OCR gereksiz).</summary>
    public void Observe(PlateBox box, DateTimeOffset now)
    {
        if (box.Width <= 0 || box.Height <= 0) return;
        Cleanup(now);

        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        var t = Match(cx, cy, now);
        if (t == null) { t = new Track(); _tracks.Add(t); }

        t.Widths.Add((now, box.Width));
        if (t.Widths.Count > _o.MaxSamples) t.Widths.RemoveAt(0);
        t.Cx = cx; t.Cy = cy; t.Dim = (box.Width + box.Height) / 2f; t.Last = now;
    }

    /// <summary>Verilen kutuya denk gelen izin genişlik-eğilimi (son/erken yarı-max). Eşleşme yoksa ya da
    /// yeterli kanıt yoksa -1 = "kararsız" (çağıran YAKLAŞIYOR sayar; recall önceliği).</summary>
    public float TrendFor(PlateBox box, DateTimeOffset now)
    {
        if (box.Width <= 0) return -1f;
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        var t = Match(cx, cy, now);
        if (t == null) return -1f;

        var w = t.Widths;
        if (w.Count < Math.Max(4, _o.MinSamples)) return -1f;
        if (w[^1].At - w[0].At < _o.MinSpan) return -1f;

        int half = w.Count / 2;
        int earlyMax = 0, recentMax = 0;
        for (int i = 0; i < half; i++) if (w[i].Width > earlyMax) earlyMax = w[i].Width;
        for (int i = half; i < w.Count; i++) if (w[i].Width > recentMax) recentMax = w[i].Width;
        if (earlyMax <= 0) return -1f;
        return (float)recentMax / earlyMax;
    }

    private Track? Match(float cx, float cy, DateTimeOffset now)
    {
        Track? best = null; float bestD = float.MaxValue;
        foreach (var t in _tracks)
        {
            if (now - t.Last > _o.Ttl) continue;
            float d = MathF.Sqrt((t.Cx - cx) * (t.Cx - cx) + (t.Cy - cy) * (t.Cy - cy));
            float gate = _o.MatchGate * MathF.Max(1f, t.Dim);
            if (d <= gate && d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    private void Cleanup(DateTimeOffset now)
    {
        for (int i = _tracks.Count - 1; i >= 0; i--)
            if (now - _tracks[i].Last > _o.Ttl) _tracks.RemoveAt(i);
    }
}
