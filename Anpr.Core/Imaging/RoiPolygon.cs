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

    /// <summary>
    /// Çokgenin piksel-uzayında sınırlayıcı dikdörtgeni (+pad oranı kadar kenar payı, kareye kırpılmış).
    /// ROI-kırpımlı tespit için: dedektöre tam kare yerine bu bölge verilirse letterbox'taki gri dolgu
    /// israfı geri kazanılır, plakalar dedektör girdisinde büyür (uzak plaka görünürlüğü).
    /// </summary>
    public (int X, int Y, int W, int H) BoundingRectPixels(int imageWidth, int imageHeight, float padRatio = 0.05f)
    {
        float minX = 1000, minY = 1000, maxX = 0, maxY = 0;
        foreach (var (x, y) in _pts)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        float padX = (maxX - minX) * padRatio, padY = (maxY - minY) * padRatio;
        int x0 = Math.Max(0, (int)((minX - padX) / 1000f * imageWidth));
        int y0 = Math.Max(0, (int)((minY - padY) / 1000f * imageHeight));
        int x1 = Math.Min(imageWidth, (int)Math.Ceiling((maxX + padX) / 1000f * imageWidth));
        int y1 = Math.Min(imageHeight, (int)Math.Ceiling((maxY + padY) / 1000f * imageHeight));
        return (x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
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
