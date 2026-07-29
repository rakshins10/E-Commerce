using ECommerce.Contracts.Payment;
using ECommerce.Contracts.Saga;
using ECommerce.EventBus;
using ECommerce.Outbox;
using ECommerce.Payment.Api.Infrastructure;
using ECommerce.Payment.Api.Model;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Payment.Api.Handlers;

/// <summary>
/// Takes payment for an order.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a simulator, and it is honest about that.</b> There is no card number, no PCI scope and no
/// provider. What it does model faithfully is the part that matters architecturally: an operation that
/// takes time, sometimes fails, must never be performed twice for the same order, and whose failure
/// triggers compensation elsewhere.
/// </para>
/// <para>
/// A real integration would replace <see cref="AuthoriseAsync"/> and change nothing else — the saga, the
/// outbox and the compensation path are all unaffected by where the money actually comes from. That
/// separation is the point of putting it behind an event boundary.
/// </para>
/// </remarks>
public sealed class RequestPaymentHandler(
    PaymentDbContext db,
    IOutboxWriter outbox,
    ILogger<RequestPaymentHandler> logger)
    : IIntegrationEventHandler<RequestPaymentCommand>
{
    /// <summary>
    /// Orders at or above this amount are declined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <b>deterministic</b> rule, not a random failure rate. Random failures make a demo look realistic
    /// and make the test suite flaky — the compensation path would pass or fail depending on the roll of
    /// a die, which is the fastest way to get a suite ignored.
    /// </para>
    /// <para>
    /// A threshold means the failure path can be exercised on demand: put the £5,200 Leather Portfolio in
    /// a basket and watch the saga release the stock and cancel the order. The seed data contains that
    /// product specifically so this is reachable from the UI.
    /// </para>
    /// </remarks>
    public const decimal DeclineThreshold = 5_000m;

    public async Task HandleAsync(
        RequestPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Idempotency, and here it protects real money. Delivery is at-least-once, so this command WILL
        // arrive twice eventually - and charging a customer twice is the single worst bug this system
        // could have. The unique index on order_id backs it up in case two deliveries race.
        PaymentRecord? existing = await db.Payments
            .FirstOrDefaultAsync(p => p.OrderId == command.OrderId, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Payment for {OrderNumber} already {Status}; ignoring duplicate request.",
                command.OrderNumber,
                existing.Status);

            // Deliberately does NOT re-publish the outcome. The saga is idempotent too, but two services
            // each relying on the other to catch duplicates is how a duplicate gets through.
            return;
        }

        bool approved = await AuthoriseAsync(command, cancellationToken);

        if (approved)
        {
            var payment = PaymentRecord.Captured(
                command.OrderId, command.OrderNumber, command.BuyerId, command.Amount, command.Currency);

            db.Payments.Add(payment);

            outbox.Add(new PaymentSucceededIntegrationEvent
            {
                OrderId = command.OrderId,
                OrderNumber = command.OrderNumber,
                PaymentReference = payment.Reference,
                Amount = command.Amount,
                Currency = command.Currency,
            });

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Payment captured for {OrderNumber}: {Amount} {Currency}, reference {Reference}.",
                command.OrderNumber,
                command.Amount,
                command.Currency,
                payment.Reference);

            return;
        }

        string reason = $"Declined: amount exceeds the {DeclineThreshold:0} {command.Currency} limit.";

        // A declined payment is still RECORDED. It is a fact about the order that support will be asked
        // about, and a payment service that keeps no record of its declines cannot answer "why was my
        // card refused?" - which is the most common question it will ever receive.
        db.Payments.Add(PaymentRecord.Declined(
            command.OrderId, command.OrderNumber, command.BuyerId, command.Amount, command.Currency, reason));

        outbox.Add(new PaymentFailedIntegrationEvent
        {
            OrderId = command.OrderId,
            OrderNumber = command.OrderNumber,
            Reason = reason,
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Payment declined for {OrderNumber}: {Reason}", command.OrderNumber, reason);
    }

    /// <summary>
    /// Where a real provider would be called.
    /// </summary>
    /// <remarks>
    /// The delay is not decoration. It makes the asynchronous nature of the saga visible: the order sits
    /// in <c>AwaitingPayment</c> for a moment, which is exactly what happens in production and exactly
    /// what a UI must be able to render without looking broken.
    /// </remarks>
    private static async Task<bool> AuthoriseAsync(
        RequestPaymentCommand command,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);

        return command.Amount < DeclineThreshold;
    }
}

/// <summary>
/// Refunds a captured payment. A compensating action.
/// </summary>
/// <remarks>
/// Not reached by the current flow, because payment is the last step that can fail. It exists because the
/// shape of a saga is what makes adding a step afterwards safe — if a shipping-label step were added
/// tomorrow, this is what would undo the charge, and writing it now means the saga's compensation story
/// is complete rather than aspirational.
/// </remarks>
public sealed class RefundPaymentHandler(
    PaymentDbContext db,
    IOutboxWriter outbox,
    ILogger<RefundPaymentHandler> logger)
    : IIntegrationEventHandler<RefundPaymentCommand>
{
    public async Task HandleAsync(
        RefundPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PaymentRecord? payment = await db.Payments
            .FirstOrDefaultAsync(p => p.OrderId == command.OrderId, cancellationToken);

        if (payment is null)
        {
            logger.LogWarning(
                "Refund requested for {OrderNumber} but no payment exists.", command.OrderNumber);
            return;
        }

        if (payment.Status != PaymentStatus.Captured)
        {
            // Refunding a declined or already-refunded payment would create money. Silently correct
            // rather than an error, because a retried compensation reaching an already-refunded payment
            // is the expected case, not an exceptional one.
            logger.LogDebug(
                "Payment for {OrderNumber} is {Status}; nothing to refund.",
                command.OrderNumber,
                payment.Status);
            return;
        }

        payment.Refund();

        outbox.Add(new PaymentRefundedIntegrationEvent
        {
            OrderId = payment.OrderId,
            OrderNumber = payment.OrderNumber,
            PaymentReference = payment.Reference,
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Refunded {OrderNumber} (compensation).", command.OrderNumber);
    }
}
