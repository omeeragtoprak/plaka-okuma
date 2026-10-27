namespace Anpr.Sdk.Frames;

/// <summary>
/// Okumaları BLOKLAYARAK yapan sarmalayıcı: <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> içteki akışın
/// eşzamanlı <c>Read</c>'ini çağırır ve tamamlanmış bir görev döner. Kare okuyucuları (Y4M/BMP/MJPEG) değişmeden
/// kalır; bütün okuma döngüsü ayrı bir iş parçacığında, iş parçacığı havuzuna hiç dönmeden koşar.
/// <para><b>Neden (2026-10-20 ölçümü, 4 MP Y4M, 5 fps, macOS):</b> ffmpeg borusundan 6 MB'lık kareyi
/// <c>Stream.ReadAsync</c> ile okumak kare başına <b>13,6–14,9 ms</b> işlemci harcıyordu (boru en çok 64 KB verir →
/// kare başına ~100 eşzamansız tur, her biri kqueue/epoll + iş parçacığı havuzu geçişi). Aynı boruyu bloklayan
/// okumayla almak <b>2,8–3,0 ms</b> — 5 kat daha ucuz (<c>Anpr.Bench transportbench</c>). 5 fps'te bu, kamera
/// başına tek çekirdeğin ~%5,5'i demekti.</para>
/// <para>İptal: bloklayan okuma iptal belirtecini göremez; çağıran iptalde ffmpeg'i sonlandırır, boru kapanınca
/// <c>Read</c> 0 döner. Belirteç her okumadan ÖNCE de denetlenir.</para>
/// </summary>
internal sealed class BlockingReadStream : Stream
{
    private readonly Stream _inner;

    public BlockingReadStream(Stream inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(_inner.Read(buffer.Span));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_inner.Read(buffer, offset, count));
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
