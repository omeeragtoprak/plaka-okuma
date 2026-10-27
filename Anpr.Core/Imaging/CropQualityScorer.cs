using SkiaSharp;

namespace Anpr.Core.Imaging;

/// <summary>
/// Kırpıntı kalite skoru (İkinci-Şans Şeridi, Aşama 1): gri Laplacian varyansı (bulanıklık) +
/// histogram uçları (aşırı karanlık/patlamış). ~0.1 ms/kırpıntı; OCR öncesi INLINE kapı ve
/// zenginleştirme aşamalarının tetikleyicisi olarak kullanılır.
/// Python eşleniği (scripts/make_slices.py) ile AYNI matematik: ITU-R 601 gri + 4-komşu Laplacian —
/// eşik taraması iki tarafta da aynı ölçeğe oturur.
/// </summary>
public static class CropQualityScorer
{
    public readonly record struct Quality(float BlurVar, float DarkPct, float BrightPct, float MeanGray)
    {
        public override string ToString() => $"blur={BlurVar:F0} dark={DarkPct:P0} bright={BrightPct:P0} mean={MeanGray:F0}";
    }

    public static Quality Score(SKBitmap bmp)
    {
        ArgumentNullException.ThrowIfNull(bmp);
        int w = bmp.Width, h = bmp.Height;
        if (w < 3 || h < 3) return new Quality(0, 0, 0, 0);

        var px = bmp.GetPixelSpan();
        int rb = bmp.RowBytes;

        // Gri düzlem (ITU-R 601) + histogram uçları
        var g = new byte[w * h];
        long sum = 0;
        int dark = 0, bright = 0;
        for (int y = 0; y < h; y++)
        {
            int row = y * rb, o = y * w;
            for (int x = 0; x < w; x++)
            {
                int s = row + x * 4; // RGBA
                byte gray = (byte)((px[s] * 299 + px[s + 1] * 587 + px[s + 2] * 114) / 1000);
                g[o + x] = gray;
                sum += gray;
                if (gray < 30) dark++;
                else if (gray > 225) bright++;
            }
        }

        // 4-komşu Laplacian varyansı (cv2.Laplacian ksize=1 ile aynı çekirdek, işaret farkı varyansı etkilemez)
        double lSum = 0, lSq = 0;
        int n = 0;
        for (int y = 1; y < h - 1; y++)
        {
            int o = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                int c = 4 * g[o + x] - g[o + x - 1] - g[o + x + 1] - g[o - w + x] - g[o + w + x];
                lSum += c;
                lSq += (double)c * c;
                n++;
            }
        }
        double mean = lSum / n;
        double var = lSq / n - mean * mean;

        float total = w * h;
        return new Quality((float)var, dark / total, bright / total, sum / total);
    }
}
