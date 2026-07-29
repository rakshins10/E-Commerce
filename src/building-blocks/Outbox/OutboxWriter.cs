using System.Diagnostics;
using System.Text.Json;

using ECommerce.EventBus;
using ECommerce.Observability;

namespace ECommerce.Outbox;

/// <summary>
/// Adds an integration event to the outbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>Note what this does not do: it does not save.</b> The row is added to the change tracker and
/// committed by whoever owns the transaction — the same <c>SaveChangesAsync</c> that writes the order.
/// That is the entire point of the pattern, and a <c>SaveChangesAsync</c> in here would quietly destroy
/// it by creating a second, separate transaction.
/// </para>
/// <para>
/// The correct call site therefore looks like this, with one commit covering both:
/// </para>
/// <code>
///   orders.Add(order);
///   outbox.Add(new OrderSubmittedIntegrationEvent(...));
///   await db.SaveChangesAsync();   // atomic: both rows, or neither
/// </code>
/// </remarks>
public interface IOutboxWriter
{
    /// <summary>Queues an event for publication when the current transaction commits.</summary>
    void Add(IntegrationEvent @event);
}

/// <inheritdoc cref="IOutboxWriter"/>
public sealed class OutboxWriter(IOutboxContext context) : IOutboxWriter
{
    public void Add(IntegrationEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Captured here, at the point the event is created, rather than in the publisher. By the time
        // the publisher runs, the HTTP request that caused this is long finished and its context is
        // gone - so capturing it later would mean capturing nothing.
        string? correlationId = CorrelationId.Current;
        string? traceParent = Activity.Current?.Id;

        var message = new OutboxMessage(
            @event.Id,
            @event.EventName,
            JsonSerializer.Serialize(@event, @event.GetType(), OutboxSerialization.Options),
            correlationId,
            traceParent);

        // Add, not Add-and-save. See the remarks above.
        context.OutboxMessages.Add(message);
    }
}
