using System.Text.Json.Serialization;

namespace Anpr.Service.Panel;

/// <summary>Bir olayın gözle doğrulama sonucu.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GroundTruthVerdict
{
    /// <summary>Okuduğumuz plaka görseldeki plakayla AYNI.</summary>
    Dogru,
    /// <summary>Okuduğumuz plaka yanlış; doğrusu <see cref="GroundTruthEntry.Truth"/> alanında.</summary>
    Yanlis,
    /// <summary>Plaka görselden İNSAN GÖZÜYLE DE okunamıyor (hareket bulanıklığı, karanlık, kir).
    /// Doğruluk oranının PAYDASINDAN çıkarılır — modelin hatası sayılamaz, ama gizlenmez de.</summary>
    Okunamadi,

    /// <summary>
    /// Okuma YANLIŞ olduğu görülüyor ama GERÇEK plaka kesin belirlenemiyor.
    /// <para>Neden ayrı bir kova (2026-07-30 canlı bulgu): <c>34ME1156</c> vakasında görselden
    /// okumanın yanlış olduğu net görülüyordu (ortadaki harfler ve rakam grubu tutmuyor) ama gerçek
    /// plaka kesin okunamıyordu. Şema <c>Yanlis</c> için gerçek plakayı ZORUNLU kıldığı için vaka
    /// <c>Okunamadi</c>'ye düşüyor, PAYDADAN çıkıyor ve <b>hata hiç sayılmıyordu</b> — yani ölçülen
    /// doğruluk bu sınıf kadar İYİMSER oluyordu.</para>
    /// <para>Bu kova PAYDADA kalır ve HATA sayılır (çünkü okuma gerçekten yanlıştır), yalnız
    /// <see cref="GroundTruthEntry.Truth"/> boştur — etiketli sete plaka etiketiyle GİRMEZ.</para>
    /// </summary>
    YanlisBelirsiz,
}

/// <summary>Tek bir gözle doğrulama kaydı (ground-truth.jsonl satırı).</summary>
public sealed record GroundTruthEntry(
    DateTimeOffset At,
    Guid EventId,
    string CameraId,
    string? Evidence,
    string Ours,
    string? Truth,
    GroundTruthVerdict Verdict,
    float Ocr);

/// <summary>OCR güven bandı — tabakalı örnekleme ve kalibrasyon eğrisi için.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConfBand
{
    /// <summary>&lt; 0,85 — zaten operatöre düşen bant.</summary>
    Dusuk,
    /// <summary>0,85 – 0,95.</summary>
    Orta,
    /// <summary>0,95 – 0,99.</summary>
    Yuksek,
    /// <summary>≥ 0,99 — modelin "eminim" dediği bant. HATALARIN ÇOĞU BURADA olabilir (ölçüldü).</summary>
    CokYuksek,
}

/// <summary>Bir banda ait doğruluk özeti.</summary>
public sealed record BandAccuracy(
    ConfBand Band, int Population, int Verified, int Correct, int Wrong, int Unreadable,
    double? Accuracy, double? CiLow, double? CiHigh);

/// <summary>Popülasyona taşınmış nihai doğruluk raporu.</summary>
public sealed record AccuracyReport(
    IReadOnlyList<BandAccuracy> Bands,
    int TotalPopulation,
    int TotalVerified,
    int TotalCorrect,
    int TotalWrong,
    int TotalUnreadable,
    double? WeightedAccuracy,
    double? WeightedCiLow,
    double? WeightedCiHigh,
    IReadOnlyList<string> Caveats,

    /// <summary>
    /// BARİYERİ AÇAN kararın doğruluğu — yalnız <c>ocr ≥ ManualReviewMinOcr</c> bantları.
    /// <para>Neden ayrı (2026-07-31): eşiğin altındaki okumalar <c>LowConfidence=true</c> ile
    /// operatöre düşer, bariyeri AÇMAZ; oradaki hatalar bariyer güvenliğini etkilemez ama genel
    /// sayıyı aşağı çeker. Canlı ölçüm: genel %95,1 iken otomatik açma %98,0. Tek sayı ikisini de
    /// yanlış anlatır — genel sayı ürünü haksız yere kötü, bariyer sayısı tek başına fazla iyi
    /// gösterir. İKİSİ BİRLİKTE raporlanmalıdır.</para>
    /// </summary>
    double? AutoOpenAccuracy = null,
    double? AutoOpenCiLow = null,
    double? AutoOpenCiHigh = null,
    int AutoOpenPopulation = 0,
    int AutoOpenVerified = 0,
    /// <summary>Otomatik açmış AMA insan gözüyle de doğrulanamamış kayıt sayısı. Doğruluk
    /// paydasında değildir (model hatası sayılamaz) ama gizlenemez: bariyer, kimsenin
    /// doğrulayamadığı bir plakayla açılmıştır.</summary>
    int AutoOpenUnreadable = 0);

/// <summary>
/// KAMERA-BAĞIMSIZ DOĞRULUK ÖLÇÜMÜ (saf matematik — I/O yok, bu yüzden test edilebilir).
///
/// <para><b>Neden gerekli:</b> paneldeki "birebir uyum" bir MUTABAKAT ölçüsüdür, doğruluk değil —
/// kamera da yanılır ve ÇOĞU ZAMAN AYNI YÖNDE yanılır. Ölçülmüş vaka: <c>25 ACY 952</c> plakalı aracı
/// hem biz hem kamera 0,99+ güvenle <c>25ACV952</c> okuduk; mutabakat %100, doğruluk %0. Gözle
/// örneklemede gerçek doğruluk ~%82,7 çıkarken panel %95,8 gösteriyordu (~13 puan fark).
/// Ürün "herhangi bir IP kamera" ile çalışacaksa referans kamera ZATEN olmayacak — doğruluğu
/// kanıtlamanın tek dürüst yolu kanıt görselinden gözle doğrulamadır.</para>
///
/// <para><b>Neden TABAKALI:</b> olayların %92,8'i ocr ≥ 0,85 bandındadır. Rastgele örneklem insan
/// emeğinin neredeyse tamamını kolay vakalara harcar ve asıl merak edilen yerde (yüksek güvenli
/// hatalar) örneklem kalmaz. Bantlara bölüp her banttan ayrı örnekleyerek aynı emekle çok daha dar
/// güven aralığı elde edilir; bantlar popülasyon payıyla ağırlıklandırılıp toplama taşınır.</para>
/// </summary>
public static class GroundTruth
{
    /// <summary>Bir OCR güvenini bandına eşler.</summary>
    public static ConfBand BandOf(float ocr) => ocr switch
    {
        < 0.85f => ConfBand.Dusuk,
        < 0.95f => ConfBand.Orta,
        < 0.99f => ConfBand.Yuksek,
        _ => ConfBand.CokYuksek,
    };

    /// <summary>
    /// Wilson skor aralığı (%95). Küçük örneklemde normal-yaklaşımdan ÇOK daha dürüsttür:
    /// 7/7 için normal-yaklaşım [100%,100%] derken Wilson [64,6%, 100%] der — yani "7/7" tek başına
    /// neredeyse hiçbir şey kanıtlamaz. n=0'da null döner (uydurma sayı üretmez).
    /// </summary>
    public static (double Low, double High)? Wilson(int ok, int n)
    {
        if (n <= 0) return null;
        const double z = 1.959963985;
        double p = (double)ok / n, z2 = z * z;
        double denom = 1 + z2 / n;
        double center = (p + z2 / (2.0 * n)) / denom;
        double half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>
    /// Tabakalı doğruluk raporu. <paramref name="population"/> = her banttaki TOPLAM olay sayısı
    /// (doğrulanmamışlar dâhil); <paramref name="verified"/> = gözle doğrulanmış kayıtlar.
    /// <para><see cref="GroundTruthVerdict.Okunamadi"/> paydadan ÇIKARILIR (insan da okuyamadıysa
    /// model hatası sayılamaz) ama sayısı raporda görünür — gizlenmez.</para>
    /// </summary>
    /// <summary>
    /// Doğrulanmış kayıtları POPÜLASYONLA aynı kümeye kısar (pay ⊆ payda değişmezi).
    /// <para><b>Neden gerekli:</b> popülasyon tek bir kapsamdan sayılır (varsayılan <c>roi</c> — bariyeri
    /// açan kanal) ama doğrulama kayıtları her kapsamdan gelebilir. Bir <c>gozlem</c> olayı doğrulanırsa
    /// PAYA girer, PAYDAYA girmez ve doğruluk sessizce şişer/sapar. Şu an canlıda 229/229 kayıt roi
    /// (seçici zaten roi filtreliyor), yani bu bir düzeltme değil — sessiz bozulmaya karşı KİLİT.</para>
    /// </summary>
    public static List<GroundTruthEntry> RestrictToPopulation(
        IReadOnlyList<GroundTruthEntry> verified, IReadOnlySet<Guid> populationEventIds)
    {
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(populationEventIds);
        return verified.Where(v => populationEventIds.Contains(v.EventId)).ToList();
    }

    /// <summary>Bandın ALT sınırı — otomatik-açma eşiğinin bandı bölüp bölmediğini anlamak için.</summary>
    public static float LowerBoundOf(ConfBand band) => band switch
    {
        ConfBand.Dusuk => 0f,
        ConfBand.Orta => 0.85f,
        ConfBand.Yuksek => 0.95f,
        _ => 0.99f,
    };

    public static AccuracyReport Compute(
        IReadOnlyDictionary<ConfBand, int> population,
        IReadOnlyList<GroundTruthEntry> verified,
        float manualReviewMinOcr = 0.85f)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(verified);

        var bands = new List<BandAccuracy>();
        int totalPop = 0;
        foreach (ConfBand b in Enum.GetValues<ConfBand>())
        {
            population.TryGetValue(b, out int pop);
            totalPop += pop;

            var inBand = verified.Where(v => BandOf(v.Ocr) == b).ToList();
            int correct = inBand.Count(v => v.Verdict == GroundTruthVerdict.Dogru);
            // YanlisBelirsiz de HATADIR — yalnız doğrusu bilinmiyor. Paydada kalır.
            int wrong = inBand.Count(v => v.Verdict is GroundTruthVerdict.Yanlis
                                                     or GroundTruthVerdict.YanlisBelirsiz);
            int unread = inBand.Count(v => v.Verdict == GroundTruthVerdict.Okunamadi);
            int denom = correct + wrong;

            double? acc = denom > 0 ? (double)correct / denom : null;
            var ci = Wilson(correct, denom);
            bands.Add(new BandAccuracy(b, pop, inBand.Count, correct, wrong, unread,
                                       acc, ci?.Low, ci?.High));
        }

        int tc = bands.Sum(x => x.Correct), tw = bands.Sum(x => x.Wrong), tu = bands.Sum(x => x.Unreadable);
        var caveats = new List<string>();

        // Ağırlıklı tahmin: yalnız ÖLÇÜLMÜŞ bantlar üzerinden, ağırlıklar o bantlara yeniden normalize
        // edilir. Ölçülmemiş bandı 0 saymak tahmini sessizce çarpıtırdı — bunun yerine uyarı yazılır.
        // Ağırlıklandırma TEK yerde tanımlıdır — genel ve otomatik-açma metrikleri aynı yolu kullanır.
        // (Ayrı bir kopya yazılsaydı, p=1'de çöken aralık hatasının ikinci metrikte tekrarlanma riski
        //  olurdu; test bunu ayrıca kilitler.)
        static (double? Acc, double? Lo, double? Hi, double Covered) Weigh(IEnumerable<BandAccuracy> src)
        {
            var m = src.Where(x => x.Accuracy is not null && x.Population > 0).ToList();
            double cov = m.Sum(x => x.Population);
            if (m.Count == 0 || cov <= 0) return (null, null, null, 0);
            return (m.Sum(x => x.Population / cov * x.Accuracy!.Value),
                    m.Sum(x => x.Population / cov * (x.CiLow ?? 0)),
                    m.Sum(x => x.Population / cov * (x.CiHigh ?? 1)),
                    cov);
        }

        var measured = bands.Where(x => x.Accuracy is not null && x.Population > 0).ToList();
        double covered = measured.Sum(x => x.Population);
        double? weighted = null, wLo = null, wHi = null;

        if (measured.Count > 0 && covered > 0)
        {
            weighted = measured.Sum(x => x.Population / covered * x.Accuracy!.Value);

            // ARALIK: bant Wilson SINIRLARININ ağırlıklı toplamı — normal-yaklaşım DEĞİL.
            //
            // NEDEN (2026-07-30 canlı bulgu): tabakalı standart hata sqrt(Σ w²·p(1-p)/n) kullanıyordum.
            // Her bantta p=1 olduğunda p(1-p)=0 → varyans 0 → aralık NOKTAYA çöküyor ve rapor
            // "18/18 doğru → %100,0 – %100,0" diyordu. Oysa aynı raporun bant satırları dürüstçe
            // 44–100%, 57–100% gösteriyordu. Küçük örneklemde "kesin %100" iddiası, bu aracın tam da
            // engellemek için var olduğu yanıltmanın kendisidir (p=0'da da simetrik olarak çöker).
            //
            // Wilson sınırları p=0 ve p=1'de ÇÖKMEZ; ağırlıklı toplamları biraz MUHAFAZAKÂR (gerçek
            // aralıktan geniş) bir bant verir. Doğruluk iddiasında fazla geniş olmak, fazla dar
            // olmaktan iyidir — yönü bilinçli seçilmiştir.
            wLo = measured.Sum(x => x.Population / covered * (x.CiLow ?? 0));
            wHi = measured.Sum(x => x.Population / covered * (x.CiHigh ?? 1));
        }

        int unmeasuredPop = totalPop - (int)covered;
        if (unmeasuredPop > 0)
            caveats.Add($"Popülasyonun {unmeasuredPop} olayı ({100.0 * unmeasuredPop / Math.Max(1, totalPop):F1}%) " +
                        "hiç doğrulanmamış bir banttadır; tahmin bu bantları KAPSAMAZ.");
        if (tc + tw < 30)
            caveats.Add($"Örneklem çok küçük (n={tc + tw}); aralık geniştir ve nokta tahmin yanıltıcıdır.");
        if (tu > 0)
            caveats.Add($"{tu} kayıt insan gözüyle de okunamadı; paydadan çıkarıldı (model hatası sayılamaz).");
        foreach (var b in bands.Where(x => x.Population > 0 && x.Verified == 0))
            caveats.Add($"'{b.Band}' bandında {b.Population} olay var ama HİÇ doğrulanmamış.");

        // --- BARİYER METRİĞİ: yalnız otomatik açan bantlar ---
        // Bir bant ancak TAMAMEN eşiğin üstündeyse sayılır. Eşik bandın ORTASINA düşerse o bandın
        // bir kısmı açar bir kısmı açmaz; bandı bütün saymak metriği kirletirdi (hem operatöre düşen
        // hatalar bariyer sayısına karışır, hem popülasyon ağırlığı şişer). Dışarıda bırakılır ve
        // NEDENİ yazılır — sessizce karıştırmak yerine eksikliği görünür kılmak.
        var autoBands = bands.Where(x => LowerBoundOf(x.Band) >= manualReviewMinOcr).ToList();
        var (aAcc, aLo, aHi, aCov) = Weigh(autoBands);
        int autoVerified = autoBands.Sum(x => x.Correct + x.Wrong + x.Unreadable);
        int autoUnread = autoBands.Sum(x => x.Unreadable);

        var split = bands.FirstOrDefault(
            x => LowerBoundOf(x.Band) < manualReviewMinOcr
                 && bands.Where(y => LowerBoundOf(y.Band) > LowerBoundOf(x.Band))
                         .All(y => LowerBoundOf(y.Band) > manualReviewMinOcr)
                 && x.Population > 0);
        if (split is not null && Math.Abs(LowerBoundOf(split.Band) - manualReviewMinOcr) > 1e-6f)
            caveats.Add($"Otomatik-açma eşiği ({manualReviewMinOcr:0.###}) bant sınırına denk gelmiyor; " +
                        $"'{split.Band}' bandı ikiye bölündüğü için bariyer metriğinin DIŞINDA bırakıldı " +
                        $"({split.Population} olay).");
        if (autoUnread > 0)
            caveats.Add($"{autoUnread} kayıt insan gözüyle de okunamadığı hâlde bariyeri otomatik açmış " +
                        "(doğruluk paydasında değil — ama doğrulanamayan bir plakayla açılmıştır).");

        return new AccuracyReport(bands, totalPop, tc + tw + tu, tc, tw, tu, weighted, wLo, wHi, caveats,
                                  aAcc, aLo, aHi, (int)aCov, autoVerified, autoUnread);
    }

    /// <summary>
    /// Sıradaki doğrulanacak olayı TABAKALI seçer: doğrulanmamışlar arasından, popülasyon payına
    /// göre en AZ temsil edilen bandı öne alır. Böylece insan emeği kolay bantta tükenmez.
    /// Hiç aday yoksa null.
    /// </summary>
    public static T? PickNext<T>(
        IReadOnlyList<T> candidates,
        Func<T, float> ocrOf,
        IReadOnlyDictionary<ConfBand, int> verifiedPerBand,
        Func<T, bool>? preferable = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(ocrOf);
        ArgumentNullException.ThrowIfNull(verifiedPerBand);
        if (candidates.Count == 0) return null;

        // TERCİH FİLTRESİ (2026-07-30 ölçümü): doğrulama emeği İKİ işe yaramalı — hem precision
        // ölçümü hem KALICI ETİKETLİ SET. Ama etiketli set ancak olayın kutusu KAYDEDİLEN KAREYLE
        // TUTARLIYSA (FrameBox, şema v3) üretilebilir; eski `Box` başka bir kareden gelebildiği için
        // ondan kırpılan görüntü kayıyor. ÖLÇÜM: 91 doğrulanmış olaydan Box ile set kurmayı denedim,
        // OCR isabeti %53,8 çıktı (üretimde bu olaylar doğru okunuyor) — set KULLANILAMAZ.
        // Bu yüzden tercih edilebilir adaylar (FrameBox taşıyanlar) varsa ÖNCE onlar doğrulanır.
        if (preferable is not null)
        {
            var preferred = candidates.Where(preferable).ToList();
            if (preferred.Count > 0) candidates = preferred;
        }

        // Hedef: her bant eşit sayıda doğrulansın (kalibrasyon için en bilgilendirici tasarım).
        // En az doğrulanmış banttan bir aday varsa onu ver.
        var byBand = candidates.GroupBy(c => BandOf(ocrOf(c)))
                               .ToDictionary(g => g.Key, g => g.ToList());
        ConfBand? best = null;
        int bestCount = int.MaxValue;
        foreach (var (band, list) in byBand)
        {
            if (list.Count == 0) continue;
            verifiedPerBand.TryGetValue(band, out int done);
            if (done < bestCount) { bestCount = done; best = band; }
        }
        return best is null ? null : byBand[best.Value][0];
    }
}
