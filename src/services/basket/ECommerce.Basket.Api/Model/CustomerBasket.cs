namespace ECommerce.Basket.Api.Model;

/// <summary>
/// A customer's basket.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a plain class and not a DDD aggregate.</b> Ordering gets an aggregate root, value
/// objects, domain events and a state machine. A basket gets a list and two methods, and that is the
/// correct answer rather than a corner cut.
/// </para>
/// <para>
/// A basket has almost no rules. It is a scratchpad the customer edits freely: add anything, remove
/// anything, change quantities, abandon it entirely. Nothing about it must be true for the business to
/// work — the moment something must be true, such as "the price is real" or "the stock exists", that
/// check belongs at checkout, where the money is. Modelling this with the full ceremony would be
/// applying a solution to a problem that is not present, and that is how DDD acquires its reputation for
/// weight.
/// </para>
/// <para>
/// <b>The one rule worth stating.</b> The prices held here are a <i>display convenience</i>, not an
/// agreement. They are re-checked against Catalog when the order is placed. A basket can sit for a week,
/// and a client can post whatever it likes — so a price that reaches the ledger without being re-derived
/// server-side is a discount anyone can grant themselves. See <c>PlaceOrderHandler</c>.
/// </para>
/// </remarks>
public sealed class CustomerBasket
{
    /// <summary>Keycloak <c>sub</c>. The Redis key is derived from this.</summary>
    public required string BuyerId { get; init; }

    public List<BasketItem> Items { get; init; } = [];

    /// <summary>The most distinct products one basket may hold.</summary>
    /// <remarks>
    /// A bound, because an unbounded list is serialised to Redis and back on every request. Without one,
    /// a script adding a million lines makes the basket endpoint slow for everybody, and Redis's memory
    /// is a shared resource.
    /// </remarks>
    public const int MaxItems = 50;

    public const int MaxQuantityPerItem = 100;

    /// <summary>Sum of the line totals, at the prices captured when each item was added.</summary>
    /// <remarks>Indicative only — see the note on prices above.</remarks>
    public decimal EstimatedTotal => Items.Sum(item => item.UnitPrice * item.Quantity);

    public int TotalUnits => Items.Sum(item => item.Quantity);

    public string Currency => Items.Count > 0 ? Items[0].Currency : "GBP";
}

/// <summary>
/// One line in a basket — one <b>variant</b>, not one product.
/// </summary>
/// <remarks>
/// <para>
/// <b>The line is identified by <see cref="Sku"/>, not by <see cref="ProductId"/>.</b> Since
/// [ADR-0020](../../../../docs/adr/0020-product-variants.md) a product has several sellable variants, and a
/// customer buying a Medium and a Large of the same shirt wants two lines. Keying on the product id would
/// merge them into one, and the warehouse would then receive an order for two of something without being
/// told which two.
/// </para>
/// <para>
/// <see cref="ProductId"/> stays, because a basket line still links back to a product page. It is a
/// reference, not the identity.
/// </para>
/// </remarks>
public sealed class BasketItem
{
    public required Guid ProductId { get; init; }

    /// <summary>The variant SKU. <b>This is the line's identity.</b></summary>
    public required string Sku { get; init; }

    public required string ProductName { get; init; }

    /// <summary>
    /// The chosen size and colour, as text — <c>"M"</c>, <c>"Navy"</c>.
    /// </summary>
    /// <remarks>
    /// Snapshotted rather than looked up, for the same reason the name and price are: a basket line records
    /// what was chosen, not a pointer to what that option is called today.
    /// </remarks>
    public string? Size { get; init; }

    public string? ColourName { get; init; }

    public string? ImageUrl { get; init; }

    /// <summary>The price when this was added. Indicative — re-derived at checkout.</summary>
    public required decimal UnitPrice { get; set; }

    public required string Currency { get; init; }

    public required int Quantity { get; set; }

    public decimal LineTotal => UnitPrice * Quantity;
}
