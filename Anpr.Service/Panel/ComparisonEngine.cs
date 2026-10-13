namespace Anpr.Service.Panel;

/// <summary>
/// Bizim okumalarımızla kameranın kendi ANPR okumalarını karşılaştıran TEK motor.
/// Saf (I/O yok, zaman okumaz) ve deterministiktir → birim testlenebilir.
///
/// <para><b>NEDEN TEK MOTOR (2026-07-28 denetimi):</b> panel (tarayıcı JS) ve /analiz (sunucu) ayrı
/// algoritmalar kullanıyordu; farklı pencere, farklı filtre, farklı derinlik → aynı an için farklı skor.
/// Denetim, panelin skorunu tek yönlü ŞİŞİREN dört mekanizma ölçtü:
/// (1) çapraz-kamera eşleşmesi kamera kaydını paydadan SİLİYORDU (yalnız yükseltebilen düzeltme, ~+10 puan),
/// (2) tek okumamız BİRDEN ÇOK kamera kaydını mazur gösterebiliyordu (tek-kullanım kısıtı yoktu; üstelik
///     aynı okuma kendi panosunda da kredi alıyordu — çift kredi),
/// (3) ROI dışı "gözlem" okumaları SKORA giriyordu ama listede gizliydi (sayaç 49, ekranda 41 satır; ~+13 puan),
/// (4) eşleşme penceresi collapse başlangıcına (t0) bağlıydı ve nominal ±30 sn yerine 233 sn'ye kadar
///     açılıyordu → aynı plakanın BAŞKA geçişi "birebir" sayılıyordu (~+8 puan).
/// Bu motor dördünü de yapısal olarak imkânsız kılar.</para>
///
/// <para><b>ÖLÇÜLMÜŞ GERÇEKLER (kalibrasyon):</b> kamera zaman damgası bizden ÖNCE gelir (medyan −2,3 sn
/// giriş / −1,2 sn çıkış) çünkü biz settle + çok-kareli oylama nedeniyle geç yayınlarız; bu yüzden
/// eşleşme penceresi dar ve neredeyse simetriktir. Kamera aynı aracı ardışık karelerde defalarca
/// raporlar (%28–34 fazla kayıt) → iki taraf da GEÇİŞE indirgenir. Kamera ROI kullanmaz, cadde
/// trafiğini de raporlar → eşleşmeyen kamera geçişleri körü körüne "kaçırma" sayılmaz, nedenine göre
/// kovalara ayrılır.</para>
/// </summary>
public static class ComparisonEngine
{
    /// <summary>Karşılaştırma parametreleri. Varsayılanlar canlı ölçümle kalibre edilmiştir.</summary>
    public sealed record Options
    {
        /// <summary>Aynı (kamera, plaka) için bu süre içindeki ardışık kayıtlar TEK geçiştir.
        /// İki tarafa da uygulanır (kamera tekrar raporlaması + bizim roi/gozlem çiftimiz).</summary>
        public TimeSpan PassWindow { get; init; } = TimeSpan.FromSeconds(20);

        /// <summary>Eşleşme için izin verilen en küçük gecikme (kamera − biz). Kamera damgası bizden
        /// ÖNCE geldiği için negatif taraf belirleyicidir (ölçülen medyan −1..−3 sn).</summary>
        public TimeSpan LagMin { get; init; } = TimeSpan.FromSeconds(-8);

        /// <summary>Eşleşme için izin verilen en büyük gecikme (kamera − biz).</summary>
        public TimeSpan LagMax { get; init; } = TimeSpan.FromSeconds(8);

        /// <summary>Eşleşme sayılan azami karakter farkı. 1 = birebir + tek-karakter belirsizliği.
        /// Daha büyüğü FARKLI ARAÇ riskidir; "benzer" notu olarak ayrıca raporlanır ama eşleşme değildir.</summary>
        public int MaxLev { get; init; } = 1;

        /// <summary>Çapraz kamera (araç diğer kapıdan da geçti) için izin verilen mutlak zaman farkı.</summary>
        public TimeSpan CrossLag { get; init; } = TimeSpan.FromSeconds(45);

        /// <summary>Kameranın DİĞER görüş alanında aynı plakayı bu süre içinde raporlaması = cadde trafiği.</summary>
        public TimeSpan StreetWindow { get; init; } = TimeSpan.FromSeconds(25);
    }

    /// <summary>Tek bir okuma (bizim ya da kameranın).</summary>
    public sealed record Obs(DateTimeOffset Ts, string CameraId, string Plate, string Scope = "roi",
                             float Ocr = 0f, bool LowConfidence = false, string? SnappedFrom = null,
                             string? Evidence = null, string? Frame = null);

    /// <summary>Aynı aracın tek geçişi (birden çok ham okumadan indirgenmiş).</summary>
    public sealed record Pass(DateTimeOffset Ts, string CameraId, string Plate, string Scope,
                              float Ocr, bool LowConfidence, string? SnappedFrom, string? Evidence,
                              string? Frame, int RawCount, DateTimeOffset LastTs);

    /// <summary>Kameranın eşleşmeyen bir geçişinin nedeni.</summary>
    public enum CamVerdict
    {
        /// <summary>Bizim ROI'mizde karşılığı yok ve başka açıklaması da yok — GERÇEK kaçırma adayı.</summary>
        KacirmaAdayi,
        /// <summary>Bizim ROI DIŞI (gözlem) okumamızda var: aracı gördük ama bariyer bölgesinde değildi.</summary>
        GozlemdeVar,
        /// <summary>Aynı plakayı kameranın DİĞER görüş alanı da yakın zamanda raporladı → cadde trafiği.</summary>
        Cadde,
        /// <summary>Kameranın kendi okuması TR plaka grameriyle uyumsuz (kameranın hatası).</summary>
        KameraHatali,
        /// <summary>Araç DİĞER kapımızdan geçerken bizde okundu (çapraz eşleşme).</summary>
        Capraz,
        /// <summary>
        /// AYNI plakayı biz de aynı kamerada okuduk, ama zaman farkı eşleşme penceresinin DIŞINDA.
        /// Kaçırma DEĞİLDİR — araç yakalandı, yalnız kamera onu bizden belirgin şekilde önce/sonra
        /// raporladı (canlı: 34SN988, fark 18 sn; kamera aracı ROI'ye girmeden görebiliyor).
        /// Ölçüm: gecikme dağılımı medyan −1,4 sn ama kuyruk uzun (p1 = −45,8 sn); pencereyi genişletmek
        /// kapsamayı %98,4'ten yalnız %98,7'ye çıkarıp yanlış-eşleşme riskini artırırdı, bu yüzden
        /// pencere DAR tutulup bu vakalar ayrı kovaya alındı.
        /// </summary>
        PencereDisi,
    }

    /// <summary>Bizim bir geçişimizin sonucu.</summary>
    public sealed record Row(Pass Ours, Pass? Camera, int Distance, double? LagSeconds, string Verdict,
                             string? SimilarNote = null);

    /// <summary>Eşleşmeyen kamera geçişi.</summary>
    public sealed record CamRow(Pass Camera, CamVerdict Verdict, string? SimilarNote = null);

    /// <summary>Karşılaştırmanın yapıldığı ortak zaman aralığı. İki taraf da bununla kırpılır.</summary>
    public sealed record Window(DateTimeOffset From, DateTimeOffset To);

    /// <summary>
    /// Sonuç. <b>İnvaryantlar</b> (testlerle sabitlenir):
    /// I1: birebir + birFark + sadeceBiz == roi geçiş sayısı;
    /// I2: eşleşen + çapraz + cadde + kameraHatali + gozlemdeVar + kacirmaAdayi == pencere içi kamera geçişi;
    /// I3: hiçbir geçiş iki kez kullanılmaz; I4: girdi sırası sonucu değiştirmez.
    /// </summary>
    public sealed record Result(Window? Window, IReadOnlyList<Row> Rows, IReadOnlyList<CamRow> CameraOnly,
                                int OursPasses, int CamPasses)
    {
        public int Exact => Rows.Count(r => r.Verdict == "birebir");
        public int Near => Rows.Count(r => r.Verdict == "1-fark");
        public int OursOnly => Rows.Count(r => r.Verdict == "sadece-biz");
        public int Matched => Exact + Near;

        public int Cross => CameraOnly.Count(c => c.Verdict == CamVerdict.Capraz);
        public int Street => CameraOnly.Count(c => c.Verdict == CamVerdict.Cadde);
        public int CamFaulty => CameraOnly.Count(c => c.Verdict == CamVerdict.KameraHatali);
        public int InObservation => CameraOnly.Count(c => c.Verdict == CamVerdict.GozlemdeVar);
        public int OutOfWindow => CameraOnly.Count(c => c.Verdict == CamVerdict.PencereDisi);
        public int MissCandidate => CameraOnly.Count(c => c.Verdict == CamVerdict.KacirmaAdayi);

        /// <summary>Eşleşenler içinde tam metin uyumu. "Okuduğumuzda ne kadar aynısını okuyoruz."</summary>
        public double? ExactAgreement => Matched > 0 ? Math.Round(100.0 * Exact / Matched, 1) : null;

        /// <summary>
        /// İYİMSER yakalama: payda yalnız AÇIKLANAMAYAN kamera geçişlerini içerir (cadde / kamera-hatası /
        /// çapraz / gözlemde / pencere-dışı hariç).
        /// <para><b>DİKKAT — bu bir ÜST SINIRDIR.</b> "Cadde" kovası kanıtlanamaz bir varsayıma dayanır:
        /// aynı plakanın iki görüş alanında ≤25 sn içinde görünmesi. Ölçüm (2026-07-29) bu varsayımı
        /// DOĞRULAMADI: zaman farkı dağılımı sürekli (medyan 5,7 sn; %13'ü &lt;3 sn, %69'u 3–8 sn) ve
        /// "cadde" sayılan 117 kaydın <b>79'unda o an bizim ROI okumamız YOK</b> — bunlar gerçek kaçırma
        /// olabilir. Bu yüzden panelde ve raporda <see cref="CaptureLow"/> ile BİRLİKTE gösterilmelidir.</para>
        /// </summary>
        public double? Capture => (Matched + MissCandidate) > 0
            ? Math.Round(100.0 * Matched / (Matched + MissCandidate), 1) : null;

        /// <summary>
        /// KÖTÜMSER yakalama (ALT SINIR): "cadde" varsayımı çöktüğünde ne olurdu — cadde kovası da
        /// paydaya girer. Gerçek yakalama <see cref="CaptureLow"/> ile <see cref="Capture"/> arasındadır;
        /// aradaki fark, kanıtlayamadığımız belirsizliğin büyüklüğüdür.
        /// </summary>
        public double? CaptureLow => (Matched + MissCandidate + Street) > 0
            ? Math.Round(100.0 * Matched / (Matched + MissCandidate + Street), 1) : null;

        /// <summary>
        /// Kamera bir referans DEĞİLDİR — o da yanılır. <see cref="ExactAgreement"/> "iki sistem aynı
        /// metni üretti" demektir, "doğru okuduk" demek DEĞİLDİR: görsel ground-truth örneklemesinde
        /// (2026-07-29) anlaşmazlıkların bir kısmında biz haklıydık (06DET904 ↔ kamera 06DET90 kesik),
        /// bir kısmında kamera (25A4855 ↔ bizim 25A48555 fazladan hane). Mutabakat, doğruluğun ÜST
        /// SINIRI için bile garanti vermez; yalnızca iki bağımsız sistemin ne kadar örtüştüğünü ölçer.
        /// </summary>
        public string AgreementCaveat => "kamera referans değildir; bu bir MUTABAKAT ölçüsüdür, doğruluk değil";
    }

    public static string Compact(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (char c in s) if (char.IsLetterOrDigit(c)) buf[n++] = char.ToUpperInvariant(c);
        return new string(buf[..n]);
    }

    /// <summary>Levenshtein; <paramref name="max"/> aşılırsa erken çıkar (max+1 döner).</summary>
    public static int Levenshtein(string a, string b, int max = int.MaxValue)
    {
        if (a == b) return 0;
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (cur[j] < rowMin) rowMin = cur[j];
            }
            if (rowMin > max) return max + 1;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// Ham okumaları GEÇİŞE indirger — <b>iki tarafa da</b> uygulanır. Aynı (kamera, plaka) için
    /// <see cref="Options.PassWindow"/> içindeki ardışık okumalar tek geçiştir; pencere her okumada kayar.
    /// İlk okuma zamanı temsil eder (kameranın ilk gördüğü an bizim olayımıza en yakın olandır).
    /// Bizim tarafta roi+gozlem aynı geçişe düşerse <b>roi kazanır</b> (bariyer kararı odur).
    /// </summary>
    public static List<Pass> ReducePasses(IEnumerable<Obs> src, Options o)
    {
        var outp = new List<Pass>();
        // Deterministik sıra: zaman, sonra kamera, sonra plaka (aynı ts'li kayıtlarda sıra bağımsızlığı).
        var ordered = src.Where(x => x.Plate.Length > 0)
                         .OrderBy(x => x.Ts).ThenBy(x => x.CameraId, StringComparer.Ordinal)
                         .ThenBy(x => x.Plate, StringComparer.Ordinal).ToList();
        var open = new Dictionary<(string, string), int>();   // (cam,plate) -> outp index
        foreach (var x in ordered)
        {
            var key = (x.CameraId, x.Plate);
            if (open.TryGetValue(key, out int idx) && (x.Ts - outp[idx].LastTs) <= o.PassWindow)
            {
                var p = outp[idx];
                bool takeScope = p.Scope != "roi" && x.Scope == "roi";      // roi, gozlem'i yükseltir
                bool better = x.Ocr > p.Ocr;
                outp[idx] = p with
                {
                    LastTs = x.Ts,
                    RawCount = p.RawCount + 1,
                    Scope = takeScope ? "roi" : p.Scope,
                    Ocr = better ? x.Ocr : p.Ocr,
                    LowConfidence = takeScope || better ? x.LowConfidence : p.LowConfidence,
                    SnappedFrom = p.SnappedFrom ?? x.SnappedFrom,
                    Evidence = p.Evidence ?? x.Evidence,
                    Frame = p.Frame ?? x.Frame,
                };
                continue;
            }
            open[key] = outp.Count;
            outp.Add(new Pass(x.Ts, x.CameraId, x.Plate, x.Scope, x.Ocr, x.LowConfidence,
                              x.SnappedFrom, x.Evidence, x.Frame, 1, x.Ts));
        }
        return outp;
    }

    /// <summary>
    /// İki tarafı eşleştirir. Sıra-bağımsızdır: tüm aday çiftler (mesafe, |gecikme|, zaman, plaka)
    /// anahtarıyla sıralanır ve baştan itibaren <b>iki tarafı da boş olan</b> çiftler bağlanır.
    /// Böylece "erken bir olay sonrakinin kaydını çalar" (greedy kaskad) sorunu yapısal olarak biter.
    /// </summary>
    public static Result Match(IReadOnlyList<Pass> oursAll, IReadOnlyList<Pass> camAll, Options o)
    {
        if (oursAll.Count == 0 || camAll.Count == 0)
            return new Result(null, Array.Empty<Row>(), Array.Empty<CamRow>(), oursAll.Count, camAll.Count);

        // ORTAK PENCERE: iki tarafın da verisi olan aralık. Servis ve kamera farklı süre çalışmış
        // olabilir (canlı: kamera 19 saat fazla veriye sahipti) ve tek taraflı fazlalık "kaçırma" ya da
        // "fazladan okuma" olarak sayılmamalı.
        // TOLERANS: sınır, eşleşme penceresi kadar genişletilir — aksi halde sınırdaki gerçek bir çift
        // (kamera damgası bizimkinden 1-3 sn farklı) kesişimin dışında kalır ve tek olaylık kümelerde
        // aralık ters dönerdi (from > to).
        var tol = TimeSpan.FromSeconds(Math.Max(Math.Abs(o.LagMin.TotalSeconds), Math.Abs(o.LagMax.TotalSeconds)));
        var lo = oursAll.Min(x => x.Ts) > camAll.Min(x => x.Ts) ? oursAll.Min(x => x.Ts) : camAll.Min(x => x.Ts);
        var hi = oursAll.Max(x => x.Ts) < camAll.Max(x => x.Ts) ? oursAll.Max(x => x.Ts) : camAll.Max(x => x.Ts);
        var from = lo - tol;
        var to = hi + tol;
        if (to < from) return new Result(null, Array.Empty<Row>(), Array.Empty<CamRow>(), oursAll.Count, camAll.Count);

        var ours = oursAll.Where(x => x.Ts >= from && x.Ts <= to).ToList();
        var cam = camAll.Where(x => x.Ts >= from && x.Ts <= to).ToList();
        var win = new Window(from, to);

        var usedOurs = new bool[ours.Count];
        var usedCam = new bool[cam.Count];
        var pairOf = new int[ours.Count];
        var distOf = new int[ours.Count];
        Array.Fill(pairOf, -1);

        // --- 1) ROI eşleştirmesi (skorlanan tek küme) ---
        var cands = new List<(int lev, double absLag, long ts, string plate, int oi, int ci, double lag)>();
        for (int i = 0; i < ours.Count; i++)
        {
            if (ours[i].Scope != "roi") continue;
            for (int j = 0; j < cam.Count; j++)
            {
                if (cam[j].CameraId != ours[i].CameraId) continue;
                double lag = (cam[j].Ts - ours[i].Ts).TotalSeconds;
                if (lag < o.LagMin.TotalSeconds || lag > o.LagMax.TotalSeconds) continue;
                int d = Levenshtein(ours[i].Plate, cam[j].Plate, o.MaxLev);
                if (d > o.MaxLev) continue;
                cands.Add((d, Math.Abs(lag), ours[i].Ts.UtcTicks, ours[i].Plate, i, j, lag));
            }
        }
        cands.Sort((a, b) =>
        {
            int c = a.lev.CompareTo(b.lev); if (c != 0) return c;
            c = a.absLag.CompareTo(b.absLag); if (c != 0) return c;
            c = a.ts.CompareTo(b.ts); if (c != 0) return c;
            c = string.CompareOrdinal(a.plate, b.plate); if (c != 0) return c;
            return a.ci.CompareTo(b.ci);
        });
        var lagOf = new double[ours.Count];
        foreach (var c in cands)
        {
            if (usedOurs[c.oi] || usedCam[c.ci]) continue;
            usedOurs[c.oi] = usedCam[c.ci] = true;
            pairOf[c.oi] = c.ci; distOf[c.oi] = c.lev; lagOf[c.oi] = c.lag;
        }

        // --- 2) Bizim satırlar ---
        var rows = new List<Row>(ours.Count);
        foreach (var (p, i) in ours.Select((p, i) => (p, i)))
        {
            if (p.Scope != "roi") continue;                       // gözlem skora girmez
            if (pairOf[i] >= 0)
                rows.Add(new Row(p, cam[pairOf[i]], distOf[i], Math.Round(lagOf[i], 1),
                                 distOf[i] == 0 ? "birebir" : "1-fark"));
            else
                rows.Add(new Row(p, null, -1, null, "sadece-biz"));
        }

        // --- 3) Eşleşmeyen kamera geçişlerini NEDENİNE göre kovala ---
        var camRows = new List<CamRow>();
        for (int j = 0; j < cam.Count; j++)
        {
            if (usedCam[j]) continue;
            var c = cam[j];

            // (a) ROI DIŞI gözlemimizde var mı → aracı gördük ama bariyer bölgesinde değildi
            int obs = -1;
            for (int i = 0; i < ours.Count; i++)
            {
                if (usedOurs[i] || ours[i].Scope == "roi" || ours[i].CameraId != c.CameraId) continue;
                double lag = (c.Ts - ours[i].Ts).TotalSeconds;
                if (lag < o.LagMin.TotalSeconds || lag > o.LagMax.TotalSeconds) continue;
                if (Levenshtein(ours[i].Plate, c.Plate, o.MaxLev) > o.MaxLev) continue;
                obs = i; break;
            }
            if (obs >= 0) { usedOurs[obs] = true; usedCam[j] = true; camRows.Add(new CamRow(c, CamVerdict.GozlemdeVar)); continue; }

            // (b) ÇAPRAZ: araç DİĞER kapımızdan geçerken okunmuş (tek kullanım — çift kredi imkânsız)
            int cross = -1;
            for (int i = 0; i < ours.Count; i++)
            {
                if (usedOurs[i] || ours[i].CameraId == c.CameraId) continue;
                if (Math.Abs((c.Ts - ours[i].Ts).TotalSeconds) > o.CrossLag.TotalSeconds) continue;
                if (Levenshtein(ours[i].Plate, c.Plate, o.MaxLev) > o.MaxLev) continue;
                cross = i; break;
            }
            if (cross >= 0) { usedOurs[cross] = true; usedCam[j] = true; camRows.Add(new CamRow(c, CamVerdict.Capraz)); continue; }

            // (c) CADDE: aynı plakayı kameranın DİĞER görüş alanı da yakın zamanda raporladı
            bool street = cam.Any(x => x.CameraId != c.CameraId && x.Plate == c.Plate
                                       && Math.Abs((x.Ts - c.Ts).TotalSeconds) <= o.StreetWindow.TotalSeconds);
            if (street) { camRows.Add(new CamRow(c, CamVerdict.Cadde)); continue; }

            // (d) KAMERANIN KENDİ HATASI: TR gramerine uymayan okuma
            if (!Anpr.Core.TurkishPlates.TurkishPlateNormalizer.Normalize(c.Plate).IsValid)
            { camRows.Add(new CamRow(c, CamVerdict.KameraHatali)); continue; }

            // (e) PENCERE DIŞI: aynı kamerada aynı(≈) plakayı biz de okuduk ama zaman farkı eşleşme
            // penceresini aşıyor. Bu bir KAÇIRMA DEĞİLDİR; kaçırma sayısını şişirmemek için ayrılır.
            int outw = -1; int outD = int.MaxValue; double outLag = 0;
            for (int i = 0; i < ours.Count; i++)
            {
                if (usedOurs[i] || ours[i].CameraId != c.CameraId) continue;
                double lag = (c.Ts - ours[i].Ts).TotalSeconds;
                if (Math.Abs(lag) > o.CrossLag.TotalSeconds) continue;
                int d = Levenshtein(ours[i].Plate, c.Plate, o.MaxLev);
                if (d > o.MaxLev) continue;
                if (d < outD || (d == outD && Math.Abs(lag) < Math.Abs(outLag)))
                { outw = i; outD = d; outLag = lag; }
            }
            if (outw >= 0)
            {
                usedOurs[outw] = true; usedCam[j] = true;
                camRows.Add(new CamRow(c, CamVerdict.PencereDisi,
                    $"bizde {ours[outw].Plate} var, fark {outLag:F0} sn (pencere dışı)"));
                continue;
            }

            // (f) Açıklanamayan → GERÇEK kaçırma adayı. Teşhis için en yakın okumamızı not düş.
            string? note = null;
            int bestD = int.MaxValue; string? bestP = null;
            foreach (var p in ours)
            {
                if (p.CameraId != c.CameraId) continue;
                if (Math.Abs((c.Ts - p.Ts).TotalSeconds) > o.CrossLag.TotalSeconds) continue;
                int d = Levenshtein(p.Plate, c.Plate, 4);
                if (d < bestD) { bestD = d; bestP = p.Plate; }
            }
            if (bestP != null && bestD <= 4) note = $"benzer: {bestP} (lev {bestD})";
            camRows.Add(new CamRow(c, CamVerdict.KacirmaAdayi, note));
        }

        return new Result(win, rows, camRows, ours.Count, cam.Count);
    }
}
