using Anpr.Sdk.Configuration;
using Anpr.Sdk.Frames;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anpr.Tests;

/// <summary>
/// KARE KİRALAMA: çalışan kareyi kiralar, işler, geri verir → tamponu yeniden kullanılır (boşta sıfır tahsis).
/// İki yönlü hata vardır: (a) geri dönüşüm çalışmazsa bellek şişer; (b) — çok daha pahalısı — hâlâ okunan ya da
/// kanıt için saklanan bir karenin tamponu yeniden kullanılırsa görüntü SESSİZCE bozulur. Testler ikisini de sıkar.
/// </summary>
public class FrameLeaseTests
{
    private const int W = 16, H = 8;
    private static readonly int Size = Anpr.Core.Imaging.Yuv420FrameView.FrameBytes(W, H);
    private static FfmpegFrameSource Source() => new("t", "rtsp://cam/x", new FfmpegOptions(), 5, NullLogger.Instance);
    private static long _seq;
    private static VideoFrame Frame(byte[]? buffer = null)
        => VideoFrame.FromYuv420(buffer ?? new byte[Size], W, H, false, DateTimeOffset.UtcNow, Interlocked.Increment(ref _seq));

    [Fact]
    public void Leased_frame_buffer_is_not_reused_while_the_worker_still_reads_it()
    {
        var src = Source();
        var f1 = Frame(); src.PublishFrame(f1, Size);
        var leased = src.TryLeaseLatestFrame();
        Assert.Same(f1, leased);

        // Okuyucu yeni kare yayınlıyor: yerine geçilen f1 KİRADA → tamponu verilmemeli.
        var spare = src.PublishFrame(Frame(), Size);
        Assert.Null(spare);
        Assert.False(f1.IsRetired);
        Assert.NotNull(f1.TryGetView());                       // çalışan okumaya devam edebilir
    }

    [Fact]
    public void Returned_frame_buffer_is_reused_by_a_later_frame_and_the_frame_becomes_unusable()
    {
        var src = Source();
        var f1 = Frame(); src.PublishFrame(f1, Size);
        var leased = src.TryLeaseLatestFrame()!;
        Assert.Null(src.PublishFrame(Frame(), Size));          // f1 kirada
        src.ReturnFrame(leased);                               // iş bitti, kimse saklamadı

        var f3 = Frame(); var spare = src.PublishFrame(f3, Size);
        // Yerine geçilen f2 alınmamıştı → onun tamponu döner; f1'in tamponu boş listede bekler.
        Assert.NotNull(spare);
        var f4 = Frame(); src.TryLeaseLatestFrame();           // f3 kirada → sıradaki yedek boş listeden gelmeli
        var spare2 = src.PublishFrame(f4, Size);
        Assert.Same(f1.Data, spare2);                          // f1'in tamponu yeniden kullanıldı

        Assert.True(f1.IsRetired);
        Assert.Throws<InvalidOperationException>(() => f1.TryGetView());     // sessiz yanlış piksel yerine hata
        Assert.Throws<InvalidOperationException>(() => f1.ToRgbaBitmap());
        Assert.Throws<InvalidOperationException>(() => f1.ToJpeg());
    }

    [Fact]
    public void Kept_frame_is_never_recycled_even_after_it_is_returned()
    {
        var src = Source();
        var f1 = Frame(); src.PublishFrame(f1, Size);
        var leased = src.TryLeaseLatestFrame()!;
        Assert.True(leased.Keep());                            // kanıt görüntüsü için saklandı
        src.PublishFrame(Frame(), Size);
        src.ReturnFrame(leased);

        // Sonraki yayınlarda f1'in tamponu ASLA yedek olarak dönmemeli.
        for (int i = 0; i < 6; i++)
        {
            src.TryLeaseLatestFrame();
            var spare = src.PublishFrame(Frame(), Size);
            Assert.NotSame(f1.Data, spare);
        }
        Assert.False(f1.IsRetired);
        Assert.NotNull(f1.TryGetView());                       // saklanan kare geçerli kalır
    }

    [Fact]
    public void Returning_the_frame_that_is_still_the_latest_does_not_retire_it()
    {
        var src = Source();
        var f1 = Frame(); src.PublishFrame(f1, Size);
        var leased = src.TryLeaseLatestFrame()!;
        src.ReturnFrame(leased);                               // hâlâ en güncel kare
        Assert.False(f1.IsRetired);
        Assert.Same(f1, src.TryGetLatestFrame());              // başka tüketici (panel) alabilir
        Assert.Same(f1.Data, f1.Data);
        // Alındı → sonraki değiş-tokuşta tamponu verilmez.
        Assert.Null(src.PublishFrame(Frame(), Size));
    }

    [Fact]
    public void Returning_twice_or_returning_an_unleased_frame_is_harmless()
    {
        var src = Source();
        var f1 = Frame(); src.PublishFrame(f1, Size);
        var leased = src.TryLeaseLatestFrame()!;
        src.PublishFrame(Frame(), Size);
        src.ReturnFrame(leased);
        src.ReturnFrame(leased);                               // ikinci kez: tampon boş listeye İKİ KEZ girmemeli
        var a = src.PublishFrame(Frame(), Size);               // f2'nin tamponu
        src.TryLeaseLatestFrame();
        var b = src.PublishFrame(Frame(), Size);               // f1'in tamponu (boş listeden)
        src.TryLeaseLatestFrame();
        var c = src.PublishFrame(Frame(), Size);               // boş liste artık boş olmalı
        Assert.NotNull(a); Assert.Same(f1.Data, b); Assert.Null(c);

        var stray = Frame();                                   // hiç kiralanmamış kare
        src.ReturnFrame(stray);
        Assert.False(stray.IsRetired);
    }

    [Fact]
    public void Steady_state_idle_loop_allocates_no_new_buffers()
    {
        // Boşta desen: kaynak 5 kare üretir, çalışan 2'sini işler. Okuyucu her kareyi "yedek" tampona okur;
        // yedek yoksa YENİ tampon ayırır. Kararlı durumda yeni tahsis olmamalı (dönüşümde ≤ 4 tampon).
        var src = Source();
        var distinct = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
        byte[]? spare = null; VideoFrame? last = null; int allocations = 0;
        for (int i = 0; i < 200; i++)
        {
            byte[] buf = spare ?? new byte[Size];
            if (spare == null && i > 10) allocations++;       // ısınmadan sonraki tahsisler
            distinct.Add(buf);
            spare = src.PublishFrame(Frame(buf), Size);
            if (i % 5 is 0 or 2)                               // çalışan 5 karenin 2'sini işler
            {
                var f = src.TryLeaseLatestFrame()!;
                Assert.NotNull(f.TryGetView());
                if (last != null && !ReferenceEquals(last, f)) src.ReturnFrame(last);   // önceki kareyi geri ver
                last = f;
            }
        }
        Assert.Equal(0, allocations);
        Assert.InRange(distinct.Count, 2, 4);
    }

    private sealed class PlainSource : IFrameSource
    {
        public VideoFrame? Latest;
        public void Start(CancellationToken cancellationToken) { }
        public VideoFrame? TryGetLatestFrame() => Latest != null && Latest.TryTake() ? Latest : null;
        public bool IsConnected => true;
        public DateTimeOffset? LastFrameUtc => null;
        public long FramesReceived => 0;
        public int RestartCount => 0;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Custom_sources_without_lease_support_fall_back_to_take_and_never_recycle()
    {
        IFrameSource src = new PlainSource { Latest = Frame() };
        var f = src.TryLeaseLatestFrame();
        Assert.NotNull(f);
        Assert.True(f!.WasTaken);                              // varsayılan: "al" → geri dönüştürülemez
        src.ReturnFrame(f);                                    // hiçbir şey yapmaz
        Assert.False(f.IsRetired);
        Assert.Equal(-1, src.LatestSequence);
    }
}
