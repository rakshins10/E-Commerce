using System.Data;

using Dapper;

using ECommerce.Auth;

namespace ECommerce.BackOffice.Api.Features;

/// <summary>
/// Dashboard figures for the admin panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>This service reads other services' databases, and that needs justifying.</b> It is the one place
/// in the repo that crosses a data boundary, and it is a deliberate, bounded exception rather than an
/// oversight.
/// </para>
/// <para>
/// The alternative — calling each service over HTTP and aggregating — was rejected because a dashboard
/// fans out to five services on every page load, and then the dashboard is down whenever any one of them
/// is. Reporting is the classic case where the boundary that helps write paths hurts read paths.
/// </para>
/// <para>
/// <b>The rule that keeps this honest:</b> connections are <b>read-only</b> and the queries are
/// aggregates. Back-office never writes to another service's tables and never reads a row it would then
/// act on — that would be reaching into someone else's aggregate, and the invariants live there for a
/// reason.
/// </para>
/// <para>
/// <b>What a production system would do instead.</b> Publish figures as events into a reporting store, or
/// use a read replica per service. Both are more work and neither changes the shape of the code here. The
/// honest position is that this is a reference implementation choosing the simplest thing that
/// demonstrates the pattern, and saying so.
/// </para>
/// </remarks>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/admin").WithTags("Back office");

        group.MapGet("/dashboard", GetDashboard)
            .RequirePermission(Permissions.Admin.DashboardRead)
            .WithSummary("Order and revenue figures.");

        group.MapGet("/audit", GetAuditLog)
            .RequirePermission(Permissions.Admin.AuditRead)
            .WithSummary("What staff have done.");

        return app;
    }

    private static async Task<IResult> GetDashboard(
        DashboardQueries queries,
        CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetAsync(cancellationToken));

    private static async Task<IResult> GetAuditLog(
        AuditQueries queries,
        CancellationToken cancellationToken,
        int limit = 50) =>
        Results.Ok(await queries.GetRecentAsync(limit, cancellationToken));
}

public sealed record DashboardDto
{
    public required int OrdersToday { get; init; }

    public required int OrdersTotal { get; init; }

    public required decimal RevenueToday { get; init; }

    public required decimal RevenueTotal { get; init; }

    public required string Currency { get; init; }

    /// <summary>Orders that have not reached a terminal state. What operations actually watches.</summary>
    public required int OrdersInFlight { get; init; }

    public required int OrdersCancelled { get; init; }

    /// <summary>Sagas that started and never finished — the operational alarm.</summary>
    public required int SagasStuck { get; init; }

    public required int LowStockItems { get; init; }

    public required IReadOnlyList<StatusCountDto> ByStatus { get; init; }
}

public sealed record StatusCountDto(string Status, int Count);

/// <summary>
/// Dashboard aggregates.
/// </summary>
/// <remarks>
/// Every query is a <c>COUNT</c> or a <c>SUM</c>. Nothing here returns a row that could be mistaken for a
/// domain object, which is the line that keeps cross-database reads from becoming cross-database
/// coupling.
/// </remarks>
public sealed class DashboardQueries(
    [FromKeyedServices("ordering")] IDbConnection ordering,
    [FromKeyedServices("saga")] IDbConnection saga,
    [FromKeyedServices("inventory")] IDbConnection inventory)
{
    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        // Revenue is computed from the ORDER LINES rather than a stored total, because Ordering
        // deliberately does not store one - see docs/services/ordering.md. Cancelled orders are
        // excluded: they are not revenue, and counting them is how a dashboard ends up flattering.
        const string orderSql = """
            SELECT COUNT(*)::int
            FROM   orders
            WHERE  placed_at >= CURRENT_DATE;

            SELECT COUNT(*)::int FROM orders;

            SELECT COALESCE(SUM(i.unit_price * i.quantity), 0)
            FROM   orders o JOIN order_items i ON i.order_id = o.id
            WHERE  o.placed_at >= CURRENT_DATE AND o.status <> 90;

            SELECT COALESCE(SUM(i.unit_price * i.quantity), 0)
            FROM   orders o JOIN order_items i ON i.order_id = o.id
            WHERE  o.status <> 90;

            -- In flight: submitted, awaiting payment, or paid but not yet delivered.
            SELECT COUNT(*)::int FROM orders WHERE status IN (10, 20, 30, 40);

            SELECT COUNT(*)::int FROM orders WHERE status = 90;

            SELECT status, COUNT(*)::int AS count FROM orders GROUP BY status ORDER BY status;
            """;

        using SqlMapper.GridReader reader = await ordering.QueryMultipleAsync(
            new CommandDefinition(orderSql, cancellationToken: cancellationToken));

        int ordersToday = await reader.ReadSingleAsync<int>();
        int ordersTotal = await reader.ReadSingleAsync<int>();
        decimal revenueToday = await reader.ReadSingleAsync<decimal>();
        decimal revenueTotal = await reader.ReadSingleAsync<decimal>();
        int inFlight = await reader.ReadSingleAsync<int>();
        int cancelled = await reader.ReadSingleAsync<int>();

        IEnumerable<(int Status, int Count)> statuses =
            await reader.ReadAsync<(int Status, int Count)>();

        int stuck = await saga.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)::int
            FROM   order_sagas
            WHERE  completed_at IS NULL
              AND  started_at < NOW() - INTERVAL '5 minutes';
            """,
            cancellationToken: cancellationToken));

        int lowStock = await inventory.ExecuteScalarAsync<int>(new CommandDefinition(
            // Available, not on-hand: stock that is spoken for cannot be sold.
            "SELECT COUNT(*)::int FROM stock_items WHERE on_hand - reserved <= reorder_level;",
            cancellationToken: cancellationToken));

        return new DashboardDto
        {
            OrdersToday = ordersToday,
            OrdersTotal = ordersTotal,
            RevenueToday = revenueToday,
            RevenueTotal = revenueTotal,
            Currency = "GBP",
            OrdersInFlight = inFlight,
            OrdersCancelled = cancelled,
            SagasStuck = stuck,
            LowStockItems = lowStock,
            ByStatus = statuses
                .Select(row => new StatusCountDto(StatusName(row.Status), row.Count))
                .ToArray(),
        };
    }

    /// <remarks>
    /// Duplicated from Ordering's read side on purpose: Back-office must not reference another service's
    /// assembly. A test asserts the two agree, so a status added in one and forgotten here fails the
    /// build rather than rendering "Unknown" on a dashboard.
    /// </remarks>
    private static string StatusName(int status) => status switch
    {
        10 => "Submitted",
        20 => "AwaitingPayment",
        30 => "Paid",
        40 => "Shipped",
        50 => "Delivered",
        90 => "Cancelled",
        _ => "Unknown",
    };
}
