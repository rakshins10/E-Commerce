using System.ComponentModel.DataAnnotations;

namespace ECommerce.EventBus.RabbitMQ;

/// <summary>
/// Configuration for the RabbitMQ transport. Bound from the <c>EventBus</c> configuration section and validated
/// at startup.
/// </summary>
/// <remarks>
/// Validated with <c>ValidateOnStart()</c> so a missing host name fails the moment the process boots, with a
/// message naming the property — rather than at the first publish attempt, minutes later, inside a background
/// service where the failure is easy to miss. Fail fast, and fail where someone is looking.
/// </remarks>
public sealed class RabbitMqOptions
{
    public const string SectionName = "EventBus";

    [Required(AllowEmptyStrings = false)]
    public string HostName { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    [Required(AllowEmptyStrings = false)]
    public string UserName { get; set; } = "guest";

    [Required(AllowEmptyStrings = false)]
    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// The single durable topic exchange every integration event is published to.
    /// </summary>
    /// <remarks>
    /// One exchange, routed by event name, rather than an exchange per event: adding an event type then needs no
    /// infrastructure change, and a subscriber can bind a wildcard pattern (<c>Order.*</c>) if it ever wants a
    /// family of events.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string ExchangeName { get; set; } = "ecommerce.events";

    /// <summary>
    /// This service's name, used to prefix its queues — e.g. <c>inventory.OrderStartedIntegrationEvent</c>.
    /// </summary>
    /// <remarks>
    /// <b>Queue per service per event</b> is the reason each subscriber gets its own copy of a message and its
    /// own failure handling. A poison message in Notification must not stall Inventory. A single shared queue
    /// would make them compete for messages instead — which is the right model <i>within</i> a service (see
    /// competing consumers below) and precisely wrong <i>between</i> services.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string SubscriptionClientName { get; set; } = string.Empty;

    /// <summary>Attempts to establish the initial connection before giving up.</summary>
    /// <remarks>
    /// Needed because in Docker Compose a service frequently starts before the broker is accepting connections.
    /// Retried with exponential backoff and jitter rather than a fixed sleep.
    /// </remarks>
    [Range(1, 20)]
    public int ConnectionRetryCount { get; set; } = 8;

    /// <summary>
    /// Unacknowledged messages a single consumer will hold at once.
    /// </summary>
    /// <remarks>
    /// The single most consequential tuning knob here. Unset (0 = unlimited), RabbitMQ pushes the entire queue to
    /// the first consumer that connects, which both exhausts its memory and defeats load balancing — the other
    /// instances sit idle with nothing left to take. A small prefetch keeps work spread across instances; too
    /// small and throughput suffers on network round trips.
    /// </remarks>
    [Range(1, 1000)]
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Delivery attempts before a message is dead-lettered.
    /// </summary>
    /// <remarks>
    /// Without a cap, a message that <i>can never</i> succeed — malformed payload, a bug in the handler — is
    /// redelivered forever, saturating the consumer and blocking everything behind it. That is a
    /// denial-of-service against yourself. Capping and dead-lettering makes the failure visible and bounded.
    /// </remarks>
    [Range(1, 20)]
    public int MaxDeliveryAttempts { get; set; } = 5;
}
