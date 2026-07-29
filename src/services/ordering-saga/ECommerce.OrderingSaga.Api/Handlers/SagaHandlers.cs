using ECommerce.Contracts.Inventory;
using ECommerce.Contracts.Ordering;
using ECommerce.Contracts.Payment;
using ECommerce.Contracts.Saga;
using ECommerce.EventBus;
using ECommerce.OrderingSaga.Api.Infrastructure;
using ECommerce.OrderingSaga.Api.Model;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.OrderingSaga.Api.Handlers;

/// <summary>
/// The checkout saga.
/// </summary>
/// <remarks>
/// <para>
/// <b>The happy path.</b>
/// </para>
/// <code>
///   OrderSubmitted ──► ReserveStock
///                          │
///                    StockReserved ──► ConfirmStock (Ordering)
///                                 └──► RequestPayment
///                                           │
///                                    PaymentSucceeded ──► MarkPaid (Ordering)
///                                                          [saga Completed]
/// </code>
/// <para>
/// <b>The two failure paths, which are the interesting part.</b>
/// </para>
/// <code>
///   StockRejected     ──► Cancel(OutOfStock)          [nothing to compensate]
///
///   PaymentFailed     ──► ReleaseStock   ◄── COMPENSATION
///                    └──► Cancel(PaymentDeclined)
/// </code>
/// <para>
/// <b>There is no rollback.</b> Each step committed in a different service's database, so undoing one
/// means performing a <i>new</i> action with the opposite effect. That is what a compensating action is,
/// and it is not the same thing as a transaction rollback:
/// </para>
/// <list type="bullet">
///   <item><description>It can fail, and then needs retrying.</description></item>
///   <item><description>It happens <i>later</i>, so the world has moved on — released stock may have
///   been sold to somebody else in the meantime.</description></item>
///   <item><description>It is visible. A rollback leaves no trace; a compensation is a real event
///   others can see, which is why <c>StockReleased</c> is published.</description></item>
/// </list>
/// <para>
/// <b>Every handler here is idempotent</b>, because delivery is at-least-once and these messages
/// <i>will</i> arrive twice. The guard is the same in each: load the saga, check
/// <see cref="OrderSaga.IsFinished"/> or the current state, and return quietly if there is nothing to do.
/// </para>
/// </remarks>
public sealed class OrderSubmittedHandler(
    SagaDbContext db,
    IOutboxWriter outbox,
    ILogger<OrderSubmittedHandler> logger)
    : IIntegrationEventHandler<OrderSubmittedIntegrationEvent>
{
    public async Task HandleAsync(
        OrderSubmittedIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // The order id is the saga's primary key, so a duplicate delivery finds the existing row rather
        // than starting a second saga for the same order - which would reserve stock twice.
        if (await db.Sagas.AnyAsync(s => s.OrderId == @event.OrderId, cancellationToken))
        {
            logger.LogDebug("Saga for {OrderNumber} already exists; ignoring duplicate.", @event.OrderNumber);
            return;
        }

        var saga = new OrderSaga(
            @event.OrderId, @event.OrderNumber, @event.BuyerId, @event.Total, @event.Currency);

        saga.Record("OrderSubmitted", $"{@event.Lines.Count} line(s), {@event.Total} {@event.Currency}");
        saga.Record("ReserveStockRequested", "Asked Inventory to reserve stock.");

        db.Sagas.Add(saga);

        // The command goes through the OUTBOX, in the same transaction as the saga row. If it were
        // published directly, a crash here would leave a saga that believes it asked for stock while
        // Inventory never heard - and the order would sit in Submitted forever with nothing to
        // diagnose. The outbox is what makes the saga's own state and its outgoing messages agree.
        outbox.Add(new ReserveStockCommand
        {
            OrderId = @event.OrderId,
            OrderNumber = @event.OrderNumber,
            Lines = @event.Lines
                .Select(line => new ReserveStockLine { Sku = line.Sku, Quantity = line.Quantity })
                .ToArray(),
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Saga started for {OrderNumber}.", @event.OrderNumber);
    }
}

/// <summary>Stock was reserved: confirm it on the order and ask for payment.</summary>
public sealed class StockReservedHandler(
    SagaDbContext db,
    IOutboxWriter outbox,
    ILogger<StockReservedHandler> logger)
    : IIntegrationEventHandler<StockReservedIntegrationEvent>
{
    public async Task HandleAsync(
        StockReservedIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        OrderSaga? saga = await db.Sagas
            .Include(s => s.Steps)
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId, cancellationToken);

        if (saga is null)
        {
            // Inventory answered a request this saga has no record of. Possible during a rolling deploy
            // or after a database restore. Logged loudly rather than swallowed: stock is now reserved
            // and nothing will ever release it.
            logger.LogError(
                "StockReserved for unknown saga {OrderNumber}. Stock is reserved with no saga to release it.",
                @event.OrderNumber);
            return;
        }

        // Idempotency: a duplicate delivery must not send a second payment request.
        if (saga.State != SagaState.AwaitingStock)
        {
            logger.LogDebug(
                "Saga {OrderNumber} is {State}; ignoring duplicate StockReserved.",
                @event.OrderNumber,
                saga.State);
            return;
        }

        saga.MarkStockReserved();
        saga.Record("StockReserved", $"{@event.Lines.Count} line(s) reserved.", SagaState.AwaitingPayment);
        saga.Record("PaymentRequested", $"Requested {saga.Amount} {saga.Currency}.");

        outbox.Add(new AdvanceOrderCommand
        {
            OrderId = saga.OrderId,
            Transition = AdvanceOrderCommand.Transitions.ConfirmStock,
        });

        outbox.Add(new RequestPaymentCommand
        {
            OrderId = saga.OrderId,
            OrderNumber = saga.OrderNumber,
            BuyerId = saga.BuyerId,
            Amount = saga.Amount,
            Currency = saga.Currency,
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Saga {OrderNumber}: stock reserved, payment requested.", saga.OrderNumber);
    }
}

/// <summary>Stock could not be reserved: cancel the order. Nothing to compensate.</summary>
public sealed class StockRejectedHandler(
    SagaDbContext db,
    IOutboxWriter outbox,
    ILogger<StockRejectedHandler> logger)
    : IIntegrationEventHandler<StockRejectedIntegrationEvent>
{
    public async Task HandleAsync(
        StockRejectedIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        OrderSaga? saga = await db.Sagas
            .Include(s => s.Steps)
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId, cancellationToken);

        if (saga is null || saga.IsFinished)
        {
            return;
        }

        string skus = string.Join(", ", @event.UnavailableSkus);

        saga.Record("StockRejected", $"Unavailable: {skus}");

        // No ReleaseStockCommand here, and that is the point. Nothing was reserved, so releasing would
        // ADD stock that never left - a corruption in the opposite direction from the failure, and one
        // that nobody would notice until a stock count came out wrong.
        outbox.Add(new AdvanceOrderCommand
        {
            OrderId = saga.OrderId,
            Transition = AdvanceOrderCommand.Transitions.Cancel,
            CancellationReason = "OutOfStock",
        });

        saga.MarkFailed($"Out of stock: {skus}");

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Saga {OrderNumber} failed: out of stock ({Skus}).", saga.OrderNumber, skus);
    }
}

/// <summary>Payment succeeded: mark the order paid and finish.</summary>
public sealed class PaymentSucceededHandler(
    SagaDbContext db,
    IOutboxWriter outbox,
    ILogger<PaymentSucceededHandler> logger)
    : IIntegrationEventHandler<PaymentSucceededIntegrationEvent>
{
    public async Task HandleAsync(
        PaymentSucceededIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        OrderSaga? saga = await db.Sagas
            .Include(s => s.Steps)
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId, cancellationToken);

        if (saga is null || saga.IsFinished)
        {
            // A duplicate arriving after completion must not send a second AdvanceOrderCommand. The
            // Order aggregate would ignore it - MarkAsPaid is idempotent too - but relying on the far
            // end to catch your duplicates is how one missing guard becomes two confirmation emails.
            return;
        }

        saga.Record("PaymentSucceeded", $"Reference {@event.PaymentReference}.");

        outbox.Add(new AdvanceOrderCommand
        {
            OrderId = saga.OrderId,
            Transition = AdvanceOrderCommand.Transitions.MarkPaid,
            PaymentReference = @event.PaymentReference,
        });

        saga.MarkPaid(@event.PaymentReference);
        saga.Record("SagaCompleted", "Order paid; checkout complete.");

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Saga {OrderNumber} completed.", saga.OrderNumber);
    }
}

/// <summary>
/// Payment failed: <b>compensate</b>, then cancel.
/// </summary>
/// <remarks>
/// The handler this entire phase exists to demonstrate. Two things must happen, in this order and for
/// different reasons: the stock goes back because somebody else should be able to buy it, and the order
/// is cancelled because it will never be fulfilled.
/// </remarks>
public sealed class PaymentFailedHandler(
    SagaDbContext db,
    IOutboxWriter outbox,
    ILogger<PaymentFailedHandler> logger)
    : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    public async Task HandleAsync(
        PaymentFailedIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        OrderSaga? saga = await db.Sagas
            .Include(s => s.Steps)
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId, cancellationToken);

        if (saga is null || saga.IsFinished)
        {
            return;
        }

        saga.Record("PaymentFailed", @event.Reason);

        // --- COMPENSATION ---------------------------------------------------------------------
        //
        // Conditional on the saga's OWN record of what happened, not on an assumption. This is the
        // whole reason the saga tracks StockReserved: releasing stock that was never reserved inflates
        // the available count, and nobody notices until a stock take disagrees with the system.
        if (saga.StockReserved)
        {
            saga.Record("CompensatingReleaseStock", "Releasing reserved stock.");

            outbox.Add(new ReleaseStockCommand
            {
                OrderId = saga.OrderId,
                OrderNumber = saga.OrderNumber,
            });

            saga.MarkStockReleased();
        }
        else
        {
            saga.Record("NoCompensationNeeded", "No stock was reserved.");
        }

        outbox.Add(new AdvanceOrderCommand
        {
            OrderId = saga.OrderId,
            Transition = AdvanceOrderCommand.Transitions.Cancel,
            CancellationReason = "PaymentDeclined",
        });

        saga.MarkFailed(@event.Reason);
        saga.Record("SagaCompensated", "Order cancelled and stock returned.");

        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Saga {OrderNumber} compensated: {Reason}. Stock released: {Released}.",
            saga.OrderNumber,
            @event.Reason,
            saga.StockReserved);
    }
}
