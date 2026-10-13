using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Anpr.Core.Imaging;
using SkiaSharp;

namespace Anpr.Sdk.Frames;

/// <summary>Kaynağın verdiği karenin piksel biçimi.</summary>
public enum VideoPixelFormat
{
    /// <summary>Kodlanmış JPEG (ffmpeg MJPEG yolu — eski davranış). Kullanılmadan önce çözülmesi gerekir.</summary>
    Jpeg = 0,

    /// <summary>
    /// Ham 32-bit BGRA pikseller (ffmpeg BMP yolu). Kodlama/çözme YOK: ffmpeg'in ürettiği pikseller olduğu
    /// gibi gelir, modele girmeden önce tek kopyayla RGBA'ya çevrilir.
    /// </summary>
    Bgra32 = 1,

    /// <summary>
    /// Düzlemsel YUV 4:2:0 (I420; ffmpeg Y4M yolu). Kameranın/çözücünün KENDİ biçimi: ffmpeg'de renk
    /// dönüşümü yapılmaz, kare 16,3 MB yerine 6,1 MB'tır (4 MP) ve motor onu tam kare RGB'ye çevirmeden
    /// okur (<see cref="VideoFrame.TryGetView"/>). Ölçüm (2026-09-30, 4 MP HEVC, kamera başına ffmpeg):
    /// donanım çözmede işlemci %14 → %4–6, yazılım çözmede bellek 93 → 51 MB.
    /// </summary>
    Yuv420p = 2,
}

/// <summary>
/// Kameradan gelen TEK kare. Kare kaynağı her kare için AYRI bir nesne üretir (mutasyon yok) — bu yüzden
/// referansı tutmak güvenlidir ve "aynı fiziksel kare mi" sorusu referans kimliğiyle O(1) cevaplanır
/// (bkz. <c>CameraWorker.IsNewFrame</c>).
/// <para><b>NEDEN VAR (Faz 2, 2026-09-01 ölçümü):</b> eski hat ffmpeg'de kareyi MJPEG'e KODLUYOR, C#'ta
/// yeniden ÇÖZÜYORDU. 4 MP HEVC akışında kamera başına ffmpeg maliyetinin %43'ü bu gereksiz JPEG
/// kodlamasıydı (%28 → %16 tek-çekirdek); üstüne her işlenen karede 4 MP JPEG çözme (zayıf x64'te
/// 15–25 ms) ekleniyordu. <see cref="VideoPixelFormat.Bgra32"/> yolu ikisini de kaldırır. Pikseller aynı kaynaktan
/// (aynı çözücü çıktısı) geldiği ve modele aynı RGBA düzeninde girdiği için doğruluk yapısal olarak
/// korunur; JPEG q4 kayıpları da ortadan kalkar.</para>
/// </summary>
public sealed class VideoFrame
{
    /// <summary>BITMAPFILEHEADER (14) + BITMAPINFOHEADER (40) — ffmpeg'in yazdığı en küçük başlık.</summary>
    public const int BmpHeaderMin = 54;

    /// <summary>Ham veri. <see cref="VideoPixelFormat.Jpeg"/>: tam JPEG dosyası. <see cref="VideoPixelFormat.Bgra32"/>: tam BMP dosyası (başlık + pikseller).</summary>
    public byte[] Data { get; }
    public VideoPixelFormat Format { get; }
    /// <summary>Piksel genişliği. JPEG karede başlık okunmadığı için 0 olabilir.</summary>
    public int Width { get; }
    /// <summary>Piksel yüksekliği. JPEG karede 0 olabilir.</summary>
    public int Height { get; }
    /// <summary>Bgra32: piksel verisinin <see cref="Data"/> içindeki başlangıcı (BMP başlığı atlanır). Jpeg: 0.</summary>
    public int PixelOffset { get; }
    /// <summary>Bgra32: bir satırın bayt uzunluğu (= Width*4; 32 bpp'de dolgu yoktur). Jpeg: 0.</summary>
    public int Stride { get; }
    /// <summary>Bgra32: satırlar yukarıdan aşağıya mı (BMP negatif yükseklik — ffmpeg böyle yazar) yoksa aşağıdan yukarıya mı.</summary>
    public bool TopDown { get; }
    /// <summary>Yuv420p: tam aralık mı (0–255, "yuvj420p") yoksa sınırlı aralık mı (16–235, yayın standardı — varsayılan).</summary>
    public bool FullRange { get; }
    /// <summary>Ham (sıkıştırılmamış) piksel taşıyor mu — Bgra32 ya da Yuv420p. JPEG kare çözülmeden piksel vermez.</summary>
    public bool IsRaw => Format != VideoPixelFormat.Jpeg;
    /// <summary>Karenin kaynaktan alındığı an (UTC).</summary>
    public DateTimeOffset CapturedUtc { get; }
    /// <summary>Kaynak içinde artan kare sayacı (teşhis).</summary>
    public long Sequence { get; }

    private VideoFrame(byte[] data, VideoPixelFormat format, int width, int height, int pixelOffset, int stride,
                       bool topDown, DateTimeOffset capturedUtc, long sequence, bool fullRange = false)
    {
        Data = data; Format = format; Width = width; Height = height; PixelOffset = pixelOffset; Stride = stride;
        TopDown = topDown; CapturedUtc = capturedUtc; Sequence = sequence; FullRange = fullRange;
    }

    /// <summary>
    /// Düzlemsel YUV 4:2:0 tampondan kare (Y, ardından U ve V). <paramref name="data"/> en az
    /// <c>G×Y + 2×⌈G/2⌉×⌈Y/2⌉</c> bayt taşımalıdır; tampon kopyalanmaz.
    /// </summary>
    public static VideoFrame FromYuv420(byte[] data, int width, int height, bool fullRange,
                                        DateTimeOffset capturedUtc, long sequence = 0, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (width < 2 || height < 2 || width > 16384 || height > 16384)
            throw new ArgumentOutOfRangeException(nameof(width), $"kare boyutu geçersiz ({width}x{height})");
        long need = Yuv420FrameView.FrameBytes(width, height);
        if (offset < 0 || data.LongLength - offset < need)
            throw new ArgumentException($"YUV 4:2:0 için veri yetersiz (gereken {need} B, gelen {data.LongLength - offset} B)", nameof(data));
        return new VideoFrame(data, VideoPixelFormat.Yuv420p, width, height, offset, width, true, capturedUtc, sequence, fullRange);
    }

    /// <summary>
    /// Karenin piksellerine KOPYASIZ erişim (yalnız <see cref="VideoPixelFormat.Yuv420p"/>); diğer biçimlerde null —
    /// çağıran <see cref="ToRgbaBitmap"/> ile bitmap üretip <see cref="SkBitmapFrameView"/> kullanır.
    /// </summary>
    public IFrameView? TryGetView()
    {
        ThrowIfRetired();
        return TryGetViewCore();
    }

    private IFrameView? TryGetViewCore()
        => Format == VideoPixelFormat.Yuv420p ? new Yuv420FrameView(Data, PixelOffset, Width, Height, FullRange) : null;

    /// <summary>Kodlanmış JPEG baytlarından kare (eski MJPEG yolu). Boyutlar çözülmeden bilinmez (0).</summary>
    public static VideoFrame FromJpeg(byte[] jpeg, DateTimeOffset capturedUtc, long sequence = 0)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        return new VideoFrame(jpeg, VideoPixelFormat.Jpeg, 0, 0, 0, 0, true, capturedUtc, sequence);
    }

    /// <summary>Tam bir BMP dosyasından (32 bpp, sıkıştırmasız) kare. Geçersizse <see cref="FormatException"/>.</summary>
    public static VideoFrame FromBmp(byte[] bmp, DateTimeOffset capturedUtc, long sequence = 0)
        => TryFromBmp(bmp, capturedUtc, sequence, out var frame, out var error)
            ? frame!
            : throw new FormatException(error);

    /// <summary>
    /// BMP başlığını doğrular ve kareyi kurar. Yalnız 32 bpp ve sıkıştırmasız (BI_RGB / BI_BITFIELDS) kabul
    /// edilir — ffmpeg'in <c>-vcodec bmp -pix_fmt bgra</c> çıktısı tam olarak budur.
    /// </summary>
    public static bool TryFromBmp(byte[] bmp, DateTimeOffset capturedUtc, long sequence,
                                  out VideoFrame? frame, out string? error)
    {
        frame = null; error = null;
        if (bmp is null || bmp.Length < BmpHeaderMin) { error = "BMP başlığı için veri yetersiz"; return false; }
        if (bmp[0] != (byte)'B' || bmp[1] != (byte)'M') { error = "BMP imzası ('BM') yok"; return false; }

        uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10, 4));
        int width = BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18, 4));
        int heightSigned = BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22, 4));
        ushort bpp = BinaryPrimitives.ReadUInt16LittleEndian(bmp.AsSpan(28, 2));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(30, 4));

        if (width <= 0 || width > 16384 || heightSigned == 0 || Math.Abs((long)heightSigned) > 16384)
        { error = $"BMP boyutu geçersiz ({width}x{heightSigned})"; return false; }
        if (bpp != 32) { error = $"BMP yalnız 32 bpp desteklenir (gelen {bpp})"; return false; }
        if (compression != 0 && compression != 3) { error = $"sıkıştırılmış BMP desteklenmez (biCompression={compression})"; return false; }

        int height = Math.Abs(heightSigned);
        bool topDown = heightSigned < 0;
        long stride = (long)width * 4;
        long needed = pixelOffset + stride * height;
        if (pixelOffset < BmpHeaderMin || needed > bmp.Length)
        { error = $"BMP piksel verisi eksik (gereken {needed} B, gelen {bmp.Length} B)"; return false; }

        frame = new VideoFrame(bmp, VideoPixelFormat.Bgra32, width, height, (int)pixelOffset, (int)stride,
                               topDown, capturedUtc, sequence);
        return true;
    }

    /// <summary>Bgra32: görüntünün <paramref name="y"/>. satırının (yukarıdan sayılır) <see cref="Data"/> içindeki başlangıcı.</summary>
    public int RowOffset(int y)
    {
        if (Format != VideoPixelFormat.Bgra32) throw new InvalidOperationException("RowOffset yalnız Bgra32 karede anlamlıdır.");
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        int row = TopDown ? y : Height - 1 - y;
        return PixelOffset + row * Stride;
    }

    /// <summary>
    /// Kareyi motorun beklediği düzende (RGBA8888, Unpremul) yeni bir bitmap olarak verir. Çağıran dispose eder.
    /// Jpeg: SkiaSharp ile çözülür (eski <c>AnprEngine.Recognize(byte[])</c> yolu ile aynı sonuç).
    /// Bgra32: kanal sırası TEK kopyada çevrilir (Skia swizzler; aşağıdan-yukarı karede <see cref="SwizzleBgraToRgba"/>). R,G,B değerleri değişmez.
    /// </summary>
    public SKBitmap ToRgbaBitmap()
    {
        ThrowIfRetired();
        return ToRgbaBitmapCore();
    }

    private SKBitmap ToRgbaBitmapCore()
    {
        if (Format == VideoPixelFormat.Jpeg) return DecodeJpegRgba(Data);
        if (Format == VideoPixelFormat.Yuv420p)
            return new Yuv420FrameView(Data, PixelOffset, Width, Height, FullRange).ExtractRgba(new SKRectI(0, 0, Width, Height));

        var dst = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        try
        {
            // ÖLÇÜLDÜ (framebench, 4 MP, 2026-09-01): Skia'nın yerleşik dönüşümü 2–2,5 ms; kendi Vector128
            // swizzle'ım 8–11 ms (span/Create yükü). Yukarıdan-aşağı kare (ffmpeg'in ürettiği) Skia yolundan,
            // aşağıdan-yukarı kare (başka kaynaklar) satır satır SIMD yoldan gider.
            bool done = false;
            if (TopDown)
            {
                var handle = GCHandle.Alloc(Data, GCHandleType.Pinned);
                try
                {
                    var srcInfo = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                    using var src = new SKPixmap(srcInfo, handle.AddrOfPinnedObject() + PixelOffset, Stride);
                    done = src.ReadPixels(dst.Info, dst.GetPixels(), dst.RowBytes);
                }
                finally { handle.Free(); }
            }
            if (!done)
            {
                unsafe
                {
                    int rowLen = Width * 4;
                    byte* dstBase = (byte*)dst.GetPixels();
                    for (int y = 0; y < Height; y++)
                    {
                        var srcRow = new ReadOnlySpan<byte>(Data, RowOffset(y), rowLen);
                        var dstRow = new Span<byte>(dstBase + (long)y * dst.RowBytes, rowLen);
                        SwizzleBgraToRgba(srcRow, dstRow);
                    }
                }
            }
        }
        catch
        {
            dst.Dispose();
            throw;
        }
        return dst;
    }

    /// <summary>
    /// BGRA → RGBA kanal takası, alfa 255'e sabitlenir (kamera karesi saydam değildir; ffmpeg'in BMP'de
    /// "ayrılmış" bayta ne yazdığına güvenilmez — alfa 0 kalırsa Skia'nın ölçekleme yolu görüntüyü
    /// SİYAHA çevirebilir). 16 baytlık vektörlerle (4 piksel/adım): x64'te SSSE3 <c>pshufb</c>, arm64'te
    /// <c>tbl</c>; donanım yoksa JIT yazılım yoluna düşer (yavaş ama doğru). Kuyruk skaler işlenir.
    /// </summary>
    internal static void SwizzleBgraToRgba(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (dst.Length < src.Length) throw new ArgumentException("hedef küçük", nameof(dst));
        int n = src.Length & ~3;   // tam pikseller
        int i = 0;
        if (Vector128.IsHardwareAccelerated && n >= 16)
        {
            var idx = Vector128.Create(
                (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            var alpha = Vector128.Create(
                (byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);
            int vEnd = n - 16;
            for (; i <= vEnd; i += 16)
            {
                var v = Vector128.Create<byte>(src.Slice(i, 16));
                var r = Vector128.Shuffle(v, idx) | alpha;
                r.CopyTo(dst.Slice(i, 16));
            }
        }
        for (; i < n; i += 4)
        {
            dst[i] = src[i + 2];
            dst[i + 1] = src[i + 1];
            dst[i + 2] = src[i];
            dst[i + 3] = 255;
        }
    }

    private static SKBitmap DecodeJpegRgba(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("Kare çözülemedi (desteklenmeyen biçim?).");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bmp.Dispose();
            throw new InvalidDataException($"Kare çözülemedi: {result}");
        }
        return bmp;
    }

    /// <summary>
    /// Kareyi JPEG olarak verir. Jpeg biçimindeki kare, <paramref name="maxWidth"/> 0 ise KOPYASIZ aynı diziyi
    /// döndürür (eski "yeniden kodlanmadan yazılır" davranışı). Bgra32 kare burada kodlanır — kanıt yazımı gibi
    /// SEYREK anlarda çağrılmalı; kare başına değil. <paramref name="maxWidth"/> &gt; 0 ise ondan genişse küçültür
    /// (panel canlı görüntüsü için 4 MP kodlamak zayıf PC'de boşunadır).
    /// </summary>
    public byte[] ToJpeg(int quality = 85, int maxWidth = 0)
    {
        ThrowIfRetired();
        return ToJpegCore(quality, maxWidth);
    }

    private byte[] ToJpegCore(int quality, int maxWidth)
    {
        quality = Math.Clamp(quality, 1, 100);
        if (Format == VideoPixelFormat.Jpeg && maxWidth <= 0) return Data;

        // YUV kare + küçültme: tam kareyi RGB'ye çevirmeden doğrudan hedef boyutta örnekle (panel önizlemesi).
        if (Format == VideoPixelFormat.Yuv420p && maxWidth > 0 && Width > maxWidth)
        {
            int sh = Math.Max(1, (int)Math.Round(Height * (maxWidth / (double)Width)));
            using var small = new Yuv420FrameView(Data, PixelOffset, Width, Height, FullRange)
                .ScaleToRgba(new SKRectI(0, 0, Width, Height), maxWidth, sh);
            using var simg = SKImage.FromBitmap(small);
            using var sdata = simg.Encode(SKEncodedImageFormat.Jpeg, quality)
                ?? throw new InvalidOperationException("JPEG kodlanamadı.");
            return sdata.ToArray();
        }

        using var bmp = ToRgbaBitmap();
        SKBitmap? scaled = null;
        try
        {
            var src = bmp;
            if (maxWidth > 0 && bmp.Width > maxWidth)
            {
                int nh = Math.Max(1, (int)Math.Round(bmp.Height * (maxWidth / (double)bmp.Width)));
                scaled = bmp.Resize(new SKImageInfo(maxWidth, nh, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.Medium);
                if (scaled != null) src = scaled;
            }
            using var img = SKImage.FromBitmap(src);
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, quality)
                ?? throw new InvalidOperationException("JPEG kodlanamadı.");
            return data.ToArray();
        }
        finally { scaled?.Dispose(); }
    }

    /// <summary>Bellek teşhisi: karenin tuttuğu bayt sayısı.</summary>
    public long ByteCount => Data.LongLength;

    // ── TAMPON GERİ DÖNÜŞÜMÜ (yalnız kare kaynağı kullanır) ─────────────────────────────────────
    // NEDEN (Faz 2, 2026-09-01 canlı ölçüm): 4 MP ham kare 16 MB'dır ve kaynak saniyede 5 üretir; boşta
    // tüketici saniyede 2 alır → üretilen karelerin ~%60'ı hiç kullanılmadan çöp olur. Her biri LOH'a
    // düşer: canlıda 5 dakikada 352 Gen2 GC ölçüldü. Alınmamış karenin tamponu bir sonraki kare için
    // yeniden kullanılır; ALINAN kare ASLA geri dönüştürülmez (kovalar/panel referans tutabilir).
    //
    // PROTOKOL (Dekker tipi, iki tam bariyer): tüketici `taken=1` yazar SONRA `retired` okur; üretici
    // `retired=1` yazar SONRA `taken` okur. Bariyerler sayesinde en az biri diğerinin yazdığını görür:
    // ya tüketici kareyi almaz (retired), ya üretici tamponu almaz (taken) — ikisi de görürse yalnız
    // israf olur, asla paylaşımlı yazma olmaz.
    private int _taken;
    private int _retired;

    /// <summary>Tüketici kareyi alır. false → kare kaynak tarafından geri alınmış, KULLANILMAMALI.</summary>
    internal bool TryTake()
    {
        Interlocked.Exchange(ref _taken, 1);          // tam bariyer
        return Volatile.Read(ref _retired) == 0;
    }

    /// <summary>Kaynak tamponu geri almayı dener. false → bir tüketici kareyi almış, tampon dokunulmaz.</summary>
    internal bool TryRetire()
    {
        Volatile.Write(ref _retired, 1);
        Interlocked.MemoryBarrier();                  // tam bariyer: retired yazımı, taken okumasından önce görünür
        if (Volatile.Read(ref _taken) == 1)
        {
            Volatile.Write(ref _retired, 0);          // geri al: kare tüketicide, geçerli kalsın
            return false;
        }
        return true;
    }

    /// <summary>
    /// KİRALAMA (2.0.0): çalışan kareyi "alır" ama sahiplenmez — işi bitince kaynağa geri verir ve tamponu
    /// yeniden kullanılır. Yalnız kaynağın değiş-tokuş kilidi altında okunur/yazılır.
    /// <para>Neden: eski kural "alınan kare asla geri dönüştürülmez" idi; çalışanın işlediği HER kare (boşta
    /// saniyede 2, kamera başına 6 MB) çöpe gidiyordu. Canlı ölçüm (iki kamera): çöp toplayıcının elinde ortalama
    /// ~140 MB bekleyen tampon. Oysa karelerin çoğunda plaka yoktur ve kare işlendikten sonra kimse ona bakmaz.</para>
    /// </summary>
    internal bool Leased;

    /// <summary>
    /// Kareyi SAKLA: kiralanmış bir kare işlem bittikten sonra da tutulacaksa (kanıt görüntüsü, panel) çağrılır;
    /// kare bundan sonra geri dönüştürülmez (çöp toplayıcıya kalır). false → kare çoktan geri verilmiş, KULLANILMAMALI.
    /// </summary>
    internal bool Keep() => TryTake();

    private void ThrowIfRetired()
    {
        // Emniyet: geri verilmiş karenin tamponu başka bir kareyle ÜZERİNE YAZILIR. Onu okumak sessizce yanlış
        // piksel döndürürdü (yanlış kanıt görüntüsü / yanlış okuma). Sessiz yanlış yerine gürültülü hata.
        if (IsRetired) throw new InvalidOperationException(
            "Kare kaynağa geri verildi (tamponu yeniden kullanılıyor). Saklanacak kare için işlem sırasında Keep() çağrılmalıydı.");
    }

    /// <summary>Teşhis: kare en az bir tüketici tarafından alındı mı.</summary>
    internal bool WasTaken => Volatile.Read(ref _taken) == 1;
    /// <summary>Teşhis: tamponu kaynak tarafından geri alındı mı (bu nesne artık kullanılmaz).</summary>
    internal bool IsRetired => Volatile.Read(ref _retired) == 1;

    public override string ToString()
        => Format switch
        {
            VideoPixelFormat.Bgra32 => $"VideoFrame(bgra {Width}x{Height}, #{Sequence})",
            VideoPixelFormat.Yuv420p => $"VideoFrame(yuv420p {Width}x{Height}, #{Sequence})",
            _ => $"VideoFrame(jpeg {Data.Length} B, #{Sequence})",
        };
}
