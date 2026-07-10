using Anpr.Core.TurkishPlates;

namespace Anpr.Core.Confirmation;

public sealed class PlateConfirmerOptions
{
    /// <summary>Yayınlandıktan sonra aynı araç bu süre boyunca tekrar yayınlanmaz (dedup).</summary>
    public TimeSpan DedupWindow { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Onay sayısına ulaşmayan bir araç, son görülmesinden bu süre sonra (artık geçmiş sayılır)
    /// yine de yayınlanır — tek karede geçen aracı kaçırmamak için.
    /// </summary>
    public TimeSpan SettleWindow { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Bu kadar kez görülünce araç hemen "onaylı" sayılır ve yayınlanır. 1 = onay kapalı.</summary>
    public int MinConfirmations { get; set; } = 2;

    /// <summary>
    /// Karakter-bazlı oylama: açıksa, aynı aracın tüm karelerinde slot-başına olasılık dağılımları
    /// toplanır ve pozisyon-başına en olası karakter seçilerek uzlaşı plaka kurulur. Kapalıysa en yüksek
    /// güvenli tüm-plaka seçilir.
    /// </summary>
    public bool CharacterVoting { get; set; } = true;

    /// <summary>
    /// TR gramer-kısıtlı çözümleme: açıksa (ve CharacterVoting açıksa) biriken dağılım, geçerli TR
    /// yapıları üzerinden çözülür (8/B, 0/O, 1/I gibi karışıklıkları pozisyona göre çözer). Kapalıysa
    /// düz pozisyon-argmax + normalizasyon.
    /// </summary>
    public bool ConstrainedDecoding { get; set; } = true;

    /// <summary>
    /// Kimlik anahtarında il'i yok say (harf|rakam). İl varyantlarını birleştirir AMA rakam-varyantlarını
    /// ayırır; live1h ölçümünde net kazanç vermedi (recall +1pp, emit gürültüsü arttı) → varsayılan KAPALI.
    /// false = il|harf (rakam-varyantlarını birleştirir, daha temiz çıktı).
    /// </summary>
    public bool MergeAcrossProvince { get; set; } = false;

    /// <summary>
    /// Bulanık zamansal kümeleme: >0 ise, aktif penceredeki (DedupWindow) okumalar KESİN kimlik yerine
    /// edit-mesafesi ≤ bu değer olacak şekilde eşleştirilir. Böylece aynı aracın geçişindeki il/rakam/harf
    /// yanlış-okuma varyantları TEK ize düşer (kamera-ANPR davranışı), karakter-oylamasıyla uzlaşı çıkar.
    /// 0 = kapalı (kesin kimlik anahtarı). Kameraya yaklaşmak için ~2 önerilir; ölçülmeli.
    /// </summary>
    public int FuzzyMergeDistance { get; set; } = 0;

    /// <summary>
    /// Güven-kapılı erken emit: onay sayısına ulaşan iz, uzlaşı OCR güveni bu eşiğin ALTINDAYSA hemen
    /// yayınlanmaz; araç yaklaştıkça gelen daha iyi okumaları bekler (en geç SettleWindow'da yayınlanır).
    /// Uzaktan 2 kırpık okumayla erken/yanlış emit'i keser (kamera tetik-çizgisi davranışına yaklaşır).
    /// Yüksek güvenli plakalar için gecikme eklemez. 0 = kapalı (eski davranış).
    /// </summary>
    public float EarlyEmitMinOcr { get; set; } = 0f;

    /// <summary>
    /// UZAMSAL iz birleştirme: metin-fuzzy eşleşme bulunamadığında, SON GÖRÜLMESİ SpatialMergeMaxGap
    /// içinde olan ve kutusu okumanın kutusuyla en az bu IoU kadar çakışan iz aynı FİZİKSEL plaka
    /// sayılıp birleştirilir (metin mesafesi SpatialMergeMaxDistance'a kadar). Aynı aracın çok farklı
    /// yanlış-okuma varyantlarının (25DDS84 → 26ADD884, lev≥3) ayrı izlere bölünüp ÇOKLU emit
    /// üretmesini keser; oylar tek havuzda toplanır, uzlaşı doğru plakayı verir. 0 = kapalı.
    /// </summary>
    public float SpatialMergeIou { get; set; } = 0.35f;

    /// <summary>Uzamsal birleşme için izin son görülmesiyle okuma arasındaki azami süre (ardışık-kare bağı;
    /// uzun tutulursa kapıda sıraya giren FARKLI araçlar birbirine zincirlenebilir).</summary>
    public TimeSpan SpatialMergeMaxGap { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Uzamsal birleşmede tolere edilen azami metin mesafesi (sağlamlık sınırı).</summary>
    public int SpatialMergeMaxDistance { get; set; } = 4;

    /// <summary>SKIP_CHAR seviyesi (TrConstrainedDecoder): zayıf hayalet karakter slotunda "karakter yok"
    /// hipotezini destekler. 0 = kapalı. Ölçümle açılır (öneri 0.5).</summary>
    public float SkipCharLevel { get; set; } = 0f;

    /// <summary>Uzlaşı emitinde üretilecek aday sayısı (top-K; whitelist toleransı + varyant bastırma). 0 = kapalı.</summary>
    public int CandidateCount { get; set; } = 5;
}

/// <summary>
/// Aynı aracın ardışık karelerdeki okumalarını birleştirip TEK ve EN GÜVENİLİR olayı üretir.
///
/// Kimlik anahtarı = il + harf bloğu (ör. "25|AFH"). CharacterVoting açıkken slot-başına TAM olasılık
/// dağılımı biriktirilir; emitte ConstrainedDecoding açıksa TR gramer-kısıtlı çözümleme, değilse
/// pozisyon-argmax + normalizasyon uygulanır. Thread-safe değildir; tek kamera döngüsünden sıralı kullanılır.
/// </summary>
public sealed class PlateConfirmer
{
    private sealed class Track
    {
        public int Count;
        public DateTimeOffset Last;
        public PlateReading Best = null!;
        public bool Emitted;
        public float[]? SlotSum;   // slots*vocab, kareler boyunca toplam
        public string? Alphabet;
        public int Slots;
        public PlateBox LastBox;   // uzamsal iz birleştirme için son görülen kutu
        public bool Enhanced;      // İkinci-Şans zenginleştirmesi bu ize bir kez uygulandı
        // Fuzzy modda bu izde görülen TÜM okuma varyantları (compact). Eşleşme yalnız Best.Text'e
        // yapılırsa Best geçiş sırasında kayar (drift) ve aynı araç ikinci ize bölünür → çift emit.
        public HashSet<string>? Variants;
    }

    private readonly Dictionary<string, Track> _tracks = new();
    private readonly PlateConfirmerOptions _o;
    private int _seq;

    public PlateConfirmer(PlateConfirmerOptions options) => _o = options;

    public void Observe(PlateReading reading, DateTimeOffset now)
    {
        if (!reading.IsValidTr) return;

        Track? t; string key;
        if (_o.FuzzyMergeDistance > 0)
        {
            // Okumaya uyan TÜM izler: birden fazlaysa okuma bir KÖPRÜdür — aynı aracın bölünmüş
            // izleridir (ölçülen vaka: 25AFB625 ←1→ 25AFB525 ←1→ 25AFB52, uçlar arası lev=2).
            // İzleri birleştir; oylamalar toplanır, konsensus doğru plakayı verir, çift emit biter.
            var matches = FindFuzzyMatches(reading, now);
            if (matches.Count == 0 && _o.SpatialMergeIou > 0)
            {
                // Metin köprüsü yok ama kutu aynı yerde ve taze -> aynı FİZİKSEL plaka (ağır yanlış-okuma varyantı).
                var spatial = FindSpatialMatch(reading, now);
                if (spatial != null) matches.Add(spatial.Value);
            }
            if (matches.Count == 0)
            {
                key = Compact(reading.Text) + "#" + (_seq++);
                t = new Track { Count = 0, Best = reading };
                _tracks[key] = t;
            }
            else
            {
                (key, t) = (matches[0].Key, matches[0].Track);
                for (int i = 1; i < matches.Count; i++)
                {
                    MergeTracks(t, matches[i].Track);
                    _tracks.Remove(matches[i].Key);
                }
                if (t.Emitted && now - t.Last > _o.DedupWindow)
                {
                    t = new Track { Count = 0, Best = reading };
                    _tracks[key] = t;
                }
            }
            (t.Variants ??= new HashSet<string>()).Add(Compact(reading.Text));
        }
        else
        {
            key = IdentityKey(reading.Text);
            if (!_tracks.TryGetValue(key, out t) || (t.Emitted && now - t.Last > _o.DedupWindow))
            {
                t = new Track { Count = 0, Best = reading };
                _tracks[key] = t;
            }
        }

        t.Count++;
        t.Last = now;
        t.LastBox = reading.Box;
        if (reading.Confidence > t.Best.Confidence) t.Best = reading;

        if (_o.CharacterVoting && reading.SlotProbs is { Length: > 0 } sp && reading.OcrAlphabet is { Length: > 0 } alpha)
        {
            if (t.SlotSum == null || t.SlotSum.Length != sp.Length)
            {
                t.SlotSum = new float[sp.Length];
                t.Alphabet = alpha;
                t.Slots = sp.Length / alpha.Length;
            }
            for (int i = 0; i < sp.Length; i++) t.SlotSum[i] += sp[i];
        }
    }

    public IReadOnlyList<PlateReading> DrainReady(DateTimeOffset now)
    {
        List<PlateReading>? ready = null;

        foreach (var t in _tracks.Values)
        {
            if (t.Emitted) continue;

            bool confirmed = t.Count >= _o.MinConfirmations;
            bool settled = now - t.Last >= _o.SettleWindow;
            if (confirmed || settled)
            {
                var result = BuildResult(t);
                // Erken (settle olmadan) emit için uzlaşı güveni eşiği geçmeli; geçmiyorsa araç
                // yaklaştıkça gelecek daha iyi okumaları bekle (en geç settle'da yine yayınlanır).
                if (!settled && _o.EarlyEmitMinOcr > 0 && result.OcrConfidence < _o.EarlyEmitMinOcr)
                    continue;
                t.Emitted = true;
                (ready ??= new List<PlateReading>()).Add(result);
            }
        }

        if (_tracks.Count >= 64)
        {
            var stale = _tracks.Where(kv => now - kv.Value.Last > _o.DedupWindow).Select(kv => kv.Key).ToList();
            foreach (var k in stale) _tracks.Remove(k);
        }

        return ready ?? (IReadOnlyList<PlateReading>)Array.Empty<PlateReading>();
    }

    /// <summary>
    /// Onaylı ama uzlaşı güveni eşiğin altında kalan, henüz zenginleştirilmemiş izler
    /// (İkinci-Şans Şeridi tetiği). Emit edilmiş izler dahil edilmez.
    /// </summary>
    public IReadOnlyList<(string Key, PlateReading Consensus)> PendingLowConf(DateTimeOffset now, float maxOcr)
    {
        List<(string, PlateReading)>? list = null;
        foreach (var kv in _tracks)
        {
            var t = kv.Value;
            if (t.Emitted || t.Enhanced) continue;
            if (t.Count < _o.MinConfirmations) continue;
            if (now - t.Last > _o.DedupWindow) continue;
            var c = BuildResult(t);
            if (c.OcrConfidence < maxOcr) (list ??= new()).Add((kv.Key, c));
        }
        return list ?? (IReadOnlyList<(string, PlateReading)>)Array.Empty<(string, PlateReading)>();
    }

    /// <summary>
    /// Zenginleştirilmiş okuma mevcut ize EK GÖZLEM olarak katılır (SlotSum toplanır, varyant eklenir).
    /// Emit tekilliği bozulmaz: olay yine normal yoldan (onay/settle) TEK kez çıkar.
    /// </summary>
    public void InjectObservation(string key, PlateReading reading)
    {
        if (!_tracks.TryGetValue(key, out var t) || !reading.IsValidTr) return;
        t.Count++;
        if (reading.Confidence > t.Best.Confidence) t.Best = reading;
        (t.Variants ??= new HashSet<string>()).Add(Compact(reading.Text));
        if (_o.CharacterVoting && reading.SlotProbs is { Length: > 0 } sp2 && reading.OcrAlphabet is { Length: > 0 })
        {
            if (t.SlotSum != null && t.SlotSum.Length == sp2.Length)
                for (int i = 0; i < sp2.Length; i++) t.SlotSum[i] += sp2[i];
        }
    }

    /// <summary>İzi "zenginleştirildi" olarak işaretler (iz başına tek deneme garantisi).</summary>
    public void MarkEnhanced(string key)
    {
        if (_tracks.TryGetValue(key, out var t)) t.Enhanced = true;
    }

    private PlateReading BuildResult(Track t)
    {
        if (!_o.CharacterVoting || t.SlotSum == null || t.Alphabet == null || t.Slots == 0)
            return t.Best;

        // Biriken dağılımı ortalamaya çevir (0..1).
        var mean = new float[t.SlotSum.Length];
        for (int i = 0; i < mean.Length; i++) mean[i] = t.SlotSum[i] / t.Count;

        string? normalized = null, rawCompact = null;
        float conf = t.Best.OcrConfidence;

        IReadOnlyList<PlateCandidate>? candidates = null;
        if (_o.ConstrainedDecoding)
        {
            var dec = TrConstrainedDecoder.Decode(mean, t.Alphabet, t.Slots, _o.SkipCharLevel);
            if (dec.Ok) { normalized = dec.Normalized; rawCompact = dec.Compact; conf = dec.Confidence; }
            if (_o.CandidateCount > 0)
            {
                var top = TrConstrainedDecoder.DecodeTopK(mean, t.Alphabet, t.Slots, _o.CandidateCount, _o.SkipCharLevel);
                if (top.Count > 0)
                {
                    var list = new List<PlateCandidate>(top.Count);
                    foreach (var c in top) list.Add(new PlateCandidate(c.Compact, c.Confidence));
                    candidates = list;
                }
            }
        }

        if (normalized == null)
        {
            // Kısıtsız: pozisyon-argmax -> ham metin -> TR normalize
            int vocab = t.Alphabet.Length;
            var raw = new char[t.Slots];
            for (int s = 0; s < t.Slots; s++)
            {
                int start = s * vocab; float bestP = -1; int bestIdx = 0;
                for (int c = 0; c < vocab; c++) if (mean[start + c] > bestP) { bestP = mean[start + c]; bestIdx = c; }
                raw[s] = t.Alphabet[bestIdx];
            }
            var tr = TurkishPlateNormalizer.Normalize(new string(raw));
            if (tr.IsValid) { normalized = tr.Normalized; rawCompact = tr.Compact; }
        }

        if (normalized == null) return t.Best; // uzlaşı geçersizse en iyi okuma

        return t.Best with
        {
            Text = normalized,
            RawText = rawCompact ?? normalized.Replace(" ", ""),
            OcrConfidence = conf,
            Confidence = t.Best.DetectorConfidence * conf,
            IsValidTr = true,
            Candidates = candidates,
        };
    }

    // MergeAcrossProvince: kimlik = HARF|RAKAM (il hariç) -> il varyantları tek ize düşer, il oylanır.
    // Aksi halde eski davranış = İL|HARF.
    private string IdentityKey(string normalizedText)
    {
        var parts = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return normalizedText;
        return _o.MergeAcrossProvince ? parts[1] + "|" + parts[2] : parts[0] + "|" + parts[1];
    }

    // Kutu-çakışmalı (aynı fiziksel plaka) iz araması: taze (≤SpatialMergeMaxGap) ve IoU eşiği geçen
    // izlerden EN YÜKSEK IoU'lusu; sağlamlık için metin mesafesi SpatialMergeMaxDistance ile sınırlı.
    private (string Key, Track Track, int Dist)? FindSpatialMatch(PlateReading reading, DateTimeOffset now)
    {
        if (reading.Box.Width <= 0) return null;
        string rc = Compact(reading.Text);
        (string, Track, int)? best = null;
        float bestIou = 0f;
        foreach (var kv in _tracks)
        {
            var t = kv.Value;
            if (now - t.Last > _o.SpatialMergeMaxGap) continue;
            if (t.LastBox.Width <= 0) continue;
            float iou = Iou(t.LastBox, reading.Box);
            if (iou < _o.SpatialMergeIou || iou <= bestIou) continue;
            int d = Levenshtein(rc, Compact(t.Best.Text));
            if (t.Variants is { } vars)
                foreach (var v in vars)
                {
                    if (d <= 1) break;
                    int dv = Levenshtein(rc, v);
                    if (dv < d) d = dv;
                }
            if (d > _o.SpatialMergeMaxDistance) continue;
            bestIou = iou;
            best = (kv.Key, t, d);
        }
        return best;
    }

    private static float Iou(PlateBox a, PlateBox b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width), y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        int iw = x2 - x1, ih = y2 - y1;
        if (iw <= 0 || ih <= 0) return 0f;
        float inter = iw * (float)ih;
        float union = a.Width * (float)a.Height + b.Width * (float)b.Height - inter;
        return union <= 0 ? 0f : inter / union;
    }

    // Aktif penceredeki (DedupWindow) izler arasında, okuması edit-mesafesi ≤ eşik olan TÜM izleri bul
    // (mesafeye göre sıralı). Eşleşme, izde GÖRÜLMÜŞ TÜM varyantlara karşı yapılır (yalnız Best'e değil —
    // Best drift edince aynı araç ikinci ize bölünüp çift emit üretiyordu). Birden çok eşleşme = okuma
    // köprü: çağıran izleri birleştirir.
    private List<(string Key, Track Track, int Dist)> FindFuzzyMatches(PlateReading reading, DateTimeOffset now)
    {
        string rc = Compact(reading.Text);
        List<(string, Track, int)>? found = null;
        foreach (var kv in _tracks)
        {
            if (now - kv.Value.Last > _o.DedupWindow) continue; // eski/farklı araç
            int d = Levenshtein(rc, Compact(kv.Value.Best.Text));
            if (kv.Value.Variants is { } vars)
                foreach (var v in vars)
                {
                    if (d <= 0) break;
                    int dv = Levenshtein(rc, v);
                    if (dv < d) d = dv;
                }
            if (d <= _o.FuzzyMergeDistance) (found ??= new()).Add((kv.Key, kv.Value, d));
        }
        found?.Sort((a, b) => a.Item3.CompareTo(b.Item3));
        return found ?? new List<(string, Track, int)>();
    }

    // İki bölünmüş izi birleştir: sayaçlar toplanır, oylama (SlotSum) birikimleri eklenir, varyantlar
    // birleşir. Biri yayınlandıysa birleşik iz de yayınlanmış sayılır (çift emit engellenir).
    private static void MergeTracks(Track into, Track other)
    {
        into.Count += other.Count;
        if (other.Last > into.Last) { into.Last = other.Last; into.LastBox = other.LastBox; }
        if (other.Best.Confidence > into.Best.Confidence) into.Best = other.Best;
        into.Emitted |= other.Emitted;
        if (other.SlotSum != null)
        {
            if (into.SlotSum == null || into.SlotSum.Length != other.SlotSum.Length)
            {
                into.SlotSum = other.SlotSum; into.Alphabet = other.Alphabet; into.Slots = other.Slots;
            }
            else
            {
                for (int i = 0; i < into.SlotSum.Length; i++) into.SlotSum[i] += other.SlotSum[i];
            }
        }
        if (other.Variants != null)
        {
            (into.Variants ??= new HashSet<string>()).UnionWith(other.Variants);
        }
    }

    private static string Compact(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (var ch in s) if (char.IsLetterOrDigit(ch)) buf[n++] = char.ToUpperInvariant(ch);
        return new string(buf[..n]);
    }

    private static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var dp = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) dp[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = dp[0]; dp[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cur = dp[j];
                dp[j] = Math.Min(Math.Min(dp[j] + 1, dp[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = cur;
            }
        }
        return dp[b.Length];
    }
}
