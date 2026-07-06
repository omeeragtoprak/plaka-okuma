using Anpr.Core.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Anpr.Core.Detection;

/// <summary>
/// YOLOv9-t "end2end" (NMS gömülü) ONNX plaka dedektörü.
/// AlprNet ile aynı modeli kullanır ama OpenCvSharp yerine SkiaSharp ile ön/son işleme yapar
/// (Apple Silicon / cross-platform CPU çalışabilsin diye).
///
/// Model I/O (probe ile doğrulandı):
///   input  "images"  Float [1,3,S,S]  (NCHW, RGB, /255, letterbox pad=114)
///   output "output0" Float [N,7]       (satır: [img_id, x1, y1, x2, y2, class_id, score])
/// </summary>
public sealed class LicensePlateDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _imgSize;
    private readonly float _scoreThreshold;
    private bool _disposed;

    public LicensePlateDetector(string modelPath, SessionOptions sessionOptions, float scoreThreshold = 0.25f)
    {
        var full = Path.GetFullPath(modelPath);
        if (!File.Exists(full)) throw new FileNotFoundException($"Dedektör modeli bulunamadı: {full}");

        _session = new InferenceSession(full, sessionOptions);
        _scoreThreshold = scoreThreshold;
        _inputName = _session.InputMetadata.Keys.First();

        var dims = _session.InputMetadata[_inputName].Dimensions; // [1,3,S,S]
        _imgSize = dims.Length >= 3 && dims[2] > 0 ? dims[2] : 640;
    }

    /// <summary>Modelin beklediği kare giriş boyutu (ör. 384).</summary>
    public int InputSize => _imgSize;

    public IReadOnlyList<PlateDetection> Detect(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var (tensor, ratio, padX, padY) = Preprocess(image);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

        using var results = _session.Run(inputs);
        var output = results[0].AsTensor<float>().ToArray();

        return Postprocess(output, ratio, padX, padY, image.Width, image.Height);
    }

    // Letterbox: en-boy oranını koruyarak yeniden boyutlandır, 114 ile ortala ve doldur.
    private (DenseTensor<float> tensor, float ratio, int padX, int padY) Preprocess(SKBitmap src)
    {
        int size = _imgSize;
        float ratio = Math.Min((float)size / src.Width, (float)size / src.Height);
        int newW = Math.Clamp((int)Math.Round(src.Width * ratio), 1, size);
        int newH = Math.Clamp((int)Math.Round(src.Height * ratio), 1, size);
        int padX = (size - newW) / 2;
        int padY = (size - newH) / 2;

        var chw = new float[3 * size * size];
        Array.Fill(chw, 114f / 255f); // letterbox dolgusu (tüm kanallara eşit)

        int plane = size * size;
        using var resized = SkiaImaging.ResizeRgba(src, newW, newH, SKFilterQuality.Low);
        var px = resized.GetPixelSpan();
        int rowBytes = resized.RowBytes;

        for (int y = 0; y < newH; y++)
        {
            int srcRow = y * rowBytes;
            int dstRow = (y + padY) * size;
            for (int x = 0; x < newW; x++)
            {
                int s = srcRow + x * 4; // RGBA
                int idx = dstRow + (x + padX);
                chw[idx] = px[s] / 255f;             // R
                chw[plane + idx] = px[s + 1] / 255f; // G
                chw[2 * plane + idx] = px[s + 2] / 255f; // B
            }
        }

        var tensor = new DenseTensor<float>(chw, new[] { 1, 3, size, size });
        return (tensor, ratio, padX, padY);
    }

    private List<PlateDetection> Postprocess(float[] preds, float ratio, int padX, int padY, int origW, int origH)
    {
        var results = new List<PlateDetection>();
        const int stride = 7; // [img_id, x1, y1, x2, y2, class_id, score]

        for (int i = 0; i + stride <= preds.Length; i += stride)
        {
            float score = preds[i + 6];
            if (score < _scoreThreshold) continue;

            float x1 = (preds[i + 1] - padX) / ratio;
            float y1 = (preds[i + 2] - padY) / ratio;
            float x2 = (preds[i + 3] - padX) / ratio;
            float y2 = (preds[i + 4] - padY) / ratio;
            int classId = (int)preds[i + 5];

            // x2/y2 dışlayıcı kenar olduğundan görüntü boyutuna (origW/origH) kadar izin ver;
            // origW-1'e sıkıştırmak kenara değen plakayı 1px kısaltırdı.
            x1 = Math.Clamp(x1, 0, origW);
            y1 = Math.Clamp(y1, 0, origH);
            x2 = Math.Clamp(x2, 0, origW);
            y2 = Math.Clamp(y2, 0, origH);

            int w = (int)(x2 - x1);
            int h = (int)(y2 - y1);
            if (w <= 1 || h <= 1) continue;

            results.Add(new PlateDetection(score, (int)x1, (int)y1, w, h, classId));
        }

        results.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        return results;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _disposed = true;
    }
}
