namespace ECommerce.EventBus;

/// <summary>
/// The only thing a service knows about messaging.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Publish-Subscribe behind an abstraction.
/// See <c>docs/adr/0016-rabbitmq-behind-ieventbus.md</c>.
/// </para>
/// <para>
/// <b>Why abstract the broker at all?</b> "So we could swap it" is usually a weak argument — most such
/// abstractions are never exercised and leak the underlying technology anyway. Three things make it worth it
/// here:
/// </para>
/// <list type="number">
///   <item><description><b>The swap is realistic.</b> Deploying this to Azure would mean Azure Service Bus. The
///   abstraction targets a substitution that would actually happen, not a hypothetical one.</description></item>
///   <item><description><b>It is genuinely thin.</b> Publish and subscribe over durable topic-routed pub/sub —
///   the intersection of what every broker offers. It does not try to abstract exchange types, prefetch, or
///   consumer groups, which is where such abstractions normally leak and become worse than no abstraction.</description></item>
///   <item><description><b>It keeps the dependency direction honest.</b> No service references
///   <c>RabbitMQ.Client</c>; only composition roots reference the implementation package. An architecture test
///   asserts this (<c>docs/adr/0008-monorepo.md</c>).</description></item>
/// </list>
/// <para>
/// <b>What this interface deliberately does NOT hide: at-least-once delivery and the absence of ordering
/// guarantees.</b> Those are properties of distributed messaging itself, not quirks of RabbitMQ. Hiding them
/// would produce consumers that quietly assume exactly-once and break against any broker. Consumers are
/// idempotent because the semantics demand it, and that requirement survives the swap.
/// </para>
/// </remarks>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all subscribers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Application code should almost never call this directly.</b> A publish that succeeds while its
    /// business transaction rolls back — or a transaction that commits while the publish fails — is the
    /// dual-write problem, and it produces silent, permanent inconsistency
    /// (<c>docs/adr/0010-transactional-outbox.md</c>).
    /// </para>
    /// <para>
    /// The correct path is: write the event to the outbox table inside the business transaction, and let the
    /// outbox dispatcher be the only caller of this method. It is public because the dispatcher lives outside
    /// this assembly, not because handlers should use it.
    /// </para>
    /// </remarks>
    Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a handler for an event type and ensures the underlying queue and binding exist.
    /// </summary>
    /// <typeparam name="TEvent">The event to subscribe to.</typeparam>
    /// <typeparam name="THandler">The handler. Resolved from DI per message, so it may take scoped
    /// dependencies such as a <c>DbContext</c>.</typeparam>
    /// <remarks>
    /// Called once per subscription at startup from the composition root. Idempotent: subscribing twice to the
    /// same pair is a no-op, so a restart or a double registration cannot create duplicate consumers.
    /// </remarks>
    Task SubscribeAsync<TEvent, THandler>(CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>;
}
