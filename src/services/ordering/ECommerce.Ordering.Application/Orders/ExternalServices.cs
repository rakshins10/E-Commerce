namespace ECommerce.Ordering.Application.Orders;

/// <summary>
/// What Ordering needs from the Basket service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a synchronous call and not an event.</b> Placing an order is a <i>command</i> the
/// customer is waiting on: they clicked "Place order" and are looking at a spinner. It must either
/// succeed or fail now, with an answer. Events are for facts other services react to afterwards, not
/// for questions with an answer the caller needs.
/// </para>
/// <para>
/// The cost is real and worth naming: Ordering cannot place an order while Basket is down. That is
/// acceptable because there is nothing to order without a basket, so the dependency is inherent rather
/// than accidental. Compare Inventory, which reacts to <c>OrderSubmitted</c> asynchronously precisely
/// because reserving stock is <i>not</i> something the customer is waiting on.
/// </para>
/// <para>
/// The interface lives in the application layer and the HTTP implementation lives in infrastructure, so
/// the handler is testable without a network and knows nothing about how the call is made.
/// </para>
/// </remarks>
public interface IBasketService
{
    Task<BasketSnapshot?> GetBasketAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Empties the basket once its order exists.</summary>
    Task ClearBasketAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A basket as Ordering sees it.</summary>
/// <remarks>
/// Ordering's own shape, not Basket's class. If it shared Basket's model, adding a field for the basket
/// UI would recompile and redeploy Ordering — which is exactly the coupling that separate services are
/// meant to remove.
/// </remarks>
public sealed record BasketSnapshot(string BuyerId, IReadOnlyList<BasketLineSnapshot> Items);

public sealed record BasketLineSnapshot(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    string Currency,
    int Quantity,
    string? Size = null,
    string? ColourName = null);

/// <summary>
/// What Ordering needs from the Catalog service: the <b>authoritative</b> price.
/// </summary>
/// <remarks>
/// <para>
/// <b>This interface exists for one reason, and it is a security one.</b> The price in a basket came
/// from a client, over the network, and may have been sitting there for a month. Trusting it at
/// checkout means:
/// </para>
/// <list type="bullet">
///   <item><description>anyone who can send an HTTP request can set their own prices;</description></item>
///   <item><description>a legitimate customer can be charged last month's price after a rise;</description></item>
///   <item><description>a product withdrawn from sale can still be bought.</description></item>
/// </list>
/// <para>
/// So every line is re-priced from Catalog at the moment the order is placed, and the basket's price is
/// used for nothing except showing the customer what changed. The rule generalises well beyond prices:
/// <b>anything that ends up in a ledger is derived server-side, never accepted from a client.</b>
/// </para>
/// </remarks>
public interface ICatalogService
{
    /// <summary>Current prices for the given products, keyed by product id.</summary>
    /// <remarks>
    /// One call for the whole basket rather than one per line. Twenty lines meaning twenty round trips
    /// would make checkout latency a function of basket size, and would multiply the chance of a partial
    /// failure by twenty.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, CatalogPrice>> GetPricesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);
}

/// <summary>What Catalog says a product costs right now.</summary>
public sealed record CatalogPrice(
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    string Currency,
    bool IsAvailable);
