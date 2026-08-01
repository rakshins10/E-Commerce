using ECommerce.Common.Exceptions;
using ECommerce.Common.Guards;
using ECommerce.Common.SeedWork;

namespace ECommerce.Ordering.Domain.Orders;

/// <summary>
/// A customer's order. The aggregate root of this bounded context.
/// </summary>
/// <remarks>
/// <para>
/// <b>What "aggregate root" actually means here.</b> An order and its lines are one consistency
/// boundary: they are loaded together, saved together, and every rule that spans them is enforced in
/// this class. Nothing outside can reach an <see cref="OrderItem"/> — there is no repository for one,
/// the collection is exposed read-only, and the item's constructor is <c>internal</c>. That is not
/// ceremony; it is the only way "the total equals the sum of the lines" can be a guarantee rather than a
/// convention.
/// </para>
/// <para>
/// <b>Why Ordering gets full DDD and Catalog does not.</b> Catalog is a <i>supporting</i> subdomain —
/// mostly CRUD over product data, where a rich model would be cost without benefit. Ordering is the
/// <i>core</i> subdomain: it holds the rules that make this a shop rather than a database. Applying the
/// same ceremony everywhere is how DDD gets a reputation for being heavy. See
/// docs/domain/bounded-contexts.md.
/// </para>
/// <para>
/// <b>The state machine.</b> An order is in exactly one <see cref="OrderStatus"/>, and every transition
/// is a method that checks the current state first. The alternative — a bag of booleans and callers who
/// remember to check them — permits states like "cancelled and shipped", and something eventually
/// produces one.
/// </para>
/// <code>
///   Submitted ──► AwaitingPayment ──► Paid ──► Shipped ──► Delivered
///       │                │              │
///       └────────────────┴──────────────┴──────────► Cancelled
/// </code>
/// <para>
/// Cancellation is legal up to and including <see cref="OrderStatus.Paid"/>, because a paid order that
/// has not left the building can still be stopped and refunded. Once it is
/// <see cref="OrderStatus.Shipped"/> it cannot be cancelled — that is a return, which is a different
/// process with different money attached, and modelling it as cancellation loses that distinction.
/// </para>
/// </remarks>
public sealed class Order : Entity<Guid>, IAggregateRoot
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
        // EF Core.
    }

    private Order(string buyerId, string buyerName, ShippingAddress shippingAddress, string currency)
    {
        Id = Guid.CreateVersion7();
        BuyerId = Guard.AgainstNullOrWhiteSpace(buyerId);
        BuyerName = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(buyerName), 200);
        ShippingAddress = Guard.AgainstNull(shippingAddress);
        Currency = Guard.AgainstNullOrWhiteSpace(currency).ToUpperInvariant();
        Status = OrderStatus.Submitted;
        PlacedAt = DateTimeOffset.UtcNow;
        OrderNumber = GenerateOrderNumber(PlacedAt, Id);
    }

    /// <summary>The most distinct lines one order may contain.</summary>
    public const int MaxItems = 50;

    /// <summary>Keycloak <c>sub</c>. The only stable identifier for a customer.</summary>
    /// <remarks>
    /// Not the email address. People change email; <c>sub</c> is immutable for the life of the account,
    /// and an order keyed by email eventually attaches to the wrong person.
    /// </remarks>
    public string BuyerId { get; private set; } = string.Empty;

    /// <summary>The buyer's name as it was at purchase. A copy, for the same reason as the address.</summary>
    public string BuyerName { get; private set; } = string.Empty;

    /// <summary>
    /// Human-facing reference, e.g. <c>ORD-20260726-4F2A</c>.
    /// </summary>
    /// <remarks>
    /// The <see cref="Entity{TId}.Id"/> is a GUID: correct for a database, useless to a customer on the
    /// phone. This is short enough to read aloud, sortable by eye, and unique. It is <i>not</i> the
    /// primary key — deriving a key from user-facing data means a change to the display format becomes a
    /// database migration.
    /// </remarks>
    public string OrderNumber { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }

    public ShippingAddress ShippingAddress { get; private set; } = null!;

    /// <summary>The currency every line on this order is priced in.</summary>
    /// <remarks>
    /// Held on the order, not just per line, so that an empty order still knows what it is denominated
    /// in and <see cref="Total"/> has something to return.
    /// </remarks>
    public string Currency { get; private set; } = "GBP";

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public OrderCancellationReason? CancellationReason { get; private set; }

    /// <summary>Whether inventory has confirmed a reservation for this order.</summary>
    /// <remarks>
    /// The saga needs this to know whether a cancellation has anything to compensate. Releasing stock
    /// that was never reserved is not a harmless no-op — it inflates the available count.
    /// </remarks>
    public bool StockReserved { get; private set; }

    /// <summary>Set once a payment has been taken, so a refund knows what to reference.</summary>
    public string? PaymentReference { get; private set; }

    /// <summary>
    /// The order total: the sum of every line.
    /// </summary>
    /// <remarks>
    /// <b>Computed, never stored.</b> A stored total is a second source of truth for the same fact, and
    /// the two drift the first time a line is edited by a path that forgets to recalculate. Deriving it
    /// makes that bug unrepresentable. (If a report needed to filter on it at scale, the read model —
    /// not the aggregate — is where a denormalised copy belongs.)
    /// </remarks>
    public Money Total =>
        _items.Aggregate(Money.Zero(Currency), (running, item) => running.Add(item.LineTotal));

    public int TotalUnits => _items.Sum(item => item.Quantity);

    /// <summary>
    /// Creates a submitted order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A factory rather than a public constructor, because creation has a rule: an order must have at
    /// least one line. A constructor that can produce an invalid object requires everyone who calls it
    /// to remember to validate afterwards, and the whole point of an aggregate is that it cannot be
    /// invalid.
    /// </para>
    /// <para>
    /// It raises <see cref="OrderSubmittedDomainEvent"/> immediately. Creation is itself something that
    /// happened, and the event is what eventually becomes the integration event that starts the saga.
    /// </para>
    /// </remarks>
    public static Order Submit(
        string buyerId,
        string buyerName,
        ShippingAddress shippingAddress,
        string currency,
        IEnumerable<OrderLineRequest> lines)
    {
        Guard.AgainstNull(lines);

        var order = new Order(buyerId, buyerName, shippingAddress, currency);

        foreach (OrderLineRequest line in lines)
        {
            order.AddItem(
                line.ProductId, line.Sku, line.ProductName, line.UnitPrice, line.Quantity, line.Size, line.ColourName);
        }

        if (order._items.Count == 0)
        {
            throw new DomainException("An order must contain at least one item.");
        }

        order.RaiseDomainEvent(new OrderSubmittedDomainEvent(
            order.Id, order.OrderNumber, order.BuyerId, order.Total));

        return order;
    }

    /// <summary>
    /// Adds a line, merging into an existing line for the same product.
    /// </summary>
    /// <remarks>
    /// Merging rather than appending is a business rule, not a tidiness preference: two lines for the
    /// same SKU produce two picking instructions for one shelf, and a customer who cannot understand
    /// their own invoice.
    /// </remarks>
    private void AddItem(
        Guid productId,
        string sku,
        string productName,
        Money unitPrice,
        int quantity,
        string? size,
        string? colourName)
    {
        AssertCurrency(unitPrice);

        // Merged on SKU, not on product id.
        //
        // The remark above always SAID "two lines for the same SKU", and before variants the two were the
        // same thing so nothing noticed. They are not the same thing now: merging on the product would
        // collapse a Medium and a Large into one line, and the picking instruction would then say "two of
        // NW-TS-001" without saying which two.
        OrderItem? existing = _items.FirstOrDefault(item =>
            string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.IncreaseQuantityBy(quantity);
            return;
        }

        if (_items.Count >= MaxItems)
        {
            throw new DomainException($"An order may not contain more than {MaxItems} distinct items.");
        }

        _items.Add(new OrderItem(productId, sku, productName, unitPrice, quantity, size, colourName));
    }

    /// <summary>Inventory has reserved the stock. The order now waits to be charged.</summary>
    public void ConfirmStock()
    {
        AssertStatus(OrderStatus.Submitted, "confirm stock for");

        StockReserved = true;
        Status = OrderStatus.AwaitingPayment;

        RaiseDomainEvent(new OrderStockConfirmedDomainEvent(Id, OrderNumber));
    }

    /// <summary>
    /// Records a successful payment.
    /// </summary>
    /// <remarks>
    /// <b>Idempotent on purpose.</b> Payment confirmation arrives over a message broker with
    /// at-least-once delivery, so the same message will eventually be handled twice. Returning quietly
    /// when the order is already paid is what stops a duplicate delivery raising a second
    /// <see cref="OrderPaidDomainEvent"/> and sending the customer two confirmation emails.
    /// </remarks>
    public void MarkAsPaid(string paymentReference)
    {
        Guard.AgainstNullOrWhiteSpace(paymentReference);

        if (Status == OrderStatus.Paid)
        {
            return;
        }

        AssertStatus(OrderStatus.AwaitingPayment, "pay for");

        PaymentReference = paymentReference;
        PaidAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Paid;

        RaiseDomainEvent(new OrderPaidDomainEvent(Id, OrderNumber, BuyerId, Total));
    }

    public void MarkAsShipped()
    {
        AssertStatus(OrderStatus.Paid, "ship");

        ShippedAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Shipped;

        RaiseDomainEvent(new OrderShippedDomainEvent(Id, OrderNumber, BuyerId));
    }

    public void MarkAsDelivered()
    {
        AssertStatus(OrderStatus.Shipped, "deliver");

        DeliveredAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Delivered;

        RaiseDomainEvent(new OrderDeliveredDomainEvent(Id, OrderNumber));
    }

    /// <summary>
    /// Cancels the order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent for the same reason <see cref="MarkAsPaid"/> is: a saga compensating a failed step may
    /// well ask twice.
    /// </para>
    /// <para>
    /// The refusal to cancel a shipped order is stated in business terms, not as a generic invalid-state
    /// error, because the caller's next step is genuinely different — they need the returns process.
    /// </para>
    /// </remarks>
    public void Cancel(OrderCancellationReason reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Shipped or OrderStatus.Delivered)
        {
            throw new DomainException(
                $"Order {OrderNumber} has already been dispatched and cannot be cancelled. "
                + "Raise a return instead.");
        }

        bool stockWasReserved = StockReserved;

        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = reason;
        Status = OrderStatus.Cancelled;
        StockReserved = false;

        RaiseDomainEvent(new OrderCancelledDomainEvent(
            Id, OrderNumber, BuyerId, reason, stockWasReserved));
    }

    /// <summary>Whether this order can still be cancelled by the person who placed it.</summary>
    /// <remarks>
    /// Exposed so the read model and the UI can hide an action that would fail. The UI hiding it is a
    /// courtesy; <see cref="Cancel"/> refusing it is the actual rule.
    /// </remarks>
    public bool CanBeCancelled =>
        Status is OrderStatus.Submitted or OrderStatus.AwaitingPayment or OrderStatus.Paid;

    /// <summary>
    /// Builds the human-facing reference: <c>ORD-yyyyMMdd-XXXX</c>.
    /// </summary>
    /// <remarks>
    /// The suffix comes from the order's own version-7 GUID rather than a database sequence. A sequence
    /// would mean a round trip before the aggregate is valid, and would leak the shop's order volume to
    /// anyone who placed two orders and subtracted. Collisions within one day are possible in principle;
    /// a unique index on the column turns that into a retry rather than a duplicate.
    /// </remarks>
    private static string GenerateOrderNumber(DateTimeOffset placedAt, Guid id) =>
        $"ORD-{placedAt:yyyyMMdd}-{id.ToString("N")[^4..].ToUpperInvariant()}";

    private void AssertCurrency(Money money)
    {
        if (!string.Equals(money.Currency, Currency, StringComparison.Ordinal))
        {
            throw new DomainException(
                $"Order {OrderNumber} is priced in {Currency}; cannot add a line in {money.Currency}.");
        }
    }

    private void AssertStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new DomainException(
                $"Cannot {action} order {OrderNumber}: it is {Status}, not {expected}.");
        }
    }
}

/// <summary>
/// One line requested at checkout.
/// </summary>
/// <remarks>
/// A plain input to <see cref="Order.Submit"/>, deliberately not an <see cref="OrderItem"/>: the caller
/// describes what it wants, the aggregate decides what that becomes. Letting callers hand over
/// constructed items is how merging, limits and currency checks get bypassed.
/// </remarks>
public sealed record OrderLineRequest(
    Guid ProductId,
    string Sku,
    string ProductName,
    Money UnitPrice,
    int Quantity,
    string? Size = null,
    string? ColourName = null);
