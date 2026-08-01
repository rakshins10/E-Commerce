using ECommerce.Ordering.Domain.Orders;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Ordering.Infrastructure;

/// <summary>
/// The Ordering service's database, and its unit of work.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <c>DbContext</c> is the Unit of Work; a <c>DbSet</c> is a Repository.</b> Worth saying plainly,
/// because a great many codebases add a <c>UnitOfWork</c> class that wraps <c>SaveChangesAsync</c> and a
/// generic <c>Repository&lt;T&gt;</c> that wraps <c>DbSet&lt;T&gt;</c>, and end up with two layers of
/// indirection over an implementation of the very patterns they are re-implementing.
/// </para>
/// <para>
/// This service does have an <see cref="Orders.OrderRepository"/>, and that is for a different reason:
/// it is an <i>aggregate</i> repository, so it can guarantee that an order is always loaded with its
/// lines. Nobody can accidentally load an <c>Order</c> without its items and then compute a total of
/// zero. That is a domain guarantee, not a data-access abstraction.
/// </para>
/// <para>
/// <b>The outbox tables live here, in this database.</b> That is not incidental — it is the entire
/// mechanism. Because <c>outbox_messages</c> is in the same database as <c>orders</c>, one
/// <c>SaveChangesAsync</c> commits both atomically, with no distributed transaction and no window in
/// which an order exists but nobody was told.
/// </para>
/// </remarks>
public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options)
    : DbContext(options), IOutboxContext
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders");

            order.HasKey(o => o.Id);

            // The domain assigns identifiers in its constructors (Guid.CreateVersion7), so EF must not
            // infer state from the key value. Without this, a non-default key reads as "already exists"
            // and an INSERT is issued as an UPDATE, failing with a DbUpdateConcurrencyException whose
            // message points nowhere near the cause. Learned the expensive way in Phase 5 - see
            // docs/services/user-profile.md.
            //
            // HasColumnName("id") is NOT optional here, even though "Id" would work through EF. The
            // read side is hand-written Dapper SQL, and PostgreSQL folds an unquoted identifier to
            // LOWERCASE - so "SELECT o.id" looks for a column named id and finds a column named "Id".
            // EF quotes everything it generates, so the write side is perfectly happy and only the
            // query fails, at runtime, with "column o.id does not exist".
            //
            // Every other column on this entity is named explicitly below; the keys are the ones easy
            // to forget precisely because nothing in the C# mentions them.
            order.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

            order.Property(o => o.OrderNumber).HasColumnName("order_number")
                .HasMaxLength(30).IsRequired();

            // Unique, because it is quoted to customers and appears on invoices. The suffix is derived
            // from a GUID rather than a sequence, so a same-day collision is possible in principle;
            // this index turns that into a retry instead of two orders sharing a reference.
            order.HasIndex(o => o.OrderNumber).IsUnique().HasDatabaseName("ix_orders_order_number");

            order.Property(o => o.BuyerId).HasColumnName("buyer_id").HasMaxLength(100).IsRequired();
            order.Property(o => o.BuyerName).HasColumnName("buyer_name").HasMaxLength(200).IsRequired();

            // "My orders, newest first" is the only query the storefront makes, and it runs on every
            // visit to the orders page. A composite index in exactly that shape answers it from the
            // index alone.
            order.HasIndex(o => new { o.BuyerId, o.PlacedAt }).HasDatabaseName("ix_orders_buyer_placed");

            // Stored as an int, not a string. The enum values are explicit and never reordered, so the
            // numbers are stable; storing names would make renaming a status a data migration.
            order.Property(o => o.Status).HasColumnName("status").HasConversion<int>().IsRequired();

            order.Property(o => o.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            order.Property(o => o.PlacedAt).HasColumnName("placed_at").IsRequired();
            order.Property(o => o.PaidAt).HasColumnName("paid_at");
            order.Property(o => o.ShippedAt).HasColumnName("shipped_at");
            order.Property(o => o.DeliveredAt).HasColumnName("delivered_at");
            order.Property(o => o.CancelledAt).HasColumnName("cancelled_at");
            order.Property(o => o.CancellationReason).HasColumnName("cancellation_reason")
                .HasConversion<int?>();
            order.Property(o => o.StockReserved).HasColumnName("stock_reserved").IsRequired();
            order.Property(o => o.PaymentReference).HasColumnName("payment_reference").HasMaxLength(100);

            // The shipping address is a value object with no identity of its own, so it maps to columns
            // on the order row rather than a table with a foreign key. A join to fetch five strings that
            // are only ever read with their parent is cost without benefit.
            order.OwnsOne(o => o.ShippingAddress, address =>
            {
                address.Property(a => a.Recipient).HasColumnName("ship_recipient")
                    .HasMaxLength(200).IsRequired();
                address.Property(a => a.Line1).HasColumnName("ship_line1").HasMaxLength(200).IsRequired();
                address.Property(a => a.Line2).HasColumnName("ship_line2").HasMaxLength(200);
                address.Property(a => a.City).HasColumnName("ship_city").HasMaxLength(100).IsRequired();
                address.Property(a => a.Postcode).HasColumnName("ship_postcode")
                    .HasMaxLength(20).IsRequired();
                address.Property(a => a.Country).HasColumnName("ship_country")
                    .HasMaxLength(2).IsRequired();
            });

            // Total is a computed property on the aggregate, deliberately not stored - see Order.cs.
            order.Ignore(o => o.Total);
            order.Ignore(o => o.TotalUnits);
            order.Ignore(o => o.CanBeCancelled);
            order.Ignore(o => o.DomainEvents);

            // The backing field, not the public property. Items is exposed as a read-only collection so
            // nothing outside the aggregate can modify it; EF writes straight to the list behind it,
            // which is how encapsulation and persistence coexist.
            order.Metadata
                .FindNavigation(nameof(Order.Items))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);

            order.OwnsMany(o => o.Items, item =>
            {
                item.ToTable("order_items");

                item.WithOwner().HasForeignKey(i => i.OrderId);
                item.HasKey(i => i.Id);
                item.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
                item.Property(i => i.OrderId).HasColumnName("order_id");

                item.Property(i => i.ProductId).HasColumnName("product_id").IsRequired();
                item.Property(i => i.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
                item.Property(i => i.ProductName).HasColumnName("product_name")
                    .HasMaxLength(200).IsRequired();

                // Nullable: not every product has a size or a colour axis. Snapshotted like the name and
                // the price - see OrderItem.
                item.Property(i => i.Size).HasColumnName("size").HasMaxLength(20);
                item.Property(i => i.ColourName).HasColumnName("colour_name").HasMaxLength(40);
                item.Property(i => i.Quantity).HasColumnName("quantity").IsRequired();

                // numeric(18,2), never a floating-point type. Binary floating point cannot represent
                // 0.10 exactly, and a total that is a penny out once in a thousand orders is a support
                // ticket nobody can reproduce.
                item.OwnsOne(i => i.UnitPrice, price =>
                {
                    price.Property(p => p.Amount).HasColumnName("unit_price")
                        .HasColumnType("numeric(18,2)").IsRequired();
                    price.Property(p => p.Currency).HasColumnName("unit_price_currency")
                        .HasMaxLength(3).IsRequired();
                });

                item.Ignore(i => i.LineTotal);
                item.Ignore(i => i.DomainEvents);
            });
        });

        // Adds outbox_messages and processed_messages to THIS service's model and migrations. Each
        // service owns its own outbox; there is no shared messaging database.
        modelBuilder.ApplyOutboxModel();
    }
}
