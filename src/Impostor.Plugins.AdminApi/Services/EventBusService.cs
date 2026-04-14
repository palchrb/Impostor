using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Impostor.Plugins.AdminApi.Services;

/// <summary>
/// Simple pub/sub bus for admin events. Each subscriber gets its own channel
/// and receives events while subscribed. Used for Server-Sent Events streams.
/// </summary>
public class EventBusService
{
    private readonly ConcurrentDictionary<Guid, Channel<AdminEvent>> _subscribers = new();

    public void Publish(AdminEvent evt)
    {
        foreach (var channel in _subscribers.Values)
        {
            // TryWrite drops the event if the channel is full - we prefer dropping
            // to blocking the publisher. BoundedChannel ensures we don't grow unbounded.
            channel.Writer.TryWrite(evt);
        }
    }

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<AdminEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _subscribers[id] = channel;
        return new Subscription(id, channel.Reader, () => Unsubscribe(id));
    }

    private void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public sealed class Subscription : IAsyncDisposable
    {
        private readonly Action _onDispose;

        internal Subscription(Guid id, ChannelReader<AdminEvent> reader, Action onDispose)
        {
            Id = id;
            Reader = reader;
            _onDispose = onDispose;
        }

        public Guid Id { get; }

        public ChannelReader<AdminEvent> Reader { get; }

        public ValueTask DisposeAsync()
        {
            _onDispose();
            return ValueTask.CompletedTask;
        }
    }
}

public record AdminEvent(string Type, DateTime Timestamp, object Data);
