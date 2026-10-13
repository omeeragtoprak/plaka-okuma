using SkiaSharp;

namespace Anpr.Core.Imaging;

/// <summary>
/// Çok-kareli füzyon: aynı plakanın birkaç kırpıntısını ORTAK boyuta getirip HİZALAYIP (tamsayı
/// kaydırma, çapraz-korelasyon) ORTALAYARAK tek, gürültüsü azalmış, etkin çözünürlüğü artmış görüntü
/// üretir. Ortalama, √N ile gürültüyü azaltır; kareler arası alt-piksel farkı varsa detay da kazandırır
/// (klasik çok-kareli süper-çözünürlük). CPU: araç başına 1 kez çalıştırılır, ucuz.
/// </summary>
public static class PlateFuser
{
    /// <summary>Kırpıntıları hizalayıp ortalayarak birleştirir. upscale ile alt-piksel detay için büyütür.</summary>
    public static SKBitmap Fuse(IReadOnlyList<SKBitmap> crops, int upscale = 2, int maxShift = 3)
    {
        if (crops.Count == 1) return SkiaImaging.ResizeRgba(crops[0], crops[0].Width * upscale, crops[0].Height * upscale);

        // Referans = en büyük kırpıntı; hedef boyut = ref × upscale.
        var refc = crops[0];
        foreach (var c in crops) if (c.Width * c.Height > refc.Width * refc.Height) refc = c;
        int W = Math.Max(8, refc.Width * upscale), H = Math.Max(8, refc.Height * upscale);

        // Hepsini W×H'ye getir.
        var resized = new List<SKBitmap>(crops.Count);
        foreach (var c in crops) resized.Add(SkiaImaging.ResizeRgba(c, W, H, SKFilterQuality.Medium));

        // Referansın luma'sı (hizalama için).
        var refLuma = Luma(resized[0], W, H);

        var accR = new float[W * H];
        var accG = new float[W * H];
        var accB = new float[W * H];
        var cnt = new int[W * H];   // her pikselin gerçek katkı sayısı (kaydırmada menzil-dışı kalanlar hariç)

        foreach (var img in resized)
        {
            var (dx, dy) = img == resized[0] ? (0, 0) : BestShift(refLuma, Luma(img, W, H), W, H, maxShift);
            var px = img.GetPixelSpan();
            int rb = img.RowBytes;
            for (int y = 0; y < H; y++)
            {
                int sy = y + dy; if (sy < 0 || sy >= H) continue;
                for (int x = 0; x < W; x++)
                {
                    int sx = x + dx; if (sx < 0 || sx >= W) continue;
                    int s = sy * rb + sx * 4;
                    int a = y * W + x;
                    accR[a] += px[s]; accG[a] += px[s + 1]; accB[a] += px[s + 2]; cnt[a]++;
                }
            }
        }

        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var outBmp = new SKBitmap(info);
        var outSpan = outBmp.GetPixels();
        // yaz: TAM kare sayısına değil, o pikselin GERÇEK katkı sayısına böl (kaydırmada bazı kareler
        // menzil-dışı kalınca kenar pikselleri yapay karartmayı önler; kare 0 daima katkıda → cnt≥1).
        var buf = new byte[W * H * 4];
        for (int a = 0; a < W * H; a++)
        {
            int c = cnt[a] > 0 ? cnt[a] : 1;
            buf[a * 4] = (byte)(accR[a] / c);
            buf[a * 4 + 1] = (byte)(accG[a] / c);
            buf[a * 4 + 2] = (byte)(accB[a] / c);
            buf[a * 4 + 3] = 255;
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, outSpan, buf.Length);

        foreach (var r in resized) r.Dispose();
        return outBmp;
    }

    private static byte[] Luma(SKBitmap b, int W, int H)
    {
        var px = b.GetPixelSpan(); int rb = b.RowBytes;
        var l = new byte[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int s = y * rb + x * 4;
                l[y * W + x] = (byte)((px[s] * 299 + px[s + 1] * 587 + px[s + 2] * 114) / 1000);
            }
        return l;
    }

    // Referansa en iyi tamsayı kaydırmayı (dx,dy) çapraz-korelasyon/SAD ile bulur.
    private static (int dx, int dy) BestShift(byte[] refL, byte[] movL, int W, int H, int maxShift)
    {
        long best = long.MaxValue; int bdx = 0, bdy = 0;
        // hız için alt-örnekle (her 2 pikselde bir)
        for (int dy = -maxShift; dy <= maxShift; dy++)
            for (int dx = -maxShift; dx <= maxShift; dx++)
            {
                long sad = 0;
                for (int y = maxShift; y < H - maxShift; y += 2)
                    for (int x = maxShift; x < W - maxShift; x += 2)
                    {
                        int r = refL[y * W + x];
                        int m = movL[(y + dy) * W + (x + dx)];
                        sad += Math.Abs(r - m);
                    }
                // Beraberlikte (düz/düşük-doku kırpıntı) en KÜÇÜK büyüklüklü kaydırmayı seç: gereksiz
                // kaymayı (dolayısıyla kenar-atlamayı) önler; hizalama gerçek kayma varsa yine yakalar.
                if (sad < best || (sad == best && Math.Abs(dx) + Math.Abs(dy) < Math.Abs(bdx) + Math.Abs(bdy)))
                    { best = sad; bdx = dx; bdy = dy; }
            }
        return (bdx, bdy);
    }
}
