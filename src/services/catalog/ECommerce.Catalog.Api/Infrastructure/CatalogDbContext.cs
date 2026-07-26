using ECommerce.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Catalog.Api.Infrastructure;

/// <summary>
/// Catalog's own database. No other service reads these tables — see
/// <c>docs/architecture.md §7</c>.
/// </summary>
/// <remarks>
/// The <c>DbContext</c> <b>is</b> the Unit of Work: it tracks changes across the whole request and
/// <c>SaveChangesAsync</c> commits them atomically. There is deliberately no hand-written
/// <c>IUnitOfWork</c> wrapper — that would be an abstraction over an abstraction, adding a layer and no
/// capability.
/// </remarks>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Brand> Brands => Set<Brand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(p => p.Id);

            // Unique at the DATABASE, not just in code. A uniqueness rule enforced only in application code
            // is a rule that two concurrent requests can both pass.
            entity.HasIndex(p => p.Sku).IsUnique();

            entity.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(4000);
            entity.Property(p => p.ImageUrl).HasMaxLength(500);

            // NUMERIC(18,2), never float or double. Binary floating point cannot represent 0.10 exactly, so
            // money in a double accumulates rounding error - the classic "the totals are off by a penny" bug.
            entity.Property(p => p.Price).HasColumnType("numeric(18,2)").IsRequired();
            entity.Property(p => p.Currency).HasMaxLength(3).IsRequired();

            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                // Restrict, not Cascade: deleting a category must not silently delete its products.
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(p => p.Brand)
                .WithMany(b => b.Products)
                .HasForeignKey(p => p.BrandId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes chosen from how the browse endpoint actually filters, not speculatively.
            entity.HasIndex(p => p.CategoryId);
            entity.HasIndex(p => p.BrandId);
            entity.HasIndex(p => p.IsActive);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.Slug).IsUnique();
            entity.Property(c => c.Name).HasMaxLength(100).IsRequired();
            entity.Property(c => c.Slug).HasMaxLength(100).IsRequired();

            entity.HasOne(c => c.Parent)
                .WithMany()
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Brand>(entity =>
        {
            entity.ToTable("brands");
            entity.HasKey(b => b.Id);
            entity.HasIndex(b => b.Slug).IsUnique();
            entity.Property(b => b.Name).HasMaxLength(100).IsRequired();
            entity.Property(b => b.Slug).HasMaxLength(100).IsRequired();
        });

        ApplySnakeCaseNames(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Renames every column, key and index to <c>snake_case</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EF Core defaults to the .NET property name, so <c>StockOnHand</c> becomes a column called
    /// <c>StockOnHand</c>. PostgreSQL folds unquoted identifiers to lower case, which means every reference
    /// to that column in hand-written SQL must be quoted — <c>p."StockOnHand"</c> — and forgetting a quote
    /// produces <c>column p.stockonhand does not exist</c>.
    /// </para>
    /// <para>
    /// Since the CQRS read side is hand-written Dapper SQL (<c>docs/adr/0012</c>), that friction is paid on
    /// every query. Converting once here means the database follows PostgreSQL's own convention and the SQL
    /// reads naturally: <c>p.stock_on_hand</c>.
    /// </para>
    /// <para>
    /// This is applied in <c>OnModelCreating</c> rather than by hand per property so a new entity cannot
    /// forget it — the convention is enforced by construction rather than by discipline.
    /// </para>
    /// </remarks>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 8);

        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];

            // Insert an underscore before an upper-case letter that follows a lower-case one or a digit,
            // so `StockOnHand` -> `stock_on_hand` but an existing `_` is not doubled.
            if (char.IsUpper(current) && i > 0 && name[i - 1] != '_' && !char.IsUpper(name[i - 1]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
