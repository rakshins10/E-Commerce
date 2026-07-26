using ECommerce.Notification.Api.Model;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Notification.Api.Infrastructure;

/// <summary>
/// Notification's database.
/// </summary>
/// <remarks>
/// It implements IOutboxContext even though this service publishes nothing, because it needs the
/// processed_messages table - the receiving half of the pattern. Sending an email is the one operation
/// here that cannot be made naturally idempotent, so deduplication has to be explicit.
/// </remarks>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options), IOutboxContext
{
    // Fully qualified: the entity is called Notification and so is a segment of this project's
    // namespace, so the short name resolves to the namespace and fails to compile.
    public DbSet<Model.Notification> Notifications => Set<Model.Notification>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Model.Notification>(notification =>
        {
            notification.ToTable("notifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
            notification.Property(n => n.OrderId).HasColumnName("order_id").IsRequired();
            notification.Property(n => n.OrderNumber).HasColumnName("order_number").HasMaxLength(30).IsRequired();
            notification.Property(n => n.BuyerId).HasColumnName("buyer_id").HasMaxLength(100).IsRequired();
            notification.Property(n => n.Channel).HasColumnName("channel").HasMaxLength(20).IsRequired();
            notification.Property(n => n.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
            notification.Property(n => n.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
            notification.Property(n => n.SentAt).HasColumnName("sent_at").IsRequired();

            // "What have we sent this customer about this order?" is the only query support ever runs.
            notification.HasIndex(n => new { n.BuyerId, n.OrderId })
                .HasDatabaseName("ix_notifications_buyer_order");
        });

        modelBuilder.ApplyOutboxModel();
    }
}
