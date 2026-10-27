using System.Text;
using Anpr.Sdk.Frames;

namespace Anpr.Tests;

/// <summary>
/// Bloklayan okuma sarmalayıcısı (2.1.0): eşzamansız boru okuması 6 MB karede kare başına ~11 ms fazladan işlemci
/// harcıyordu. Sarmalayıcı okuyucuları değiştirmeden okumayı eşzamanlı yapar; iptal, ffmpeg sonlandırılarak (boru
/// kapanır) gerçekleşir.
/// </summary>
public class BlockingReadStreamTests
{
    [Fact]
    public async Task ReadAsync_completes_synchronously_with_the_inner_bytes()
    {
        using var s = new BlockingReadStream(new MemoryStream(new byte[] { 1, 2, 3, 4 }));
        var buf = new byte[8];
        var vt = s.ReadAsync(buf.AsMemory(), CancellationToken.None);
        Assert.True(vt.IsCompletedSuccessfully);   // iş parçacığı havuzuna dönülmez
        Assert.Equal(4, await vt);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buf[..4]);
    }

    [Fact]
    public async Task Cancelled_token_is_honoured_before_blocking()
    {
        using var s = new BlockingReadStream(new MemoryStream(new byte[] { 1 }));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await s.ReadAsync(new byte[1].AsMemory(), cts.Token));
    }

    [Fact]
    public async Task Y4m_frames_read_through_the_wrapper_are_identical()
    {
        const int w = 4, h = 2;
        var frame = Enumerable.Range(0, w * h * 3 / 2).Select(i => (byte)i).ToArray();
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes($"YUV4MPEG2 W{w} H{h} F5:1 Ip A1:1 C420jpeg\n"));
        for (int k = 0; k < 2; k++) { ms.Write("FRAME\n"u8); ms.Write(frame); }
        ms.Position = 0;
        using var s = new BlockingReadStream(ms);

        var header = await Y4mFrameReader.ReadHeaderAsync(s, CancellationToken.None);
        Assert.Equal(w, header!.Value.Width);
        var marker = new byte[8];
        Assert.Equal(frame, await Y4mFrameReader.ReadFrameAsync(s, header.Value, marker, CancellationToken.None));
        Assert.Equal(frame, await Y4mFrameReader.ReadFrameAsync(s, header.Value, marker, CancellationToken.None));
        Assert.Null(await Y4mFrameReader.ReadFrameAsync(s, header.Value, marker, CancellationToken.None));   // EOF
    }

    [Fact]
    public void A_reader_blocked_on_a_child_process_pipe_is_released_when_the_process_is_killed()
    {
        // Üründeki iptal yolu: iptalde ffmpeg sonlandırılır → boru kapanır → bloklayan Read 0 döner.
        if (OperatingSystem.IsWindows()) return;   // alt süreç olarak POSIX 'sleep' kullanılır
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("sleep 30");
        using var proc = System.Diagnostics.Process.Start(psi)!;
        using var s = new BlockingReadStream(proc.StandardOutput.BaseStream);
        int got = -1;
        var reader = new Thread(() => { try { got = s.Read(new byte[16], 0, 16); } catch { got = -2; } }) { IsBackground = true };
        reader.Start();
        try
        {
            Assert.False(reader.Join(200));        // veri yok → okuma bloklu
            proc.Kill(entireProcessTree: true);     // ürün: KillOnly
            Assert.True(reader.Join(5000), "okuma bırakılmadı");
            Assert.Equal(0, got);                   // EOF
        }
        finally { try { if (!proc.HasExited) proc.Kill(true); } catch { } }
    }
}
