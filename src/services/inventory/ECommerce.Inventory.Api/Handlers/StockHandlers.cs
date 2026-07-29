using ECommerce.Contracts.Inventory;
using ECommerce.Contracts.Ordering;
using ECommerce.Contracts.Saga;
using ECommerce.EventBus;
using ECommerce.Inventory.Api.Infrastructure;
using ECommerce.Inventory.Api.Model;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Inventory.Api.Handlers;

/// <summary>
/// Reserves stock when the saga asks.
/// </summary>
/// <remarks>
/// <para>
/// <b>All or nothing.</b> A ten-line order where one item is out of stock reserves nothing. Partially
/// reserving would leave the saga holding stock for an order that cannot be fulfilled, and the customer
/// would be charged for items they will not receive — while other customers are blocked from buying the
/// items now sitting reserved for an order that is about to be cancelled.
/// </para>
/// <para>
/// Enforced by the transaction: every <c>Reserve</c> happens before a single <c>SaveChangesAsync</c>, so
/// a throw part way through leaves the database exactly as it was.
/// </para>
/// </remarks>
public sealed class ReserveStockHandler(
    InventoryDbContext db,
    IOutboxWriter outbox,
    ILogger<ReserveStockHandler> logger)
    : IIntegrationEventHandler<ReserveStockCommand>
{
    public async Task HandleAsync(
        ReserveStockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Idempotency, enforced by the unique index on order_id as well as this check. A duplicate
        // delivery must not reserve the same stock twice - which would silently make two items
        // unavailable for every one actually ordered.
        if (await db.Reservations.AnyAsync(r => r.OrderId == command.OrderId, cancellationToken))
        {
            logger.LogDebug("Stock already reserved for {OrderNumber}; ignoring duplicate.", command.OrderNumber);
            return;
        }

        var unavailable = new List<string>();
        var reserved = new List<(string Sku, int Quantity)>();

        foreach (ReserveStockLine line in command.Lines)
        {
            StockItem? item = await db.StockItems
                .FirstOrDefaultAsync(s => s.Sku == line.Sku, cancellationToken);

            if (item is null)
            {
                // A SKU Inventory has never heard of. Treated as unavailable rather than ignored:
                // silently dropping it would fulfil an order missing an item the customer paid for.
                unavailable.Add(line.Sku);
                continue;
            }

            try
            {
                item.Reserve(line.Quantity);
                reserved.Add((line.Sku, line.Quantity));
            }
            catch (InsufficientStockException)
            {
                unavailable.Add(line.Sku);
            }
        }

        if (unavailable.Count > 0)
        {
            // Discard every successful Reserve() from the loop above. All-or-nothing needs no
            // compensating action precisely because nothing was ever committed - the change tracker
            // is cleared before anything reaches the database.
            //
            // Order matters here: clear FIRST, then add the outbox row. Adding it before the clear
            // would throw the rejection away along with the reservations, and the saga would wait
            // forever for an answer that was never sent.
            db.ChangeTracker.Clear();

            outbox.Add(new StockRejectedIntegrationEvent
            {
                OrderId = command.OrderId,
                OrderNumber = command.OrderNumber,
                UnavailableSkus = unavailable,
            });

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Stock rejected for {OrderNumber}: {Skus}.",
                command.OrderNumber,
                string.Join(", ", unavailable));

            return;
        }

        db.Reservations.Add(new StockReservation(command.OrderId, command.OrderNumber, reserved));

        outbox.Add(new StockReservedIntegrationEvent
        {
            OrderId = command.OrderId,
            OrderNumber = command.OrderNumber,
            Lines = reserved
                .Select(line => new StockLineContract { Sku = line.Sku, Quantity = line.Quantity })
                .ToArray(),
        });

        // ONE transaction: the reservation, the decremented availability, and the event that announces
        // it. Either the whole thing happened or none of it did.
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Reserved stock for {OrderNumber}.", command.OrderNumber);
    }
}

/// <summary>
/// Releases a reservation. <b>The compensating action.</b>
/// </summary>
/// <remarks>
/// <para>
/// Everything about this handler is shaped by one fact: <b>it will be retried, possibly long after the
/// original attempt succeeded.</b> So it is idempotent at two levels — the reservation is marked
/// released and ignored on a second visit, and <c>StockItem.Release</c> clamps at zero even if it is
/// somehow reached twice.
/// </para>
/// <para>
/// Releasing a reservation that was never made is not a harmless no-op: it would raise the available
/// count above what physically exists, and the shop would cheerfully sell stock it does not have.
/// </para>
/// </remarks>
public sealed class ReleaseStockHandler(
    InventoryDbContext db,
    IOutboxWriter outbox,
    ILogger<ReleaseStockHandler> logger)
    : IIntegrationEventHandler<ReleaseStockCommand>
{
    public async Task HandleAsync(
        ReleaseStockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockReservation? reservation = await db.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OrderId == command.OrderId, cancellationToken);

        if (reservation is null)
        {
            // Nothing to compensate. Logged at warning rather than silently returning, because a saga
            // asking to release a reservation Inventory has no record of means the two disagree about
            // what happened - and that is worth someone looking at.
            logger.LogWarning(
                "Release requested for {OrderNumber} but no reservation exists.", command.OrderNumber);
            return;
        }

        if (reservation.IsReleased)
        {
            logger.LogDebug("Reservation for {OrderNumber} already released.", command.OrderNumber);
            return;
        }

        foreach (ReservationLine line in reservation.Lines)
        {
            StockItem? item = await db.StockItems
                .FirstOrDefaultAsync(s => s.Sku == line.Sku, cancellationToken);

            item?.Release(line.Quantity);
        }

        reservation.MarkReleased();

        // Published so the customer's timeline can show that the reservation was undone. A compensating
        // action that leaves no trace is indistinguishable from nothing having happened, which is
        // exactly the confusion this is meant to avoid.
        outbox.Add(new StockReleasedIntegrationEvent
        {
            OrderId = command.OrderId,
            OrderNumber = command.OrderNumber,
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Released stock for {OrderNumber} (compensation).", command.OrderNumber);
    }
}

/// <summary>
/// Ships reserved stock when an order is dispatched.
/// </summary>
/// <remarks>
/// The only place <c>OnHand</c> falls. Until this point the goods are physically present and merely
/// spoken for, which is exactly what the reserved/on-hand split exists to express.
/// </remarks>
public sealed class OrderShippedHandler(
    InventoryDbContext db,
    ILogger<OrderShippedHandler> logger)
    : IIntegrationEventHandler<OrderShippedIntegrationEvent>
{
    public async Task HandleAsync(
        OrderShippedIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        StockReservation? reservation = await db.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OrderId == @event.OrderId, cancellationToken);

        if (reservation is null || reservation.IsReleased)
        {
            return;
        }

        foreach (ReservationLine line in reservation.Lines)
        {
            StockItem? item = await db.StockItems
                .FirstOrDefaultAsync(s => s.Sku == line.Sku, cancellationToken);

            item?.Ship(line.Quantity);
        }

        // Marked released so a later compensation cannot put shipped stock back on the shelf.
        reservation.MarkReleased();

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Shipped stock for {OrderNumber}.", @event.OrderNumber);
    }
}
