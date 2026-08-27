using System.Collections.Concurrent;
using Pisces.Core.Interfaces;

namespace Pisces.Infrastructure.EventBus;

/// <summary>
/// Simple thread-safe in-process event bus.
/// All handlers are invoked concurrently — handlers must be thread-safe.
/// Exceptions in handlers are caught and logged, not propagated.
/// </summary>
public sealed class InProcessEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly ILogger<InProcessEventBus> _logger;

    public InProcessEventBus(ILogger<InProcessEventBus> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return;

        List<Delegate> snapshot;
        lock (handlers)
            snapshot = [..handlers];

        var tasks = snapshot
            .OfType<Func<TEvent, CancellationToken, Task>>()
            .Select(async h =>
            {
                try { await h(@event, ct); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Event handler failed for {EventType}", typeof(TEvent).Name);
                }
            });

        await Task.WhenAll(tasks);
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : class
    {
        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => []);
        lock (handlers)
            handlers.Add(handler);

        return new Subscription(() =>
        {
            lock (handlers)
                handlers.Remove(handler);
        });
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
