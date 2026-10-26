using SkiaSharp;

namespace Anpr.Core.Imaging;

/// <summary>
/// Plaka kırpıntısını eğiklikten kurtarır (deskew). Yönetilen + cross-platform (OpenCv yok).
///
/// Yöntem: gri+ikili (Otsu), sonra aday açılar için satır-projeksiyon "enerjisini" ölçer; karakter
/// satırları en çok hizalandığında (enerji en yüksek) açı bulunur ve görüntü o kadar döndürülür.
/// Bu bir ROTASYON düzeltmesidir (tam perspektif değil). Açılı karelerde OCR'ı iyileştirmeyi hedefler;
/// kazanç açılı veride ÖLÇÜLMELİ (varsayılan kapalı).
/// </summary>
public static class PlateRectifier
{
    /// <summary>Eğikliği kestirip düzeltir. |açı| küçükse aynı bitmap'i döner.</summary>
    public static SKBitmap Deskew(SKBitmap src, double maxAngleDeg = 12, double stepDeg = 1.0)
    {
        double angle = EstimateSkewAngle(src, maxAngleDeg, stepDeg);
        if (Math.Abs(angle) < 1.0) return src;
        return Rotate(src, -angle); // tespit edilen eğikliği TERS yönde döndürerek düzelt
    }

    /// <summary>Eğim açısını (derece) kestirir. Pozitif = saat yönünde eğik.</summary>
    public static double EstimateSkewAngle(SKBitmap src, double maxAngleDeg = 12, double stepDeg = 1.0)
    {
        int w = src.Width, h = src.Height;
        if (w < 8 || h < 8) return 0;

        var px = src.GetPixelSpan();
        int rb = src.RowBytes;
        var gray = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = y * rb + x * 4;
                gray[y * w + x] = (byte)((px[s] * 299 + px[s + 1] * 587 + px[s + 2] * 114) / 1000);
            }

        byte thr = Otsu(gray);
        // Plakada karakterler genelde koyu; ön planı (koyu pikseller) topla.
        var fg = new List<(int x, int y)>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (gray[y * w + x] <= thr) fg.Add((x, y)); // <= : Otsu thr=0 (saf siyah) durumunu da kapsar
        if (fg.Count < 20) return 0;

        double cx = w / 2.0, cy = h / 2.0;
        // Geniş histogram aralığı (offset h, boyut 3h): döndürülen noktalar [0,h) dışına taşsa bile
        // kırpılmaz; böylece skor noktaları KAYBETMEYE değil, yalnızca SATIR YOĞUNLAŞMASINA bağlı olur.
        int offset = h, size = 3 * h;
        double bestAngle = 0, bestScore = -1;
        for (double a = -maxAngleDeg; a <= maxAngleDeg + 1e-9; a += stepDeg)
        {
            double rad = a * Math.PI / 180.0;
            double sin = Math.Sin(rad), cos = Math.Cos(rad);
            var hist = new int[size];
            foreach (var (x, y) in fg)
            {
                double yr = -(x - cx) * sin + (y - cy) * cos + cy;
                int row = (int)Math.Round(yr) + offset;
                if (row >= 0 && row < size) hist[row]++;
            }
            double score = 0;
            for (int r = 0; r < size; r++) score += (double)hist[r] * hist[r]; // tepe-keskinliği
            if (score > bestScore) { bestScore = score; bestAngle = a; }
        }
        return bestAngle;
    }

    private static SKBitmap Rotate(SKBitmap src, double deg)
    {
        var info = new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var dst = new SKBitmap(info);
        using var c = new SKCanvas(dst);
        c.Clear(new SKColor(114, 114, 114)); // letterbox dolgusu
        c.Translate(src.Width / 2f, src.Height / 2f);
        c.RotateDegrees((float)deg);
        c.Translate(-src.Width / 2f, -src.Height / 2f);
        c.DrawBitmap(src, 0, 0);
        return dst;
    }

    private static byte Otsu(byte[] gray)
    {
        var hist = new int[256];
        foreach (var g in gray) hist[g]++;
        int total = gray.Length;
        double sum = 0;
        for (int i = 0; i < 256; i++) sum += i * (double)hist[i];
        double sumB = 0; int wB = 0; double maxVar = -1; int thr = 127;
        for (int t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            int wF = total - wB;
            if (wF == 0) break;
            sumB += t * (double)hist[t];
            double mB = sumB / wB, mF = (sum - sumB) / wF;
            double between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > maxVar) { maxVar = between; thr = t; }
        }
        return (byte)thr;
    }
}
