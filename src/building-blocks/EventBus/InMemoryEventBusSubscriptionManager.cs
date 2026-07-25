using System.Collections.Concurrent;

namespace ECommerce.EventBus;

/// <summary>
/// The default <see cref="IEventBusSubscriptionManager"/>: a process-local registry of event name → handlers.
/// </summary>
/// <remarks>
/// <para>
/// "In memory" is not a limitation here. Subscriptions describe <i>what this process can handle</i>, which is
/// fixed at compile time and registered at startup. It is not shared state and there is nothing to persist —
/// the durable part (queues and bindings) lives in the broker.
/// </para>
/// <para>
/// Registration happens once, single-threaded, during startup; lookups then run concurrently on every consumer
/// thread for the process lifetime. <see cref="ConcurrentDictionary{TKey,TValue}"/> plus immutable value lists
/// gives lock-free reads on that hot path.
/// </para>
/// </remarks>
public sealed class InMemoryEventBusSubscriptionManager : IEventBusSubscriptionManager
{
    private readonly ConcurrentDictionary<string, List<SubscriptionInfo>> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Type> _eventTypes = new(StringComparer.Ordinal);
    private readonly Lock _registrationLock = new();

    public bool AddSubscription<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        string eventName = typeof(TEvent).Name;

        // Registration is startup-only and rare, so a lock here costs nothing and keeps the
        // check-then-add sequence atomic. Reads never take it.
        lock (_registrationLock)
        {
            List<SubscriptionInfo> subscriptions = _handlers.GetOrAdd(eventName, _ => []);

            // Idempotent: subscribing the same handler twice must not create a second consumer, or every
            // message would be processed twice by design.
            if (subscriptions.Exists(s => s.HandlerType == typeof(THandler)))
            {
                return false;
            }

            subscriptions.Add(new SubscriptionInfo(eventName, typeof(TEvent), typeof(THandler)));
            _eventTypes[eventName] = typeof(TEvent);
            return true;
        }
    }

    public bool HasSubscriptionsFor(string eventName) => _handlers.ContainsKey(eventName);

    public IReadOnlyCollection<SubscriptionInfo> GetHandlersFor(string eventName) =>
        _handlers.TryGetValue(eventName, out List<SubscriptionInfo>? subscriptions)
            ? subscriptions.ToArray()
            : [];

    public Type? GetEventTypeByName(string eventName) =>
        _eventTypes.TryGetValue(eventName, out Type? type) ? type : null;

    public IReadOnlyCollection<string> GetSubscribedEventNames() => _handlers.Keys.ToArray();
}
