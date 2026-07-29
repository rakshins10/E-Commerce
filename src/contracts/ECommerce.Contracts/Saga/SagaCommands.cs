using ECommerce.EventBus;

namespace ECommerce.Contracts.Saga;

/// <summary>
/// Commands the saga sends to participants.
/// </summary>
/// <remarks>
/// <para>
/// <b>Commands are not events, and the difference is the whole design.</b>
/// </para>
/// <list type="table">
///   <listheader><term>Event</term><description>Command</description></listheader>
///   <item>
///     <term>"This happened" — past tense</term>
///     <description>"Do this" — imperative</description>
///   </item>
///   <item>
///     <term>Broadcast; zero or many listeners</term>
///     <description>Addressed to exactly one service</description>
///   </item>
///   <item>
///     <term>The publisher does not know or care who reacts</term>
///     <description>The sender expects a specific outcome and waits for it</description>
///   </item>
///   <item>
///     <term>Cannot be rejected — it already happened</term>
///     <description>Can fail, and the failure is meaningful</description>
///   </item>
/// </list>
/// <para>
/// <b>Why this repo uses ORCHESTRATION rather than choreography.</b> In choreography each service listens
/// for the previous service's event and reacts: Inventory hears <c>OrderSubmitted</c>, Payment hears
/// <c>StockReserved</c>, and so on. It needs no coordinator and is genuinely elegant for two or three
/// steps.
/// </para>
/// <para>
/// It stops being elegant the moment you ask <b>"where is order 12345 stuck?"</b>. The answer is
/// distributed across four services' logs and exists nowhere as a single fact. Compensation is worse: to
/// undo a failed payment you need to know that stock was reserved, which is knowledge no single service
/// holds.
/// </para>
/// <para>
/// An orchestrator makes the process an <b>explicit, queryable thing</b>. One row per order says which
/// step it is on, when each step completed, and what compensation ran. The cost is a service that knows
/// about all the others — real coupling, accepted deliberately, and confined to one place rather than
/// smeared across four.
/// </para>
/// </remarks>
public sealed record ReserveStockCommand : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required IReadOnlyList<ReserveStockLine> Lines { get; init; }
}

public sealed record ReserveStockLine
{
    public required string Sku { get; init; }

    public required int Quantity { get; init; }
}

/// <summary>
/// Put reserved stock back. A <b>compensating action</b>.
/// </summary>
/// <remarks>
/// <para>
/// The defining property of a saga: there is no rollback, because each step committed in a different
/// database. Undoing a step means performing a <i>new</i> action that has the opposite effect.
/// </para>
/// <para>
/// A compensating action is not the same as a rollback, and pretending otherwise causes bugs. It must be:
/// </para>
/// <list type="bullet">
///   <item><description><b>Idempotent</b> — it will be retried.</description></item>
///   <item><description><b>Safe to run when the step never happened</b> — releasing stock that was never
///   reserved inflates the available count, which is a corruption in the opposite direction.</description></item>
///   <item><description><b>Semantically honest</b> — the released stock may have been sold to someone
///   else in the meantime. The world moved on; you cannot rewind it.</description></item>
/// </list>
/// </remarks>
public sealed record ReleaseStockCommand : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }
}

/// <summary>Take payment for an order.</summary>
public sealed record RequestPaymentCommand : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string BuyerId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}

/// <summary>Refund a captured payment. A <b>compensating action</b>.</summary>
/// <remarks>
/// Reserved for the case where a later step fails after payment succeeded. Not used in the current flow,
/// because payment is the last step that can fail — but declared, because the shape of the saga is what
/// makes adding a step afterwards safe.
/// </remarks>
public sealed record RefundPaymentCommand : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string PaymentReference { get; init; }
}

/// <summary>Move an order to a new state. Sent by the saga to Ordering.</summary>
/// <remarks>
/// One command with a discriminator rather than four separate ones. The saga's job is to sequence
/// transitions, and the aggregate already refuses any that are illegal — so the alternative would be four
/// nearly-identical records and four handlers doing the same dispatch.
/// </remarks>
public sealed record AdvanceOrderCommand : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string Transition { get; init; }

    /// <summary>Present for <c>Paid</c>; null otherwise.</summary>
    public string? PaymentReference { get; init; }

    /// <summary>Present for <c>Cancel</c>; null otherwise.</summary>
    public string? CancellationReason { get; init; }

    public static class Transitions
    {
        public const string ConfirmStock = "ConfirmStock";
        public const string MarkPaid = "MarkPaid";
        public const string Cancel = "Cancel";
    }
}
