using Anpr.Sdk.Workers;

namespace Anpr.Tests;

/// <summary>
/// Hareket tetikli zamanlama (MotionGate = On): dedektörün HANGİ karede koşacağına karar veren durum makinesi.
/// İki yönlü hata vardır ve ikisi de burada sıkılır: gereksiz işleme (boş sahnede işlemci) ve — daha pahalısı —
/// araç varken kare atlama (kaçan geçiş).
/// </summary>
public class MotionScheduleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static MotionSchedule Make(double hold = 1.0, double active = 4.0, double forced = 2.0)
        => new(TimeSpan.FromSeconds(hold), TimeSpan.FromSeconds(active), TimeSpan.FromSeconds(forced));

    [Fact]
    public void Empty_scene_processes_only_one_frame_per_forced_interval()
    {
        var s = Make(forced: 2.0);
        int processed = 0;
        for (int i = 0; i < 100; i++)   // 20 sn, 5 fps, hiç hareket yok
        {
            var now = T0.AddSeconds(i * 0.2);
            if (s.Decide(now, motion: false) != ScheduleReason.Skip) { processed++; s.OnProcessed(now, false); }
        }
        Assert.Equal(10, processed);    // 0, 2, 4, … 18. saniyeler: zorunlu bakış
    }

    [Fact]
    public void First_frame_is_forced_so_a_parked_vehicle_at_startup_is_seen()
    {
        var s = Make();
        Assert.Equal(ScheduleReason.Forced, s.Decide(T0, motion: false));
    }

    [Fact]
    public void Motion_is_processed_on_every_frame_and_hold_covers_the_tail()
    {
        var s = Make(hold: 1.0, forced: 60);
        s.Decide(T0, false); s.OnProcessed(T0, false);          // açılış bakışı
        Assert.Equal(ScheduleReason.Skip, s.Decide(T0.AddSeconds(0.2), false));

        // Araç 1 sn boyunca hareket ediyor: beş karenin beşi de işlenir
        for (int i = 0; i < 5; i++)
        {
            var now = T0.AddSeconds(1.0 + i * 0.2);
            Assert.Equal(ScheduleReason.Motion, s.Decide(now, true));
            s.OnProcessed(now, false);
        }
        // Hareket bitti (son hareket 1,8 sn'de): 1 sn bekleme → 2,8'e kadar işlenir, sonra atlanır
        Assert.Equal(ScheduleReason.MotionHold, s.Decide(T0.AddSeconds(2.0), false));
        Assert.Equal(ScheduleReason.MotionHold, s.Decide(T0.AddSeconds(2.8), false));
        Assert.Equal(ScheduleReason.Skip, s.Decide(T0.AddSeconds(3.0), false));
    }

    [Fact]
    public void Single_frame_motion_is_processed_but_does_not_start_the_hold()
    {
        // Anahtar kare (I-frame) darbesi: tek kare "hareket". Kare işlenir, ama ardından bekleme BAŞLAMAZ.
        var s = Make(hold: 1.0, forced: 60);
        s.Decide(T0, false); s.OnProcessed(T0, false);
        Assert.Equal(ScheduleReason.Motion, s.Decide(T0.AddSeconds(4.0), true));
        s.OnProcessed(T0.AddSeconds(4.0), false);
        Assert.Equal(ScheduleReason.Skip, s.Decide(T0.AddSeconds(4.2), false));
        Assert.Equal(ScheduleReason.Skip, s.Decide(T0.AddSeconds(4.4), false));

        // Art arda iki hareketli kare = gerçek hareket → bekleme başlar.
        s.Decide(T0.AddSeconds(8.0), true); s.Decide(T0.AddSeconds(8.2), true);
        Assert.Equal(ScheduleReason.MotionHold, s.Decide(T0.AddSeconds(8.4), false));
        Assert.Equal(ScheduleReason.MotionHold, s.Decide(T0.AddSeconds(9.2), false));
        Assert.Equal(ScheduleReason.Skip, s.Decide(T0.AddSeconds(9.4), false));

        // Arada hareketsiz kare varsa koşu sıfırlanır: hareket–boş–hareket bekleme başlatmaz.
        var t = Make(hold: 1.0, forced: 60);
        t.Decide(T0, false); t.OnProcessed(T0, false);
        t.Decide(T0.AddSeconds(1.0), true);
        Assert.Equal(ScheduleReason.Skip, t.Decide(T0.AddSeconds(1.2), false));
        t.Decide(T0.AddSeconds(1.4), true);
        Assert.Equal(ScheduleReason.Skip, t.Decide(T0.AddSeconds(1.6), false));
    }

    [Fact]
    public void Zone_detection_keeps_processing_a_stationary_vehicle()
    {
        // Bariyerde DURAN araç hareket üretmez; plakası görüldüğü sürece aktif izleme kareleri işletir.
        var s = Make(hold: 0.4, active: 4.0, forced: 60);
        s.Decide(T0, true); s.OnProcessed(T0, zoneDetection: true);
        Assert.True(s.IsActive(T0.AddSeconds(3.9)));
        Assert.Equal(ScheduleReason.ActiveTracking, s.Decide(T0.AddSeconds(2.0), false));
        s.OnProcessed(T0.AddSeconds(2.0), zoneDetection: true);          // hâlâ görünüyor → süre uzar
        Assert.Equal(ScheduleReason.ActiveTracking, s.Decide(T0.AddSeconds(5.9), false));
        s.OnProcessed(T0.AddSeconds(5.9), zoneDetection: false);         // artık görünmüyor
        Assert.Equal(ScheduleReason.Skip, s.Decide(T0.AddSeconds(6.2), false));
    }

    [Fact]
    public void Forced_interval_counts_from_the_last_PROCESSED_frame_not_the_last_forced_one()
    {
        // Hareketle işlenen kare de "sahneye bakıldı" sayılır: hemen ardından zorunlu bakış gerekmez.
        var s = Make(hold: 0, forced: 2.0);
        s.Decide(T0, false); s.OnProcessed(T0, false);
        s.Decide(T0.AddSeconds(1.5), true); s.OnProcessed(T0.AddSeconds(1.5), false);
        Assert.Equal(ScheduleReason.Skip, s.Decide(T0.AddSeconds(2.0), false));    // son işlemden beri 0,5 sn
        Assert.Equal(ScheduleReason.Forced, s.Decide(T0.AddSeconds(3.5), false));  // 2 sn doldu
    }

    [Fact]
    public void Forced_interval_cannot_be_disabled()
    {
        // Emniyet kemeri: 0 ya da negatif verilse bile en geç 0,5 sn'de bir bakılır.
        var s = new MotionSchedule(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        s.Decide(T0, false); s.OnProcessed(T0, false);
        Assert.Equal(ScheduleReason.Skip, s.Decide(T0.AddSeconds(0.3), false));
        Assert.Equal(ScheduleReason.Forced, s.Decide(T0.AddSeconds(0.5), false));
    }

    [Fact]
    public void A_one_second_pass_gets_every_frame_where_the_timer_loop_gets_two()
    {
        // Ölçülen vaka (2026-09-30): bölgeden ~1 sn'de geçen araç, boşta 2 fps yoklamada 2 kareye denk
        // geliyordu (bazen 1) ve 2 onay şartını tutturamayıp yayınlanmıyordu. Hareket tetikli zamanlamada
        // aracın göründüğü 5 karenin 5'i de işlenir.
        var s = Make();
        s.Decide(T0, false); s.OnProcessed(T0, false);
        int processed = 0;
        for (int i = 0; i < 5; i++)
        {
            var now = T0.AddSeconds(10 + i * 0.2);
            if (s.Decide(now, motion: true) != ScheduleReason.Skip) { processed++; s.OnProcessed(now, zoneDetection: i >= 1); }
        }
        Assert.Equal(5, processed);
    }
}
