using System.Globalization;
using System.Text;
using Anpr.Core.Imaging;

namespace Anpr.Sdk.Frames;

/// <summary>
/// ffmpeg'in <c>-f yuv4mpegpipe</c> (Y4M) borusunu okur: akışın başında TEK bir metin başlığı
/// (<c>YUV4MPEG2 W2688 H1520 F5:1 … C420mpeg2 XCOLORRANGE=LIMITED</c>), ardından her kare için
/// <c>FRAME\n</c> işareti + sabit uzunlukta Y, U, V düzlemleri gelir.
/// <para>BMP yoluna göre farkı: piksel biçimi çözücünün kendi biçimidir (YUV 4:2:0) → ffmpeg renk dönüşümü
/// yapmaz, kare 16,3 MB yerine 6,1 MB'tır. Kare boyu başlıktan bilindiği için gövde tek okumada DOĞRUDAN kare
/// tamponuna alınır (ara kopya yok) ve alınmamış karenin tamponu yeniden kullanılır.</para>
/// </summary>
internal static class Y4mFrameReader
{
    /// <summary>Akış başlığından çıkan kare tanımı.</summary>
    public readonly record struct Header(int Width, int Height, bool FullRange)
    {
        public int FrameBytes => Yuv420FrameView.FrameBytes(Width, Height);
    }

    private const int MaxHeaderBytes = 1024;
    private static readonly byte[] FrameMagic = "FRAME"u8.ToArray();

    /// <summary>Akışın ilk satırını (başlık) okur. Akış bittiyse null; başlık bozuksa <see cref="InvalidDataException"/>.</summary>
    public static async Task<Header?> ReadHeaderAsync(Stream stream, CancellationToken ct)
    {
        var line = await ReadLineAsync(stream, MaxHeaderBytes, ct).ConfigureAwait(false);
        return line == null ? null : ParseHeader(line);
    }

    /// <summary>
    /// Başlık satırını çözer (saf/test-edilebilir). Yalnız 8-bit 4:2:0 kabul edilir (<c>C420*</c>; etiket yoksa
    /// Y4M varsayılanı da 4:2:0'dır). 10-bit (<c>C420p10</c>), 4:2:2, 4:4:4 ve mono reddedilir.
    /// </summary>
    internal static Header ParseHeader(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "YUV4MPEG2")
            throw new InvalidDataException("Y4M imzası ('YUV4MPEG2') yok");
        int w = 0, h = 0; bool full = false; string chroma = "420";
        foreach (var t in parts.AsSpan(1))
        {
            switch (t[0])
            {
                case 'W': int.TryParse(t.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out w); break;
                case 'H': int.TryParse(t.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out h); break;
                case 'C': chroma = t[1..]; break;
                case 'X':
                    if (t.StartsWith("XCOLORRANGE=", StringComparison.Ordinal))
                        full = t.AsSpan(12).Equals("FULL", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }
        if (w < 2 || h < 2 || w > 16384 || h > 16384)
            throw new InvalidDataException($"Y4M boyutu geçersiz ({w}x{h})");
        // 8-bit 4:2:0 çeşitleri (yalnız kroma KONUMU farklıdır, bellek düzeni aynı): 420, 420jpeg, 420mpeg2, 420paldv.
        // "420p10"/"420p12" (yüksek bit derinliği), 422, 444, mono REDDEDİLİR — AÇIK LİSTE: adında harf aramak
        // "420jpeg"i de reddediyordu (testle yakalandı).
        if (chroma is not ("420" or "420jpeg" or "420mpeg2" or "420paldv"))
            throw new InvalidDataException($"Y4M yalnız 8-bit 4:2:0 desteklenir (gelen C{chroma})");
        return new Header(w, h, full);
    }

    /// <summary>
    /// Sonraki kareyi okur. Akış bittiyse null. <paramref name="reuse"/> doğru boydaysa o tampon doldurulur.
    /// Kare işareti beklenen yerde değilse akış hizasını kaybetmiştir → <see cref="InvalidDataException"/>
    /// (çağıran ffmpeg'i yeniden başlatır; sabit uzunluklu akışta "kayarak toparlanmak" yanlış kare üretir).
    /// </summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, Header header, byte[] marker, CancellationToken ct, byte[]? reuse = null)
    {
        if (marker.Length < 6) throw new ArgumentException("en az 6 bayt", nameof(marker));
        if (!await ReadExactAsync(stream, marker, 0, 6, ct).ConfigureAwait(false)) return null;
        if (!marker.AsSpan(0, 5).SequenceEqual(FrameMagic))
            throw new InvalidDataException("Y4M kare işareti ('FRAME') beklenen yerde değil — akış hizasını kaybetti");
        if (marker[5] != (byte)'\n')
        {
            // "FRAME Ixyz\n" gibi parametreli işaret: satır sonuna kadar atla.
            if (marker[5] != (byte)' ') throw new InvalidDataException("Y4M kare işareti bozuk");
            if (await ReadLineAsync(stream, 256, ct).ConfigureAwait(false) == null) return null;
        }
        int size = header.FrameBytes;
        var frame = reuse != null && reuse.Length == size ? reuse : GC.AllocateUninitializedArray<byte>(size);
        if (!await ReadExactAsync(stream, frame, 0, size, ct).ConfigureAwait(false)) return null;
        return frame;
    }

    private static async Task<string?> ReadLineAsync(Stream s, int max, CancellationToken ct)
    {
        var one = new byte[1];
        var sb = new StringBuilder(96);
        while (true)
        {
            int n = await s.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n <= 0) return null;
            if (one[0] == (byte)'\n') return sb.ToString();
            sb.Append((char)one[0]);
            if (sb.Length > max) throw new InvalidDataException("Y4M başlık satırı çok uzun");
        }
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, int offset, int count, CancellationToken ct)
    {
        while (count > 0)
        {
            int n = await s.ReadAsync(buf.AsMemory(offset, count), ct).ConfigureAwait(false);
            if (n <= 0) return false;
            offset += n; count -= n;
        }
        return true;
    }
}
