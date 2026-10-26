namespace Anpr.Sdk.Workers;

/// <summary>Bir kaynak karesinin neden işlendiği (ya da atlandığı).</summary>
internal enum ScheduleReason
{
    /// <summary>Atlandı: hareket yok, bekleme/izleme süresi dışında, zorunlu aralık dolmadı.</summary>
    Skip = 0,
    /// <summary>Bu karede hareket var.</summary>
    Motion = 1,
    /// <summary>Hareket az önce bitti; bekleme süresi içinde (aracın son kareleri kaçmasın).</summary>
    MotionHold = 2,
    /// <summary>Yakın zamanda bölge-içi plaka tespit edildi (aktif izleme — duran araç hareket üretmez).</summary>
    ActiveTracking = 3,
    /// <summary>Zorunlu aralık doldu: hareket görülmese de sahneye bir kez bakılır (emniyet kemeri).</summary>
    Forced = 4,
}

/// <summary>
/// HAREKET TETİKLİ ZAMANLAMA (2.0.0, <c>MotionGate = On</c>): "hangi kaynak karesinde dedektör koşsun?" kararı.
/// Saf durum makinesidir (saat ve kare dışarıdan verilir) — birim testlenir ve <c>scripts/gate_sim.py</c> aynı
/// kuralı kayıtlı akış üzerinde benzetir.
/// <para><b>Neden (2026-09-30 ölçümü):</b> eski döngü zamanlayıcıyla yoklar — boşta saniyede 2 kare. Bölgeden
/// ~1 saniyede geçen bir araç o 2 yoklamanın HANGİ ANA denk geldiğine göre yakalanır ya da kaçar. Aynı 10 dakikalık
/// kayıt aynı ayarla üç kez oynatıldı: net okunan (OCR 0,995) bir araç koşuların yalnız birinde yayınlandı.
/// Bu sınıfla karar kaynağın HER karesinde verilir: hareket varken her kare işlenir (araç kaçmaz), sahne boşken
/// hiçbiri işlenmez (yalnız zorunlu aralıkta bir bakış) — hem daha az işlemci hem daha çok yakalama.</para>
/// <para>İşleme kuralı: <c>hareket ∨ (son SÜREKLİ hareketten beri ≤ bekleme) ∨ (aktif izleme) ∨ (son işlemden beri ≥ zorunlu aralık)</c>.</para>
/// <para><b>Tek karelik hareket bekleme başlatmaz.</b> IP kameralar her anahtar karede (I-frame, tipik 2–4 sn'de bir)
/// görüntünün dokusunu bir anda değiştirir: ölçülen kamerada 4 sn'de bir, ızgaranın ~%16'sı TEK kare boyunca
/// "değişti" görünüyor. O kare işlenir (zararsız — zaten zorunlu bakışın yerini tutar) ama ardından bekleme
/// başlatılsaydı her darbe 5 kare daha işletirdi (ölçüm: işlenen kare %55 → %37). Gerçek araç art arda
/// en az iki karede hareket üretir; bekleme ancak o zaman başlar.</para>
/// </summary>
internal sealed class MotionSchedule
{
    private readonly TimeSpan _motionHold, _activeHold, _forcedInterval;
    private DateTimeOffset _lastMotion = DateTimeOffset.MinValue;   // son SÜREKLİ (≥2 kare) hareketin son karesi
    private int _motionRun;                                          // art arda hareketli kare sayısı
    private DateTimeOffset _activeUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastProcessed = DateTimeOffset.MinValue;

    public MotionSchedule(TimeSpan motionHold, TimeSpan activeHold, TimeSpan forcedInterval)
    {
        _motionHold = motionHold < TimeSpan.Zero ? TimeSpan.Zero : motionHold;
        _activeHold = activeHold < TimeSpan.Zero ? TimeSpan.Zero : activeHold;
        // Emniyet kemeri kapatılamaz: en geç bu aralıkta bir kare işlenir.
        _forcedInterval = forcedInterval < TimeSpan.FromSeconds(0.5) ? TimeSpan.FromSeconds(0.5) : forcedInterval;
    }

    /// <summary>Aktif izleme süresi içinde mi (son bölge-içi tespitten beri).</summary>
    public bool IsActive(DateTimeOffset now) => now < _activeUntil;

    /// <summary>
    /// Yeni bir kaynak karesi için karar. <paramref name="motion"/>: kapının o kare için hareket kararı.
    /// Dönen değer <see cref="ScheduleReason.Skip"/> değilse kare İŞLENMELİ ve ardından
    /// <see cref="OnProcessed"/> çağrılmalıdır.
    /// </summary>
    public ScheduleReason Decide(DateTimeOffset now, bool motion)
    {
        if (motion)
        {
            if (++_motionRun >= 2) _lastMotion = now;
            return ScheduleReason.Motion;
        }
        _motionRun = 0;
        if (_lastMotion != DateTimeOffset.MinValue && now - _lastMotion <= _motionHold) return ScheduleReason.MotionHold;
        if (now < _activeUntil) return ScheduleReason.ActiveTracking;
        if (_lastProcessed == DateTimeOffset.MinValue || now - _lastProcessed >= _forcedInterval) return ScheduleReason.Forced;
        return ScheduleReason.Skip;
    }

    /// <summary>Kare işlendi. <paramref name="zoneDetection"/>: karede bölge-içi plaka tespiti var mıydı.</summary>
    public void OnProcessed(DateTimeOffset now, bool zoneDetection)
    {
        _lastProcessed = now;
        if (zoneDetection) _activeUntil = now + _activeHold;
    }
}
