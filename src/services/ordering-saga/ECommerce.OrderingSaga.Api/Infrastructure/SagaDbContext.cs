using ECommerce.OrderingSaga.Api.Model;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.OrderingSaga.Api.Infrastructure;

/// <summary>
/// The saga's own database.
/// </summary>
/// <remarks>
/// It holds the outbox tables too, because the saga is itself a publisher: every command it sends goes
/// out through the outbox, in the same transaction as the state change that decided to send it. Without
/// that, a crash between "record that we asked for stock" and "actually ask" leaves a saga waiting
/// forever for a reply to a question nobody heard.
/// </remarks>
public sealed class SagaDbContext(DbContextOptions<SagaDbContext> options)
    : DbContext(options), IOutboxContext
{
    public DbSet<OrderSaga> Sagas => Set<OrderSaga>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrderSaga>(saga =>
        {
            saga.ToTable("order_sagas");

            // The order id IS the key. One order, one saga - so a duplicate OrderSubmitted hits the
            // unique constraint rather than starting a second saga that reserves stock again.
            saga.HasKey(s => s.OrderId);
            saga.Property(s => s.OrderId).HasColumnName("order_id").ValueGeneratedNever();

            saga.Property(s => s.OrderNumber).HasColumnName("order_number").HasMaxLength(30).IsRequired();
            saga.HasIndex(s => s.OrderNumber).HasDatabaseName("ix_order_sagas_order_number");

            saga.Property(s => s.BuyerId).HasColumnName("buyer_id").HasMaxLength(100).IsRequired();
            saga.Property(s => s.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
            saga.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            saga.Property(s => s.State).HasColumnName("state").HasConversion<int>().IsRequired();
            saga.Property(s => s.StockReserved).HasColumnName("stock_reserved").IsRequired();
            saga.Property(s => s.PaymentReference).HasColumnName("payment_reference").HasMaxLength(100);
            saga.Property(s => s.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
            saga.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();
            saga.Property(s => s.CompletedAt).HasColumnName("completed_at");

            // "Which sagas are stuck?" is the operational question this exists to answer, and it runs
            // over unfinished rows only - so the index covers exactly those.
            saga.HasIndex(s => s.State).HasDatabaseName("ix_order_sagas_state");

            saga.OwnsMany(s => s.Steps, step =>
            {
                step.ToTable("saga_steps");
                step.WithOwner().HasForeignKey(x => x.OrderId);
                step.HasKey(x => x.Id);
                step.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                step.Property(x => x.OrderId).HasColumnName("order_id");
                step.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
                step.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(500).IsRequired();
                step.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
                step.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
            });
        });

        modelBuilder.ApplyOutboxModel();
    }
}
