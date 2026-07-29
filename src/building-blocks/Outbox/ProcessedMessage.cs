namespace ECommerce.Outbox;

/// <summary>
/// A record that this service has already handled a given message. The receiving half of the outbox
/// pattern, sometimes called the <i>inbox</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is necessary.</b> Delivery is at-least-once, so the same event <i>will</i> arrive twice:
/// the publisher can crash after the broker accepts a message but before it records the success; a
/// consumer can crash after doing the work but before acknowledging; a network partition can cause a
/// redelivery of something already handled. None of these are exotic — they are ordinary Tuesday.
/// </para>
/// <para>
/// Handling <c>OrderPaid</c> twice means two confirmation emails. Handling <c>StockReserved</c> twice
/// means the warehouse count is wrong. The event bus cannot prevent this for you: exactly-once delivery
/// across a network is not achievable, which is why the responsibility sits with the consumer.
/// </para>
/// <para>
/// <b>How this works.</b> Before handling, the consumer checks whether it has seen the message id.
/// After handling, it records the id <b>in the same transaction as the work itself</b>. That last part
/// is the whole trick — recording it separately reintroduces the exact dual-write problem the outbox
/// exists to solve, one layer down.
/// </para>
/// <para>
/// <b>Idempotent handlers are still better where they are achievable.</b> An operation that is naturally
/// idempotent — <c>Order.MarkAsPaid</c> returning quietly when the order is already paid — needs no
/// bookkeeping at all and cannot get the bookkeeping wrong. This table is for the operations that are
/// not, such as sending an email or incrementing a counter.
/// </para>
/// </remarks>
public sealed class ProcessedMessage
{
    private ProcessedMessage()
    {
        // EF Core.
    }

    public ProcessedMessage(Guid messageId, string eventName, string consumer)
    {
        MessageId = messageId;
        EventName = eventName;
        Consumer = consumer;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The id assigned when the event was created, and preserved end to end.</summary>
    public Guid MessageId { get; private set; }

    public string EventName { get; private set; } = string.Empty;

    /// <summary>
    /// Which handler processed it.
    /// </summary>
    /// <remarks>
    /// Part of the key, because one service can legitimately have several handlers for the same event —
    /// one updating a read model, another sending a notification. Deduplicating on the message id alone
    /// would let whichever handler ran first silently suppress the others.
    /// </remarks>
    public string Consumer { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }
}
