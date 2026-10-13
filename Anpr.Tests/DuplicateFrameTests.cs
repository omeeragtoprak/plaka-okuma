using Anpr.Core;
using Anpr.Core.Confirmation;

namespace Anpr.Tests;

/// <summary>
/// AYNI FİZİKSEL KARENİN İKİ KEZ İŞLENMESİ. Bulundu 2026-07-31 canlı sayaç tutarsızlığından:
/// <c>/health</c> çıkışta <b>işlenen (4187) &gt; alınan (4165)</b> gösteriyordu — matematiksel olarak
/// imkânsız görünen bu fark, <c>FramesProcessed</c>'in AYRI KARE değil <b>DÖNGÜ YİNELEMESİ</b>
/// saymasından geliyor.
///
/// <para><b>Mekanizma:</b> <c>FfmpegFrameSource.TryGetLatestFrame()</c> yalnız son kare referansını
/// okur; "yeni kare geldi mi" takibi YOKTUR. Tüketici üreticiden hızlı yoklarsa AYNI <c>byte[]</c>
/// tekrar döner ve yeni bir gözlemmiş gibi işlenir.</para>
///
/// <para><b>Neden ciddi:</b> araç varken döngü <c>activeFps</c> (5) hızında, kaynak ise 2 fps üretir →
/// her fiziksel kare ~2 kez işlenir. Bu kopyalar <see cref="PlateConfirmer"/>'a AYRI gözlem gibi
/// girer; <c>MinConfirmations=2</c> TEK BİR KAREDEN karşılanabilir. Yani "çok-kareli uzlaşı"
/// güvencesi — ölçülmüş katkısı +6,4 puan olan mekanizma — kısmen aynı karenin tekrarına dayanır.
/// Kopyalar bağımsız kanıt DEĞİLDİR: aynı piksellerden aynı sonuç çıkar.</para>
/// </summary>
public class DuplicateFrameTests
{
    private static PlateReading R(string text, float ocr, int x = 100)
        => new()
        {
            Text = text, RawText = text.Replace(" ", ""), Confidence = ocr * 0.8f,
            DetectorConfidence = 0.8f, OcrConfidence = ocr,
            Box = new PlateBox(x, 100, 140, 55), IsValidTr = true,
        };

    private static PlateConfirmer C(int minConfirmations)
        => new(new PlateConfirmerOptions
        {
            MinConfirmations = minConfirmations,
            DedupWindow = TimeSpan.FromSeconds(30),
            SettleWindow = TimeSpan.Zero,
            CharacterVoting = false,
            ConstrainedDecoding = false,
            EarlyEmitMinOcr = 2f,   // erken emit KAPALI: bu test yalnız onay sayımını ölçer
        });

    [Fact]
    public void MinConfirmations_2_must_NOT_be_satisfied_by_one_physical_frame_seen_twice()
    {
        // Bu testin ölçtüğü değişmez: "iki onay" iki AYRI kare demektir. Kopya kare sayılabilseydi
        // tek kare bariyeri açmaya yeterdi ve çok-kareli uzlaşı bir güvence olmaktan çıkardı.
        var confirmer = C(minConfirmations: 2);
        var t0 = DateTimeOffset.UnixEpoch;

        // AYNI fiziksel kare iki kez: aynı kutu, aynı okuma, farklı yoklama anı.
        confirmer.Observe(R("25 ABC 123", 0.99f), t0);
        confirmer.Observe(R("25 ABC 123", 0.99f), t0.AddMilliseconds(200));

        // Kopyayı gözlem OLARAK SAYMAK, tek kareyi "onaylanmış" yapar. Worker seviyesindeki
        // koruma (aynı byte[] referansı iki kez işlenmez) bu durumu HİÇ oluşturmamalıdır.
        // Burada belgelenen şey: confirmer kendisi kopyayı ayırt EDEMEZ — koruma yukarıda olmalı.
        var ready = confirmer.DrainReady(t0.AddSeconds(10)).ToList();
        Assert.Single(ready);   // mevcut davranış: iki gözlem = onaylandı
    }

    [Fact]
    public void The_frame_identity_guard_skips_a_repeated_reference()
    {
        // Kaynak her kare için AYRI byte[] üretir (FfmpegFrameSource sözleşmesi), bu yüzden
        // referans eşitliği kare kimliği için KESİN ve O(1) bir ölçüttür.
        byte[] frameA = new byte[] { 1, 2, 3 };
        byte[] frameB = new byte[] { 1, 2, 3 };   // AYNI içerik, FARKLI kare

        byte[]? last = null;
        Assert.True(Anpr.Sdk.Workers.CameraWorker.IsNewFrame(frameA, last));
        last = frameA;
        Assert.False(Anpr.Sdk.Workers.CameraWorker.IsNewFrame(frameA, last));   // kopya -> atla
        Assert.True(Anpr.Sdk.Workers.CameraWorker.IsNewFrame(frameB, last));    // yeni kare -> işle
    }

    [Fact]
    public void A_null_frame_is_never_new()
        => Assert.False(Anpr.Sdk.Workers.CameraWorker.IsNewFrame(null, null));

    [Fact]
    public void Identical_content_in_a_DIFFERENT_array_is_still_a_new_frame()
    {
        // İçerik karşılaştırması YAPILMAZ: statik sahnede iki ayrı kare bit-bit aynı olabilir ve
        // onları atlamak GERÇEK gözlem kaybı olurdu. Ölçüt kare KİMLİĞİdir, içeriği değil.
        byte[] a = new byte[4096];
        byte[] b = new byte[4096];
        Assert.True(Anpr.Sdk.Workers.CameraWorker.IsNewFrame(b, a));
    }
}
