using Microsoft.EntityFrameworkCore;

namespace ECommerce.Outbox;

/// <summary>
/// Maps the outbox and inbox tables into a service's model.
/// </summary>
/// <remarks>
/// Called from each service's <c>OnModelCreating</c>, so the tables appear in that service's own
/// migrations and are created by its own deployment. There is no shared outbox database and no shared
/// migration story — each service owns its data, including the parts that happen to be infrastructure.
/// </remarks>
public static class OutboxModelBuilderExtensions
{
    public static ModelBuilder ApplyOutboxModel(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");

            entity.HasKey(message => message.Id);

            // Never database-generated: the id comes from the integration event so that the same value
            // reaches the consumer and can be deduplicated there.
            //
            // Named explicitly, because PostgreSQL folds unquoted identifiers to lowercase and any
            // hand-written SQL against this table - a diagnostic query as much as application code -
            // would otherwise fail to find a column called "Id".
            entity.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();

            entity.Property(message => message.EventName).HasColumnName("event_name")
                .HasMaxLength(200).IsRequired();

            entity.Property(message => message.Payload).HasColumnName("payload")
                .HasColumnType("jsonb").IsRequired();

            entity.Property(message => message.CorrelationId).HasColumnName("correlation_id")
                .HasMaxLength(100);

            entity.Property(message => message.TraceParent).HasColumnName("trace_parent")
                .HasMaxLength(100);

            entity.Property(message => message.OccurredAt).HasColumnName("occurred_at").IsRequired();
            entity.Property(message => message.PublishedAt).HasColumnName("published_at");
            entity.Property(message => message.Attempts).HasColumnName("attempts").IsRequired();
            entity.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(2000);

            // The publisher's only query is "unpublished, oldest first", and it runs every second or so
            // forever. A partial index covers exactly that: it holds only the pending rows, so it stays
            // small even when the table has millions of published ones, and PostgreSQL does not have to
            // wade through history to find the handful of rows that still matter.
            entity.HasIndex(message => message.OccurredAt)
                .HasDatabaseName("ix_outbox_messages_pending")
                .HasFilter("published_at IS NULL");
        });

        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.ToTable("processed_messages");

            // Composite key, because one service may have several handlers for the same event and each
            // must deduplicate independently.
            entity.HasKey(message => new { message.MessageId, message.Consumer });

            entity.Property(message => message.MessageId).HasColumnName("message_id");
            entity.Property(message => message.Consumer).HasColumnName("consumer").HasMaxLength(200);
            entity.Property(message => message.EventName).HasColumnName("event_name")
                .HasMaxLength(200).IsRequired();
            entity.Property(message => message.ProcessedAt).HasColumnName("processed_at").IsRequired();
        });

        return modelBuilder;
    }
}
