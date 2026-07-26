namespace ECommerce.Notification.Api.Model;

/// <summary>
/// A message that would have been sent to a customer.
/// </summary>
/// <remarks>
/// Stored rather than actually emailed. In a real system this row would still exist - it is what lets
/// support answer "did we tell them?" and what a resend is built from. The SMTP call would be an
/// additional step, not a replacement for this.
/// </remarks>
public sealed class Notification
{
    private Notification()
    {
        // EF Core.
    }

    public Notification(Guid orderId, string orderNumber, string buyerId, string subject, string body)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        OrderNumber = orderNumber;
        BuyerId = buyerId;
        Subject = subject;
        Body = body;
        Channel = "Email";
        SentAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Keycloak `sub`, not an email address - the same reasoning as everywhere else.</summary>
    public string BuyerId { get; private set; } = string.Empty;

    public string Channel { get; private set; } = "Email";

    public string Subject { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset SentAt { get; private set; }
}
