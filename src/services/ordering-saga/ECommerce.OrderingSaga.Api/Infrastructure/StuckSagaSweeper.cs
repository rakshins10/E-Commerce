using ECommerce.Contracts.Inventory;
using ECommerce.Contracts.Saga;
using ECommerce.OrderingSaga.Api.Model;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.OrderingSaga.Api.Infrastructure;

/// <summary>How the sweeper decides a saga has been abandoned.</summary>
public sealed class SagaSweepOptions
{
    public const string SectionName = "Saga:Sweep";

    /// <summary>Seconds between sweeps.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// A saga still unfinished after this many seconds is compensated.
    /// </summary>
    /// <remarks>
    /// The default is fifteen minutes against a happy path of a few seconds - three orders of magnitude
    /// of margin, because the cost of sweeping too early is real: cancelling an order whose payment was
    /// merely slow. The late-success path exists for exactly that case (see
    /// <c>PaymentSucceededHandler</c>), but a refund is a worse customer experience than a slow
    /// confirmation, so the threshold errs long. Dev compose sets it much lower so the behaviour can be
    /// demonstrated without a fifteen-minute wait.
    /// </remarks>
    public int StuckAfterSeconds { get; set; } = 900;
}

/// <summary>
/// Finds sagas that have stopped moving and compensates them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the timeout story the saga was missing.</b> Every step of the checkout saga waits for an
/// answer that arrives over a message broker - and if a service dies mid-conversation, nothing arrives,
/// ever. Without a sweeper the order sits in <c>AwaitingPayment</c> forever, the customer's stock stays
/// reserved forever, and the only way anyone notices is a support ticket. A saga without timeouts is not
/// resilient; it is merely optimistic.
/// </para>
/// <para>
/// <b>Compensation here is the same compensation</b> as <c>PaymentFailedHandler</c>'s, deliberately:
/// release the stock only if this saga's own record says it was reserved, cancel the order, mark the saga
/// compensated - all through the outbox in one transaction. A timeout is not a special kind of failure;
/// it is a failure whose notification never came, so it takes the same exit.
/// </para>
/// <para>
/// <b>The race this creates is handled, not ignored.</b> Payment may succeed AFTER the sweep decided to
/// cancel - money taken for an order that no longer exists. <c>PaymentSucceededHandler</c> detects a
/// success arriving on a compensated saga and sends <c>RefundPaymentCommand</c>. That command existed
/// from the start precisely so that adding a timeout would have a complete story rather than an
/// aspirational one.
/// </para>
/// <para>
/// <b>Safe with multiple replicas, boringly.</b> Two replicas can sweep the same saga; both write the
/// same terminal state, and the second <c>SaveChanges</c> either no-ops on identical values or the
/// duplicate commands are absorbed by the consumers' idempotency (release is clamped at zero, cancel of
/// a cancelled order is ignored). Correctness by idempotent effects rather than by leader election,
/// which is one fewer thing to run.
/// </para>
/// </remarks>
public sealed class StuckSagaSweeper(
    IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Options.IOptions<SagaSweepOptions> options,
    ILogger<StuckSagaSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SagaSweepOptions sweep = options.Value;

        logger.LogInformation(
            "Stuck-saga sweeper running: every {Interval}s, compensating sagas older than {StuckAfter}s.",
            sweep.IntervalSeconds,
            sweep.StuckAfterSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(sweep.IntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SweepOnceAsync(sweep, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The sweeper is itself a resilience mechanism, so it must not die of one bad pass -
                // the next tick retries everything still stuck.
                logger.LogError(ex, "Stuck-saga sweep failed; will retry next interval.");
            }
        }
    }

    private async Task SweepOnceAsync(SagaSweepOptions sweep, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        SagaDbContext db = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        IOutboxWriter outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddSeconds(-sweep.StuckAfterSeconds);

        List<OrderSaga> stuck = await db.Sagas
            .Include(saga => saga.Steps)
            .Where(saga =>
                saga.State != SagaState.Completed
                && saga.State != SagaState.Compensated
                && saga.StartedAt < cutoff)
            .OrderBy(saga => saga.StartedAt)
            // Bounded, like the outbox batch: a backlog after long downtime is drained over a few
            // sweeps rather than in one enormous transaction.
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stuck.Count == 0)
        {
            return;
        }

        foreach (OrderSaga saga in stuck)
        {
            double age = Math.Round((DateTimeOffset.UtcNow - saga.StartedAt).TotalSeconds);

            saga.Record(
                "TimedOut",
                $"No progress for {age}s (threshold {sweep.StuckAfterSeconds}s); compensating.");

            // Identical to PaymentFailedHandler, and conditional on the saga's OWN record for the same
            // reason: releasing stock that was never reserved corrupts the available count upward.
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
                CancellationReason = "TimedOut",
            });

            saga.MarkFailed($"Timed out after {age}s in state that never progressed.");
            saga.Record("SagaCompensated", "Order cancelled and stock returned.");

            logger.LogWarning(
                "Saga {OrderNumber} swept: stuck for {Age}s. Order cancelled, stock released: {Released}.",
                saga.OrderNumber,
                age,
                saga.StockReserved);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogWarning("Swept {Count} stuck saga(s).", stuck.Count);
    }
}
