namespace ECommerce.EventBus;

/// <summary>
/// Non-generic marker so the subscription manager can hold handlers without knowing their event type.
/// Do not implement directly — implement <see cref="IIntegrationEventHandler{TEvent}"/>.
/// </summary>
public interface IIntegrationEventHandler;

/// <summary>
/// Handles one integration event type.
/// </summary>
/// <typeparam name="TEvent">The event handled.</typeparam>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Idempotent Consumer.
/// See <c>docs/adr/0010-transactional-outbox.md</c> and <c>docs/events/event-catalogue.md</c>.
/// </para>
/// <para>
/// <b>Every implementation of this interface must be idempotent. This is not optional.</b> Delivery is
/// at-least-once, so handling the same message twice is a normal occurrence, not an edge case — the broker
/// redelivers after a consumer crashes between doing the work and acknowledging, and the outbox dispatcher
/// republishes after crashing between publishing and marking the row processed.
/// </para>
/// <para>
/// The consequence of ignoring this is not an abstract correctness problem: a duplicate
/// <c>OrderPaymentSucceeded</c> handled twice charges a customer twice. <b>An outbox without idempotent
/// consumers has not fixed the reliability problem — it has moved it from the publisher to the consumer, where
/// it is more expensive.</b>
/// </para>
/// <para>
/// The mechanism used here: record <see cref="IntegrationEvent.Id"/> in a <c>processed_messages</c> table in the
/// consumer's own database, <i>inside the same transaction</i> as the business work. Same transaction is the
/// crucial part — recording it separately reintroduces the dual-write problem the outbox exists to solve. Where
/// the operation is naturally idempotent (setting a status to a fixed value), the table is belt and braces;
/// where it is not (incrementing a balance, sending an email), it is the only thing preventing duplication.
/// </para>
/// <para>
/// <b>On failure:</b> throw. The infrastructure will negatively acknowledge, retry with backoff, and after a
/// capped number of attempts route the message to a dead-letter queue. Do not swallow exceptions to "keep the
/// consumer running" — that discards the message silently, which is the failure mode this entire design exists
/// to prevent.
/// </para>
/// </remarks>
public interface IIntegrationEventHandler<in TEvent> : IIntegrationEventHandler
    where TEvent : IntegrationEvent
{
    /// <summary>
    /// Handles the event. Must be idempotent — see the remarks on this interface.
    /// </summary>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
