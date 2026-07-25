namespace ECommerce.Common.SeedWork;

/// <summary>
/// A <b>domain event</b>: something that happened inside a single service, expressed in the language of the
/// domain, and handled <i>in the same transaction</i> as the change that raised it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Domain Events (DDD tactical).
/// See <c>docs/adr/0012-cqrs-with-mediatr.md</c> and <c>docs/concept-map.md</c>.
/// </para>
/// <para>
/// Do not confuse this with an <b>integration event</b> (<c>ECommerce.EventBus.IntegrationEvent</c>):
/// </para>
/// <list type="table">
///   <item><term>Scope</term><description>Domain: inside one service, in-process. Integration: across services, over the bus.</description></item>
///   <item><term>Timing</term><description>Domain: same transaction. Integration: after commit, via the outbox.</description></item>
///   <item><term>Coupling</term><description>Domain: may reference domain types. Integration: primitives only — it is a published contract.</description></item>
///   <item><term>On handler failure</term><description>Domain: the whole transaction rolls back. Integration: the message is retried, then dead-lettered.</description></item>
/// </list>
/// <para>
/// <b>Why this interface is empty and references nothing.</b> Microsoft's eShopOnContainers reference has its
/// <c>Entity</c> base type depend on MediatR's <c>INotification</c>. That is convenient — domain events can be
/// published straight through the mediator — but it drags a third-party package into the domain layer, and the
/// domain layer is the one place in this codebase that is supposed to depend on nothing at all
/// (<c>docs/architecture.md §4</c>). We keep the domain pure and let the <i>application</i> layer adapt these
/// to MediatR notifications when it dispatches them. The cost is one small adapter; the benefit is a domain
/// project whose dependency list is genuinely empty and can be unit-tested with no framework at all.
/// </para>
/// </remarks>
public interface IDomainEvent
{
    /// <summary>
    /// When the fact this event describes actually occurred.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTimeOffset"/> rather than <see cref="DateTime"/>, deliberately: it carries the offset, so
    /// it cannot silently mean "local time on whichever machine created it". Npgsql is strict about this and
    /// will reject a non-UTC <see cref="DateTime"/> outright — which is a feature, not an obstacle.
    /// </remarks>
    DateTimeOffset OccurredAt { get; }
}
