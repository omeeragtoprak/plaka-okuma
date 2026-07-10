using System.Collections.Concurrent;

namespace Anpr.Service.Events;

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

    public bool IsFrequent(string compact)
        => _counts.TryGetValue(compact, out int n) && n >= MinSightings;

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
