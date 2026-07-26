using ECommerce.Common.Exceptions;
using ECommerce.Common.SeedWork;
using ECommerce.Contracts.Ordering;
using ECommerce.Ordering.Domain.Orders;
using ECommerce.Outbox;

using Microsoft.Extensions.Logging;

namespace ECommerce.Ordering.Application.Orders;

/// <summary>
/// The remaining commands that change an order.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these follows the same four steps: load the aggregate, call the method, translate the
/// domain event into the published contract, commit both together. The repetition is the point — when
/// every state change looks identical, a reviewer can see at a glance that none of them writes to the
/// database without also writing the outbox.
/// </para>
/// <para>
/// <b>No MediatR.</b> Plain classes, injected and called directly. MediatR earns its place when you want
/// pipeline behaviours — validation, logging, transactions — applied uniformly across dozens of
/// handlers. With a handful, it adds a layer of indirection that makes "find the code that runs when I
/// call this" a multi-step search, for no benefit. This is a deliberate divergence from
/// eShopOnContainers, recorded in docs/adr/0012.
/// </para>
/// </remarks>
public sealed class CancelOrderHandler(
    IRepository<Order, Guid> orders,
    IOutboxWriter outbox,
    IOrderingUnitOfWork unitOfWork,
    ILogger<CancelOrderHandler> logger)
{
    public async Task<Order> HandleAsync(
        Guid orderId,
        string requestedByBuyerId,
        bool isStaff,
        CancellationToken cancellationToken = default)
    {
        Order order = await orders.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false)
                      ?? throw new OrderNotFoundException(orderId);

        // Resource-based authorization: holding order:cancel is not enough on its own, because the
        // question "may you cancel AN order" and "may you cancel THIS order" are different. Staff may
        // cancel anyone's; a customer may cancel only their own. The permission is checked on the route;
        // ownership can only be checked here, once the resource is loaded.
        if (!isStaff && !string.Equals(order.BuyerId, requestedByBuyerId, StringComparison.Ordinal))
        {
            // 404, not 403. Telling an attacker "this order exists but is not yours" confirms the id is
            // real, which is a small information leak that makes enumeration worthwhile.
            throw new OrderNotFoundException(orderId);
        }

        OrderCancellationReason reason = isStaff
            ? OrderCancellationReason.CancelledByStaff
            : OrderCancellationReason.RequestedByCustomer;

        order.Cancel(reason);

        outbox.Add(new OrderCancelledIntegrationEvent
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            BuyerId = order.BuyerId,
            Reason = reason.ToString(),
            // Read from the domain event rather than the aggregate: Cancel() has already reset the flag,
            // so the aggregate no longer knows there was stock. The event is what remembers.
            StockWasReserved = order.DomainEvents
                .OfType<OrderCancelledDomainEvent>()
                .Select(e => e.StockWasReserved)
                .FirstOrDefault(),
        });

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        order.ClearDomainEvents();

        logger.LogInformation(
            "Order {OrderNumber} cancelled ({Reason}).", order.OrderNumber, reason);

        return order;
    }

    /// <summary>
    /// Cancels on the saga's instruction, with the saga's reason.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="HandleAsync"/> because there is no requester to check ownership against -
    /// the saga is acting on the system's behalf, not a person's. Giving it a distinct entry point means
    /// the ownership check on the customer-facing path cannot be accidentally bypassed by passing a flag.
    ///
    /// The reason is the saga's (PaymentDeclined, OutOfStock) rather than one of the human ones, which is
    /// exactly what makes the customer's order page able to say WHY rather than just "cancelled".
    /// </remarks>
    public async Task<Order> HandleForSagaAsync(
        Guid orderId,
        OrderCancellationReason reason,
        CancellationToken cancellationToken = default)
    {
        Order order = await orders.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false)
                      ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            // Idempotent: a redelivered cancellation must not publish a second event.
            return order;
        }

        order.Cancel(reason);

        outbox.Add(new OrderCancelledIntegrationEvent
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            BuyerId = order.BuyerId,
            Reason = reason.ToString(),
            StockWasReserved = order.DomainEvents
                .OfType<OrderCancelledDomainEvent>()
                .Select(e => e.StockWasReserved)
                .FirstOrDefault(),
        });

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        order.ClearDomainEvents();

        logger.LogInformation(
            "Order {OrderNumber} cancelled by the saga ({Reason}).", order.OrderNumber, reason);

        return order;
    }
}

/// <summary>
/// Advances an order through the fulfilment states.
/// </summary>
/// <remarks>
/// In Phase 6 these are driven by staff endpoints so the flow can be exercised end to end. In Phase 7
/// the same methods are called by the saga in response to payment and inventory events — which is
/// possible only because the rules live in the aggregate rather than in the endpoint.
/// </remarks>
public sealed class AdvanceOrderHandler(
    IRepository<Order, Guid> orders,
    IOutboxWriter outbox,
    IOrderingUnitOfWork unitOfWork,
    ILogger<AdvanceOrderHandler> logger)
{
    public Task<Order> ConfirmStockAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        MutateAsync(
            orderId,
            order => order.ConfirmStock(),
            order => new OrderStockConfirmedIntegrationEvent
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
            },
            cancellationToken);

    public Task<Order> MarkPaidAsync(
        Guid orderId,
        string paymentReference,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            orderId,
            order => order.MarkAsPaid(paymentReference),
            order => new OrderPaidIntegrationEvent
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                BuyerId = order.BuyerId,
                Total = order.Total.Amount,
                Currency = order.Total.Currency,
            },
            cancellationToken);

    public Task<Order> MarkShippedAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        MutateAsync(
            orderId,
            order => order.MarkAsShipped(),
            order => new OrderShippedIntegrationEvent
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                BuyerId = order.BuyerId,
            },
            cancellationToken);

    public Task<Order> MarkDeliveredAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        MutateAsync(
            orderId,
            order => order.MarkAsDelivered(),
            order => new OrderDeliveredIntegrationEvent
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
            },
            cancellationToken);

    /// <summary>Load, mutate, write the outbox, commit. The shape every state change shares.</summary>
    private async Task<Order> MutateAsync(
        Guid orderId,
        Action<Order> mutate,
        Func<Order, EventBus.IntegrationEvent> toIntegrationEvent,
        CancellationToken cancellationToken)
    {
        Order order = await orders.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false)
                      ?? throw new OrderNotFoundException(orderId);

        OrderStatus before = order.Status;

        mutate(order);

        // An idempotent no-op - MarkAsPaid on an already-paid order - must not publish a second event.
        // Detecting it by comparing the status before and after keeps that decision in one place rather
        // than in each of the four callers.
        if (order.Status == before)
        {
            logger.LogDebug(
                "Order {OrderNumber} is already {Status}; nothing to do.", order.OrderNumber, before);

            return order;
        }

        outbox.Add(toIntegrationEvent(order));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        order.ClearDomainEvents();

        logger.LogInformation(
            "Order {OrderNumber}: {Before} to {After}.", order.OrderNumber, before, order.Status);

        return order;
    }
}

/// <summary>Thrown when an order id does not resolve, or resolves to someone else's order.</summary>
/// <remarks>
/// The same exception for both cases, on purpose. A distinct "not yours" error would confirm to an
/// attacker that the id is real.
/// </remarks>
public sealed class OrderNotFoundException(Guid orderId)
    : DomainException($"Order {orderId} was not found.")
{
    public Guid OrderId { get; } = orderId;
}
