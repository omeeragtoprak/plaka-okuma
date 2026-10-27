using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Anpr.Core.Imaging;

/// <summary>
/// WPOD ailesi öğrenilmiş perspektif rektifikasyonu (İkinci-Şans Şeridi, Aşama 2).
/// Kırpıntı, kenarları çoğaltılarak (%50 marj) beslenir; ağın stride-16 hücre olasılıkları + 2x3
/// affine regresyonundan plaka DÖRTGENİ çıkarılır; DLT homografisiyle frontal dikdörtgene warp
/// edilir (%10 kenar payı). OpenCV YOK: homografi Gauss eliminasyonu, warp SkiaSharp.
/// Elenen naif deskew'den farkı: öğrenilmiş köşe tespiti + TAM perspektif (yalnız rotasyon değil).
/// <para>İki model desteklenir; tür çıktı kanal sayısından anlaşılır:
/// · <b>IWPOD-NET</b> (MIT, varsayılan, 7 kanal: 1 olasılık + 6 affine) — giriş kısa kenarı 160.
/// · <b>WPOD-NET</b> (GPL-3.0, pakete girmez, 8 kanal: 2 olasılık + 6 affine) — giriş kısa kenarı 208.
/// ÖLÇÜM (2026-10-27, C# üretim yolu `Anpr.Bench rectab`, kapı ocr&lt;0,98 + dağılım toplamı): seçim seti 625
/// kırpıntıda HAM 493 · WPOD 525 · IWPOD 535; AYRI TUTULMUŞ insan-hükümlü 239 zor kırpıntıda HAM 88 · WPOD 104 ·
/// IWPOD 114 (yalnız-WPOD-doğru 6 · yalnız-IWPOD-doğru 16, McNemar p=0,053). Süre medyan 41 ms (WPOD 66),
/// yavaş çekirdekte 198 ms (WPOD 332).</para>
/// </summary>
public sealed class PlateRectifier2 : IDisposable
{
    private const int NetStride = 16;
    private const float Side = 7.75f; // ((208+40)/2)/16 — WPOD/IWPOD ortak kanonik yarı-kenar ölçeği
    private const int AffineChannels = 6;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private bool _disposed;

    /// <summary>Ağa verilen (kenarları çoğaltılmış) kırpıntının kısa kenarı: IWPOD 160, WPOD 208.
    /// İkisi de ayrı ölçümle seçildi (IWPOD 160/208/256 arasından; 256 açıkça zararlı).</summary>
    public int InputMinSide { get; }

    public PlateRectifier2(string modelPath, SessionOptions options) : this(modelPath, options, shareKey: null) { }

    /// <summary><paramref name="shareKey"/> null değilse oturum aynı model+ayar için paylaşılır (bkz. OnnxSessionPool).</summary>
    internal PlateRectifier2(string modelPath, SessionOptions options, string? shareKey)
    {
        _session = Onnx.OnnxSessionPool.Acquire(Path.GetFullPath(modelPath), options, shareKey);
        _inputName = _session.InputMetadata.Keys.First();
        InputMinSide = InputMinSideFor(_session.OutputMetadata.Values.First().Dimensions);
    }

    /// <summary>Çıktı [1, ry, rx, C]: C=7 IWPOD → 160; C=8 WPOD ya da bilinmeyen → 208 (eski davranış).</summary>
    internal static int InputMinSideFor(int[] outputDims)
        => outputDims.Length == 4 && outputDims[3] == 1 + AffineChannels ? 160 : 208;

    public readonly record struct RectifyResult(SKBitmap Image, float SkewDeg, float PerspRatio, float Prob);

    /// <summary>
    /// Kırpıntıyı rektifiye eder; dörtgen bulunamazsa null (çağıran aşamayı atlar).
    /// padRatio: kenar çoğaltma marjı (WPOD bağlam ister; ölçülen reçete 0.5).
    /// </summary>
    public RectifyResult? Rectify(SKBitmap crop, float padRatio = 0.5f, float threshold = 0.35f, int outMaxSide = 240)
    {
        ArgumentNullException.ThrowIfNull(crop);
        if (crop.Width < 16 || crop.Height < 8) return null;

        using var padded = PadReplicate(crop, padRatio);
        var quad = FindQuad(padded, threshold);
        if (quad == null) return null;

        var (x, y, prob) = quad.Value;

        static float E(float[] xs, float[] ys, int i, int j)
            => MathF.Sqrt((xs[j] - xs[i]) * (xs[j] - xs[i]) + (ys[j] - ys[i]) * (ys[j] - ys[i]));

        float w = MathF.Max(E(x, y, 0, 1), E(x, y, 3, 2));
        float h = MathF.Max(E(x, y, 0, 3), E(x, y, 1, 2));
        if (w < 8 || h < 4) return null;

        float scale = outMaxSide / MathF.Max(w, h);
        int W = Math.Max(16, (int)(w * scale)), H = Math.Max(8, (int)(h * scale));
        int mx = W / 10, my = H / 10;

        // dst köşeler (TL,TR,BR,BL) — %10 kenar payı içeride
        float[] dx = { mx, mx + W, mx + W, mx };
        float[] dy = { my, my, my + H, my + H };

        var hMat = SolveHomography(x, y, dx, dy);
        if (hMat == null) return null;

        var outBmp = WarpPerspective(padded, hMat, W + 2 * mx, H + 2 * my);

        float skew = (MathF.Abs(AngleDeg(x, y, 0, 1)) + MathF.Abs(AngleDeg(x, y, 3, 2))) / 2f;
        float lr1 = E(x, y, 0, 3), lr2 = E(x, y, 1, 2);
        float persp = MathF.Max(lr1, lr2) / MathF.Max(1e-6f, MathF.Min(lr1, lr2));
        return new RectifyResult(outBmp, skew, persp, prob);
    }

    private static float AngleDeg(float[] x, float[] y, int i, int j)
        => MathF.Atan2(y[j] - y[i], x[j] - x[i]) * 180f / MathF.PI;

    // ---- WPOD çıkarımı + decode ----

    private (float[] X, float[] Y, float Prob)? FindQuad(SKBitmap bgrSource, float threshold)
    {
        int w0 = bgrSource.Width, h0 = bgrSource.Height;
        float factor = (float)InputMinSide / Math.Min(w0, h0);
        int w = (int)(w0 * factor), h = (int)(h0 * factor);
        if (w % NetStride != 0) w += NetStride - w % NetStride;
        if (h % NetStride != 0) h += NetStride - h % NetStride;

        using var resized = bgrSource.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                                             SKFilterQuality.Medium);
        if (resized == null) return null;

        var tensor = new DenseTensor<float>(new[] { 1, h, w, 3 });
        var px = resized.GetPixelSpan();
        int rb = resized.RowBytes;
        for (int yy = 0; yy < h; yy++)
        {
            int row = yy * rb;
            for (int xx = 0; xx < w; xx++)
            {
                int s = row + xx * 4; // RGBA
                tensor[0, yy, xx, 0] = px[s + 2] / 255f; // B
                tensor[0, yy, xx, 1] = px[s + 1] / 255f; // G
                tensor[0, yy, xx, 2] = px[s] / 255f;     // R
            }
        }

        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
        var outT = results.First().AsTensor<float>();
        // [1, ry, rx, C]: kanal 0 = plaka olasılığı; SON 6 kanal = 2x3 affine
        // (WPOD C=8: kanal 1 arka plan olasılığı · IWPOD C=7: arada kanal yok)
        int ry = outT.Dimensions[1], rx = outT.Dimensions[2];
        int aff0 = outT.Dimensions[3] - AffineChannels;
        if (aff0 < 1) return null;

        float bestP = threshold;
        int bx = -1, by = -1;
        for (int cy = 0; cy < ry; cy++)
            for (int cx = 0; cx < rx; cx++)
            {
                float p = outT[0, cy, cx, 0];
                if (p > bestP) { bestP = p; bx = cx; by = cy; }
            }
        if (bx < 0) return null;

        Span<float> a = stackalloc float[6];
        for (int i = 0; i < 6; i++) a[i] = outT[0, by, bx, aff0 + i];
        a[0] = MathF.Max(a[0], 0f); // A00
        a[4] = MathF.Max(a[4], 0f); // A11 (2x3 satır-öncelikli: [a00 a01 a02; a10 a11 a12])

        // kanonik kare köşeleri (±0.5): TL,TR,BR,BL
        float[] cxs = { -0.5f, 0.5f, 0.5f, -0.5f };
        float[] cys = { -0.5f, -0.5f, 0.5f, 0.5f };
        var qx = new float[4];
        var qy = new float[4];
        float mnx = (float)w / NetStride, mny = (float)h / NetStride;
        for (int i = 0; i < 4; i++)
        {
            float ptx = a[0] * cxs[i] + a[1] * cys[i] + a[2];
            float pty = a[3] * cxs[i] + a[4] * cys[i] + a[5];
            ptx = ptx * Side + (bx + 0.5f);
            pty = pty * Side + (by + 0.5f);
            qx[i] = ptx / mnx * w0; // 0..1 normalize -> kaynak piksel
            qy[i] = pty / mny * h0;
        }
        return (qx, qy, bestP);
    }

    // ---- Geometri ----

    /// <summary>4 nokta eşleşmesinden 3x3 homografi (h33=1; 8x8 Gauss, kısmi pivot). Dejenerede null.</summary>
    internal static float[]? SolveHomography(float[] sx, float[] sy, float[] dx, float[] dy)
    {
        var m = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double X = sx[i], Y = sy[i], U = dx[i], V = dy[i];
            int r = i * 2;
            m[r, 0] = X; m[r, 1] = Y; m[r, 2] = 1; m[r, 6] = -U * X; m[r, 7] = -U * Y; m[r, 8] = U;
            m[r + 1, 3] = X; m[r + 1, 4] = Y; m[r + 1, 5] = 1; m[r + 1, 6] = -V * X; m[r + 1, 7] = -V * Y; m[r + 1, 8] = V;
        }
        for (int c = 0; c < 8; c++)
        {
            int piv = c;
            for (int r = c + 1; r < 8; r++)
                if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
            if (Math.Abs(m[piv, c]) < 1e-9) return null;
            if (piv != c)
                for (int k = c; k < 9; k++) (m[c, k], m[piv, k]) = (m[piv, k], m[c, k]);
            for (int r = 0; r < 8; r++)
            {
                if (r == c) continue;
                double f = m[r, c] / m[c, c];
                for (int k = c; k < 9; k++) m[r, k] -= f * m[c, k];
            }
        }
        var hm = new float[9];
        for (int i = 0; i < 8; i++) hm[i] = (float)(m[i, 8] / m[i, i]);
        hm[8] = 1f;
        return hm;
    }

    /// <summary>Ters-haritalı bilinear perspektif warp (SkiaSharp piksel erişimiyle; küçük hedefler için yeterli hız).</summary>
    internal static SKBitmap WarpPerspective(SKBitmap src, float[] h, int outW, int outH)
    {
        var inv = Invert3x3(h);
        var dst = new SKBitmap(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var sp = src.GetPixelSpan();
        int srb = src.RowBytes, sw = src.Width, sh = src.Height;
        var dp = dst.GetPixels();
        var buf = new byte[outW * outH * 4];

        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                float wz = inv[6] * x + inv[7] * y + inv[8];
                float sxf = (inv[0] * x + inv[1] * y + inv[2]) / wz;
                float syf = (inv[3] * x + inv[4] * y + inv[5]) / wz;
                int o = (y * outW + x) * 4;
                if (sxf < 0 || syf < 0 || sxf >= sw - 1 || syf >= sh - 1) { buf[o + 3] = 255; continue; }
                int x0 = (int)sxf, y0 = (int)syf;
                float fx = sxf - x0, fy = syf - y0;
                int p00 = y0 * srb + x0 * 4, p01 = p00 + 4, p10 = p00 + srb, p11 = p10 + 4;
                for (int c = 0; c < 3; c++)
                {
                    float v = sp[p00 + c] * (1 - fx) * (1 - fy) + sp[p01 + c] * fx * (1 - fy)
                            + sp[p10 + c] * (1 - fx) * fy + sp[p11 + c] * fx * fy;
                    buf[o + c] = (byte)v;
                }
                buf[o + 3] = 255;
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, dp, buf.Length);
        return dst;
    }

    private static float[] Invert3x3(float[] m)
    {
        float a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], hh = m[7], i = m[8];
        float det = a * (e * i - f * hh) - b * (d * i - f * g) + c * (d * hh - e * g);
        float id = 1f / det;
        return new[]
        {
            (e * i - f * hh) * id, (c * hh - b * i) * id, (b * f - c * e) * id,
            (f * g - d * i) * id, (a * i - c * g) * id, (c * d - a * f) * id,
            (d * hh - e * g) * id, (b * g - a * hh) * id, (a * e - b * d) * id,
        };
    }

    private static SKBitmap PadReplicate(SKBitmap src, float ratio)
    {
        int px = (int)(src.Width * ratio), py = (int)(src.Height * ratio);
        int w = src.Width + 2 * px, h = src.Height + 2 * py;
        var outB = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var sp = src.GetPixelSpan();
        int srb = src.RowBytes, sw = src.Width, sh = src.Height;
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Clamp(y - py, 0, sh - 1);
            int srow = sy * srb;
            int drow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Clamp(x - px, 0, sw - 1);
                int so = srow + sx * 4, dofs = drow + x * 4;
                buf[dofs] = sp[so]; buf[dofs + 1] = sp[so + 1]; buf[dofs + 2] = sp[so + 2]; buf[dofs + 3] = 255;
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, outB.GetPixels(), buf.Length);
        return outB;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Onnx.OnnxSessionPool.Release(_session);
        _disposed = true;
    }
}
