using Anpr.Core.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Anpr.Core.Recognition;

/// <summary>
/// fast-plate-ocr CCT ONNX karakter tanıyıcısı (AlprNet ile aynı model).
/// SkiaSharp ile ön işleme yapar (OpenCvSharp bağımlılığı yok).
///
/// Model I/O (probe ile doğrulandı):
///   input  "input"      UInt8 [N,64,128,3]  (NHWC, RGB, normalize YOK — ham 0..255)
///   output "Identity:0" Float [N,9,37]      (slot x vocab; vocab='0-9A-Z_')
///
/// Not: cct config'inde keep_aspect_ratio=false olduğundan görüntü doğrudan 128x64'e
/// "stretch" edilir (modelin eğitildiği ön işleme). keep_aspect_ratio=true ise en-boy
/// korunup 114 ile ortalanarak doldurulur.
/// </summary>
public sealed class LicensePlateRecognizer : IDisposable
{
    private readonly InferenceSession _session;
    private readonly PlateOcrConfig _config;
    private readonly string _inputName;
    private readonly string _plateOutputName;
    private bool _disposed;

    public LicensePlateRecognizer(string modelPath, string configPath, SessionOptions sessionOptions)
    {
        var full = Path.GetFullPath(modelPath);
        if (!File.Exists(full)) throw new FileNotFoundException($"OCR modeli bulunamadı: {full}");

        _config = PlateOcrConfig.FromYaml(configPath);
        _session = new InferenceSession(full, sessionOptions);
        _inputName = _session.InputMetadata.Keys.First();

        // V1 tek çıktı ("Identity:0"); V2 iki çıktı: "plate" [N,slots,vocab] + "region" [N,66].
        // Karakter çıktısını ADIYLA seç (region'ı yok say).
        _plateOutputName = _session.OutputMetadata.Keys.FirstOrDefault(k =>
                               k.Equals("plate", StringComparison.OrdinalIgnoreCase))
                           ?? _session.OutputMetadata.Keys.First();
    }

    public PlateOcrConfig Config => _config;

    public PlateOcrResult Recognize(SKBitmap plateCrop)
    {
        ArgumentNullException.ThrowIfNull(plateCrop);

        var input = BuildInput(plateCrop);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, input) };

        using var results = _session.Run(inputs);
        var plate = results.First(r => r.Name == _plateOutputName);
        var logits = plate.AsTensor<float>().ToArray(); // [1,slots,vocab] düz dizi

        return Decode(logits);
    }

    // NHWC uint8 tensör üretir. RGB sırası (model RGB bekliyor).
    private DenseTensor<byte> BuildInput(SKBitmap src)
    {
        int W = _config.ImgWidth, H = _config.ImgHeight, C = _config.NumChannels;
        var tensor = new DenseTensor<byte>(new[] { 1, H, W, C });

        if (!_config.KeepAspectRatio)
        {
            using var resized = SkiaImaging.ResizeRgba(src, W, H, SKFilterQuality.Medium);
            CopyInto(tensor, resized, 0, 0, C);
        }
        else
        {
            tensor.Buffer.Span.Fill(114); // letterbox dolgusu

            float scale = Math.Min((float)W / src.Width, (float)H / src.Height);
            int nw = Math.Max(1, (int)(src.Width * scale));
            int nh = Math.Max(1, (int)(src.Height * scale));
            using var resized = SkiaImaging.ResizeRgba(src, nw, nh, SKFilterQuality.Medium);
            CopyInto(tensor, resized, (W - nw) / 2, (H - nh) / 2, C);
        }

        return tensor;
    }

    private static void CopyInto(DenseTensor<byte> tensor, SKBitmap img, int offsetX, int offsetY, int channels)
    {
        var px = img.GetPixelSpan();
        int rowBytes = img.RowBytes;
        for (int y = 0; y < img.Height; y++)
        {
            int srcRow = y * rowBytes;
            int ty = y + offsetY;
            for (int x = 0; x < img.Width; x++)
            {
                int s = srcRow + x * 4; // RGBA
                int tx = x + offsetX;
                if (channels == 3)
                {
                    tensor[0, ty, tx, 0] = px[s];     // R
                    tensor[0, ty, tx, 1] = px[s + 1]; // G
                    tensor[0, ty, tx, 2] = px[s + 2]; // B
                }
                else // grayscale: ITU-R 601 luma
                {
                    tensor[0, ty, tx, 0] = (byte)((px[s] * 299 + px[s + 1] * 587 + px[s + 2] * 114) / 1000);
                }
            }
        }
    }

    // Her slot için argmax; pad karakterlerini at, ortalama güveni hesapla.
    // ÖNEMLİ: Bu CCT modelinin "Identity:0" çıktısı ZATEN softmax olasılığıdır
    // (her slot vocab üzerinde toplamı 1.0, değerler [0,1] — probe ile doğrulandı).
    // Bu yüzden TEKRAR softmax UYGULANMAZ; güven = doğrudan slottaki en yüksek olasılık.
    // (AlprNet burada tekrar softmax uygular; metin doğru çıkar ama güven skoru hatalı olur.)
    private PlateOcrResult Decode(float[] probs)
    {
        int slots = _config.MaxPlateSlots;
        int vocab = _config.Alphabet.Length;

        var chars = new char[slots];

        float sumNonPad = 0f;
        int countNonPad = 0;
        char padChar = _config.PadChar[0];

        for (int t = 0; t < slots; t++)
        {
            int start = t * vocab;

            float max = float.NegativeInfinity;
            int bestIdx = 0;
            for (int c = 0; c < vocab; c++)
            {
                float v = probs[start + c];
                if (v > max) { max = v; bestIdx = c; }
            }

            char ch = _config.Alphabet[bestIdx];
            chars[t] = ch;

            if (ch != padChar)
            {
                sumNonPad += max;
                countNonPad++;
            }
        }

        string text = new string(chars).Replace(_config.PadChar, string.Empty).Trim();
        float meanConf = countNonPad > 0 ? sumNonPad / countNonPad : 0f;

        // Tam slot×vocab dağılımını (kopya) dışa ver: oylama + gramer-kısıtlı çözümleme için.
        var slotProbs = new float[slots * vocab];
        Array.Copy(probs, slotProbs, Math.Min(probs.Length, slotProbs.Length));

        return new PlateOcrResult(text, meanConf, slotProbs, _config.Alphabet);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _disposed = true;
    }
}
