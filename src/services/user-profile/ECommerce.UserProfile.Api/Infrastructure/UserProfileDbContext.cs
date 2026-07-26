using ECommerce.UserProfile.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.UserProfile.Api.Infrastructure;

/// <summary>
/// User Profile's own database. No other service reads these tables.
/// </summary>
public class UserProfileDbContext(DbContextOptions<UserProfileDbContext> options) : DbContext(options)
{
    public DbSet<Domain.UserProfile> Profiles => Set<Domain.UserProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.UserProfile>(entity =>
        {
            entity.ToTable("user_profiles");
            entity.HasKey(p => p.Id);

            // ValueGeneratedNever on every key in this model - see the note at the bottom of this file.
            // Without it, EF assumes a non-default key means the row already exists.
            entity.Property(p => p.Id).ValueGeneratedNever();

            // Unique at the DATABASE. Two concurrent first-logins for the same user would otherwise both
            // pass an application-level "does a profile exist?" check and create two profiles.
            entity.HasIndex(p => p.Subject).IsUnique();

            entity.Property(p => p.Subject).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Email).HasMaxLength(256);
            entity.Property(p => p.DisplayName).HasMaxLength(100);
            entity.Property(p => p.PhoneNumber).HasMaxLength(30);

            // Preferences is a value object with no identity, so it is OWNED - stored as columns on this
            // row rather than in a table of its own. A separate table would imply a lifetime it does not
            // have and force a join on every profile read.
            entity.OwnsOne(p => p.Preferences, preferences =>
            {
                preferences.Property(x => x.Locale).HasColumnName("locale").HasMaxLength(10).IsRequired();
                preferences.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
                preferences.Property(x => x.Theme).HasColumnName("theme").HasMaxLength(10).IsRequired();
                preferences.Property(x => x.MarketingEmail).HasColumnName("marketing_email");
                preferences.Property(x => x.MarketingSms).HasColumnName("marketing_sms");
                preferences.Property(x => x.OrderUpdatesEmail).HasColumnName("order_updates_email");
                preferences.Property(x => x.OrderUpdatesSms).HasColumnName("order_updates_sms");
            });

            // Cascade here is correct, unlike in Catalog: an address has no meaning without its profile, so
            // deleting the profile must take them with it. That is also what GDPR erasure needs.
            entity.HasMany(p => p.Addresses)
                .WithOne()
                .HasForeignKey(a => a.UserProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.Consents)
                .WithOne()
                .HasForeignKey(c => c.UserProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // The collections are exposed as IReadOnlyCollection, so EF must be told to write through the
            // backing field rather than the property - otherwise it cannot materialise them.
            entity.Navigation(p => p.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(p => p.Consents).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Address>(entity =>
        {
            entity.ToTable("addresses");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).ValueGeneratedNever();
            entity.Property(a => a.Label).HasMaxLength(50).IsRequired();
            entity.Property(a => a.Line1).HasMaxLength(200).IsRequired();
            entity.Property(a => a.Line2).HasMaxLength(200);
            entity.Property(a => a.City).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Postcode).HasMaxLength(20).IsRequired();
            entity.Property(a => a.Country).HasMaxLength(2).IsRequired();
            entity.HasIndex(a => a.UserProfileId);
        });

        modelBuilder.Entity<ConsentRecord>(entity =>
        {
            entity.ToTable("consent_records");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.Property(c => c.ConsentType).HasMaxLength(50).IsRequired();
            entity.Property(c => c.Version).HasMaxLength(20).IsRequired();
            entity.HasIndex(c => new { c.UserProfileId, c.ConsentType });
        });

        base.OnModelCreating(modelBuilder);
    }

    // -------------------------------------------------------------------------
    //  No snake_case column convention here - deliberately, unlike Catalog.
    // -------------------------------------------------------------------------
    //  Catalog applies one because its CQRS read side is hand-written Dapper SQL,
    //  where PascalCase columns must be quoted on every reference and a missing
    //  quote gives "column p.stockonhand does not exist". That friction is real
    //  and worth a convention.
    //
    //  This service has NO hand-written SQL. It reads one small aggregate at a
    //  time through EF, so the convention would buy nothing - and it actively
    //  broke the owned Preferences type: the owned entity's key must map to the
    //  SAME column as its owner's primary key, and renaming one side produced
    //
    //      UPDATE user_profiles ... WHERE "UserProfileId" = @p   -- 0 rows
    //
    //  which surfaced as a DbUpdateConcurrencyException rather than anything
    //  that pointed at column naming.
    //
    //  Table names are still snake_case via ToTable(), and the Preferences
    //  columns are named explicitly above. Apply a convention where it earns its
    //  keep, not everywhere for symmetry.
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    //  Why ValueGeneratedNever() on every key
    // -------------------------------------------------------------------------
    //  The domain generates its own identifiers - `Guid.CreateVersion7()` in each
    //  constructor - so an entity has a real key the moment it is created, before
    //  it has ever been saved.
    //
    //  EF's default for a Guid key is ValueGeneratedOnAdd, and its change tracker
    //  then infers state from the key: a DEFAULT key means "new", a non-default
    //  key means "already exists". So when `profile.AddAddress(...)` put a fully
    //  formed Address into a tracked collection, DetectChanges marked it
    //  **Modified** rather than Added and EF issued
    //
    //      UPDATE addresses SET ... WHERE "Id" = @p    -- 0 rows
    //
    //  surfacing as DbUpdateConcurrencyException: "expected to affect 1 row(s),
    //  but actually affected 0 row(s)". That message names no entity, which makes
    //  it one of the least actionable exceptions in EF Core - the endpoint now
    //  catches it and reports which entity and state were involved.
    //
    //  ValueGeneratedNever() tells EF the application owns key generation, so it
    //  stops using the key as an existence heuristic and correctly treats new
    //  instances in a navigation as Added.
    //
    //  Worth knowing generally: it bites any aggregate that creates children with
    //  client-side identifiers, which is the normal pattern in DDD.
    // -------------------------------------------------------------------------

}
