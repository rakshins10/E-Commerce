using ECommerce.Common.SeedWork;
using ECommerce.Ordering.Domain.Orders;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Ordering.Infrastructure.Orders;

/// <summary>
/// Loads and stores whole orders.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists when <c>DbSet&lt;Order&gt;</c> already is a repository.</b> Not to abstract EF —
/// wrapping <c>DbSet</c> in a near-identical interface buys nothing and is the most common piece of
/// cargo-cult layering in .NET codebases. It exists to guarantee one thing the raw <c>DbSet</c> cannot:
/// <b>an order is never loaded without its lines</b>.
/// </para>
/// <para>
/// Forget the <c>Include</c> once and <c>order.Total</c> silently returns zero, because the aggregate
/// faithfully sums a collection that was never populated. That is a wrong number rather than an
/// exception, which makes it the worst kind of bug. Putting the <c>Include</c> in the single place
/// orders are loaded makes forgetting it impossible.
/// </para>
/// <para>
/// It is an <i>aggregate</i> repository, deliberately not a generic one. One repository per aggregate
/// root is the pattern; <c>IRepository&lt;T&gt;</c> for every entity re-invents <c>DbSet</c> with fewer
/// features and quietly encourages loading pieces of an aggregate on their own — which is exactly what
/// an aggregate boundary is meant to prevent.
/// </para>
/// </remarks>
public sealed class OrderRepository(OrderingDbContext context) : IRepository<Order, Guid>
{
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Loads by the customer-facing reference rather than the primary key.</summary>
    /// <remarks>
    /// Support staff have the order number, not the GUID. Giving them a route that takes what they
    /// actually hold saves a lookup step and a transcription error.
    /// </remarks>
    public async Task<Order?> GetByOrderNumberAsync(
        string orderNumber,
        CancellationToken cancellationToken = default) =>
        await context.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.OrderNumber == orderNumber, cancellationToken)
            .ConfigureAwait(false);

    public async Task<Order> AddAsync(Order aggregate, CancellationToken cancellationToken = default)
    {
        // Note: no SaveChangesAsync. The caller owns the transaction, because the caller is also writing
        // the outbox row and the two must commit together. A repository that saves on every call makes
        // the outbox pattern impossible to implement correctly.
        await context.Orders.AddAsync(aggregate, cancellationToken).ConfigureAwait(false);
        return aggregate;
    }

    public void Update(Order aggregate)
    {
        // Present to satisfy IRepository, and a no-op in practice: an order loaded through this
        // repository is already tracked, so mutating the aggregate is enough. Calling Update() on a
        // tracked entity marks every property modified, which turns a one-column change into a full-row
        // UPDATE for no benefit.
        context.Entry(aggregate).State = EntityState.Modified;
    }
}
