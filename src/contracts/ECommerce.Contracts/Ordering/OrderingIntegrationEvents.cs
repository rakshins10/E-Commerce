using ECommerce.EventBus;

namespace ECommerce.Contracts.Ordering;

/// <summary>
/// Events published by the Ordering service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these are separate from the domain events in <c>Order.cs</c>.</b> A domain event is an
/// internal note to self, free to reference <c>Money</c> or <c>OrderStatus</c> and free to be renamed on
/// a whim. An integration event is a published contract: other teams deserialise it, and a rename is an
/// outage. Collapsing the two means every refactor of the aggregate is a breaking change for three other
/// services, which is precisely the coupling microservices are meant to avoid.
/// </para>
/// <para>
/// So the aggregate raises <c>OrderPaidDomainEvent</c> carrying a <c>Money</c>, and the application layer
/// translates it into <see cref="OrderPaidIntegrationEvent"/> carrying a <c>decimal</c> and a
/// <c>string</c>. The translation is a few lines and it is the seam that lets the inside change freely.
/// </para>
/// <para>
/// <b>Versioning rule: additive only.</b> New properties must be optional, because during a rolling
/// deploy the old and new versions run simultaneously and each will see the other's messages. Removing
/// or renaming a property breaks every consumer that has not restarted yet. When a genuinely
/// incompatible change is needed, publish <c>OrderPaidV2</c> alongside and retire the original once
/// consumers have moved.
/// </para>
/// </remarks>
public sealed record OrderSubmittedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    /// <summary>Keycloak <c>sub</c>. Never an email address — see the Order aggregate.</summary>
    public required string BuyerId { get; init; }

    public required decimal Total { get; init; }

    public required string Currency { get; init; }

    /// <summary>What to reserve. Everything Inventory needs, so it never has to call back.</summary>
    /// <remarks>
    /// <b>Events carry their data.</b> An event holding only an order id would force every consumer to
    /// make a synchronous call back to Ordering, which reintroduces the runtime coupling that
    /// asynchronous messaging exists to remove — and means Ordering being down stops Inventory working.
    /// The cost is a larger message and a snapshot that could be stale; for a fact about the past, a
    /// snapshot is exactly right.
    /// </remarks>
    public required IReadOnlyList<OrderLineContract> Lines { get; init; }
}

/// <summary>One line of an order, as it appears on the wire.</summary>
public sealed record OrderLineContract
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }
}

/// <summary>Stock has been reserved; the order is ready to be charged.</summary>
public sealed record OrderStockConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }
}

/// <summary>The order has been paid for.</summary>
public sealed record OrderPaidIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string BuyerId { get; init; }

    public required decimal Total { get; init; }

    public required string Currency { get; init; }
}

/// <summary>The order has been dispatched.</summary>
public sealed record OrderShippedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string BuyerId { get; init; }
}

/// <summary>The order has been delivered.</summary>
public sealed record OrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }
}

/// <summary>
/// The order has been cancelled.
/// </summary>
/// <remarks>
/// <see cref="StockWasReserved"/> tells a compensating handler whether there is anything to release.
/// Releasing stock that was never reserved inflates the available count — a corruption in the opposite
/// direction from the one being fixed, and harder to notice.
/// </remarks>
public sealed record OrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string BuyerId { get; init; }

    /// <summary>Sent as a string, not the domain enum, so a new reason does not break old consumers.</summary>
    public required string Reason { get; init; }

    public required bool StockWasReserved { get; init; }
}
