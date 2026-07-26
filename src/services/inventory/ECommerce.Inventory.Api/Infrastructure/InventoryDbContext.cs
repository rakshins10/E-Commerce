using ECommerce.Inventory.Api.Model;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Inventory.Api.Infrastructure;

/// <summary>Inventory's database, including its outbox.</summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : DbContext(options), IOutboxContext
{
    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StockItem>(item =>
        {
            item.ToTable("stock_items");
            item.HasKey(s => s.Id);
            item.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            item.Property(s => s.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();

            // The SKU is how every other service refers to stock, so it must be unique and it is the
            // only thing ever looked up by.
            item.HasIndex(s => s.Sku).IsUnique().HasDatabaseName("ix_stock_items_sku");

            item.Property(s => s.ProductName).HasColumnName("product_name").HasMaxLength(200).IsRequired();
            item.Property(s => s.OnHand).HasColumnName("on_hand").IsRequired();
            item.Property(s => s.Reserved).HasColumnName("reserved").IsRequired();
            item.Property(s => s.ReorderLevel).HasColumnName("reorder_level").IsRequired();
            item.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // Available is OnHand - Reserved, computed. Storing it would be a second source of truth
            // that drifts the first time one number is updated and the other is not.
            item.Ignore(s => s.Available);
        });

        modelBuilder.Entity<StockReservation>(reservation =>
        {
            reservation.ToTable("stock_reservations");
            reservation.HasKey(r => r.Id);
            reservation.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            reservation.Property(r => r.OrderId).HasColumnName("order_id").IsRequired();

            // UNIQUE. The database guarantee that a duplicate ReserveStockCommand cannot reserve the
            // same stock twice, backing up the check in the handler.
            reservation.HasIndex(r => r.OrderId).IsUnique()
                .HasDatabaseName("ix_stock_reservations_order_id");

            reservation.Property(r => r.OrderNumber).HasColumnName("order_number").HasMaxLength(30).IsRequired();
            reservation.Property(r => r.IsReleased).HasColumnName("is_released").IsRequired();
            reservation.Property(r => r.ReservedAt).HasColumnName("reserved_at").IsRequired();
            reservation.Property(r => r.ReleasedAt).HasColumnName("released_at");

            reservation.OwnsMany(r => r.Lines, line =>
            {
                line.ToTable("stock_reservation_lines");
                line.WithOwner().HasForeignKey(l => l.StockReservationId);
                line.HasKey(l => l.Id);
                line.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
                line.Property(l => l.StockReservationId).HasColumnName("stock_reservation_id");
                line.Property(l => l.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
                line.Property(l => l.Quantity).HasColumnName("quantity").IsRequired();
            });
        });

        modelBuilder.ApplyOutboxModel();
    }
}
