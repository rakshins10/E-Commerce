using ECommerce.Outbox;
using ECommerce.Payment.Api.Model;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Payment.Api.Infrastructure;

/// <summary>Payment's database, including its outbox.</summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : DbContext(options), IOutboxContext
{
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaymentRecord>(payment =>
        {
            payment.ToTable("payments");
            payment.HasKey(p => p.Id);
            payment.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            payment.Property(p => p.OrderId).HasColumnName("order_id").IsRequired();

            // UNIQUE, and this one protects real money: it is the database's guarantee that an order is
            // charged at most once, even if two duplicate commands are handled concurrently by two
            // replicas and both pass the in-code check.
            payment.HasIndex(p => p.OrderId).IsUnique().HasDatabaseName("ix_payments_order_id");

            payment.Property(p => p.OrderNumber).HasColumnName("order_number").HasMaxLength(30).IsRequired();
            payment.Property(p => p.BuyerId).HasColumnName("buyer_id").HasMaxLength(100).IsRequired();
            payment.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
            payment.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            payment.Property(p => p.Status).HasColumnName("status").HasConversion<int>().IsRequired();
            payment.Property(p => p.Reference).HasColumnName("reference").HasMaxLength(50).IsRequired();
            payment.Property(p => p.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
            payment.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
            payment.Property(p => p.RefundedAt).HasColumnName("refunded_at");
        });

        modelBuilder.ApplyOutboxModel();
    }
}
