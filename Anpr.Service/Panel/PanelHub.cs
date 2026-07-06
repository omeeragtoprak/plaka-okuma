using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Anpr.Service.Panel;

/// <summary>
/// Canlı panel için SSE dağıtım merkezi: yayınlanan her nesne JSON'a çevrilip tüm abonelere itilir.
/// Aboneler sınırlı kanal kullanır (yavaş istemci en eski mesajları düşürür; servis asla bloklanmaz).
/// </summary>
public sealed class PanelHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subs = new();

    public sealed class Subscription : IDisposable
    {
        private readonly PanelHub _hub;
        private readonly Guid _id;
        public ChannelReader<string> Reader { get; }

        internal Subscription(PanelHub hub, Guid id, ChannelReader<string> reader)
        {
            _hub = hub; _id = id; Reader = reader;
        }

        public void Dispose() => _hub._subs.TryRemove(_id, out _);
    }

    public Subscription Subscribe()
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var id = Guid.NewGuid();
        _subs[id] = ch;
        return new Subscription(this, id, ch.Reader);
    }

    public int SubscriberCount => _subs.Count;

    public void Publish(object payload)
    {
        if (_subs.IsEmpty) return;
        string json = JsonSerializer.Serialize(payload);
        foreach (var ch in _subs.Values) ch.Writer.TryWrite(json);
    }
}
