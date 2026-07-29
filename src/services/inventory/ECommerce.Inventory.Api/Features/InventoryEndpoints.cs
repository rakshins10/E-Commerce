using System.Data;

using Dapper;

using ECommerce.Auth;
using ECommerce.Inventory.Api.Infrastructure;
using ECommerce.Inventory.Api.Model;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Inventory.Api.Features;

/// <summary>
/// Stock levels for staff.
/// </summary>
/// <remarks>
/// Read-mostly. The only write is a manual adjustment — goods in, damage, or a stock take correction.
/// Reservations are never made or released through HTTP: they happen in response to saga commands, and an
/// endpoint that could reserve stock directly would let someone create a reservation no saga knows about
/// and nothing will ever release.
/// </remarks>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/inventory").WithTags("Inventory");

        group.MapGet("/", GetStock)
            .RequirePermission(Permissions.Inventory.Read)
            .WithSummary("Stock levels, most constrained first.");

        group.MapGet("/low-stock", GetLowStock)
            .RequirePermission(Permissions.Inventory.Read)
            .WithSummary("Items at or below their reorder level.");

        group.MapPost("/{sku}/adjust", AdjustStock)
            .RequirePermission(Permissions.Inventory.Adjust)
            .WithSummary("Manual correction: goods in, damage, or a stock take.");

        return app;
    }

    private static async Task<IResult> GetStock(StockQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetAllAsync(cancellationToken));

    private static async Task<IResult> GetLowStock(
        StockQueries queries,
        CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetLowStockAsync(cancellationToken));

    private static async Task<IResult> AdjustStock(
        string sku,
        AdjustStockRequest request,
        InventoryDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        StockItem? item = await db.StockItems.FirstOrDefaultAsync(s => s.Sku == sku, cancellationToken);

        if (item is null)
        {
            return Results.NotFound();
        }

        item.Adjust(request.Delta);
        await db.SaveChangesAsync(cancellationToken);

        // Adjustments are logged at Information with the reason, because a stock level that changed
        // without an order behind it is exactly what somebody investigating a discrepancy needs to find.
        loggerFactory.CreateLogger("Inventory").LogInformation(
            "Stock for {Sku} adjusted by {Delta}: {Reason}. Now {OnHand} on hand.",
            sku,
            request.Delta,
            request.Reason,
            item.OnHand);

        return Results.Ok(new StockItemDto(
            item.Sku, item.ProductName, item.OnHand, item.Reserved, item.Available, item.ReorderLevel));
    }
}

public sealed record AdjustStockRequest
{
    public required int Delta { get; init; }

    /// <summary>Why. Required, because an unexplained stock movement is impossible to audit.</summary>
    public required string Reason { get; init; }
}

public sealed record StockItemDto(
    string Sku,
    string ProductName,
    int OnHand,
    int Reserved,
    int Available,
    int ReorderLevel);

/// <summary>The inventory read side. Dapper, like every other read side here.</summary>
public sealed class StockQueries(IDbConnection connection)
{
    public async Task<IReadOnlyList<StockItemDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        // Available is computed in SQL for the same reason it is computed in C#: storing it would be a
        // second source of truth. Ordered by availability so the items that need attention are first.
        const string sql = """
            SELECT sku                     AS Sku,
                   product_name            AS ProductName,
                   on_hand                 AS OnHand,
                   reserved                AS Reserved,
                   on_hand - reserved      AS Available,
                   reorder_level           AS ReorderLevel
            FROM   stock_items
            ORDER  BY on_hand - reserved, sku;
            """;

        return (await connection.QueryAsync<StockItemDto>(
            new CommandDefinition(sql, cancellationToken: cancellationToken))).ToArray();
    }

    public async Task<IReadOnlyList<StockItemDto>> GetLowStockAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT sku                 AS Sku,
                   product_name        AS ProductName,
                   on_hand             AS OnHand,
                   reserved            AS Reserved,
                   on_hand - reserved  AS Available,
                   reorder_level       AS ReorderLevel
            FROM   stock_items
            -- AVAILABLE against the reorder level, not on-hand. Stock that is spoken for cannot be
            -- sold, so a shelf full of reserved items still needs reordering.
            WHERE  on_hand - reserved <= reorder_level
            ORDER  BY on_hand - reserved, sku;
            """;

        return (await connection.QueryAsync<StockItemDto>(
            new CommandDefinition(sql, cancellationToken: cancellationToken))).ToArray();
    }
}
