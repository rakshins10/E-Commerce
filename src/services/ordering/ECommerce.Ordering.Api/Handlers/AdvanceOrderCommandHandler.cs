using ECommerce.Contracts.Saga;
using ECommerce.EventBus;
using ECommerce.Ordering.Application.Orders;
using ECommerce.Ordering.Domain.Orders;

namespace ECommerce.Ordering.Api.Handlers;

/// <summary>
/// Applies a state transition the saga has decided on.
/// </summary>
/// <remarks>
/// <para>
/// <b>The saga decides <i>when</i>; the aggregate decides <i>whether</i>.</b> That division is what keeps
/// this safe. The saga sequences steps and knows nothing about what makes a transition legal — it cannot
/// talk the order into being paid before its stock is confirmed, because
/// <see cref="Order.MarkAsPaid"/> refuses.
/// </para>
/// <para>
/// The alternative, an orchestrator that sets the status directly, would put the rules in two places and
/// let the saga produce states the aggregate would never allow. Sagas are hard enough without the
/// participants being unable to defend themselves.
/// </para>
/// <para>
/// Idempotency comes from the aggregate too: <c>MarkAsPaid</c> on an already-paid order returns quietly,
/// so a redelivered command is a no-op rather than a duplicate event. Nothing here needs a
/// <c>ProcessedMessage</c> row, because the operations are naturally idempotent — which is always
/// preferable to bookkeeping that can itself be got wrong.
/// </para>
/// </remarks>
public sealed class AdvanceOrderCommandHandler(
    AdvanceOrderHandler orders,
    CancelOrderHandler cancellations,
    ILogger<AdvanceOrderCommandHandler> logger)
    : IIntegrationEventHandler<AdvanceOrderCommand>
{
    public async Task HandleAsync(
        AdvanceOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            switch (command.Transition)
            {
                case AdvanceOrderCommand.Transitions.ConfirmStock:
                    await orders.ConfirmStockAsync(command.OrderId, cancellationToken);
                    break;

                case AdvanceOrderCommand.Transitions.MarkPaid:
                    await orders.MarkPaidAsync(
                        command.OrderId,
                        command.PaymentReference ?? "unknown",
                        cancellationToken);
                    break;

                case AdvanceOrderCommand.Transitions.Cancel:
                    await cancellations.HandleForSagaAsync(
                        command.OrderId,
                        ParseReason(command.CancellationReason),
                        cancellationToken);
                    break;

                default:
                    // Unknown transitions are logged and dropped, not dead-lettered. During a rolling
                    // deploy a newer saga can send a transition this build has never heard of;
                    // retrying it forever would achieve nothing and fill the queue.
                    logger.LogError(
                        "Unknown transition '{Transition}' for order {OrderId}.",
                        command.Transition,
                        command.OrderId);
                    break;
            }
        }
        catch (OrderNotFoundException)
        {
            // The order does not exist. Nothing to retry - redelivering will not make it appear - so
            // this is logged and swallowed rather than dead-lettered.
            logger.LogError(
                "Saga sent {Transition} for order {OrderId}, which does not exist.",
                command.Transition,
                command.OrderId);
        }
        catch (Common.Exceptions.DomainException ex)
        {
            // The aggregate refused. Usually a duplicate delivery arriving after the transition already
            // happened, which is expected - but it can also be a genuine saga bug, so it is logged at
            // warning with enough detail to tell the two apart.
            logger.LogWarning(
                ex,
                "Order {OrderId} refused transition {Transition}.",
                command.OrderId,
                command.Transition);
        }
    }

    /// <remarks>
    /// The reason travels as a string so that a new one does not break a consumer that has not been
    /// redeployed. An unrecognised value falls back rather than throwing — see ADR-0019.
    /// </remarks>
    private static OrderCancellationReason ParseReason(string? reason) =>
        Enum.TryParse(reason, out OrderCancellationReason parsed)
            ? parsed
            : OrderCancellationReason.CancelledByStaff;
}
