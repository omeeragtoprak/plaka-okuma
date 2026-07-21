using Anpr.Sdk.Events;
using System.Text.Json;
using Anpr.Sdk.Configuration;
using Microsoft.Extensions.Options;

namespace Anpr.Service.Events;

/// <summary>
/// Son N olayı bellekte tutan halka tampon. Hem bir IEventSink'tir (olayları biriktirir)
/// hem de /events/recent endpoint'i tarafından okunur.
/// Kalıcılık: her olay events.jsonl'e de eklenir; açılışta son N geri yüklenir — servis
/// yeniden başlasa da panel/exe tarafında geçmiş kaybolmaz (2026-07-07 vakası).
/// </summary>
public sealed class InMemoryEventStore : IEventSink
{
    private readonly int _capacity;
    private readonly LinkedList<PlateEvent> _events = new();
    private readonly object _lock = new();
    private readonly string _persistPath;
    private readonly int _retentionDays;

    private long _totalCount;

    public InMemoryEventStore(IOptions<AnprServiceOptions> options)
    {
        _capacity = Math.Max(1, options.Value.RecentEventsCapacity);
        _retentionDays = options.Value.EventRetentionDays;
        _persistPath = Path.Combine(AppContext.BaseDirectory, "events.jsonl");
        LoadPersisted();
    }

    private void LoadPersisted()
    {
        try
        {
            if (!File.Exists(_persistPath)) return;

            // KVKK saklama + dosya büyümesi: açılışta saklama süresi (EventRetentionDays) dolan
            // satırlar atılır ve dosya yalnız tutulanlarla YENİDEN yazılır (aksi halde sınırsız büyür).
            var cutoff = _retentionDays > 0 ? DateTimeOffset.Now.AddDays(-_retentionDays) : DateTimeOffset.MinValue;
            var kept = new List<string>();
            foreach (var line in File.ReadLines(_persistPath))
            {
                try
                {
                    var e = JsonSerializer.Deserialize<PlateEvent>(line);
                    if (e != null && e.Timestamp >= cutoff) kept.Add(line);
                }
                catch { /* bozuk satır atlanır */ }
            }
            // ATOMİK yeniden yazım: doğrudan WriteAllLines çökme anında dosyayı truncate edip TÜM event
            // geçmişini (KVKK/denetim kaydı) kaybettirir. Temp dosyaya yaz, sonra atomik Move ile değiştir
            // (aynı klasör = aynı volüm → atomik). Çökme olsa bile eski dosya bozulmadan kalır.
            var tmp = _persistPath + ".tmp";
            File.WriteAllLines(tmp, kept);
            File.Move(tmp, _persistPath, overwrite: true);

            foreach (var line in kept.TakeLast(_capacity))
            {
                var e = JsonSerializer.Deserialize<PlateEvent>(line);
                if (e != null) _events.AddFirst(e);
            }
            _totalCount = _events.Count;
        }
        catch { /* bozuk dosya: temiz başla */ }
    }

    public long TotalCount => Interlocked.Read(ref _totalCount);

    public Task PublishAsync(PlateEvent e, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _events.AddFirst(e);
            while (_events.Count > _capacity)
                _events.RemoveLast();
            try { File.AppendAllText(_persistPath, JsonSerializer.Serialize(e) + Environment.NewLine); }
            catch { /* disk hatası olayı düşürmesin */ }
        }
        Interlocked.Increment(ref _totalCount);
        return Task.CompletedTask;
    }

    /// <summary>En yeni olaydan başlayarak en fazla <paramref name="count"/> olay döner.</summary>
    public IReadOnlyList<PlateEvent> GetRecent(int count)
    {
        lock (_lock)
        {
            // Alt sınır 0: count<=0 boş liste döndürür (1 değil).
            return _events.Take(Math.Clamp(count, 0, _capacity)).ToList();
        }
    }
}
