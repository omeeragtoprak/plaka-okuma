using Anpr.Core;
using Anpr.Sdk;
using Anpr.Sdk.Configuration;
using Anpr.Sdk.Events;

namespace Anpr.Tests;

/// <summary>M2: PlateDetected bounded-dispatcher — serileşmiş teslim, yavaş-handler izolasyonu, taşma-sayım, drain.</summary>
public class AnprRuntimeDispatchTests
{
    private static PlateEvent Ev(string c) => new()
    {
        Timestamp = DateTimeOffset.UnixEpoch, CameraId = "c", CameraName = "C",
        Plate = c, PlateCompact = c, RawText = c,
        Confidence = 0.9f, DetectorConfidence = 0.9f, OcrConfidence = 0.9f, Box = new PlateBox(0, 0, 10, 10),
    };

    [Fact]
    public async Task Events_delivered_serialized_and_in_order()
    {
        await using var rt = new AnprRuntime(new AnprServiceOptions());
        var received = new List<string>();
        int inFlight = 0, maxInFlight = 0;
        using var done = new CountdownEvent(3);
        rt.PlateDetected += e =>
        {
            int cur = Interlocked.Increment(ref inFlight);
            lock (received) { received.Add(e.PlateCompact); if (cur > maxInFlight) maxInFlight = cur; }
            Thread.Sleep(5);   // tut: teslim EŞZAMANLI olsaydı çağrılar örtüşür, maxInFlight>1 olurdu
            Interlocked.Decrement(ref inFlight);
            done.Signal();
        };
        rt.EnqueueEvent(Ev("25AAA111"));
        rt.EnqueueEvent(Ev("25BBB222"));
        rt.EnqueueEvent(Ev("25CCC333"));
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { "25AAA111", "25BBB222", "25CCC333" }, received);  // FIFO sıralı
        Assert.Equal(1, maxInFlight);   // NON-REENTRANT: serileşmiş (asla eşzamanlı) — thread-affinity değil
    }

    [Fact]
    public async Task Overflow_drops_and_counts_when_handler_too_slow()
    {
        await using var rt = new AnprRuntime(new AnprServiceOptions());
        using var gate = new ManualResetEventSlim(false);
        rt.PlateDetected += _ => gate.Wait(TimeSpan.FromSeconds(10));   // ilk olayda blokla → kuyruk dolar
        for (int i = 0; i < 300; i++) rt.EnqueueEvent(Ev($"25XX{i:D3}"));
        Assert.True(rt.DroppedEvents > 0, $"300 olay 256 kapasiteyi aşmalıydı; düşen={rt.DroppedEvents}");
        gate.Set();   // serbest bırak (dispose kalanı drain edebilsin)
    }

    [Fact]
    public async Task Pending_events_drained_on_dispose()
    {
        int received = 0;
        var rt = new AnprRuntime(new AnprServiceOptions());
        rt.PlateDetected += _ => Interlocked.Increment(ref received);
        for (int i = 0; i < 10; i++) rt.EnqueueEvent(Ev($"25YY{i:D3}"));
        await rt.DisposeAsync();                 // drain (≤5s) — kuyruktaki hepsi teslim edilmeli
        Assert.Equal(10, received);
    }

    [Fact]
    public async Task Events_still_delivered_after_stop_then_start()
    {
        // CRITICAL REGRESYON (2026-07-28): StopAsync kanalı KALICI kapatıyordu (drain için) ve StartAsync
        // yeniden kurmuyordu. PMS'in çok muhtemel bir işlemi (kamera IP değişti → "ANPR'ı yeniden başlat",
        // config reload, bakım penceresi) sistemi sessizce ölümcül duruma sokuyordu: her şey sağlıklı
        // görünürken PlateDetected bir daha HİÇ tetiklenmiyor, yani bariyer hiç açılmıyordu.
        await using var rt = new AnprRuntime(new AnprServiceOptions());
        int received = 0;
        rt.PlateDetected += _ => Interlocked.Increment(ref received);

        await rt.StartAsync();                // PMS akışı: start → çalış → stop → (yeniden) start
        rt.EnqueueEvent(Ev("25AAA111"));
        await rt.StopAsync();                 // kanalı kapatır + bekleyeni boşaltır
        Assert.Equal(1, received);

        await rt.StartAsync();                // kameralar yok; yalnız dispatcher yeniden kurulmalı
        rt.EnqueueEvent(Ev("25BBB222"));
        for (int i = 0; i < 50 && received < 2; i++) await Task.Delay(20);

        Assert.Equal(2, received);            // eskiden 1'de kalıyordu (olay sessizce düşüyordu)
        Assert.Equal(0, rt.DroppedEvents);    // kapalı kanala yazım düşüş olarak sayılırdı
    }

    [Fact]
    public async Task Double_start_is_still_rejected()
    {
        // Yeniden başlatma desteği, "zaten başlatıldı" korumasını gevşetmemeli.
        await using var rt = new AnprRuntime(new AnprServiceOptions());
        await rt.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rt.StartAsync());
        await rt.StopAsync();
    }
}
