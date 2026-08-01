using ECommerce.Common.Exceptions;
using ECommerce.Common.Guards;
using ECommerce.Common.SeedWork;

namespace ECommerce.Ordering.Domain.Orders;

/// <summary>
/// One line on an order.
/// </summary>
/// <remarks>
/// <para>
/// <b>An entity, not a value object.</b> Two lines for the same product at the same price are still two
/// distinct lines — you can change the quantity of one without touching the other, and a warehouse
/// picking list refers to a specific line. Identity survives every field changing, which is the test.
/// </para>
/// <para>
/// <b>Inside the Order aggregate, never outside it.</b> There is no repository for order items and no way
/// to load one on its own. Every change goes through <see cref="Order"/>, because "the total equals the
/// sum of the lines" is a rule about the <i>order</i>, and a rule can only be enforced by whoever owns
/// every piece of state it mentions. That boundary is what an aggregate is.
/// </para>
/// <para>
/// Note the constructor and mutators are <c>internal</c>: the compiler, not code review, is what stops an
/// endpoint constructing a line and adding it to a collection behind the aggregate's back.
/// </para>
/// <para>
/// <b>ProductName and UnitPrice are copied, not looked up.</b> Prices change and products get renamed.
/// An order shows what the customer actually agreed to pay, not what the item costs today — otherwise
/// every historical order silently repriced itself the moment marketing ran a sale, and the totals would
/// stop matching what was charged. See <see cref="ShippingAddress"/> for the same reasoning about
/// addresses.
/// </para>
/// </remarks>
public sealed class OrderItem : Entity<Guid>
{
    private OrderItem()
    {
        // EF Core.
    }

    internal OrderItem(
        Guid productId,
        string sku,
        string productName,
        Money unitPrice,
        int quantity,
        string? size = null,
        string? colourName = null)
    {
        Id = Guid.CreateVersion7();
        ProductId = Guard.AgainstEmpty(productId);
        Sku = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(sku), 64);
        ProductName = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(productName), 200);
        Size = size is null ? null : Guard.AgainstTooLong(size, 20);
        ColourName = colourName is null ? null : Guard.AgainstTooLong(colourName, 40);
        UnitPrice = Guard.AgainstNull(unitPrice);
        Quantity = Guard.AgainstNonPositive(quantity);

        Guard.Against(quantity > MaxQuantityPerLine,
            $"A single line may not exceed {MaxQuantityPerLine} units.");
    }

    /// <summary>
    /// The most of one product a customer may order in a single line.
    /// </summary>
    /// <remarks>
    /// A real limit, not defensive noise. Without one, a fat finger or a script orders 2,000,000 units,
    /// the total overflows the money column, and the reservation attempt takes the inventory service
    /// down with it. Bounding it in the domain means every entry point is bounded.
    /// </remarks>
    public const int MaxQuantityPerLine = 100;

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>The SKU as it was at purchase. What the warehouse actually picks by.</summary>
    public string Sku { get; private set; } = string.Empty;

    public string ProductName { get; private set; } = string.Empty;

    /// <summary>
    /// The size and colour bought, as text.
    /// </summary>
    /// <remarks>
    /// A snapshot, exactly like <see cref="ProductName"/> and <see cref="UnitPrice"/>. The SKU already
    /// identifies the variant precisely — the warehouse picks by it — but a customer reading their order
    /// history needs "Medium, Navy" rather than <c>NW-TS-001-M-NAV</c>, and renaming a colour next year
    /// must not rewrite what last year's dispatch note said.
    /// </remarks>
    public string? Size { get; private set; }

    public string? ColourName { get; private set; }

    public Money UnitPrice { get; private set; } = null!;

    public int Quantity { get; private set; }

    /// <summary>Quantity times unit price. Computed, never stored — see below.</summary>
    /// <remarks>
    /// Storing a line total would allow it to disagree with its own inputs, and reconciling that
    /// disagreement later is guesswork. Deriving it makes the inconsistency unrepresentable.
    /// </remarks>
    public Money LineTotal => UnitPrice.Multiply(Quantity);

    internal void ChangeQuantity(int quantity)
    {
        Guard.AgainstNonPositive(quantity);

        if (quantity > MaxQuantityPerLine)
        {
            throw new DomainException($"A single line may not exceed {MaxQuantityPerLine} units.");
        }

        Quantity = quantity;
    }

    internal void IncreaseQuantityBy(int amount)
    {
        Guard.AgainstNonPositive(amount);
        ChangeQuantity(Quantity + amount);
    }
}
