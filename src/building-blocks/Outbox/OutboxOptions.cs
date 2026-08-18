namespace ECommerce.Outbox;

/// <summary>
/// How the outbox publisher behaves. Bound from the <c>Outbox</c> configuration section.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// How long to wait between passes when there is nothing to publish.
    /// </summary>
    /// <remarks>
    /// This is the floor on how stale a consumer's view can be, so it is a latency budget rather than a
    /// tuning knob: one second means an order confirmation email can be a second behind the order.
    /// Lower costs a query per interval against an index that is nearly always empty - cheap, but not
    /// free, and it multiplies by the number of replicas.
    /// </remarks>
    public int PollingIntervalMs { get; set; } = 1_000;

    /// <summary>How many messages to publish per pass.</summary>
    /// <remarks>
    /// Bounded so a backlog is drained steadily rather than in one enormous transaction that holds
    /// connections and delays everything else.
    /// </remarks>
    public int BatchSize { get; set; } = 50;

    /// <summary>How long to wait after startup before the first pass.</summary>
    /// <remarks>Keeps the publisher out of the way of migrations and warm-up on a cold start.</remarks>
    public int StartupDelayMs { get; set; } = 5_000;

    /// <summary>
    /// After this many failed publishes a message is <b>parked</b>: left in the table, no longer retried.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A retry budget, and the honest end of at-least-once delivery. Without one, a message that can
    /// never publish - a payload that stopped deserialising after a bad deploy, say - is retried every
    /// second forever. Nothing errors, nothing pages; the only symptom is an <c>attempts</c> counter
    /// quietly climbing, which is precisely the failure mode the outbox was built to avoid.
    /// </para>
    /// <para>
    /// Parking is a WHERE clause, not a schema change: the fetch skips rows at the budget, so the row
    /// stays visible in the table with its <c>last_error</c> for diagnosis. Re-queueing after a fix is
    /// <c>UPDATE outbox_messages SET attempts = 0 WHERE published_at IS NULL</c> - see the runbook.
    /// </para>
    /// <para>
    /// 25 attempts at the default one-second interval is ~25 seconds of transient trouble absorbed,
    /// which rides out a broker restart but not a misconfiguration.
    /// </para>
    /// </remarks>
    public int MaxAttempts { get; set; } = 25;
}
