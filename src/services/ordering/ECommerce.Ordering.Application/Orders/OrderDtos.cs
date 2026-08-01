namespace ECommerce.Ordering.Application.Orders;

/// <summary>
/// What the API sends and receives.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the aggregate is never returned directly.</b> Three reasons, in order of how much they hurt.
/// </para>
/// <para>
/// First, serialising <c>Order</c> exposes every property the domain happens to have — including ones
/// added later for internal bookkeeping, which then silently become part of the public API.
/// </para>
/// <para>
/// Second, it inverts the dependency: the aggregate would have to be shaped around what JSON the client
/// wants, and renaming a domain concept becomes a breaking API change.
/// </para>
/// <para>
/// Third, the aggregate's computed properties (<c>Total</c>) and read-only collections do not survive
/// round-tripping — a client could not POST back what it received, which is exactly the confusion that
/// leads people to make domain properties settable.
/// </para>
/// </remarks>
public sealed record PlaceOrderRequest
{
    /// <summary>Where to send it. Copied onto the order, never referenced — see ShippingAddress.</summary>
    public required AddressRequest ShippingAddress { get; init; }

    /// <summary>ISO 4217. Defaults are not assumed: an amount without a currency is not a price.</summary>
    public string Currency { get; init; } = "GBP";
}

public sealed record AddressRequest
{
    public required string Recipient { get; init; }

    public required string Line1 { get; init; }

    public string? Line2 { get; init; }

    public required string City { get; init; }

    public required string Postcode { get; init; }

    public string Country { get; init; } = "GB";
}

/// <summary>An order as the storefront displays it.</summary>
public sealed record OrderDto
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required string Status { get; init; }

    /// <summary>Whether the customer may still cancel. Computed by the aggregate, not the client.</summary>
    /// <remarks>
    /// Sent so the UI can hide an action that would fail. The UI hiding it is a courtesy; the aggregate
    /// refusing it is the rule. See docs/authorization-model.md — the same principle as permissions.
    /// </remarks>
    public required bool CanBeCancelled { get; init; }

    public required decimal Total { get; init; }

    public required string Currency { get; init; }

    public required int TotalUnits { get; init; }

    public required DateTimeOffset PlacedAt { get; init; }

    public DateTimeOffset? PaidAt { get; init; }

    public DateTimeOffset? ShippedAt { get; init; }

    public DateTimeOffset? DeliveredAt { get; init; }

    public DateTimeOffset? CancelledAt { get; init; }

    public string? CancellationReason { get; init; }

    public required AddressDto ShippingAddress { get; init; }

    public required IReadOnlyList<OrderItemDto> Items { get; init; }
}

public sealed record AddressDto
{
    public required string Recipient { get; init; }

    public required string Line1 { get; init; }

    public string? Line2 { get; init; }

    public required string City { get; init; }

    public required string Postcode { get; init; }

    public required string Country { get; init; }
}

public sealed record OrderItemDto
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string ProductName { get; init; }

    /// <summary>The size and colour bought. Null on a product that has neither axis.</summary>
    public string? Size { get; init; }

    public string? ColourName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}

/// <summary>A row in "my orders". Deliberately smaller than <see cref="OrderDto"/>.</summary>
/// <remarks>
/// The list page shows a reference, a date, a status and a total. Sending the full order with every line
/// for twenty orders is an order of magnitude more data than the screen uses, and the read side exists
/// precisely so the list query can be shaped for the list.
/// </remarks>
public sealed record OrderSummaryDto
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required string Status { get; init; }

    public required decimal Total { get; init; }

    public required string Currency { get; init; }

    public required int TotalUnits { get; init; }

    public required DateTimeOffset PlacedAt { get; init; }

    public required bool CanBeCancelled { get; init; }
}
