using ECommerce.EventBus;

namespace ECommerce.Contracts.Payment;

/// <summary>Events published by the Payment service.</summary>
public sealed record PaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    /// <summary>The provider's reference, so a later refund has something to quote.</summary>
    public required string PaymentReference { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}

/// <summary>
/// A payment was declined.
/// </summary>
/// <remarks>
/// <see cref="Reason"/> is a string, not an enum, so a new decline reason does not break a consumer that
/// has not been redeployed. An unknown string falls into a default branch; an unknown enum value is a
/// deserialisation failure. See ADR-0019.
/// </remarks>
public sealed record PaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string Reason { get; init; }
}

/// <summary>A captured payment has been refunded. The result of a compensating action.</summary>
public sealed record PaymentRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string PaymentReference { get; init; }
}
