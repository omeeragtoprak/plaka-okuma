using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Anpr.Core.Imaging;

/// <summary>
/// Bir karenin piksellerine <b>biçimden bağımsız</b> erişim. Motor (dedektör + OCR) kareyi artık "tam RGBA
/// bitmap" olarak değil bu arayüz üzerinden görür:
/// <list type="bullet">
/// <item><see cref="FillDetectorInput"/> — dedektörün küçük (ör. 384×384) girdisini kaynaktan DOĞRUDAN
/// örnekler; maliyet kaynağın değil ÇIKTININ piksel sayısıyla orantılıdır.</item>
/// <item><see cref="ExtractRgba"/> — yalnız istenen küçük bölgeyi (plaka kırpıntısı) RGBA'ya çevirir.</item>
/// </list>
/// <para><b>Neden (2026-09-30 ölçümü):</b> 4 MP karede her işlenen kare için 16 MB'lık RGBA kopyası üretiliyordu
/// (bellek + ~2 ms, zayıf PC'de ~8 ms). Kamera akışı zaten YUV 4:2:0'dır; onu ffmpeg'de BGRA'ya çevirmek
/// donanım çözmeli yolda ffmpeg işlemci zamanının üçte ikisiydi (%14 → %4–6 tek çekirdek). YUV kare doğrudan
/// bu arayüzle okunur; hiçbir yerde tam kare RGB'ye çevrilmez.</para>
/// </summary>
public interface IFrameView
{
    /// <summary>Kare genişliği (piksel).</summary>
    int Width { get; }

    /// <summary>Kare yüksekliği (piksel).</summary>
    int Height { get; }

    /// <summary>
    /// <see cref="ExtractRgba"/> bağımsız bir KOPYA mı döndürür (true) yoksa kareyle piksel paylaşan bir
    /// görünüm mü (false)? Kopyaysa çağıran onu saklayabilir; görünümse saklamadan önce kopyalamalıdır.
    /// </summary>
    bool ExtractIsOwnedCopy { get; }

    /// <summary>
    /// Dedektör girdisi: <paramref name="region"/> bölgesini <paramref name="newW"/>×<paramref name="newH"/>
    /// boyutuna çift doğrusal (bilinear, piksel-merkezli) örnekler ve R,G,B düzlemlerini [0..1] aralığında
    /// <paramref name="chw"/> dizisine yazar. Düzlem boyu <paramref name="size"/>², görüntü
    /// (<paramref name="padX"/>, <paramref name="padY"/>) kaydırmasıyla yerleştirilir (letterbox).
    /// Dolgu alanına DOKUNULMAZ (çağıran doldurur).
    /// </summary>
    void FillDetectorInput(SKRectI region, float[] chw, int size, int newW, int newH, int padX, int padY);

    /// <summary>Bölgeyi RGBA8888/Unpremul bitmap olarak verir; çağıran dispose eder.</summary>
    SKBitmap ExtractRgba(SKRectI rect);
}

/// <summary>
/// RGBA <see cref="SKBitmap"/> üstünde <see cref="IFrameView"/>. Davranış, görünüm soyutlaması gelmeden
/// önceki yolla <b>bire bir aynıdır</b>: alt bölge piksel paylaşımlı çıkarılır, dedektör girdisi SkiaSharp'ın
/// <c>Low</c> (bilinear) yeniden boyutlandırmasıyla üretilir. JPEG/PNG/BMP kaynaklı her çağrı bu yoldan geçer.
/// Bitmap'in sahibi çağırandır (bu sınıf dispose etmez).
/// </summary>
public sealed class SkBitmapFrameView : IFrameView
{
    private readonly SKBitmap _image;

    /// <param name="image">RGBA8888 bitmap (sahibi çağıran; görünüm yaşadığı sürece geçerli kalmalı).</param>
    public SkBitmapFrameView(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _image = image;
    }

    /// <inheritdoc/>
    public int Width => _image.Width;
    /// <inheritdoc/>
    public int Height => _image.Height;
    /// <inheritdoc/>
    public bool ExtractIsOwnedCopy => false;

    /// <inheritdoc/>
    public void FillDetectorInput(SKRectI region, float[] chw, int size, int newW, int newH, int padX, int padY)
    {
        bool full = region.Left == 0 && region.Top == 0 && region.Width == _image.Width && region.Height == _image.Height;
        SKBitmap? sub = full ? null : SkiaImaging.ExtractSubset(_image, region);
        try
        {
            using var resized = SkiaImaging.ResizeRgba(sub ?? _image, newW, newH, SKFilterQuality.Low);
            var px = resized.GetPixelSpan();
            int rowBytes = resized.RowBytes;
            int plane = size * size;
            for (int y = 0; y < newH; y++)
            {
                int srcRow = y * rowBytes;
                int dstRow = (y + padY) * size;
                for (int x = 0; x < newW; x++)
                {
                    int s = srcRow + x * 4; // RGBA
                    int idx = dstRow + (x + padX);
                    chw[idx] = px[s] / 255f;                 // R
                    chw[plane + idx] = px[s + 1] / 255f;     // G
                    chw[2 * plane + idx] = px[s + 2] / 255f; // B
                }
            }
        }
        finally { sub?.Dispose(); }
    }

    /// <inheritdoc/>
    public SKBitmap ExtractRgba(SKRectI rect) => SkiaImaging.ExtractSubset(_image, rect);
}

/// <summary>
/// Düzlemsel YUV 4:2:0 (I420) kare üstünde <see cref="IFrameView"/> — kameranın/çözücünün ürettiği biçim.
/// Bellek düzeni: Y (G×Y), ardından U ve V (her biri ⌈G/2⌉×⌈Y/2⌉). 4 MP karede 6,1 MB (BGRA: 16,3 MB).
/// <para><b>Renk dönüşümü ffmpeg'in BGRA çıkışıyla eşleştirildi (2026-09-30, aynı kare iki yoldan):</b>
/// BT.601 + en-yakın-komşu kroma çoğaltma → ortalama mutlak fark 0,97 seviye, azami 3 (yalnız yuvarlama).
/// BT.709 ya da bilinear kroma daha UZAK çıktı (p99 4–5, azami 26–82). Yani bu sınıfın ürettiği pikseller,
/// modellerin aylardır gördüğü BGRA yolunun pikselleriyle yuvarlama farkı dışında aynıdır.</para>
/// </summary>
public sealed class Yuv420FrameView : IFrameView
{
    private readonly byte[] _d;
    private readonly int _yOff, _uOff, _vOff, _w, _h, _cw;
    private readonly ColorTables _t;

    /// <param name="data">Y, U, V düzlemlerini art arda taşıyan tampon.</param>
    /// <param name="offset">Y düzleminin tampondaki başlangıcı.</param>
    /// <param name="width">Kare genişliği.</param>
    /// <param name="height">Kare yüksekliği.</param>
    /// <param name="fullRange">true: tam aralık (0–255, "yuvj420p"); false: sınırlı aralık (16–235, yayın standardı).</param>
    public Yuv420FrameView(byte[] data, int offset, int width, int height, bool fullRange = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (width < 2 || height < 2 || width > 16384 || height > 16384)
            throw new ArgumentOutOfRangeException(nameof(width), $"kare boyutu geçersiz ({width}x{height})");
        int cw = (width + 1) / 2, ch = (height + 1) / 2;
        long need = (long)width * height + 2L * cw * ch;
        if (offset < 0 || data.LongLength - offset < need)
            throw new ArgumentException($"YUV 4:2:0 için veri yetersiz (gereken {need} B, gelen {data.LongLength - offset} B)", nameof(data));
        _d = data; _w = width; _h = height; _cw = cw;
        _yOff = offset; _uOff = offset + width * height; _vOff = _uOff + cw * ch;
        _t = fullRange ? ColorTables.Full : ColorTables.Limited;
    }

    /// <summary>Bir kare için gereken bayt sayısı (Y + U + V).</summary>
    public static int FrameBytes(int width, int height) => width * height + 2 * ((width + 1) / 2) * ((height + 1) / 2);

    /// <inheritdoc/>
    public int Width => _w;
    /// <inheritdoc/>
    public int Height => _h;
    /// <inheritdoc/>
    public bool ExtractIsOwnedCopy => true;

    /// <summary>(x,y) pikselinin parlaklığı (Y). Hareket kapısı gibi yalnız luma isteyenler için.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Luma(int x, int y) => _d[_yOff + y * _w + x];

    /// <inheritdoc/>
    public void FillDetectorInput(SKRectI region, float[] chw, int size, int newW, int newH, int padX, int padY)
        => Sample(region, newW, newH, chw, size, padX, padY, default, 0);

    /// <summary>Bölgeyi <paramref name="dstW"/>×<paramref name="dstH"/> boyutunda RGBA bitmap olarak verir
    /// (çift doğrusal örnekleme). Panel önizlemesi gibi küçültülmüş görüntüler için: tam kare çevrilmez.</summary>
    public SKBitmap ScaleToRgba(SKRectI region, int dstW, int dstH)
    {
        if (dstW < 1 || dstH < 1) throw new ArgumentOutOfRangeException(nameof(dstW));
        var bmp = new SKBitmap(new SKImageInfo(dstW, dstH, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        try
        {
            Sample(region, dstW, dstH, null, 0, 0, 0, WritablePixels(bmp), bmp.RowBytes);
            return bmp;
        }
        catch { bmp.Dispose(); throw; }
    }

    // Ortak çift doğrusal örnekleyici. chw != null → R,G,B düzlemlerine [0..1] float yazar (dedektör girdisi);
    // chw == null → rgba'ya bayt yazar (küçültülmüş önizleme). Çıktı pikselinin MERKEZİ kaynağa taşınır
    // (Skia/cv2 bilinear ile aynı kural); kenarda bölge sınırına kıstırılır (alt-bitmap davranışı).
    private void Sample(SKRectI region, int newW, int newH, float[]? chw, int size, int padX, int padY,
                        Span<byte> rgba, int rgbaRowBytes)
    {
        var r = ClampRect(region);
        if (r.Width < 1 || r.Height < 1 || newW < 1 || newH < 1) return;
        int plane = size * size;

        int[] xa = new int[newW], xb = new int[newW];
        float[] fx = new float[newW];
        double sxScale = (double)r.Width / newW;
        for (int ox = 0; ox < newW; ox++)
        {
            double sx = (ox + 0.5) * sxScale - 0.5;
            int x0 = (int)Math.Floor(sx);
            fx[ox] = (float)(sx - x0);
            xa[ox] = r.Left + Math.Clamp(x0, 0, r.Width - 1);
            xb[ox] = r.Left + Math.Clamp(x0 + 1, 0, r.Width - 1);
        }

        var d = _d;
        var t = _t;
        double syScale = (double)r.Height / newH;
        for (int oy = 0; oy < newH; oy++)
        {
            double sy = (oy + 0.5) * syScale - 0.5;
            int y0 = (int)Math.Floor(sy);
            float fy = (float)(sy - y0);
            int ya = r.Top + Math.Clamp(y0, 0, r.Height - 1);
            int yb = r.Top + Math.Clamp(y0 + 1, 0, r.Height - 1);
            int rowA = _yOff + ya * _w, rowB = _yOff + yb * _w;
            // Kroma "en yakın komşu" çoğaltılmış sayılır: (x,y) pikselinin kroması = C[y/2][x/2].
            int uA = _uOff + (ya >> 1) * _cw, uB = _uOff + (yb >> 1) * _cw;
            int vA = _vOff + (ya >> 1) * _cw, vB = _vOff + (yb >> 1) * _cw;
            int dstRow = (oy + padY) * size + padX;
            int o = oy * rgbaRowBytes;

            for (int ox = 0; ox < newW; ox++)
            {
                int a = xa[ox], b = xb[ox], ca = a >> 1, cb = b >> 1;
                float wx = fx[ox];
                // Y, U, V ayrı ayrı harmanlanır, renk dönüşümü EN SONDA bir kez yapılır (dönüşüm doğrusaldır;
                // harman→dönüşüm ile dönüşüm→harman yalnız kırpma/yuvarlama farkıyla ayrılır).
                float yTop = d[rowA + a] + (d[rowA + b] - d[rowA + a]) * wx;
                float yBot = d[rowB + a] + (d[rowB + b] - d[rowB + a]) * wx;
                float uTop = d[uA + ca] + (d[uA + cb] - d[uA + ca]) * wx;
                float uBot = d[uB + ca] + (d[uB + cb] - d[uB + ca]) * wx;
                float vTop = d[vA + ca] + (d[vA + cb] - d[vA + ca]) * wx;
                float vBot = d[vB + ca] + (d[vB + cb] - d[vB + ca]) * wx;
                float yy = (yTop + (yBot - yTop) * fy - t.YOffset) * t.YGain;
                float uu = uTop + (uBot - uTop) * fy - 128f;
                float vv = vTop + (vBot - vTop) * fy - 128f;
                float rr = yy + t.Rv * vv, gg = yy + t.Gu * uu + t.Gv * vv, bb = yy + t.Bu * uu;

                if (chw != null)
                {
                    int idx = dstRow + ox;
                    chw[idx] = Unit(rr);
                    chw[plane + idx] = Unit(gg);
                    chw[2 * plane + idx] = Unit(bb);
                }
                else
                {
                    rgba[o] = Byte8(rr); rgba[o + 1] = Byte8(gg); rgba[o + 2] = Byte8(bb); rgba[o + 3] = 255;
                    o += 4;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Unit(float v) => v <= 0f ? 0f : v >= 255f ? 1f : v * (1f / 255f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Byte8(float v) => v <= 0f ? (byte)0 : v >= 255f ? (byte)255 : (byte)(v + 0.5f);

    /// <inheritdoc/>
    public SKBitmap ExtractRgba(SKRectI rect)
    {
        var r = ClampRect(rect);
        if (r.Width < 1 || r.Height < 1) throw new InvalidOperationException("Alt bölge çıkarılamadı.");
        var bmp = new SKBitmap(new SKImageInfo(r.Width, r.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        try
        {
            ConvertRegion(r, WritablePixels(bmp), bmp.RowBytes);
            return bmp;
        }
        catch { bmp.Dispose(); throw; }
    }

    // SkiaSharp 2.88'de GetPixelSpan() salt-okunurdur; yazmak için piksel işaretçisi üstünde Span kurulur.
    private static unsafe Span<byte> WritablePixels(SKBitmap bmp) => new((void*)bmp.GetPixels(), bmp.RowBytes * bmp.Height);

    private void ConvertRegion(SKRectI r, Span<byte> dst, int rowBytes)
    {
        var d = _d;
        int[] yt = _t.Y, rv = _t.RvTab, gu = _t.GuTab, gv = _t.GvTab, bu = _t.BuTab;
        for (int y = r.Top; y < r.Bottom; y++)
        {
            int yRow = _yOff + y * _w;
            int uRow = _uOff + (y >> 1) * _cw, vRow = _vOff + (y >> 1) * _cw;
            var row = dst.Slice((y - r.Top) * rowBytes, r.Width * 4);
            int o = 0;
            for (int x = r.Left; x < r.Right; x++, o += 4)
            {
                int c = x >> 1;
                int u = d[uRow + c], v = d[vRow + c];
                int yy = yt[d[yRow + x]];
                row[o] = Clamp8((yy + rv[v] + 32768) >> 16);
                row[o + 1] = Clamp8((yy + gu[u] + gv[v] + 32768) >> 16);
                row[o + 2] = Clamp8((yy + bu[u] + 32768) >> 16);
                row[o + 3] = 255;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp8(int v) => (uint)v > 255u ? (v < 0 ? (byte)0 : (byte)255) : (byte)v;

    private SKRectI ClampRect(SKRectI r)
    {
        int l = Math.Clamp(r.Left, 0, _w), t = Math.Clamp(r.Top, 0, _h);
        int rr = Math.Clamp(r.Right, l, _w), b = Math.Clamp(r.Bottom, t, _h);
        return new SKRectI(l, t, rr, b);
    }

    /// <summary>YUV→RGB katsayıları (BT.601). 16.16 sabit nokta tabloları + kayan nokta karşılıkları.</summary>
    private sealed class ColorTables
    {
        public readonly int[] Y = new int[256], RvTab = new int[256], GuTab = new int[256], GvTab = new int[256], BuTab = new int[256];
        public readonly float YOffset, YGain, Rv, Gu, Gv, Bu;

        public static readonly ColorTables Limited = new(fullRange: false);
        public static readonly ColorTables Full = new(fullRange: true);

        private ColorTables(bool fullRange)
        {
            // BT.601: Kr=0,299 Kb=0,114. Sınırlı aralıkta luma 16–235 (×255/219), kroma 16–240 (×255/224).
            double yGain = fullRange ? 1.0 : 255.0 / 219.0;
            double cGain = fullRange ? 1.0 : 255.0 / 224.0;
            double rv = 1.402 * cGain, gu = -0.344136 * cGain, gv = -0.714136 * cGain, bu = 1.772 * cGain;
            YOffset = fullRange ? 0f : 16f; YGain = (float)yGain;
            Rv = (float)rv; Gu = (float)gu; Gv = (float)gv; Bu = (float)bu;
            for (int i = 0; i < 256; i++)
            {
                Y[i] = (int)Math.Round((i - (fullRange ? 0 : 16)) * yGain * 65536.0);
                RvTab[i] = (int)Math.Round((i - 128) * rv * 65536.0);
                GuTab[i] = (int)Math.Round((i - 128) * gu * 65536.0);
                GvTab[i] = (int)Math.Round((i - 128) * gv * 65536.0);
                BuTab[i] = (int)Math.Round((i - 128) * bu * 65536.0);
            }
        }
    }
}
