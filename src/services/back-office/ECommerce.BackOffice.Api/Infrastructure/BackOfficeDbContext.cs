using ECommerce.BackOffice.Api.Features;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.BackOffice.Api.Infrastructure;

/// <summary>
/// Back-office's own database. It holds exactly one thing: the audit log.
/// </summary>
/// <remarks>
/// This service reads other services' databases for dashboard aggregates, but WRITES only here. That
/// asymmetry is the boundary: reporting may look across, but no service other than the owner changes a
/// table.
/// </remarks>
public sealed class BackOfficeDbContext(DbContextOptions<BackOfficeDbContext> options) : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.ToTable("audit_entries");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entry.Property(e => e.ActorId).HasColumnName("actor_id").HasMaxLength(100).IsRequired();
            entry.Property(e => e.ActorName).HasColumnName("actor_name").HasMaxLength(200).IsRequired();
            entry.Property(e => e.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            entry.Property(e => e.Target).HasColumnName("target").HasMaxLength(200).IsRequired();
            entry.Property(e => e.Detail).HasColumnName("detail").HasMaxLength(1000);
            entry.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

            // Newest first is the only way this is ever read.
            entry.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_audit_entries_occurred_at");

            // "What did this person do?" is the second question anybody asks.
            entry.HasIndex(e => e.ActorId).HasDatabaseName("ix_audit_entries_actor");
        });
    }
}
