using System.Data;

using Dapper;

using ECommerce.Common.Pagination;

namespace ECommerce.Ordering.Application.Orders;

/// <summary>
/// The read side. Dapper, hand-written SQL, DTOs only.
/// </summary>
/// <remarks>
/// <para>
/// <b>CQRS as a hard boundary, not a naming convention.</b> Writes go through EF Core and the aggregate;
/// reads come through here and never touch a domain type. The separation is enforced by the fact that
/// this class has no way to obtain an <c>Order</c> — it holds an <see cref="IDbConnection"/>, not a
/// <c>DbContext</c>.
/// </para>
/// <para>
/// <b>Why bother.</b> The two sides genuinely want different things. A write needs the whole aggregate
/// loaded so its invariants can be checked. A read of "my orders" needs a reference, a date, a status
/// and a total for twenty rows — and materialising twenty aggregates with every line, to display none of
/// them, is an order of magnitude more work and more data than the screen uses.
/// </para>
/// <para>
/// <b>What it costs.</b> Hand-written SQL is not refactor-safe: rename a column and the compiler says
/// nothing. That is a real cost, paid deliberately, and the reason the integration tests run against a
/// real PostgreSQL rather than an in-memory substitute — a typo here can only be caught by executing it.
/// </para>
/// </remarks>
public sealed class OrderQueries(IDbConnection connection)
{
    /// <summary>
    /// "My orders", newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The total is computed in SQL by summing the lines rather than read from a column, because the
    /// aggregate deliberately does not store it. Doing the arithmetic in the database means one round
    /// trip instead of fetching every line to add up in C#.
    /// </para>
    /// <para>
    /// <c>buyer_id</c> is a parameter, always. String concatenation here would be SQL injection with a
    /// value taken straight from a token.
    /// </para>
    /// </remarks>
    public async Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(
        string buyerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        PageRequest pagination = new PageRequest(page, pageSize).Normalise();

        const string sql = """
            SELECT COUNT(*)::int
            FROM   orders
            WHERE  buyer_id = @BuyerId;

            SELECT o.id                                            AS Id,
                   o.order_number                                  AS OrderNumber,
                   o.status                                        AS Status,
                   o.currency                                      AS Currency,
                   o.placed_at                                     AS PlacedAt,
                   COALESCE(SUM(i.unit_price * i.quantity), 0)     AS Total,
                   COALESCE(SUM(i.quantity), 0)::int               AS TotalUnits
            FROM   orders o
            LEFT   JOIN order_items i ON i.order_id = o.id
            WHERE  o.buyer_id = @BuyerId
            GROUP  BY o.id, o.order_number, o.status, o.currency, o.placed_at
            -- id as a tiebreaker: two orders placed in the same millisecond would otherwise come back in
            -- an arbitrary order, and page 2 could repeat a row from page 1.
            ORDER  BY o.placed_at DESC, o.id DESC
            LIMIT  @PageSize OFFSET @Offset;
            """;

        var command = new CommandDefinition(
            sql,
            new { BuyerId = buyerId, pagination.PageSize, Offset = pagination.Skip },
            cancellationToken: cancellationToken);

        // One round trip for both the count and the page. Two separate queries would be two round trips
        // and, without a transaction, could disagree with each other.
        using SqlMapper.GridReader reader =
            await connection.QueryMultipleAsync(command).ConfigureAwait(false);

        int total = await reader.ReadSingleAsync<int>().ConfigureAwait(false);
        IEnumerable<OrderSummaryRow> rows = await reader.ReadAsync<OrderSummaryRow>().ConfigureAwait(false);

        IReadOnlyList<OrderSummaryDto> items = rows.Select(row => new OrderSummaryDto
        {
            Id = row.Id,
            OrderNumber = row.OrderNumber,
            Status = OrderStatusNames.ToName(row.Status),
            Total = row.Total,
            Currency = row.Currency,
            TotalUnits = row.TotalUnits,
            PlacedAt = row.PlacedAt,
            CanBeCancelled = OrderStatusNames.CanBeCancelled(row.Status),
        }).ToArray();

        return new PagedResult<OrderSummaryDto>(items, pagination.Page, pagination.PageSize, total);
    }

    /// <summary>
    /// Every order, newest first. Staff only.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate method from <see cref="GetMyOrdersAsync"/> rather than the same one with
    /// a nullable filter. Reading "all orders" and reading "my orders" are different capabilities, and
    /// keeping them as different methods means an endpoint cannot accidentally pass null and widen its
    /// own scope.
    /// </remarks>
    public async Task<PagedResult<OrderSummaryDto>> GetAllOrdersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        PageRequest pagination = new PageRequest(page, pageSize).Normalise();

        const string sql = """
            SELECT COUNT(*)::int FROM orders;

            SELECT o.id                                        AS Id,
                   o.order_number                              AS OrderNumber,
                   o.status                                    AS Status,
                   o.currency                                  AS Currency,
                   o.placed_at                                 AS PlacedAt,
                   COALESCE(SUM(i.unit_price * i.quantity), 0) AS Total,
                   COALESCE(SUM(i.quantity), 0)::int           AS TotalUnits
            FROM   orders o
            LEFT   JOIN order_items i ON i.order_id = o.id
            GROUP  BY o.id, o.order_number, o.status, o.currency, o.placed_at
            ORDER  BY o.placed_at DESC, o.id DESC
            LIMIT  @PageSize OFFSET @Offset;
            """;

        var command = new CommandDefinition(
            sql,
            new { pagination.PageSize, Offset = pagination.Skip },
            cancellationToken: cancellationToken);

        using SqlMapper.GridReader reader =
            await connection.QueryMultipleAsync(command).ConfigureAwait(false);

        int total = await reader.ReadSingleAsync<int>().ConfigureAwait(false);
        IEnumerable<OrderSummaryRow> rows = await reader.ReadAsync<OrderSummaryRow>().ConfigureAwait(false);

        IReadOnlyList<OrderSummaryDto> items = rows.Select(row => new OrderSummaryDto
        {
            Id = row.Id,
            OrderNumber = row.OrderNumber,
            Status = OrderStatusNames.ToName(row.Status),
            Total = row.Total,
            Currency = row.Currency,
            TotalUnits = row.TotalUnits,
            PlacedAt = row.PlacedAt,
            CanBeCancelled = OrderStatusNames.CanBeCancelled(row.Status),
        }).ToArray();

        return new PagedResult<OrderSummaryDto>(items, pagination.Page, pagination.PageSize, total);
    }

    /// <summary>One order in full, including its lines.</summary>
    /// <remarks>
    /// <paramref name="buyerId"/> is applied in the WHERE clause rather than checked after loading.
    /// Filtering in SQL means another customer's order is never in memory to be leaked by a logging
    /// statement or a serialisation mistake, and it turns an authorization check into something the
    /// database enforces.
    /// </remarks>
    public async Task<OrderDto?> GetOrderAsync(
        Guid orderId,
        string? buyerId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            -- Every column is ALIASED to the C# property name. Dapper matches names exactly and does
            -- not translate snake_case, so an unaliased "order_number" silently leaves OrderNumber at
            -- its default. That fails as a wrong VALUE rather than an error - an order with no
            -- reference and a total of zero - which is far harder to spot than an exception.
            SELECT o.id                  AS Id,
                   o.order_number        AS OrderNumber,
                   o.status              AS Status,
                   o.currency            AS Currency,
                   o.placed_at           AS PlacedAt,
                   o.paid_at             AS PaidAt,
                   o.shipped_at          AS ShippedAt,
                   o.delivered_at        AS DeliveredAt,
                   o.cancelled_at        AS CancelledAt,
                   o.cancellation_reason AS CancellationReason,
                   o.ship_recipient      AS ShipRecipient,
                   o.ship_line1          AS ShipLine1,
                   o.ship_line2          AS ShipLine2,
                   o.ship_city           AS ShipCity,
                   o.ship_postcode       AS ShipPostcode,
                   o.ship_country        AS ShipCountry
            FROM   orders o
            WHERE  o.id = @OrderId
              -- A null buyerId means "staff, any order". A non-null one restricts to that customer.
              AND  (@BuyerId IS NULL OR o.buyer_id = @BuyerId);

            SELECT i.product_id   AS ProductId,
                   i.sku          AS Sku,
                   i.product_name AS ProductName,
                   i.size         AS Size,
                   i.colour_name  AS ColourName,
                   i.quantity     AS Quantity,
                   i.unit_price   AS UnitPrice
            FROM   order_items i
            WHERE  i.order_id = @OrderId
            ORDER  BY i.product_name;
            """;

        var command = new CommandDefinition(
            sql,
            new { OrderId = orderId, BuyerId = buyerId },
            cancellationToken: cancellationToken);

        using SqlMapper.GridReader reader =
            await connection.QueryMultipleAsync(command).ConfigureAwait(false);

        OrderRow? order = await reader.ReadSingleOrDefaultAsync<OrderRow>().ConfigureAwait(false);

        if (order is null)
        {
            return null;
        }

        IEnumerable<OrderItemRow> lines = await reader.ReadAsync<OrderItemRow>().ConfigureAwait(false);

        OrderItemDto[] items = lines.Select(line => new OrderItemDto
        {
            ProductId = line.ProductId,
            Sku = line.Sku,
            ProductName = line.ProductName,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            LineTotal = line.UnitPrice * line.Quantity,
        }).ToArray();

        return new OrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            Status = OrderStatusNames.ToName(order.Status),
            CanBeCancelled = OrderStatusNames.CanBeCancelled(order.Status),
            Total = items.Sum(item => item.LineTotal),
            Currency = order.Currency,
            TotalUnits = items.Sum(item => item.Quantity),
            PlacedAt = order.PlacedAt,
            PaidAt = order.PaidAt,
            ShippedAt = order.ShippedAt,
            DeliveredAt = order.DeliveredAt,
            CancelledAt = order.CancelledAt,
            CancellationReason = OrderStatusNames.ToCancellationReasonName(order.CancellationReason),
            ShippingAddress = new AddressDto
            {
                Recipient = order.ShipRecipient,
                Line1 = order.ShipLine1,
                Line2 = order.ShipLine2,
                City = order.ShipCity,
                Postcode = order.ShipPostcode,
                Country = order.ShipCountry,
            },
            Items = items,
        };
    }

    // Row types are private and deliberately dumb: exactly the shape the SELECT returns, mapped to a DTO
    // above. Letting Dapper populate the public DTO directly would tie the API contract to the column
    // list, so a rename in the database would silently change the JSON.
    private sealed record OrderSummaryRow
    {
        public Guid Id { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public int Status { get; init; }

        public string Currency { get; init; } = "GBP";

        public DateTimeOffset PlacedAt { get; init; }

        public decimal Total { get; init; }

        public int TotalUnits { get; init; }
    }

    private sealed record OrderRow
    {
        public Guid Id { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public int Status { get; init; }

        public string Currency { get; init; } = "GBP";

        public DateTimeOffset PlacedAt { get; init; }

        public DateTimeOffset? PaidAt { get; init; }

        public DateTimeOffset? ShippedAt { get; init; }

        public DateTimeOffset? DeliveredAt { get; init; }

        public DateTimeOffset? CancelledAt { get; init; }

        public int? CancellationReason { get; init; }

        public string ShipRecipient { get; init; } = string.Empty;

        public string ShipLine1 { get; init; } = string.Empty;

        public string? ShipLine2 { get; init; }

        public string ShipCity { get; init; } = string.Empty;

        public string ShipPostcode { get; init; } = string.Empty;

        public string ShipCountry { get; init; } = "GB";
    }

    private sealed record OrderItemRow
    {
        public Guid ProductId { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string ProductName { get; init; } = string.Empty;

        public int Quantity { get; init; }

        public decimal UnitPrice { get; init; }
    }
}

/// <summary>
/// Translates the persisted status numbers into names for the API.
/// </summary>
/// <remarks>
/// The read side must not reference the domain enum — that is the boundary CQRS is drawing here — so the
/// mapping is stated explicitly. The duplication is real and small, and a test asserts the two agree, so
/// a status added to the domain and forgotten here fails the build rather than rendering "Unknown" to a
/// customer.
/// </remarks>
public static class OrderStatusNames
{
    public const int Submitted = 10;
    public const int AwaitingPayment = 20;
    public const int Paid = 30;
    public const int Shipped = 40;
    public const int Delivered = 50;
    public const int Cancelled = 90;

    public static string ToName(int status) => status switch
    {
        Submitted => "Submitted",
        AwaitingPayment => "AwaitingPayment",
        Paid => "Paid",
        Shipped => "Shipped",
        Delivered => "Delivered",
        Cancelled => "Cancelled",
        _ => "Unknown",
    };

    public static bool CanBeCancelled(int status) =>
        status is Submitted or AwaitingPayment or Paid;

    public static string? ToCancellationReasonName(int? reason) => reason switch
    {
        1 => "RequestedByCustomer",
        2 => "CancelledByStaff",
        3 => "PaymentDeclined",
        4 => "OutOfStock",
        _ => null,
    };
}
