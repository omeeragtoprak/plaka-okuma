using Anpr.Service.Configuration;
using Microsoft.Extensions.Options;

namespace Anpr.Service.Events;

/// <summary>
/// Son N olayı bellekte tutan halka tampon. Hem bir IEventSink'tir (olayları biriktirir)
/// hem de /events/recent endpoint'i tarafından okunur.
/// </summary>
public sealed class InMemoryEventStore : IEventSink
{
    private readonly int _capacity;
    private readonly LinkedList<PlateEvent> _events = new();
    private readonly object _lock = new();

    private long _totalCount;

    public InMemoryEventStore(IOptions<AnprServiceOptions> options)
    {
        _capacity = Math.Max(1, options.Value.RecentEventsCapacity);
    }

    public long TotalCount => Interlocked.Read(ref _totalCount);

    public Task PublishAsync(PlateEvent e, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _events.AddFirst(e);
            while (_events.Count > _capacity)
                _events.RemoveLast();
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
