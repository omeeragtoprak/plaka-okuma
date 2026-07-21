using System.Collections.Concurrent;

namespace Anpr.Sdk.Events;

/// <summary>
/// Müdavim plaka kaydı: yüksek güvenle (≥0.98) defalarca görülmüş plakalar. Otoparkta düzenli
/// araçlar baskındır; bir müdavimin yakın-varyantı (lev≤2) kısa süre içinde ikinci olay üretiyorsa
/// bu neredeyse kesin aynı geçişin yanlış-okumasıdır (ölçülen vaka: 25FZ069 müdavim, 4 sn sonra
/// 25FZ289@0.89 emit — iki bastırma kuralı da kaçırdı). Kayıt events.jsonl'dan yüklenir, canlı güncellenir.
/// </summary>
public sealed class FrequentPlateRegistry
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    /// <summary>Bu kadar yüksek-güvenli görülme müdavim sayılır.</summary>
    public int MinSightings { get; init; } = 5;

    /// <summary>Kayda katılmak için gereken OCR güveni.</summary>
    public float MinConfidence { get; init; } = 0.98f;

    public int Count => _counts.Count;

    /// <summary>Manuel whitelist: sitenin BİLİNEN plakaları (ör. resmi/protokol araçları). Bunlara
    /// snap her zaman güvenlidir (küçük, küratörlü küme). Config'den doldurulur.</summary>
    public IReadOnlyCollection<string> KnownPlates { get; init; } = System.Array.Empty<string>();

    /// <summary>Config'den gelen whitelist'i güvenle normalize eder: null/boş öğeler atlanır (bir
    /// null girdi SDK init'ini çökertmemeli), boşluk/harf-dışı temizlenir, BÜYÜK harfe çevrilir.</summary>
    public static HashSet<string> NormalizeKnownPlates(IEnumerable<string?>? plates)
    {
        var set = new HashSet<string>();
        if (plates == null) return set;
        foreach (var p in plates)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var c = new string(p.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (c.Length >= 5) set.Add(c);
        }
        return set;
    }

    public bool IsFrequent(string compact)
        => _counts.TryGetValue(compact, out int n) && n >= MinSightings;

    /// <summary>
    /// Yanlış-okuma düzeltme: <paramref name="compact"/> okuması, BİLİNEN bir plakaya (manuel whitelist
    /// ve isteğe bağlı müdavimler) edit-mesafesi ≤ <paramref name="maxLev"/> yakınsa o bilinen plakaya
    /// "snap"lenir; değilse aynen döner. Okumanın kendisi zaten biliniyorsa dokunulmaz. Aynı mesafede
    /// birden çok aday varsa: whitelist > en yüksek görülme sayılı müdavim. Siyah resmi plaka gibi
    /// FREKANSLI araçların sistematik yanlış-okumalarını düzeltir (offline ölçüm: lev≤1 siyah +27pp,
    /// beyazda 0 yanlış-snap). maxLev≤0 = kapalı.
    /// </summary>
    public string SnapTarget(string compact, int maxLev, bool includeFrequent)
    {
        if (maxLev <= 0 || compact.Length < 5) return compact;
        if (KnownPlates.Contains(compact) || IsFrequent(compact)) return compact;

        string best = compact; int bestLev = maxLev + 1; long bestScore = -1;
        void Consider(string cand, long score)
        {
            if (cand == compact) return;
            int d = BoundedLev(compact, cand, maxLev);
            if (d > maxLev) return;
            if (d < bestLev || (d == bestLev && score > bestScore)) { bestLev = d; best = cand; bestScore = score; }
        }
        foreach (var w in KnownPlates) Consider(w, long.MaxValue);          // whitelist önceliği
        if (includeFrequent)
            foreach (var kv in _counts)
                if (kv.Value >= MinSightings) Consider(kv.Key, kv.Value);
        return best;
    }

    // Erken-çıkışlı Levenshtein: mesafe maxLev'i aşarsa maxLev+1 döner (hız).
    private static int BoundedLev(string a, string b, int max)
    {
        if (System.Math.Abs(a.Length - b.Length) > max) return max + 1;
        var dp = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) dp[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = dp[0]; dp[0] = i; int rowMin = dp[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cur = dp[j];
                dp[j] = System.Math.Min(System.Math.Min(dp[j] + 1, dp[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = cur; if (dp[j] < rowMin) rowMin = dp[j];
            }
            if (rowMin > max) return max + 1;
        }
        return dp[b.Length];
    }

    public void Record(string compact, float ocrConfidence)
    {
        if (ocrConfidence >= MinConfidence && compact.Length >= 7)
            _counts.AddOrUpdate(compact, 1, (_, n) => n + 1);
    }

    /// <summary>events.jsonl geçmişinden tohumla (PascalCase/camelCase toleranslı, bozuk satırlar atlanır).</summary>
    public int LoadFromJsonl(string path)
    {
        if (!File.Exists(path)) return 0;
        int loaded = 0;
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var root = doc.RootElement;
                string? compact = Get(root, "plateCompact")?.GetString();
                float conf = (float)(Get(root, "ocrConfidence")?.GetDouble() ?? 0);
                string scope = Get(root, "scope")?.GetString() ?? "roi";
                if (compact != null && scope == "roi") { Record(compact, conf); loaded++; }
            }
            catch { /* bozuk satır */ }
        }
        return loaded;

        static System.Text.Json.JsonElement? Get(System.Text.Json.JsonElement e, string name)
        {
            if (e.TryGetProperty(name, out var v)) return v;
            string pascal = char.ToUpperInvariant(name[0]) + name[1..];
            return e.TryGetProperty(pascal, out var v2) ? v2 : null;
        }
    }
}
