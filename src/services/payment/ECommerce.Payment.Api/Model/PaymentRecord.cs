namespace ECommerce.Payment.Api.Model;

/// <summary>
/// What happened when we tried to take money for an order.
/// </summary>
/// <remarks>
/// <b>Declines are recorded, not just successes.</b> "Why was my card refused?" is the most common
/// question this service will ever be asked, and a payment service that keeps no record of its declines
/// cannot answer it. It is also the evidence for a chargeback dispute.
/// </remarks>
public sealed class PaymentRecord
{
    private PaymentRecord()
    {
        // EF Core.
    }

    private PaymentRecord(
        Guid orderId,
        string orderNumber,
        string buyerId,
        decimal amount,
        string currency,
        PaymentStatus status,
        string? failureReason)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        OrderNumber = orderNumber;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = status;
        FailureReason = failureReason;
        CreatedAt = DateTimeOffset.UtcNow;

        // A reference exists even for a decline, so support has something to quote when a customer
        // rings about a payment that did not go through.
        Reference = $"pay_{Id:N}"[..20];
    }

    public static PaymentRecord Captured(
        Guid orderId, string orderNumber, string buyerId, decimal amount, string currency) =>
        new(orderId, orderNumber, buyerId, amount, currency, PaymentStatus.Captured, null);

    public static PaymentRecord Declined(
        Guid orderId, string orderNumber, string buyerId, decimal amount, string currency, string reason) =>
        new(orderId, orderNumber, buyerId, amount, currency, PaymentStatus.Declined, reason);

    public Guid Id { get; private set; }

    /// <summary>Uniquely indexed. The database guarantee that one order is charged at most once.</summary>
    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public string BuyerId { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "GBP";

    public PaymentStatus Status { get; private set; }

    /// <summary>The provider's reference, quoted on refunds and to support.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RefundedAt { get; private set; }

    public void Refund()
    {
        Status = PaymentStatus.Refunded;
        RefundedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Persisted, so values are explicit and never reordered.</summary>
public enum PaymentStatus
{
    Captured = 10,
    Declined = 20,
    Refunded = 30,
}
