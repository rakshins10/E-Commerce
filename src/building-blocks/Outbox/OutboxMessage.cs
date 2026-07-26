namespace ECommerce.Outbox;

/// <summary>
/// An integration event queued for publication, stored in the same database as the data that produced it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this solves.</b> A service that changes its database and then publishes a message is
/// doing two things that can fail independently:
/// </para>
/// <code>
///   await db.SaveChangesAsync();      // committed
///   await bus.PublishAsync(@event);   // ...and the process dies here
/// </code>
/// <para>
/// The order exists and nobody was told. Stock is never reserved, no payment is taken, no email is sent,
/// and nothing in the system is aware anything is wrong. Swap the two lines and you get the opposite,
/// worse bug: other services react to an order that was never saved.
/// </para>
/// <para>
/// <b>Why "just use a distributed transaction" is not the answer.</b> A two-phase commit across
/// PostgreSQL and RabbitMQ needs both to support XA, holds locks for the duration of a network round
/// trip, and blocks indefinitely if the coordinator dies at the wrong moment. It converts an
/// availability problem into a distributed-locking problem, which is why microservice architectures
/// almost universally do not use it.
/// </para>
/// <para>
/// <b>The outbox.</b> Write the event into <i>this table</i>, in the <i>same transaction</i> as the
/// order. One database, one commit — either both happen or neither does, with no coordination protocol
/// required. A background publisher then reads unpublished rows and sends them to the broker.
/// </para>
/// <code>
///   BEGIN
///     INSERT INTO orders ...
///     INSERT INTO outbox_messages ...    -- same transaction, atomic together
///   COMMIT
///                                        -- separately, later:
///   publisher: SELECT unpublished -> RabbitMQ -> mark published
/// </code>
/// <para>
/// <b>What you trade for it.</b> Publication becomes asynchronous, so consumers see the event a moment
/// later than the commit — the outbox buys atomicity at the cost of latency, not of correctness. And
/// because the publisher can crash between sending and recording success, delivery is
/// <b>at-least-once</b>: the same event will sometimes be published twice. That is not a flaw to be
/// engineered away — exactly-once delivery is not achievable across a network — so every consumer must
/// be idempotent. See <see cref="ProcessedMessage"/> for the receiving half.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        // EF Core.
    }

    public OutboxMessage(Guid id, string eventName, string payload, string? correlationId, string? traceParent)
    {
        Id = id;
        EventName = eventName;
        Payload = payload;
        CorrelationId = correlationId;
        TraceParent = traceParent;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The integration event's own id, reused as the row's primary key.
    /// </summary>
    /// <remarks>
    /// Deliberately not a fresh identifier. The same value travels to the broker as the message id, so a
    /// consumer that has already handled it can recognise the duplicate. Generating a separate key here
    /// would break that chain and leave consumers with nothing stable to deduplicate on.
    /// </remarks>
    public Guid Id { get; private set; }

    /// <summary>The event type name, used to route and to deserialise on the far side.</summary>
    public string EventName { get; private set; } = string.Empty;

    /// <summary>The serialised event.</summary>
    /// <remarks>
    /// Stored as JSON text rather than a binary format on purpose. When a message fails in production
    /// the first thing anybody does is look at it, and <c>SELECT payload FROM outbox_messages</c> being
    /// readable is worth more than the bytes it saves.
    /// </remarks>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>Carried through so one request can be followed across every service it touches.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// W3C trace context, so the consumer's span joins the producer's trace.
    /// </summary>
    /// <remarks>
    /// Without this the trace stops dead at the publish. You would see the order being created and,
    /// entirely disconnected, some payment activity, with nothing tying them together — which is exactly
    /// the visibility a distributed system needs most.
    /// </remarks>
    public string? TraceParent { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Null until the broker has accepted the message.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>How many publish attempts have been made.</summary>
    public int Attempts { get; private set; }

    /// <summary>The last failure, kept for diagnosis.</summary>
    public string? LastError { get; private set; }

    public void MarkPublished()
    {
        PublishedAt = DateTimeOffset.UtcNow;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;

        // Truncated because a stack trace from a driver can run to kilobytes, and a table of them is a
        // slow way to fill a disk. The full detail is in the logs, correlated by id.
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
