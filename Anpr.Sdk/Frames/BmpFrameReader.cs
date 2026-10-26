using System.Buffers.Binary;

namespace Anpr.Sdk.Frames;

/// <summary>
/// ffmpeg'in <c>-f image2pipe -vcodec bmp</c> borusundan kareleri ayırır. BMP her dosyanın 2. baytından
/// itibaren TAM UZUNLUĞUNU taşır; bu yüzden MJPEG yolundaki gibi bayt bayt işaret (SOI/EOI) taramak
/// gerekmez: 14 bayt başlık okunur, uzunluk alınır, kalan tek seferde DOĞRUDAN kare tamponuna okunur
/// (ara tampon/kopya yok — 16 MB'lık kareler için önemli).
/// <para>Bozuk akışta ('BM' yok ya da uzunluk mantıksız) tek bayt kayarak yeniden hizalanır; kaç bayt
/// atlandığı <c>onResync</c> ile bildirilir (sayaç; normal akışta hiç çağrılmaz).</para>
/// </summary>
internal static class BmpFrameReader
{
    /// <summary>Bir karenin kabul edilen üst sınırı (16384×16384×4 ≈ 1 GB'ın çok altı; 4 MP kare 16,3 MB).</summary>
    public const int MaxFrameBytes = 256 * 1024 * 1024;

    /// <summary>
    /// Akıştan bir sonraki tam BMP dosyasını okur. Akış bitti (EOF) → null.
    /// <paramref name="header"/> en az 14 baytlık çalışma tamponudur (çağıran yeniden kullanır).
    /// </summary>
    public static async Task<byte[]?> ReadNextAsync(Stream stream, byte[] header, CancellationToken ct, Action<int>? onResync = null,
                                                    byte[]? reuse = null)
    {
        if (header.Length < 14) throw new ArgumentException("en az 14 bayt", nameof(header));
        if (!await ReadExactAsync(stream, header, 0, 14, ct).ConfigureAwait(false)) return null;

        int skipped = 0;
        while (!LooksLikeHeader(header))
        {
            // Tek bayt kay: ilk baytı at, sona bir bayt oku.
            Buffer.BlockCopy(header, 1, header, 0, 13);
            if (!await ReadExactAsync(stream, header, 13, 1, ct).ConfigureAwait(false)) return null;
            skipped++;
        }
        if (skipped > 0) onResync?.Invoke(skipped);

        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
        // Geri dönüşüm: boyut tutuyorsa alınmamış eski karenin tamponu (bkz. VideoFrame.TryRetire); yoksa yeni.
        // Sıfırlama yok: her bayt hemen aşağıda üzerine yazılır (zayıf PC'de 16 MB'ı sıfırlamak ~2 ms boşa gider).
        var frame = reuse != null && reuse.Length == size ? reuse : GC.AllocateUninitializedArray<byte>(size);
        Buffer.BlockCopy(header, 0, frame, 0, 14);
        if (!await ReadExactAsync(stream, frame, 14, size - 14, ct).ConfigureAwait(false)) return null;
        return frame;
    }

    internal static bool LooksLikeHeader(byte[] h)
    {
        if (h[0] != (byte)'B' || h[1] != (byte)'M') return false;
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(2, 4));
        return size >= VideoFrame.BmpHeaderMin && size <= MaxFrameBytes;
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
