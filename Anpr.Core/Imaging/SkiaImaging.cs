using SkiaSharp;

namespace Anpr.Core.Imaging;

/// <summary>
/// SkiaSharp yardımcıları. Tüm görüntüler deterministik RGBA8888/Unpremul düzenine çözülür,
/// böylece piksel okuma (R,G,B,A sırası, satır = genişlik*4 bayt) sabittir.
/// </summary>
internal static class SkiaImaging
{
    /// <summary>Kodlanmış (JPEG/PNG...) byte dizisini RGBA8888 bitmap'e çözer.</summary>
    public static SKBitmap DecodeRgba(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("Görüntü çözülemedi (desteklenmeyen biçim?).");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bmp.Dispose();
            throw new InvalidDataException($"Görüntü çözülemedi: {result}");
        }

        // EXIF yönelimini uygula (SKCodec.GetPixels ham piksel verir, döndürmez).
        // Telefonla çekilmiş EXIF-döndürülmüş fotoğraflar doğru yönde olsun diye.
        return ApplyOrigin(bmp, codec.EncodedOrigin);
    }

    private static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft)
            return src;

        bool swapWh = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                                or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var dstInfo = new SKImageInfo(swapWh ? src.Height : src.Width, swapWh ? src.Width : src.Height,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);

        var dst = new SKBitmap(dstInfo);
        using (var canvas = new SKCanvas(dst))
        {
            // Hedef merkezine taşı, EXIF dönüşümünü uygula, sonra görüntüyü çiz.
            canvas.Translate(dstInfo.Width / 2f, dstInfo.Height / 2f);
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); break;       // yatay ayna
                case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); break;     // dikey ayna
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.RotateDegrees(270); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftBottom: canvas.RotateDegrees(270); break;
            }
            canvas.Translate(-src.Width / 2f, -src.Height / 2f);
            canvas.DrawBitmap(src, 0, 0);
        }
        src.Dispose();
        return dst;
    }

    /// <summary>Ham RGBA byte dizisinden (ffmpeg rawvideo çıktısı gibi) bitmap kurar (kopyalar).</summary>
    public static SKBitmap FromRgba(byte[] rgba, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bmp = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bmp.GetPixels(), info.RowBytes * height);
        return bmp;
    }

    /// <summary>RGBA8888 olarak yeniden boyutlandırır.</summary>
    public static SKBitmap ResizeRgba(SKBitmap src, int width, int height, SKFilterQuality quality = SKFilterQuality.Medium)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        return src.Resize(info, quality)
            ?? throw new InvalidOperationException("Görüntü yeniden boyutlandırılamadı.");
    }

    /// <summary>Alt bölgeyi paylaşımlı (kopyasız) çıkarır. Dönen bitmap, src yaşadığı sürece geçerlidir.</summary>
    public static SKBitmap ExtractSubset(SKBitmap src, SKRectI rect)
    {
        var sub = new SKBitmap();
        if (!src.ExtractSubset(sub, rect))
        {
            sub.Dispose();
            throw new InvalidOperationException("Alt bölge çıkarılamadı.");
        }
        return sub;
    }
}
