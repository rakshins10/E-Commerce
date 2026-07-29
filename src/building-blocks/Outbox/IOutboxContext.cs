using Microsoft.EntityFrameworkCore;

namespace ECommerce.Outbox;

/// <summary>
/// What the outbox publisher needs from a service's <see cref="DbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// A narrow interface rather than a base class, so a service's context can inherit from whatever it
/// likes and still take part. The publisher depends on this and nothing else, which is why it can be
/// shared by Ordering, Payment and Inventory without any of them knowing about each other.
/// </para>
/// <para>
/// Note that <see cref="OutboxMessages"/> is a normal <see cref="DbSet{TEntity}"/> on the service's own
/// context — that is the point. The outbox rows live in the <i>same database</i> as the business data,
/// which is what makes writing both in one transaction possible. An outbox in its own database would be
/// a dual write again, wearing a different hat.
/// </para>
/// </remarks>
public interface IOutboxContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<ProcessedMessage> ProcessedMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
