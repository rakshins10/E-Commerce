namespace ECommerce.EventBus;

/// <summary>
/// One subscription: the wire name of an event, its CLR type, and the handler type that processes it.
/// </summary>
/// <param name="EventName">Routing key on the wire, e.g. <c>OrderStartedIntegrationEvent</c>.</param>
/// <param name="EventType">CLR type to deserialise the payload into.</param>
/// <param name="HandlerType">Handler type, resolved from DI per message.</param>
public sealed record SubscriptionInfo(string EventName, Type EventType, Type HandlerType);

/// <summary>
/// In-memory registry mapping an event's wire name to the CLR type and handler that deal with it.
/// </summary>
/// <remarks>
/// <para>
/// A message arriving from the broker is a routing key and a byte array. Something must decide which type to
/// deserialise into and which handler to invoke — that is this registry's entire job, and keeping it separate
/// from the transport keeps the RabbitMQ client free of reflection and type lookups.
/// </para>
/// <para>
/// Implementations must be <b>thread-safe for reads</b>. Registration happens once at startup on a single
/// thread; lookups then happen concurrently on every consumer thread for the life of the process.
/// </para>
/// </remarks>
public interface IEventBusSubscriptionManager
{
    /// <summary>Registers a handler for an event type. Idempotent per (event, handler) pair.</summary>
    /// <returns><see langword="true"/> if this call added a new subscription; <see langword="false"/> if it
    /// was already present. The transport uses this to avoid re-declaring a queue it already owns.</returns>
    bool AddSubscription<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>;

    /// <summary>Whether anything in this process handles the named event.</summary>
    bool HasSubscriptionsFor(string eventName);

    /// <summary>
    /// All handlers registered for the named event, or an empty collection if none.
    /// </summary>
    /// <remarks>
    /// A collection rather than a single handler: one service can legitimately react to the same event in
    /// several unrelated ways. Note that all of them share one delivery — if one throws, the message is
    /// negatively acknowledged and <i>all</i> of them run again on redelivery, which is another reason every
    /// handler must be idempotent.
    /// </remarks>
    IReadOnlyCollection<SubscriptionInfo> GetHandlersFor(string eventName);

    /// <summary>The CLR type registered for a wire name, or <see langword="null"/> if unknown.</summary>
    /// <remarks>
    /// An unknown name is normal, not an error: a queue may receive an event this service does not subscribe
    /// to, or a newer publisher may emit a type this build predates. The transport acknowledges and discards
    /// rather than dead-lettering, since retrying will never make the type known.
    /// </remarks>
    Type? GetEventTypeByName(string eventName);

    /// <summary>Every distinct event name with at least one handler. Used at startup to declare queues.</summary>
    IReadOnlyCollection<string> GetSubscribedEventNames();
}
