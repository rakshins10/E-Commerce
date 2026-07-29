using ECommerce.Contracts.Ordering;
using ECommerce.EventBus;
using ECommerce.Notification.Api.Infrastructure;
using ECommerce.Notification.Api.Model;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Notification.Api.Handlers;

/// <summary>
/// Sends the customer an email when something happens to their order.
/// </summary>
/// <remarks>
/// <para>
/// <b>The clearest example in the repo of why consumers must be idempotent.</b> Sending an email is not
/// naturally idempotent — there is no "send this email unless you already did" operation, and the second
/// one is already in the customer's inbox by the time you notice. Everything else in this system can
/// shrug off a duplicate; this cannot.
/// </para>
/// <para>
/// So this is where <see cref="ECommerce.Outbox.ProcessedMessage"/> earns its place: the message id is
/// recorded <b>in the same transaction</b> as the notification row. Recording it separately would
/// reintroduce the dual-write problem the outbox exists to solve, one layer down.
/// </para>
/// <para>
/// <b>No email is actually sent.</b> Notifications are written to a table and logged. Wiring an SMTP
/// server into a reference repo adds a credential to manage and a thing to break, and changes nothing
/// about the pattern being demonstrated. The row is the evidence the notification would have gone.
/// </para>
/// <para>
/// <b>Marketing consent is not consulted here, deliberately.</b> These are <i>service</i> messages —
/// part of performing the contract the customer entered by buying something — not marketing. Under UK
/// GDPR/PECR they do not require opt-in, and a customer who unsubscribed from adverts still expects a
/// dispatch email. See the User Profile service for the other half of that distinction.
/// </para>
/// </remarks>
public abstract class NotificationHandlerBase(NotificationDbContext db, ILogger logger)
{
    /// <summary>
    /// Records a notification, once.
    /// </summary>
    /// <returns><c>true</c> if it was recorded; <c>false</c> if this message was already handled.</returns>
    protected async Task<bool> SendOnceAsync(
        Guid messageId,
        string eventName,
        Guid orderId,
        string orderNumber,
        string buyerId,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        const string consumer = "notification";

        bool alreadyHandled = await db.ProcessedMessages
            .AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);

        if (alreadyHandled)
        {
            logger.LogDebug(
                "Message {MessageId} already handled for {OrderNumber}; not sending again.",
                messageId,
                orderNumber);

            return false;
        }

        // Fully qualified: the entity name collides with a segment of this project's namespace.
        db.Notifications.Add(new Model.Notification(orderId, orderNumber, buyerId, subject, body));

        // The SAME transaction. This is the entire point - if the two were separate, a crash between
        // them would either send twice or record a send that never happened.
        db.ProcessedMessages.Add(new ECommerce.Outbox.ProcessedMessage(messageId, eventName, consumer));

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Notification queued for {OrderNumber}: {Subject}", orderNumber, subject);

        return true;
    }
}

/// <summary>Order confirmation.</summary>
public sealed class OrderSubmittedNotificationHandler(
    NotificationDbContext db,
    ILogger<OrderSubmittedNotificationHandler> logger)
    : NotificationHandlerBase(db, logger), IIntegrationEventHandler<OrderSubmittedIntegrationEvent>
{
    public Task HandleAsync(
        OrderSubmittedIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return SendOnceAsync(
            @event.Id,
            @event.EventName,
            @event.OrderId,
            @event.OrderNumber,
            @event.BuyerId,
            $"We have received order {@event.OrderNumber}",
            $"Thank you. Your order {@event.OrderNumber} for {@event.Total} {@event.Currency} "
            + "has been received and we are preparing it now.",
            cancellationToken);
    }
}

/// <summary>Payment receipt.</summary>
public sealed class OrderPaidNotificationHandler(
    NotificationDbContext db,
    ILogger<OrderPaidNotificationHandler> logger)
    : NotificationHandlerBase(db, logger), IIntegrationEventHandler<OrderPaidIntegrationEvent>
{
    public Task HandleAsync(
        OrderPaidIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return SendOnceAsync(
            @event.Id,
            @event.EventName,
            @event.OrderId,
            @event.OrderNumber,
            @event.BuyerId,
            $"Payment received for order {@event.OrderNumber}",
            $"We have taken {@event.Total} {@event.Currency} for order {@event.OrderNumber}.",
            cancellationToken);
    }
}

/// <summary>Dispatch notice.</summary>
public sealed class OrderShippedNotificationHandler(
    NotificationDbContext db,
    ILogger<OrderShippedNotificationHandler> logger)
    : NotificationHandlerBase(db, logger), IIntegrationEventHandler<OrderShippedIntegrationEvent>
{
    public Task HandleAsync(
        OrderShippedIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return SendOnceAsync(
            @event.Id,
            @event.EventName,
            @event.OrderId,
            @event.OrderNumber,
            @event.BuyerId,
            $"Order {@event.OrderNumber} is on its way",
            $"Your order {@event.OrderNumber} has left our warehouse.",
            cancellationToken);
    }
}

/// <summary>
/// Cancellation notice.
/// </summary>
/// <remarks>
/// The wording depends on <b>why</b>, which is exactly why the reason is part of the event rather than
/// something a consumer has to go and ask for. "You cancelled this order" and "your payment was declined"
/// need completely different messages, and the second needs to tell the customer what to do next.
/// </remarks>
public sealed class OrderCancelledNotificationHandler(
    NotificationDbContext db,
    ILogger<OrderCancelledNotificationHandler> logger)
    : NotificationHandlerBase(db, logger), IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public Task HandleAsync(
        OrderCancelledIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        string body = @event.Reason switch
        {
            "PaymentDeclined" =>
                $"We could not take payment for order {@event.OrderNumber}, so it has been cancelled. "
                + "Nothing has been charged. Please check your payment details and try again.",

            "OutOfStock" =>
                $"Unfortunately an item in order {@event.OrderNumber} sold out before we could reserve it, "
                + "so the order has been cancelled. Nothing has been charged.",

            "RequestedByCustomer" =>
                $"Your order {@event.OrderNumber} has been cancelled as you asked.",

            // A reason this build has never heard of. A generic message is far better than a crash or a
            // blank email - and the string-not-enum contract is what makes reaching this branch possible
            // instead of failing to deserialise. See ADR-0019.
            _ => $"Your order {@event.OrderNumber} has been cancelled.",
        };

        return SendOnceAsync(
            @event.Id,
            @event.EventName,
            @event.OrderId,
            @event.OrderNumber,
            @event.BuyerId,
            $"Order {@event.OrderNumber} has been cancelled",
            body,
            cancellationToken);
    }
}
