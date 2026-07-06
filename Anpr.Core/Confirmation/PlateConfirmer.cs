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
    /// Karakter-bazlı oylama: açıksa, aynı aracın tüm karelerinde her SLOT pozisyonu için olasılıklar
    /// toplanır ve pozisyon-başına en olası karakter seçilerek uzlaşı plaka kurulur. Tek karede hiçbiri
    /// tam doğru olmasa bile doğru plaka çıkabilir. Kapalıysa en yüksek güvenli tüm-plaka seçilir.
    /// </summary>
    public bool CharacterVoting { get; set; } = true;
}

/// <summary>
/// Aynı aracın ardışık karelerdeki okumalarını birleştirip TEK ve EN GÜVENİLİR olayı üretir.
///
/// Kimlik anahtarı = il + harf bloğu (ör. "25|AFH"). İki birleştirme stratejisi:
///  - CharacterVoting=true: slot-bazlı olasılık toplamı -> pozisyon-başına argmax -> uzlaşı plaka.
///  - false: en yüksek güvenli tüm-plaka okuması.
///
/// Tek karede görülen araç da SettleWindow sonunda yayınlanır (kaçırılmaz). Thread-safe değildir;
/// tek bir kamera döngüsünden sıralı kullanılır.
/// </summary>
public sealed class PlateConfirmer
{
    private sealed class Track
    {
        public int Count;
        public DateTimeOffset Last;
        public PlateReading Best = null!;
        public bool Emitted;
        // slot index -> (karakter -> olasılık toplamı)
        public Dictionary<int, Dictionary<char, float>>? SlotVotes;
        public int SlotLen;
    }

    private readonly Dictionary<string, Track> _tracks = new();
    private readonly PlateConfirmerOptions _o;

    public PlateConfirmer(PlateConfirmerOptions options) => _o = options;

    public void Observe(PlateReading reading, DateTimeOffset now)
    {
        if (!reading.IsValidTr) return;

        string key = IdentityKey(reading.Text);
        if (!_tracks.TryGetValue(key, out var t) || (t.Emitted && now - t.Last > _o.DedupWindow))
        {
            t = new Track { Count = 0, Best = reading };
            _tracks[key] = t;
        }

        t.Count++;
        t.Last = now;
        if (reading.Confidence > t.Best.Confidence) t.Best = reading;

        if (_o.CharacterVoting && reading.Slots is { Count: > 0 } slots)
        {
            t.SlotVotes ??= new Dictionary<int, Dictionary<char, float>>();
            t.SlotLen = Math.Max(t.SlotLen, slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                if (!t.SlotVotes.TryGetValue(i, out var m)) { m = new Dictionary<char, float>(); t.SlotVotes[i] = m; }
                m.TryGetValue(slots[i].Char, out var p);
                m[slots[i].Char] = p + slots[i].Prob; // olasılıkla ağırlıklı oy
            }
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
                t.Emitted = true;
                (ready ??= new List<PlateReading>()).Add(BuildResult(t));
            }
        }

        if (_tracks.Count >= 64)
        {
            var stale = _tracks.Where(kv => now - kv.Value.Last > _o.DedupWindow).Select(kv => kv.Key).ToList();
            foreach (var k in stale) _tracks.Remove(k);
        }

        return ready ?? (IReadOnlyList<PlateReading>)Array.Empty<PlateReading>();
    }

    // Karakter oylaması açıksa slot uzlaşısından plaka kur; değilse/başarısızsa en iyi okumayı döndür.
    private PlateReading BuildResult(Track t)
    {
        if (!_o.CharacterVoting || t.SlotVotes == null || t.SlotLen == 0)
            return t.Best;

        var raw = new char[t.SlotLen];
        float sumProb = 0; int nonPad = 0;
        for (int i = 0; i < t.SlotLen; i++)
        {
            char bestCh = '_'; float bestP = -1;
            if (t.SlotVotes.TryGetValue(i, out var m))
                foreach (var kv in m)
                    if (kv.Value > bestP) { bestP = kv.Value; bestCh = kv.Key; }
            raw[i] = bestCh;
            if (char.IsLetterOrDigit(bestCh)) { sumProb += bestP / Math.Max(1, t.Count); nonPad++; }
        }

        string consensusRaw = new string(raw);
        var tr = TurkishPlateNormalizer.Normalize(consensusRaw);
        if (!tr.IsValid)
            return t.Best; // uzlaşı geçersizse güvenli tarafa: en iyi okuma

        float conf = nonPad > 0 ? Math.Min(1f, sumProb / nonPad) : t.Best.OcrConfidence;
        return t.Best with
        {
            Text = tr.Normalized!,
            RawText = new string(consensusRaw.Where(char.IsLetterOrDigit).ToArray()),
            OcrConfidence = conf,
            Confidence = t.Best.DetectorConfidence * conf,
            IsValidTr = true,
        };
    }

    private static string IdentityKey(string normalizedText)
    {
        var parts = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[0] + "|" + parts[1] : normalizedText;
    }
}
