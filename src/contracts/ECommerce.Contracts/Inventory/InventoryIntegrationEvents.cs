using ECommerce.EventBus;

namespace ECommerce.Contracts.Inventory;

/// <summary>
/// Events published by the Inventory service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reserving stock is asynchronous, and payment is too.</b> That is the decision this whole phase
/// turns on. The customer is not waiting on either: they clicked "Place order", got an order number, and
/// are looking at a confirmation. Whether the warehouse can actually fulfil it, and whether the card
/// clears, are answered afterwards.
/// </para>
/// <para>
/// Compare the basket, which Ordering fetches <i>synchronously</i> during checkout — there is nothing to
/// order without it, so the customer genuinely is waiting. The rule of thumb: <b>synchronous when the
/// caller cannot proceed without the answer; asynchronous when the answer changes what happens next but
/// not whether the request succeeded.</b>
/// </para>
/// </remarks>
public sealed record StockReservedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    /// <summary>What was reserved, so a compensating release knows exactly what to put back.</summary>
    public required IReadOnlyList<StockLineContract> Lines { get; init; }
}

/// <summary>
/// Stock could not be reserved.
/// </summary>
/// <remarks>
/// <see cref="UnavailableSkus"/> is named rather than summarised, because "something in your order is out
/// of stock" is useless to a customer with twelve items. The saga passes it through to the cancellation
/// so the order page can say which one.
/// </remarks>
public sealed record StockRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required IReadOnlyList<string> UnavailableSkus { get; init; }
}

/// <summary>Stock reserved for an order has been put back.</summary>
/// <remarks>
/// The result of a <b>compensating action</b>. Published so the timeline can show that the reservation
/// was undone, rather than leaving a gap where the customer sees stock reserved and then nothing.
/// </remarks>
public sealed record StockReleasedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }
}

public sealed record StockLineContract
{
    public required string Sku { get; init; }

    public required int Quantity { get; init; }
}
