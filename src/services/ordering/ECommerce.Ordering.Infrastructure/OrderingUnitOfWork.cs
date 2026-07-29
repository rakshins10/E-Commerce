using ECommerce.Ordering.Application.Orders;

namespace ECommerce.Ordering.Infrastructure;

/// <summary>
/// Commits the current unit of work.
/// </summary>
/// <remarks>
/// A three-line adapter, and the reason it exists is worth stating: it lets the application layer commit
/// without referencing EF Core, which is what keeps the layering rule in
/// tests/unit/ECommerce.Architecture.Tests enforceable rather than aspirational.
///
/// Note it does NOT wrap SaveChangesAsync in an explicit transaction. It does not need to -
/// SaveChangesAsync is already atomic across every tracked change, which is exactly why the order and
/// its outbox row commit together.
/// </remarks>
public sealed class OrderingUnitOfWork(OrderingDbContext context) : IOrderingUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
