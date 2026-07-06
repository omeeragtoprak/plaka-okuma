namespace Anpr.Core.Imaging;

/// <summary>
/// İlgi alanı (ROI) çokgeni: 0-1000 normalize koordinatlarda tanımlanır (Hikvision şerit koordinatlarıyla
/// aynı ölçek — kameranın kapsamı birebir kopyalanabilsin diye). Tespit kutusunun MERKEZİ çokgen içinde
/// değilse plaka OKUNMAZ (hem kapsam hizalama hem CPU tasarrufu). Boş/geçersiz tanım = filtre kapalı.
/// Format: "x1,y1;x2,y2;x3,y3;..." (en az 3 nokta).
/// </summary>
public sealed class RoiPolygon
{
    private readonly (float X, float Y)[] _pts;

    private RoiPolygon((float X, float Y)[] pts) => _pts = pts;

    public int PointCount => _pts.Length;

    /// <summary>"x,y;x,y;..." dizgesinden çokgen üretir; &lt;3 nokta ya da bozuk girişte null (filtre kapalı).</summary>
    public static RoiPolygon? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var pts = new List<(float, float)>();
        foreach (var pair in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var xy = pair.Split(',', StringSplitOptions.TrimEntries);
            if (xy.Length != 2 ||
                !float.TryParse(xy[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(xy[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                return null;
            pts.Add((x, y));
        }
        return pts.Count >= 3 ? new RoiPolygon(pts.ToArray()) : null;
    }

    /// <summary>Piksel-koordinatlı nokta (kare boyutlarıyla 0-1000'e ölçeklenir) çokgenin içinde mi (ray-casting).</summary>
    public bool ContainsPixel(float px, float py, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0) return true;
        float x = px * 1000f / imageWidth;
        float y = py * 1000f / imageHeight;
        bool inside = false;
        for (int i = 0, j = _pts.Length - 1; i < _pts.Length; j = i++)
        {
            var (xi, yi) = _pts[i];
            var (xj, yj) = _pts[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}
