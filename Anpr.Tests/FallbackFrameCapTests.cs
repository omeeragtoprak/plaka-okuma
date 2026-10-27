using Anpr.Core;
using Anpr.Sdk.Frames;
using Anpr.Sdk.Workers;
using SkiaSharp;

namespace Anpr.Tests;

/// <summary>
/// Yedek kanıt halkasında tam kare tavanı (2.1.0): 4 MP karede bir kare ~6 MB; altı kaydın altısında kare tutmak
/// trafikte kamera başına 36 MB demekti. Yalnız en yeni <see cref="CameraWorker.FallbackFrameCapacity"/> kayıtta kare
/// kalır; kırpıntılar her kayıtta durur (kanıt en kötü ihtimalle kırpıntıdan üretilir).
/// </summary>
public class FallbackFrameCapTests
{
    private static (SKBitmap, VideoFrame?, PlateBox, float, DateTimeOffset) Entry(int i, bool withFrame) =>
        (new SKBitmap(4, 2),
         withFrame ? VideoFrame.FromYuv420(new byte[8 * 8 * 3 / 2], 8, 8, false, DateTimeOffset.UtcNow, i) : null,
         new PlateBox(i, 0, 10, 4), 0.5f, DateTimeOffset.UtcNow.AddSeconds(i));

    [Fact]
    public void Only_the_newest_entries_keep_their_full_frame()
    {
        var list = Enumerable.Range(0, 6).Select(i => Entry(i, withFrame: true)).ToList();
        CameraWorker.DropOldFallbackFrames(list, 2);

        Assert.Equal(new[] { false, false, false, false, true, true }, list.Select(e => e.Item2 != null).ToArray());
        Assert.All(list, e => Assert.NotNull(e.Item1));   // kırpıntılar kalır
    }

    [Fact]
    public void Entries_without_a_frame_do_not_consume_the_budget()
    {
        // En yeni kayıt karesiz (kare çoktan geri verilmişti) → kare taşıyan en yeni İKİ kayıt korunur.
        var list = new List<(SKBitmap, VideoFrame?, PlateBox, float, DateTimeOffset)>
        {
            Entry(0, true), Entry(1, true), Entry(2, true), Entry(3, false),
        };
        CameraWorker.DropOldFallbackFrames(list, 2);
        Assert.Equal(new[] { false, true, true, false }, list.Select(e => e.Item2 != null).ToArray());
    }

    [Fact]
    public void Adding_to_the_ring_applies_both_caps_and_disposes_evicted_crops()
    {
        // Üründeki yol (StoreFallbackCrop → AddFallback): 7. kayıt eklenince en eski kırpıntı bırakılır ve
        // kare yalnız en yeni ikisinde kalır.
        var list = new List<(SKBitmap, VideoFrame?, PlateBox, float, DateTimeOffset)>();
        var entries = Enumerable.Range(0, 7).Select(i => Entry(i, withFrame: true)).ToList();
        foreach (var e in entries) CameraWorker.AddFallback(list, e, capacity: 6, frameCapacity: 2);

        Assert.Equal(6, list.Count);
        Assert.Equal(IntPtr.Zero, entries[0].Item1.Handle);                       // atılan kırpıntı dispose edildi
        Assert.Equal(new[] { false, false, false, false, true, true }, list.Select(e => e.Item2 != null).ToArray());
    }

    [Fact]
    public void Capacity_is_the_documented_two()
        => Assert.Equal(2, CameraWorker.FallbackFrameCapacity);
}
