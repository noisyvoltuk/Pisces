namespace Pisces.Core.Interfaces;

/// <summary>
/// Lightweight in-process pub/sub event bus.
/// All inter-service communication goes through here — nothing calls anything directly.
/// Implementation: InProcessEventBus (Infrastructure project).
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publish an event to all subscribers of that event type.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class;

    /// <summary>
    /// Subscribe to events of a given type.
    /// Returns a disposable — dispose to unsubscribe.
    /// </summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : class;
}
