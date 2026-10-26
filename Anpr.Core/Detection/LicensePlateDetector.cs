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
        : this(modelPath, sessionOptions, scoreThreshold, shareKey: null) { }

    /// <summary><paramref name="shareKey"/> null değilse oturum aynı model+ayar için paylaşılır (bkz. OnnxSessionPool).</summary>
    internal LicensePlateDetector(string modelPath, SessionOptions sessionOptions, float scoreThreshold, string? shareKey)
    {
        var full = Path.GetFullPath(modelPath);
        if (!File.Exists(full)) throw new FileNotFoundException($"Dedektör modeli bulunamadı: {full}");

        _session = Onnx.OnnxSessionPool.Acquire(full, sessionOptions, shareKey);
        _scoreThreshold = scoreThreshold;
        _inputName = _session.InputMetadata.Keys.First();

        var dims = _session.InputMetadata[_inputName].Dimensions; // [1,3,S,S]
        _imgSize = dims.Length >= 3 && dims[2] > 0 ? dims[2] : 640;
    }

    /// <summary>Teşhis/test: kullanılan ONNX oturumu (paylaşım doğrulaması için).</summary>
    internal InferenceSession Session => _session;

    /// <summary>Modelin beklediği kare giriş boyutu (ör. 384).</summary>
    public int InputSize => _imgSize;

    /// <summary>RGBA bitmap'in TAMAMINDA tespit (eski imza; davranış değişmedi).</summary>
    public IReadOnlyList<PlateDetection> Detect(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Detect(new SkBitmapFrameView(image), new SKRectI(0, 0, image.Width, image.Height));
    }

    // Girdi tamponu oturum boyunca yeniden kullanılır: 384×384×3 float = 1,8 MB → her çıkarımda büyük-nesne
    // yığınına (LOH) yeni tahsis ve ardından Gen2 çöp toplama demekti. Dedektör zaten tek iş parçacıklıdır.
    private float[]? _chw;
    private (int W, int H, int PadX, int PadY) _chwLayout;

    /// <summary>
    /// Karenin <paramref name="region"/> bölgesinde tespit. Bölge, dedektör girdisine letterbox ile sığdırılır
    /// (bölge küçüldükçe plaka girdide BÜYÜR). Dönen kutular TAM KARE koordinatlarındadır.
    /// </summary>
    public IReadOnlyList<PlateDetection> Detect(IFrameView view, SKRectI region)
    {
        ArgumentNullException.ThrowIfNull(view);
        int rw = region.Width, rh = region.Height;
        if (rw < 1 || rh < 1) return Array.Empty<PlateDetection>();

        // Letterbox: en-boy oranını koruyarak yeniden boyutlandır, 114 ile ortala ve doldur.
        int size = _imgSize;
        float ratio = Math.Min((float)size / rw, (float)size / rh);
        int newW = Math.Clamp((int)Math.Round(rw * ratio), 1, size);
        int newH = Math.Clamp((int)Math.Round(rh * ratio), 1, size);
        int padX = (size - newW) / 2;
        int padY = (size - newH) / 2;

        var chw = _chw ??= new float[3 * size * size];
        var layout = (newW, newH, padX, padY);
        if (layout != _chwLayout)
        {
            Array.Fill(chw, 114f / 255f); // letterbox dolgusu (tüm kanallara eşit); görüntü alanı aşağıda yazılır
            _chwLayout = layout;
        }
        view.FillDetectorInput(region, chw, size, newW, newH, padX, padY);

        var tensor = new DenseTensor<float>(chw, new[] { 1, 3, size, size });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

        using var results = _session.Run(inputs);
        var output = results[0].AsTensor<float>().ToArray();

        var dets = Postprocess(output, ratio, padX, padY, rw, rh);
        if (region.Left != 0 || region.Top != 0)
            for (int i = 0; i < dets.Count; i++)
                dets[i] = dets[i] with { X = dets[i].X + region.Left, Y = dets[i].Y + region.Top }; // tam-kare koordinatı
        return dets;
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
        Onnx.OnnxSessionPool.Release(_session);
        _disposed = true;
    }
}
