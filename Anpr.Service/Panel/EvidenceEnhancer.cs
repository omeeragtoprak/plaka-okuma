using SkiaSharp;

namespace Anpr.Service.Panel;

/// <summary>
/// İNSAN GÖZÜ İÇİN kanıt görseli iyileştirme. <b>OCR hattını etkilemez</b> — yalnız doğrulama
/// sayfasındaki operatörün plakayı okuyabilmesi için görüntüyü gerer/keskinleştirir.
///
/// <para><b>Neden var (ölçülmüş):</b> 22 gözle doğrulamada 4 kayıt (%18) insan gözüyle de okunamadı;
/// yani "operatör onayına düşür" güvenlik ağının bir SINIRI var — operatör de okuyamaz. Tam kareden
/// geniş kadraj + persentil germe + keskinleştirme uygulandığında 5 belirsiz vakanın 2'si KURTARILDI
/// (<c>25 EJ 175</c> ve <c>25 ATE 548</c>). Yani bu, doğrulama veriminde ölçülmüş bir kazançtır.</para>
///
/// <para><b>Yöntem:</b> (1) griye çevir — plaka kontrastı renk taşımaz, kroma gürültü ekler;
/// (2) <b>persentil germe</b> (varsayılan %2–%98): gece pozlamasında histogram bir uca yığılır,
/// mutlak germe yerine persentil kullanmak aşırı pozlanmış ve karanlık kareleri AYNI kodla düzeltir;
/// (3) 3×3 unsharp: bulanık kenarları ayırır. Hepsi tek geçişte, ~150×60 kırpıntıda ihmal edilebilir
/// maliyet — ve zaten yalnız operatör düğmeye bastığında çalışır, ANPR döngüsünde DEĞİL.</para>
/// </summary>
public static class EvidenceEnhancer
{
    /// <summary>Alt/üst persentil (0-100). Gece/aşırı pozlama için mutlak eşikten üstündür.</summary>
    public const float LowPercentile = 2f;
    public const float HighPercentile = 98f;

    /// <summary>
    /// Gri-tonlama + persentil germe + unsharp uygular. Girdi bitmap DEĞİŞTİRİLMEZ; yeni bitmap döner.
    /// </summary>
    /// <param name="src">Kaynak (renkli ya da gri).</param>
    /// <param name="sharpenAmount">0 = keskinleştirme yok. 1.0 tipik.</param>
    public static SKBitmap Enhance(SKBitmap src, float sharpenAmount = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(src);
        int w = src.Width, h = src.Height;
        var gray = new byte[w * h];

        // 1) gri
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = src.GetPixel(x, y);
                gray[y * w + x] = (byte)((c.Red * 77 + c.Green * 151 + c.Blue * 28) >> 8);
            }

        // 2) persentil germe — histogramdan alt/üst kesim noktalarını bul
        Span<int> hist = stackalloc int[256];
        foreach (byte v in gray) hist[v]++;
        int total = gray.Length;
        int loTarget = (int)(total * LowPercentile / 100f);
        int hiTarget = (int)(total * HighPercentile / 100f);
        int lo = 0, hi = 255, acc = 0;
        for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= loTarget) { lo = v; break; } }
        acc = 0;
        for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= hiTarget) { hi = v; break; } }
        if (hi <= lo) { lo = 0; hi = 255; }   // dejenere histogram (tek renk) — germe yapma

        float scale = 255f / (hi - lo);
        var stretched = new byte[w * h];
        for (int i = 0; i < gray.Length; i++)
        {
            int v = (int)((gray[i] - lo) * scale);
            stretched[i] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
        }

        // 3) unsharp: 3x3 kutu bulanıklığı çıkarılıp fark geri eklenir
        var outBuf = stretched;
        if (sharpenAmount > 0f && w >= 3 && h >= 3)
        {
            outBuf = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                    {
                        outBuf[y * w + x] = stretched[y * w + x];
                        continue;
                    }
                    int sum = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            sum += stretched[(y + dy) * w + (x + dx)];
                    int blur = sum / 9;
                    int cur = stretched[y * w + x];
                    int v = cur + (int)(sharpenAmount * (cur - blur));
                    outBuf[y * w + x] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                }
        }

        var dst = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        dst.Pixels = outBuf.Select(v => new SKColor(v, v, v)).ToArray();
        return dst;
    }

    /// <summary>
    /// Tam kareden, verilen kutunun çevresinde <paramref name="padRatio"/> kadar GENİŞ kadrajla kırpar.
    /// <para>Neden geniş: OCR kırpıntısı bilinçli olarak dardır ve son karakteri kesebiliyor
    /// (34ZH7641 vakası). Operatörün karar verebilmesi için plakanın TAMAMI ve biraz çevresi gerekir.
    /// Kutu kareye taşarsa güvenle kırpılır.</para>
    /// </summary>
    public static SKBitmap CropWide(SKBitmap frame, int x, int y, int boxW, int boxH, float padRatio)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int px = (int)(boxW * padRatio), py = (int)(boxH * padRatio);
        int x0 = Math.Max(0, x - px), y0 = Math.Max(0, y - py);
        int x1 = Math.Min(frame.Width, x + boxW + px), y1 = Math.Min(frame.Height, y + boxH + py);
        if (x1 <= x0 || y1 <= y0) return frame.Copy();

        var dst = new SKBitmap(x1 - x0, y1 - y0, frame.ColorType, frame.AlphaType);
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(frame, new SKRect(x0, y0, x1, y1), new SKRect(0, 0, dst.Width, dst.Height));
        return dst;
    }

    /// <summary>Küçük kırpıntıyı operatörün görebileceği boyuta çıkarır (tamsayı katları, yumuşatma ile).</summary>
    public static SKBitmap UpscaleTo(SKBitmap src, int targetWidth)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (src.Width >= targetWidth || src.Width == 0) return src.Copy();
        int factor = Math.Max(1, Math.Min(8, targetWidth / src.Width));
        var dst = new SKBitmap(src.Width * factor, src.Height * factor, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(src, new SKRect(0, 0, dst.Width, dst.Height), paint);
        return dst;
    }
}
