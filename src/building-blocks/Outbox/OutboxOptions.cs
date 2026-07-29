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
    /// Lower costs a query per interval against an index that is nearly always empty — cheap, but not
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
}
