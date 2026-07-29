using ECommerce.Common.SeedWork;

namespace ECommerce.Ordering.Domain.Orders;

/// <summary>
/// Things that have happened to an order, expressed in the language of the business.
/// </summary>
/// <remarks>
/// <para>
/// <b>Domain events are not integration events.</b> The distinction matters and is easy to blur:
/// </para>
/// <list type="table">
///   <listheader><term>Domain event</term><description>Integration event</description></listheader>
///   <item>
///     <term>Stays inside this service</term>
///     <description>Crosses the network to other services</description>
///   </item>
///   <item>
///     <term>Raised by the aggregate, in memory</term>
///     <description>Published by infrastructure, through the broker</description>
///   </item>
///   <item>
///     <term>Handled in the same transaction</term>
///     <description>Handled eventually, in someone else's transaction</description>
///   </item>
///   <item>
///     <term>Free to reference domain types</term>
///     <description>A published contract — primitives only, versioned carefully</description>
///   </item>
/// </list>
/// <para>
/// Blurring them couples your domain model to your wire format, and then a rename inside the aggregate
/// is a breaking change for three other teams. Here, the application layer translates domain events into
/// integration events and writes those to the outbox.
/// </para>
/// <para>
/// All are past tense. An event is a fact that has already happened and cannot be argued with;
/// <c>PlaceOrder</c> is a command, which can be rejected, and naming an event as a command is the first
/// step towards handlers that try to reject one.
/// </para>
/// </remarks>
public sealed record OrderSubmittedDomainEvent(
    Guid OrderId,
    string OrderNumber,
    string BuyerId,
    Money Total) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>Stock has been confirmed and the order is waiting to be charged.</summary>
public sealed record OrderStockConfirmedDomainEvent(Guid OrderId, string OrderNumber) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>Payment succeeded.</summary>
public sealed record OrderPaidDomainEvent(
    Guid OrderId,
    string OrderNumber,
    string BuyerId,
    Money Total) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>The order left the warehouse.</summary>
public sealed record OrderShippedDomainEvent(
    Guid OrderId,
    string OrderNumber,
    string BuyerId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>The order reached the customer.</summary>
public sealed record OrderDeliveredDomainEvent(Guid OrderId, string OrderNumber) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The order was cancelled.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reason is part of the event, not a footnote.</b> "Cancelled because payment was declined"
/// and "cancelled because the customer changed their mind" lead to completely different follow-up:
/// the first releases stock and may retry, the second releases stock and stops. A bare
/// <c>OrderCancelled</c> forces every consumer to go and ask why, which defeats the point of publishing
/// a fact in the first place.
/// </para>
/// <para>
/// <c>StockWasReserved</c> tells the saga whether there is anything to compensate. Releasing stock that
/// was never reserved would corrupt inventory counts in the opposite direction.
/// </para>
/// </remarks>
public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    string OrderNumber,
    string BuyerId,
    OrderCancellationReason Reason,
    bool StockWasReserved) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>Why an order was cancelled.</summary>
/// <remarks>Persisted, so the values are explicit and never reordered.</remarks>
public enum OrderCancellationReason
{
    /// <summary>The customer asked to cancel.</summary>
    RequestedByCustomer = 1,

    /// <summary>Staff cancelled it on the customer's behalf.</summary>
    CancelledByStaff = 2,

    /// <summary>The payment was declined. The saga compensates by releasing stock.</summary>
    PaymentDeclined = 3,

    /// <summary>Stock could not be reserved. Nothing to compensate.</summary>
    OutOfStock = 4,
}
