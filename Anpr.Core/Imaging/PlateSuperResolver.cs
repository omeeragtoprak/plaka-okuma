using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Anpr.Core.Imaging;

/// <summary>
/// FSRCNN ×2 süper çözünürlük (İkinci-Şans Şeridi, Aşama 4): KÜÇÜK (&lt;100 px) plaka kırpıntısının
/// Y (parlaklık) kanalı ağla, renk kanalları bikübik büyütülür. ÖLÇÜM: küçük dilimde 28→30/41
/// (+2/−0, oylama-birleşimli); 0.8 ms/çağrı (2 thread). Model: DIV2K-eğitimli FSRCNN_x2 (Saafke)
/// tf2onnx dönüşümü — opset 17, yalnız Conv/Relu/DepthToSpace (ConvInteger YOK).
/// cv2 ile AYNI YCrCb matematiği (BT.601 tam-aralık) — ölçülen python davranışı birebir taşınır.
/// </summary>
public sealed class PlateSuperResolver : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private bool _disposed;

    public PlateSuperResolver(string modelPath, SessionOptions options)
    {
        _session = new InferenceSession(Path.GetFullPath(modelPath), options);
        _inputName = _session.InputMetadata.Keys.First();
    }

    /// <summary>Kırpıntıyı ×2 büyütür (Y: ağ, CrCb: bikübik). Hata durumunda null (aşama atlanır).</summary>
    public SKBitmap? Upscale(SKBitmap crop)
    {
        ArgumentNullException.ThrowIfNull(crop);
        int w = crop.Width, h = crop.Height;
        if (w < 8 || h < 4) return null;

        var px = crop.GetPixelSpan();
        int rb = crop.RowBytes;

        // RGB -> Y (ağ girişi) + Cr/Cb düzlemleri (cv2 BT.601 tam-aralık)
        var yT = new DenseTensor<float>(new[] { 1, h, w, 1 });
        var cr = new float[w * h];
        var cb = new float[w * h];
        for (int yy = 0; yy < h; yy++)
        {
            int row = yy * rb, o = yy * w;
            for (int xx = 0; xx < w; xx++)
            {
                int s = row + xx * 4;
                float r = px[s], g = px[s + 1], b = px[s + 2];
                float Y = 0.299f * r + 0.587f * g + 0.114f * b;
                yT[0, yy, xx, 0] = Y / 255f;
                cr[o + xx] = (r - Y) * 0.713f + 128f;
                cb[o + xx] = (b - Y) * 0.564f + 128f;
            }
        }

        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, yT) });
        var outY = results.First().AsTensor<float>();
        int H = outY.Dimensions[1], W = outY.Dimensions[2];

        // CrCb bikübik yerine bilinear ×2 (renk kanalında fark ihmal edilebilir; SkiaSharp ile ucuz)
        var outBmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var buf = new byte[W * H * 4];
        float sxScale = (float)w / W, syScale = (float)h / H;
        for (int yy = 0; yy < H; yy++)
        {
            float sy = Math.Min(h - 1.001f, yy * syScale);
            int y0 = (int)sy; float fy = sy - y0;
            for (int xx = 0; xx < W; xx++)
            {
                float sx = Math.Min(w - 1.001f, xx * sxScale);
                int x0 = (int)sx; float fx = sx - x0;
                int i00 = y0 * w + x0, i01 = i00 + 1, i10 = i00 + w, i11 = i10 + 1;
                float crV = cr[i00] * (1 - fx) * (1 - fy) + cr[i01] * fx * (1 - fy) + cr[i10] * (1 - fx) * fy + cr[i11] * fx * fy;
                float cbV = cb[i00] * (1 - fx) * (1 - fy) + cb[i01] * fx * (1 - fy) + cb[i10] * (1 - fx) * fy + cb[i11] * fx * fy;
                float Y = Math.Clamp(outY[0, yy, xx, 0] * 255f, 0f, 255f);
                // YCrCb -> RGB (cv2 tersi)
                float r = Y + 1.403f * (crV - 128f);
                float g = Y - 0.714f * (crV - 128f) - 0.344f * (cbV - 128f);
                float b = Y + 1.773f * (cbV - 128f);
                int o = (yy * W + xx) * 4;
                buf[o] = (byte)Math.Clamp(r, 0, 255);
                buf[o + 1] = (byte)Math.Clamp(g, 0, 255);
                buf[o + 2] = (byte)Math.Clamp(b, 0, 255);
                buf[o + 3] = 255;
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, outBmp.GetPixels(), buf.Length);
        return outBmp;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _disposed = true;
    }
}
