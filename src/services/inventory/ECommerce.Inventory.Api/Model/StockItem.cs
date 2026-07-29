using ECommerce.Common.Exceptions;

namespace ECommerce.Inventory.Api.Model;

/// <summary>
/// The stock position for one SKU.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three numbers, and the distinction between them is the whole model.</b>
/// </para>
/// <list type="table">
///   <item>
///     <term><see cref="OnHand"/></term>
///     <description>Physically on the shelf. Only a goods-in or a dispatch changes this.</description>
///   </item>
///   <item>
///     <term><see cref="Reserved"/></term>
///     <description>Spoken for by an order that has not shipped. Still on the shelf.</description>
///   </item>
///   <item>
///     <term><see cref="Available"/></term>
///     <description><c>OnHand − Reserved</c>. What a new order may take.</description>
///   </item>
/// </list>
/// <para>
/// Collapsing reserved into on-hand — decrementing the count when an order is placed — is the obvious
/// simplification and it is wrong in a way that costs money. The warehouse would report fewer items than
/// are physically present, a stock take would disagree with the system, and cancelling an order could not
/// be distinguished from a sale. Keeping them separate means the physical count and the commercial count
/// are both true at once.
/// </para>
/// <para>
/// <b>Available is derived, never stored</b>, for the same reason an order total is: a stored copy is a
/// second source of truth that drifts the first time somebody updates one number and not the other.
/// </para>
/// </remarks>
public sealed class StockItem
{
    private StockItem()
    {
        // EF Core.
    }

    public StockItem(string sku, string productName, int onHand)
    {
        Id = Guid.CreateVersion7();
        Sku = sku;
        ProductName = productName;
        OnHand = onHand;
        Reserved = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string ProductName { get; private set; } = string.Empty;

    public int OnHand { get; private set; }

    public int Reserved { get; private set; }

    /// <summary>What a new order may take. Derived.</summary>
    public int Available => OnHand - Reserved;

    /// <summary>Below this, the buyer should reorder. Not enforced — a signal, not a rule.</summary>
    public int ReorderLevel { get; private set; } = 5;

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Reserves stock for an order.
    /// </summary>
    /// <remarks>
    /// Refuses rather than going negative. A reservation system that allows a negative available count
    /// has stopped being a reservation system — it accepts every order and discovers the problem in the
    /// warehouse, which is the failure this service exists to prevent.
    /// </remarks>
    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Reservation quantity must be positive.");
        }

        if (quantity > Available)
        {
            throw new InsufficientStockException(Sku, quantity, Available);
        }

        Reserved += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns reserved stock. The compensating action.
    /// </summary>
    /// <remarks>
    /// <b>Clamped at zero on purpose.</b> A compensating action will be retried, and it may arrive for a
    /// reservation that was already released. Subtracting again would push <see cref="Reserved"/>
    /// negative and inflate <see cref="Available"/> above what physically exists — a corruption in the
    /// opposite direction from the failure being fixed, and one nobody notices until a stock take
    /// disagrees.
    /// </remarks>
    public void Release(int quantity)
    {
        Reserved = Math.Max(0, Reserved - quantity);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Ships reserved stock: it leaves the building.
    /// </summary>
    /// <remarks>
    /// The moment <see cref="OnHand"/> finally drops. Both numbers move together, because the item is no
    /// longer on the shelf <i>and</i> no longer spoken for.
    /// </remarks>
    public void Ship(int quantity)
    {
        int shipped = Math.Min(quantity, Reserved);

        Reserved -= shipped;
        OnHand -= shipped;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>A manual correction — goods in, damage, or a stock take.</summary>
    public void Adjust(int delta)
    {
        if (OnHand + delta < 0)
        {
            throw new DomainException($"Adjusting {Sku} by {delta} would make stock negative.");
        }

        OnHand += delta;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>There is not enough stock to reserve.</summary>
/// <remarks>
/// Carries the SKU and both numbers, because "out of stock" without saying which item, how many were
/// wanted and how many exist is a message that forces whoever reads it to go and look it up.
/// </remarks>
public sealed class InsufficientStockException(string sku, int requested, int available)
    : DomainException($"Only {available} of {sku} available; {requested} requested.")
{
    public string Sku { get; } = sku;

    public int Requested { get; } = requested;

    public int Available { get; } = available;
}

/// <summary>What was reserved for one order, so it can be released or shipped as a unit.</summary>
public sealed class StockReservation
{
    private StockReservation()
    {
        // EF Core.
    }

    public StockReservation(Guid orderId, string orderNumber, IEnumerable<(string Sku, int Quantity)> lines)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        OrderNumber = orderNumber;
        ReservedAt = DateTimeOffset.UtcNow;
        Lines = lines.Select(line => new ReservationLine(Id, line.Sku, line.Quantity)).ToList();
    }

    public Guid Id { get; private set; }

    /// <summary>Uniquely indexed: one reservation per order, so a duplicate command cannot double-reserve.</summary>
    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public bool IsReleased { get; private set; }

    public DateTimeOffset ReservedAt { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public List<ReservationLine> Lines { get; private set; } = [];

    public void MarkReleased()
    {
        IsReleased = true;
        ReleasedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class ReservationLine
{
    private ReservationLine()
    {
        // EF Core.
    }

    public ReservationLine(Guid reservationId, string sku, int quantity)
    {
        Id = Guid.CreateVersion7();
        StockReservationId = reservationId;
        Sku = sku;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }

    public Guid StockReservationId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public int Quantity { get; private set; }
}
