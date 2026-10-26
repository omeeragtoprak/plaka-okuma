using System.Text.Json;
using Anpr.Sdk.Events;

namespace Anpr.Service.Panel;

/// <summary>
/// Analiz motoru: kendi olaylarımızı (events.jsonl) kameranın bağımsız ANPR kayıtlarıyla
/// (camera-records.jsonl) eşleştirir ve denetlenebilir istatistik üretir.
/// <para>Eşleştirme SUNUCU tarafında yapılır: panelin istemci tarafındaki eşleştirmesi yalnız ekranda
/// duran son N kaydı görür ve her iki listeyi ayrı ayrı kırpar — bu yüzden "tam" bir karşılaştırma
/// tablosu üretemez. Burada tüm kayıt kümesi üzerinde tek-kullanımlık (greedy-en-yakın) eşleştirme yapılır.</para>
/// </summary>
public sealed class AnalyticsService
{
    // Kamera kendi ANPR sonucunu bizden SONRA yayınlıyor (ölçüldü: tipik +4..6 sn, poll gecikmesi dahil).
    // Bu yüzden pencere asimetrik: bizden biraz önce, epeyce sonra.
    //
    // 🔴 ASİMETRİNİN YÖNÜ VERİYLE UYUŞMUYOR (2026-08-05, 3925 eşleşen çift) — ve nedeni ÖLÇÜLEN
    // BÜYÜKLÜK ile SABİTİN UYGULANDIĞI BÜYÜKLÜĞÜN FARKLI OLMASI:
    //   · Yukarıdaki "+4..6 sn" ERİŞİLEBİLİRLİK gecikmesidir ("poll gecikmesi dahil" — kaydı biz NE
    //     ZAMAN GÖREBİLİYORUZ). Muhtemelen doğrudur ve ölçülmemiştir burada.
    //   · Ama bu pencere `TimeIso`ya, yani KAMERANIN KENDİ DAMGASINA uygulanıyor (kayıtta başka zaman
    //     alanı YOK). O eksende kamera bizden ÖNCE damgalıyor: medyan -1,5 sn · p05 -6 · p95 +2.
    // Yani sabit, ölçülmemiş bir eksene taşınmış. ÖLÇÜLEN SONUÇ:
    //   ✂️  -8 sn sınırında KESİLEN gerçek eşleşme: 154/3925 = %3,9 (134'ü -30..-8 aralığında)
    //   ⚠️  +8..+30 sn'de kabul edilen uzak eşleşme: 20/3925 = %0,5 — p95'in çok ötesi, muhtemelen
    //       AYNI PLAKANIN BAŞKA BİR GEÇİŞİYLE yanlış çiftleşme
    //   simetrik ±10 sn olsaydı %96,9 yakalanırdı; bugünkü -8/+30 penceresi %95,8 yakalıyor.
    //
    // ⚠️ DEĞİŞTİRİLMEDİ — GEREKÇE: bu pencere, bugüne dek YAYINLANMIŞ tüm kamera-karşılaştırma
    // rakamlarının (diag4 referansları dâhil) üretildiği sabittir. Değiştirmek o rakamları sessizce
    // kaydırır ve geçmişle karşılaştırılamaz hâle getirir; doğru yol bilinçli bir YENİDEN TEMEL ALMA
    // (re-baseline) turudur — kullanıcı kararı. Bu not, o tur geldiğinde ölçümün hazır olması için.
    private static readonly TimeSpan MatchBefore = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MatchAfter = TimeSpan.FromSeconds(30);

    private readonly string _root;
    public AnalyticsService() => _root = AppContext.BaseDirectory;

    public sealed record OurEvent(DateTimeOffset Ts, string CameraId, string Plate, float Ocr, float Conf,
                                  bool LowConfidence, string Scope, string? SnappedFrom, string? Evidence,
                                  string? Frame, string[] Candidates, Guid EventId = default, float DetectorConfidence = 0f,
                                  int BoxWidth = 0, int BoxHeight = 0, bool HasFrameBox = false);

    public sealed record CamRecord(DateTimeOffset Ts, string CameraId, string Plate);

    /// <summary>Bir olayın kamera karşılığıyla birlikte satırı (karşılaştırma tablosunun bir satırı).</summary>
    public sealed record MatchRow(string Time, string CameraId, string Ours, string? Camera, int Distance,
                                  double? LagSeconds, float Ocr, bool LowConfidence, string Verdict,
                                  string? SnappedFrom, string? Evidence, string? Frame, string Scope,
                                  string[] Candidates);

    public static string Compact(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (char c in s) if (char.IsLetterOrDigit(c)) buf[n++] = char.ToUpperInvariant(c);
        return new string(buf[..n]);
    }

    /// <summary>Levenshtein mesafesi (karşılaştırma tablosundaki "fark" sütunu).</summary>
    public static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// Olaylardan YER-GERÇEĞİ GEREKTİRMEYEN kayma göstergeleri üretir (<see cref="DriftMonitor"/> girdisi).
    /// <para>Buradaki her gösterge, sahada sessiz bozulmanın bilinen bir yoluna karşılık gelir:
    /// <b>8-hane payı</b> düşerse kırpma son karakteri kesiyor olabilir (34ZH7641 vakası);
    /// <b>güven dağılımı</b> düşerse optik/ışık koşulu değişmiş olabilir; <b>otomatik açma oranı</b>
    /// düşerse operatör yükü artıyor; <b>düşük-güven payı</b> artarsa kalite bozuluyor.</para>
    /// <para>NOT: kare kaybı ve gecikme (p50/p95) BURADA YOK — onlar süreç başından beri KÜMÜLATİF
    /// sayaçlardır, pencereye bölünemezler. /health'te anlık değer olarak gösterilirler.</para>
    /// </summary>
    public static List<MetricSample> BuildDriftMetrics(IReadOnlyList<OurEvent> evs)
    {
        ArgumentNullException.ThrowIfNull(evs);
        var roi = evs.Where(e => e.Scope == "roi").ToList();
        int n = roi.Count;
        var list = new List<MetricSample>();
        if (n == 0) return list;

        static double Median(IEnumerable<float> src)
        {
            var v = src.OrderBy(x => x).ToList();
            return v.Count == 0 ? 0 : v[v.Count / 2];
        }

        list.Add(new MetricSample("otomatik açma oranı", roi.Count(e => !e.LowConfidence) / (double)n, n, true,
            "Operatöre düşmeden bariyer açan olay payı. Düşerse operatör yükü artmış."));
        list.Add(new MetricSample("düşük güven payı", roi.Count(e => e.LowConfidence) / (double)n, n, true,
            "LowConfidence=true payı. Artarsa görüntü/okuma kalitesi bozuluyor."));
        // KESİLME İMZASI: bir okuma, BAŞKA bir okumanın tam ön-eki mi (X ⊂ X+1 hane)? Aynı aracın hem
        // tam hem kesik okunması bunun doğrudan izidir (34ZH7641 → 34ZH764 vakası). Benzersiz plaka
        // sayısına normalize edilir.
        //
        // NEDEN "8-hane payı" DEĞİL (2026-07-30 canlı yanlış alarm): önce 8 karakterli plaka payını
        // izliyordum ve kayma izlemesi UYARI verdi (%63,9 → %57,7). Araştırınca ortaya çıktı ki pay
        // BENZERSİZ plakada da düşmüştü (%64,0 → %58,1) ama kesilme imzası AZALMIŞTI (7 → 2 çift).
        // Yani sinyal, hangi araçların geçtiğini (trafik karışımı) kalite kaymasıyla karıştırıyordu —
        // 'olay/saat' göstergesiyle AYNI hata sınıfı. Ön-ek imzası olguyu DOĞRUDAN ölçer ve plaka
        // karışımından bağımsızdır.
        var uniq = roi.Select(e => e.Plate).ToHashSet(StringComparer.Ordinal);
        int prefixPairs = uniq.Count(a => uniq.Any(b => b.Length == a.Length + 1
                                                        && b.StartsWith(a, StringComparison.Ordinal)));
        if (uniq.Count > 0)
            list.Add(new MetricSample("kesilme imzası", prefixPairs / (double)uniq.Count, uniq.Count, true,
                "Bir okumanın BAŞKA bir okumanın tam ön-eki olduğu plakaların payı (X ⊂ X+1 hane). " +
                "ARTARSA kırpma son karakteri kesiyor demektir. Trafik karışımından bağımsızdır."));
        list.Add(new MetricSample("snap oranı", roi.Count(e => e.SnappedFrom != null) / (double)n, n, true,
            "Whitelist düzeltmesi uygulanan olay payı. Artarsa model sistematik kaymış olabilir."));
        list.Add(new MetricSample("ocr<0,85 payı", roi.Count(e => e.Ocr < 0.85f) / (double)n, n, true,
            "Eşik altı okuma payı — kalite kapısının hemen üstündeki bant."));
        list.Add(new MetricSample("ocr medyan", Median(roi.Select(e => e.Ocr)), n, false,
            "OCR güveni medyanı. Kalibrasyon bozuk olsa da DAĞILIM KAYMASI gerçek bir sinyaldir."));
        list.Add(new MetricSample("dedektör medyan", Median(roi.Select(e => e.DetectorConfidence)), n, false,
            "Dedektör kutu güveni medyanı. Düşerse kamera odağı/açısı veya sahne değişmiş olabilir; kesilme vakalarında bu değer aile ortalamasının ALTINDA ölçüldü."));

        // "olay/saat" BİLİNÇLİ OLARAK YOK. Denendi ve KALDIRILDI (2026-07-30): trafik değişkenliğini
        // ve duruş süresini kalite kaymasıyla karıştırıyor — ilk canlı koşuda servis yeniden
        // başlatıldığı için −%80 "UYARI" üretti, oysa modelde hiçbir bozulma yoktu. Normal
        // değişkenlikte tetiklenen gösterge, operatörü uyarılara karşı körleştirir; yani hiç
        // göstergeden KÖTÜDÜR. Duruş/erişilebilirlik zaten /health'te (restartCount, framesReceived)
        // ve olay sayıları yanıtın "counts" alanında görünür.
        return list;
    }

    /// <summary>
    /// AYNI UZUNLUKTA iki plakanın kaçta kaçının aynı konumda aynı karakter olduğu (0-1).
    /// <para>Neden Levenshtein YETMİYOR (2026-07-30 canlı bulgu): <c>25ATE548</c> ile gerçek plaka
    /// <c>25AFL548</c> arası lev=2 olduğu için lev≤1 filtresi bu çifti HİÇ göstermiyordu — ve ben
    /// bulanık kırpıntıya bakıp yanlış okumayı "doğru" ONAYLADIM. Oysa konum-benzerliği 0,75:
    /// iki plaka 8 karakterin 6'sında aynı ("25", "548" ortak). Ağır bozulmuş okumalarda karakterler
    /// DEĞİŞİR ama KONUM korunur; Levenshtein bunu yakalayamaz, konum-benzerliği yakalar.</para>
    /// <para>Eşik 0,75 ÖLÇÜMLE seçildi: bilinen iki vakayı da yakalıyor (25ATE548↔25AFL548 ve
    /// 34ME1156↔34PE8156, ikisi de lev=2 / benzerlik 0,75) ve plaka başına ortalama komşu sayısını
    /// 0,39 → 1,04 yapıyor. 0,70'te 1,53'e çıkıp gürültü artıyor.</para>
    /// </summary>
    public static double PositionalSimilarity(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length != b.Length || a.Length == 0) return 0;
        int same = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] == b[i]) same++;
        return (double)same / a.Length;
    }

    /// <summary>Konum-benzerliği eşiği — bu değerin üstündeki çiftler çapraz kanıtta gösterilir.</summary>
    public const double SimilarityThreshold = 0.75;

    /// <summary>
    /// EŞ-ZAMANLI kanıt penceresi (saniye). Metinden BAĞIMSIZ sinyal: aynı araç saniyeler içinde
    /// bir kez de güçlü okunmuş olur. ÖLÇÜLDÜ (22 doğrulanmış hata): ±15 sn tam yakalamayı
    /// (%77) MEDYAN 1 adayla verir; ±30/45/90 sn yakalamayı ARTIRMAZ, yalnız aday sayısını
    /// büyütür (medyan 1 → 3, maks 3 → 5).
    /// </summary>
    public const double SamePassWindowSeconds = 15.0;

    /// <summary>
    /// Eş-zamanlı adayın minimum OCR güveni. ÖLÇÜLDÜ: 0,95 → %77 yakalama; 0,99 → %50.
    /// Zayıf bir okumayı başka bir zayıf okumayla açıklamak kanıt değil gürültüdür.
    /// </summary>
    public const float SamePassMinOcr = 0.95f;

    /// <summary>Doğrulama sırasında gösterilen çapraz kanıt satırı.</summary>
    /// <param name="Relation">"ayni" = aynı plaka dizgisi · "komsu" = lev≤1 (ardışık filo plakası olabilir) ·
/// "benzer" = aynı uzunlukta, konum-benzerliği ≥0,75 (ağır bozulmuş okumanın GERÇEK plakası olabilir).</param>
    public sealed record ContextShot(string Plate, string Relation, float Ocr, int BoxArea,
                                     string? Evidence, DateTimeOffset At, Guid EventId);

    /// <summary>Gözle incelenmesi önerilen plaka çifti (triyaj kaydı — hüküm DEĞİL).</summary>
    public sealed record SuspectPair(string Dominant, int DominantRoiCount, float DominantMaxOcr,
                                     string Rare, int RareCount, float RareMaxOcr,
                                     string? DominantEvidence, string? RareEvidence);

    /// <summary>
    /// ŞÜPHELİ ÇİFT TRİYAJI: sık okunan bir plakanın, <b>bariyer kanalında hiç görünmemiş</b> benzer
    /// bir komşusu varsa ikisini yan yana koyar.
    ///
    /// <para><b>Neden (2026-07-31):</b> <c>25ACV952</c> (gerçeği <c>25 ACY 952</c>) 39 olayda bariyeri
    /// otomatik açtı ve hiçbir otomatik koruma göremedi — güven 0,999, kamera da aynı yönde yanılıyor.
    /// Bu sınıfı bulmanın tek yolu kanıt görselini gözle okumak; bu kuyruk <b>nereye bakılacağını</b>
    /// daraltır (canlıda 477 plaka → 9 çift).</para>
    ///
    /// <para><b>GÖZLEM KATMANI ŞART:</b> gerçek plaka <c>25ACY952</c> roi'de HİÇ okunmamıştı, yalnız
    /// gözlem katmanında bir kez. Yalnız roi'ye bakan bir analiz bu vakayı yapısal olarak GÖREMEZ —
    /// ölçüldü ve teste bağlandı.</para>
    ///
    /// <para><b>BU BİR DEDEKTÖR DEĞİLDİR.</b> Canlı 9 çiftin 8'inde nadir taraf yalnızca baskın
    /// plakanın zayıf yanlış okumasıdır (masum); yalnız 1'inde nadir taraf GERÇEK plakadır. Veride
    /// ikisi AYNI görünür. Ayrımı yalnız insan, kanıt görselinden yapabilir — çıktı bir İNCELEME
    /// KUYRUĞUDUR, hüküm değil.</para>
    /// </summary>
    /// <param name="minDominantRoi">Baskın tarafın asgari roi olay sayısı. Ölçüm: 20 → 9 çift,
    /// 10 → 21, 3 → 46. Kuyruğun tek değeri KISA olmasıdır.</param>
    public static List<SuspectPair> BuildSuspectPairs(
        IReadOnlyList<OurEvent> evs, int minDominantRoi = 20, int max = 25)
    {
        ArgumentNullException.ThrowIfNull(evs);

        var roiCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var allCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var maxOcr = new Dictionary<string, float>(StringComparer.Ordinal);
        var sample = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var e in evs)
        {
            if (e.Plate.Length == 0) continue;
            allCount[e.Plate] = allCount.GetValueOrDefault(e.Plate) + 1;
            if (e.Scope == "roi") roiCount[e.Plate] = roiCount.GetValueOrDefault(e.Plate) + 1;
            if (e.Ocr > maxOcr.GetValueOrDefault(e.Plate))
            {
                maxOcr[e.Plate] = e.Ocr;
                if (e.Evidence is not null) sample[e.Plate] = e.Evidence;
            }
            sample.TryAdd(e.Plate, e.Evidence);
        }

        var dominants = roiCount.Where(kv => kv.Value >= minDominantRoi).Select(kv => kv.Key).ToList();
        // Aday nadirler: bariyer kanalında HİÇ görünmemiş plakalar. Roi'de de okunmuşsa kendi başına
        // "gerçek araç" kanıtı vardır (bu sahada 25ADL989/990/991/992 hepsi gerçek) → şüpheli değil.
        var rares = allCount.Keys.Where(p => !roiCount.ContainsKey(p)).ToList();

        var pairs = new List<SuspectPair>();
        foreach (string d in dominants)
            foreach (string r in rares)
            {
                if (Levenshtein(d, r) > 1 && PositionalSimilarity(d, r) < SimilarityThreshold) continue;
                pairs.Add(new SuspectPair(d, roiCount[d], maxOcr[d],
                                          r, allCount[r], maxOcr[r],
                                          sample.GetValueOrDefault(d), sample.GetValueOrDefault(r)));
            }

        // Etki sırası: en çok bariyer açan baskın en üstte — operatör yukarıdan aşağı bakar.
        return pairs.OrderByDescending(p => p.DominantRoiCount)
                    .ThenBy(p => p.Rare, StringComparer.Ordinal)
                    .Take(max)
                    .ToList();
    }

    /// <summary>
    /// Bir olayı doğrularken gösterilecek ÇAPRAZ KANIT listesi: aynı plakanın (ve lev≤1 komşularının)
    /// diğer okumaları, EN NET olan başta (kutu alanına göre).
    ///
    /// <para><b>Neden var (ölçülmüş insan hatası):</b> 2026-07-30 doğrulama kampanyasında bulanık tek
    /// kırpıntı doğrulayıcıyı İKİ KEZ yanılttı — <c>25ADL989</c> "26ADL989", <c>25ADL991</c>
    /// "25ADL981" okundu ve her ikisinde de yanlış bir "Yanlış" verdict'i kaydedilmek üzereydi; yani
    /// yer-gerçeği ZEHİRLENECEKTİ. Aynı plakanın en büyük kutulu kanıtına bakmak ikisini de düzeltti.
    /// Bu panel o manuel adımı otomatikleştirir.</para>
    ///
    /// <para><b>Sıralama neden kutu alanına göre:</b> kutu alanı, kırpıntının kaç gerçek piksel taşıdığının
    /// doğrudan vekilidir. OCR güvenine göre sıralamak YANILTICI olurdu — ölçüldüğü üzere güven
    /// kalibre değil (kesik okuma 0,9977 alabiliyor), ama piksel sayısı yalan söylemez.</para>
    ///
    /// <para>Lev≤1 komşular AYRI etiketlenir: bu sahada ardışık filo plakaları var (25ADL989/990/991/992
    /// hepsi gerçek araç) — komşuyu "aynı araç" sanmak yeni bir hata sınıfı yaratırdı.</para>
    /// </summary>
    public static List<ContextShot> BuildVerificationContext(
        IReadOnlyList<OurEvent> evs, Guid eventId, int max = 6)
    {
        ArgumentNullException.ThrowIfNull(evs);
        var target = evs.FirstOrDefault(e => e.EventId == eventId);
        if (target is null) return new List<ContextShot>();

        var shots = new List<ContextShot>();
        foreach (var e in evs)
        {
            if (e.EventId == eventId || e.Evidence is null) continue;
            int d = Levenshtein(e.Plate, target.Plate);
            double sim = PositionalSimilarity(e.Plate, target.Plate);
            // lev≤1 KOMŞU + konum-benzeri: ikincisi olmadan ağır bozulmuş okumanın gerçek plakası
            // hiç yüzeye çıkmıyordu (25ATE548 vakası lev=2 olduğu için görünmüyordu).
            if (d > 1 && sim < SimilarityThreshold)
            {
                // METİN BENZERLİĞİ YETMİYOR — ÖLÇÜLDÜ (2026-07-31, 22 doğrulanmış hata):
                // metin paneli gerçek plakayı yalnız %27'sinde yüzeye çıkardı. Ağır bozulmuş okumada
                // karakterlerin ÇOĞU değişir (26FFV779 ← 06FJV769 benzerlik 0,625; 27BH8600 ← 77BH860
                // uzunluk bile farklı → 0) ve eşiği düşürmek ölçülmüş biçimde gürültü katıyor.
                //
                // METİNDEN BAĞIMSIZ SİNYAL: aynı araç saniyeler içinde bir kez de GÜÇLÜ okunmuş olur.
                // Aynı 22 hatada bu sinyal %77 yakaladı ve ikisi birlikte %100 — tamamlayıcılar.
                // Süpürme (±15/30/45/90 sn × ocr 0,95/0,99): ±15 sn + 0,95 tam yakalamayı MEDYAN 1
                // adayla veriyor (maks 3); pencereyi büyütmek yalnız gürültü, eşiği 0,99'a çekmek
                // yakalamayı %77 → %50 düşürüyor.
                //
                // UYARI (sözleşme): bu AYNI ARAÇ OLDUĞUNU KANITLAMAZ — yoğun trafikte 15 sn içinde
                // başka araç da geçer. İnsana ADAY sunar; hüküm insanındır. Bu yüzden "ayni" etiketi
                // ASLA verilmez (teste bağlandı).
                double dt = Math.Abs((e.Ts - target.Ts).TotalSeconds);
                if (dt > SamePassWindowSeconds || e.Ocr < SamePassMinOcr) continue;
                shots.Add(new ContextShot(e.Plate, "es-zamanli", e.Ocr,
                                          e.BoxWidth * e.BoxHeight, e.Evidence, e.Ts, e.EventId));
                continue;
            }
            shots.Add(new ContextShot(e.Plate, d == 0 ? "ayni" : d <= 1 ? "komsu" : "benzer", e.Ocr,
                                      e.BoxWidth * e.BoxHeight, e.Evidence, e.Ts, e.EventId));
        }
        // Aynı plaka önce (en güvenilir çapraz kanıt), sonra lev1 komşular, sonra konum-benzerleri;
        // eş-zamanlılar EN SONDA (en zayıf iddia). Her grupta EN BÜYÜK KUTU önce — güvene göre
        // sıralamak yanıltıcı olurdu (kalibre değil), piksel sayısı yalan söylemez.
        static int Rank(ContextShot s) => s.Relation switch
            { "ayni" => 0, "komsu" => 1, "benzer" => 2, _ => 3 };

        // SIRALAMA ÖLÇÜTÜ İLİŞKİ TÜRÜNE GÖRE DEĞİŞİR — ikisi FARKLI soru sorar:
        //   metin kanıtı  → "hangi kırpıntı en net?"        → KUTU ALANI
        //   eş-zamanlı    → "hangisi aynı araç olabilir?"   → ZAMAN YAKINLIĞI
        // CANLI KUSUR (2026-07-31): ikisini de kutu alanıyla sıralıyordum ve `25ZCG011` olayında
        // gerçek plaka (`25TGG001`, −5,2 sn, 6235px) +11,5 sn'deki BAŞKA bir aracın daha büyük
        // kutusu tarafından şeritten atıldı. Zamansal yakınlık aynı-araç için doğrudan vekildir;
        // piksel sayısı değildir.
        var ordered = shots
            .OrderBy(Rank)
            .ThenBy(s => Rank(s) == 3 ? Math.Abs((s.At - target.Ts).TotalSeconds) : 0)
            .ThenByDescending(s => s.BoxArea)
            .ToList();

        // DİZGİ BAŞINA TAVAN — canlıda yakalanan üçüncü kusur: `25ACV952` olayında şerit AYNI
        // DİZGİNİN 6 kopyasıyla doldu ve gerçek plaka `25ACY952` (lev=1 komşu!) dışarı itildi;
        // panel altı kez "kendini" gösterip tek işe yarar adayı sakladı. Aynı dizginin 3. kırpıntısı
        // bilgi KATMAZ (en net ikisi zaten baştadır); şeridin değeri ADAY ÇEŞİTLİLİĞİNDEDİR.
        const int maxPerPlate = 2;
        var perPlate = new Dictionary<string, int>(StringComparer.Ordinal);
        ordered = ordered.Where(s =>
        {
            perPlate.TryGetValue(s.Plate, out int c);
            if (c >= maxPerPlate) return false;
            perPlate[s.Plate] = c + 1;
            return true;
        }).ToList();

        // SLOT AYIRMA — aynı canlı vakada yakalanan ikinci kusur: 6 metin-benzeri aday şeridi
        // doldurup eş-zamanlı sinyali tamamen dışarı itiyordu; panel yanlış cevabı gösterip
        // doğrusunu saklıyordu. Metin sinyalinin ölçülmüş yakalaması yalnız %27 — tüm slotları ona
        // vermek panelin varlık sebebini yok eder. 2 slot yeter: eş-zamanlı aday sayısı ölçümde
        // medyan 1 / p90 2 / maks 3.
        const int reservedForTime = 2;
        var timed = ordered.Where(s => Rank(s) == 3).Take(reservedForTime).ToList();
        var rest = ordered.Where(s => !timed.Contains(s)).Take(Math.Max(0, max - timed.Count));
        return rest.Concat(timed)
                   .OrderBy(Rank)
                   .ThenBy(s => Rank(s) == 3 ? Math.Abs((s.At - target.Ts).TotalSeconds) : 0)
                   .ThenByDescending(s => s.BoxArea)
                   .ToList();
    }

    public List<OurEvent> LoadOurs()
    {
        var list = new List<OurEvent>();
        string p = Path.Combine(_root, "events.jsonl");
        if (!File.Exists(p)) return list;
        foreach (var line in ReadLinesSafe(p))
        {
            try
            {
                var e = JsonSerializer.Deserialize<PlateEvent>(line);
                if (e == null) continue;
                list.Add(new OurEvent(e.Timestamp, e.CameraId, Compact(e.PlateCompact), e.OcrConfidence,
                    e.Confidence, e.LowConfidence, e.Scope, e.SnappedFrom, e.EvidencePath, e.FramePath,
                    e.Candidates?.Select(c => c.Compact).ToArray() ?? Array.Empty<string>(), e.EventId,
                    e.DetectorConfidence, e.Box.Width, e.Box.Height, e.FrameBox is not null));
            }
            catch { /* bozuk satır: atla (kısmi yazım) */ }
        }
        return list;
    }

    public List<CamRecord> LoadCamera()
    {
        var list = new List<CamRecord>();
        string p = Path.Combine(_root, "camera-records.jsonl");
        if (!File.Exists(p)) return list;
        foreach (var line in ReadLinesSafe(p))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                string plate = Compact(r.GetProperty("Plate").GetString());
                if (plate.Length == 0) continue;
                list.Add(new CamRecord(DateTimeOffset.Parse(r.GetProperty("TimeIso").GetString()!),
                                       r.GetProperty("CameraId").GetString() ?? "", plate));
            }
            catch { }
        }
        return list;
    }

    /// <summary>
    /// Kamera kayıtlarını GEÇİŞ bazına indirger: kamera aynı aracı ardışık karelerde birden çok kez
    /// raporluyor (ölçüm 2026-07-28: 703 ham kayıt → 461 gerçek geçiş, <b>%34 fazla</b>; canlı örnek
    /// 25ADP187 giriş kamerasında 16:36:01.096/.295/.404 = 300 ms içinde 3 kayıt).
    /// <para>Bu yapılmazsa fazla kayıtlar hiçbir olayla eşleşemez ve tabloda "sadece kamera" olarak
    /// görünür — karşılaştırma, biz araçları kaçırıyormuşuz gibi YANILTIR. Bizim tarafımızda aynı işi
    /// varyant bastırma + dedup penceresi zaten yapıyor, bu yüzden iki taraf ancak burada eşitlenir.</para>
    /// Aynı (kamera, plaka) için <paramref name="window"/> içinde kalan ardışık kayıtlar tek geçiş sayılır
    /// ve İLKİ temsil eder (kamera ilk gördüğü an, bizim olayımıza en yakın olandır).
    /// </summary>
    public static List<CamRecord> ClusterPasses(IEnumerable<CamRecord> src, TimeSpan window)
    {
        var outp = new List<CamRecord>();
        var last = new Dictionary<(string, string), DateTimeOffset>();
        foreach (var r in src.OrderBy(r => r.Ts))
        {
            var key = (r.CameraId, r.Plate);
            if (last.TryGetValue(key, out var prev) && (r.Ts - prev) <= window)
            {
                last[key] = r.Ts;   // aynı geçişin devamı: pencereyi kaydır, kayıt EKLEME
                continue;
            }
            last[key] = r.Ts;
            outp.Add(r);
        }
        return outp;
    }

    /// <summary>Aynı aracın kamerada ardışık raporlanma penceresi. Bariyerde aynı plakanın 20 sn içinde
    /// GERÇEKTEN ikinci kez geçmesi mümkün değildir; ölçülen tekrarlar 0.3–3 sn aralığında.</summary>
    private static readonly TimeSpan PassWindow = TimeSpan.FromSeconds(20);

    private static IEnumerable<string> ReadLinesSafe(string path)
    {
        // Servis dosyaya yazmaya devam ediyor → paylaşımlı okuma şart.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } l) if (l.Length > 0) yield return l;
    }

    /// <summary>Ham okumaları motorun beklediği biçime çevirir (JSONL → Obs).</summary>
    private static IEnumerable<ComparisonEngine.Obs> ToObs(IEnumerable<OurEvent> src)
        => src.Select(o => new ComparisonEngine.Obs(o.Ts, o.CameraId, o.Plate, o.Scope, o.Ocr,
                                                    o.LowConfidence, o.SnappedFrom, o.Evidence, o.Frame));

    private static IEnumerable<ComparisonEngine.Obs> ToObs(IEnumerable<CamRecord> src)
        => src.Select(c => new ComparisonEngine.Obs(c.Ts, c.CameraId, c.Plate));

    /// <summary>
    /// Karşılaştırmayı TEK MOTOR üzerinden üretir (<see cref="ComparisonEngine"/>).
    /// Bu sınıf artık yalnız yükleyici + adaptördür; eşleştirme mantığı burada DEĞİLDİR — canlı panel
    /// ve /analiz aynı sonucu görsün diye (2026-07-28: iki ayrı algoritma farklı skor üretiyordu).
    /// </summary>
    public ComparisonEngine.Result Compare(string? cameraId, DateTimeOffset? since = null)
    {
        var o = new ComparisonEngine.Options();
        var ours = LoadOurs().Where(x => cameraId is null || x.CameraId == cameraId)
                             .Where(x => since is null || x.Ts >= since);
        var cam = LoadCamera().Where(x => cameraId is null || x.CameraId == cameraId)
                              .Where(x => since is null || x.Ts >= since);
        return ComparisonEngine.Match(ComparisonEngine.ReducePasses(ToObs(ours), o),
                                      ComparisonEngine.ReducePasses(ToObs(cam), o), o);
    }
}
