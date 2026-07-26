namespace ECommerce.OrderingSaga.Api.Model;

/// <summary>
/// One order's progress through the checkout process.
/// </summary>
/// <remarks>
/// <para>
/// <b>This row is the reason orchestration was chosen.</b> In a choreographed saga the question "where is
/// order 12345 stuck?" has no single answer — it is distributed across four services' logs and exists
/// nowhere as a fact. Here it is a <c>SELECT</c>.
/// </para>
/// <para>
/// It also makes compensation possible. To undo a failed payment you must know whether stock was
/// reserved, and no individual service holds that knowledge. The saga does, because it watched it happen.
/// </para>
/// </remarks>
public sealed class OrderSaga
{
    private OrderSaga()
    {
        // EF Core.
    }

    public OrderSaga(Guid orderId, string orderNumber, string buyerId, decimal amount, string currency)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        State = SagaState.AwaitingStock;
        StartedAt = DateTimeOffset.UtcNow;
        Steps = [];
    }

    /// <summary>
    /// The order id, used as the saga's primary key.
    /// </summary>
    /// <remarks>
    /// Deliberately not a separate identifier. One order has exactly one saga, so a distinct key would be
    /// a second thing to join on and a second thing to get wrong — and using the order id makes the
    /// unique constraint do the deduplication work when <c>OrderSubmitted</c> is delivered twice.
    /// </remarks>
    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public string BuyerId { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "GBP";

    public SagaState State { get; private set; }

    /// <summary>
    /// Whether stock is currently reserved.
    /// </summary>
    /// <remarks>
    /// The single most important field here. It is what stops the saga issuing a
    /// <c>ReleaseStockCommand</c> for a reservation that never happened — which would inflate the
    /// available count, a corruption in the opposite direction from the failure being compensated.
    /// </remarks>
    public bool StockReserved { get; private set; }

    public string? PaymentReference { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// An append-only log of everything that happened, in order.
    /// </summary>
    /// <remarks>
    /// Not merely for debugging — it is what the customer's order timeline and the admin panel render.
    /// A saga that records only its current state can tell you it failed but not what it tried, and
    /// "compensation ran at 14:02" is exactly the fact somebody needs at 14:05.
    /// </remarks>
    public List<SagaStep> Steps { get; private set; } = [];

    /// <summary>Records a step and moves the saga on.</summary>
    public void Record(string step, string detail, SagaState? newState = null)
    {
        Steps.Add(new SagaStep(OrderId, step, detail, Steps.Count));

        if (newState is not null)
        {
            State = newState.Value;
        }
    }

    public void MarkStockReserved() => StockReserved = true;

    public void MarkStockReleased() => StockReserved = false;

    public void MarkPaid(string paymentReference)
    {
        PaymentReference = paymentReference;
        State = SagaState.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        State = SagaState.Compensated;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Whether this saga has already finished, and should ignore further messages.</summary>
    /// <remarks>
    /// The guard that makes at-least-once delivery survivable here. A duplicate
    /// <c>PaymentSucceeded</c> arriving after completion must not re-run the completion steps and send a
    /// second <c>AdvanceOrderCommand</c>.
    /// </remarks>
    public bool IsFinished => State is SagaState.Completed or SagaState.Compensated;
}

/// <summary>Where a saga is. Persisted, so values are explicit and never reordered.</summary>
public enum SagaState
{
    /// <summary>Waiting for Inventory to confirm or reject the reservation.</summary>
    AwaitingStock = 10,

    /// <summary>Stock is reserved; waiting for the payment result.</summary>
    AwaitingPayment = 20,

    /// <summary>Everything succeeded. Terminal.</summary>
    Completed = 30,

    /// <summary>A step failed and compensation has run. Terminal.</summary>
    Compensated = 90,
}

/// <summary>One entry in a saga's log.</summary>
public sealed class SagaStep
{
    private SagaStep()
    {
        // EF Core.
    }

    public SagaStep(Guid orderId, string name, string detail, int sequence)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        Name = name;
        Detail = detail;
        Sequence = sequence;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Detail { get; private set; } = string.Empty;

    /// <summary>
    /// Position in the sequence.
    /// </summary>
    /// <remarks>
    /// Ordering by <c>OccurredAt</c> alone is not reliable: two steps recorded in the same millisecond
    /// come back in an arbitrary order, and a timeline that shows compensation before the failure it
    /// compensates is worse than no timeline.
    /// </remarks>
    public int Sequence { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
