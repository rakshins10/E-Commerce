using System.Text.Json.Serialization;

namespace ECommerce.EventBus;

/// <summary>
/// Base class for an <b>integration event</b>: a fact that has already happened in one service and that other
/// services may care about, published asynchronously over the event bus.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Integration Events / Publish-Subscribe.
/// See <c>docs/adr/0016-rabbitmq-behind-ieventbus.md</c> and <c>docs/events/event-catalogue.md</c>.
/// </para>
/// <para>
/// <b>An integration event is a published contract, not an internal class.</b> Once another service consumes it,
/// changing it is a breaking change to someone else's deployment. Three rules follow, and they are the whole
/// discipline of this type:
/// </para>
/// <list type="number">
///   <item><description><b>Primitives and simple DTOs only.</b> Never a domain entity, never a value object.
///   Serialising a domain type onto the wire exports your internal model as a public contract and guarantees
///   that refactoring it breaks a neighbour.</description></item>
///   <item><description><b>Past tense, always.</b> <c>OrderConfirmed</c>, never <c>ConfirmOrder</c>. An event
///   reports something that <i>has happened</i> and cannot be refused. A subscriber may react; it may not veto.
///   (Commands sent to saga participants are a separate concept and are named imperatively.)</description></item>
///   <item><description><b>Additive change only.</b> Add optional fields; never remove or repurpose one.
///   Consumers must tolerate unknown fields, so an additive change needs no coordinated release — which is
///   precisely what makes independent deployability real rather than theoretical.</description></item>
/// </list>
/// <para>
/// Contrast with <c>ECommerce.Common.SeedWork.IDomainEvent</c>, which is in-process, inside the transaction, and
/// free to reference domain types. The two are routinely conflated; the distinction is set out in
/// <c>docs/adr/0012-cqrs-with-mediatr.md</c>.
/// </para>
/// </remarks>
public abstract record IntegrationEvent
{
    /// <summary>
    /// Unique id for this message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the deduplication key, and it is the reason the whole system can be correct.</b> Delivery is
    /// at-least-once (<c>docs/adr/0010-transactional-outbox.md</c>), so a consumer <i>will</i> occasionally see
    /// the same message twice. Consumers record processed ids and ignore repeats.
    /// </para>
    /// <para>
    /// Critically, this id is assigned when the event is written to the <b>outbox</b>, inside the business
    /// transaction — not when it is published. If it were generated at publish time, a redelivery would carry a
    /// fresh id and deduplication would silently do nothing.
    /// </para>
    /// </remarks>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// When the fact occurred — not when it was published or received.
    /// </summary>
    /// <remarks>
    /// The gap between the two can be significant when the broker has been unavailable and the outbox has been
    /// draining. Consumers that care about ordering or staleness must use this, never their own clock.
    /// </remarks>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Correlation id tying this event to the originating user request, propagated across every service and
    /// every asynchronous hop.
    /// </summary>
    /// <remarks>
    /// Without this, a distributed trace breaks at the first message boundary and "show me everything that
    /// happened for order X" becomes impossible. See <c>docs/operations/observability.md</c>.
    /// </remarks>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// W3C traceparent of the publishing operation, so the consumer's span links back to the producer's trace.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>
    /// The routing key / logical name used on the wire. Defaults to the concrete type name.
    /// </summary>
    /// <remarks>
    /// Not serialised: it is metadata about the message, not part of it. Kept overridable so an event can be
    /// renamed in C# without changing the wire contract every subscriber is bound to.
    /// </remarks>
    [JsonIgnore]
    public virtual string EventName => GetType().Name;
}
