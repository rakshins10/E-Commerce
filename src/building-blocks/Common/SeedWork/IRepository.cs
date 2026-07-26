namespace ECommerce.Common.SeedWork;

/// <summary>
/// A <b>repository</b> for one aggregate root: the illusion of an in-memory collection of aggregates, hiding
/// whatever persistence actually happens underneath.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type. Constrained to <see cref="IAggregateRoot"/> so a
/// repository can never be written for an entity <i>inside</i> an aggregate.</typeparam>
/// <typeparam name="TId">The aggregate's identity type.</typeparam>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Repository + Unit of Work (DDD tactical).
/// See <c>docs/architecture.md §4</c> and <c>docs/concept-map.md</c>.
/// </para>
/// <para>
/// <b>Why this interface is declared in the domain layer but implemented in infrastructure.</b> "Somewhere I can
/// get an Order" is a domain concept; "an EF Core <c>DbSet</c> over Postgres" is not. Declaring the interface
/// inward and the implementation outward is what "dependencies point inward" means concretely — the domain
/// stays testable with an in-memory fake and never learns that EF Core exists.
/// </para>
/// <para>
/// <b>Note what is deliberately missing: there is no <c>SaveChanges</c> here.</b> Persisting is the job of the
/// Unit of Work, and EF Core's <c>DbContext</c> <i>already is</i> one — it tracks changes across many
/// repositories and commits them atomically. Wrapping it in a hand-written <c>IUnitOfWork</c> that forwards to
/// <c>SaveChangesAsync</c> is a very common and entirely redundant abstraction over an abstraction. In this
/// codebase the commit happens once per request, in the MediatR <c>TransactionBehaviour</c>
/// (<c>docs/adr/0012-cqrs-with-mediatr.md</c>), which is also where domain events are dispatched and integration
/// events land in the outbox — all inside one transaction.
/// </para>
/// <para>
/// <b>Also deliberately missing: <c>IQueryable</c>.</b> Returning <c>IQueryable</c> from a repository leaks the
/// persistence model to callers and lets query concerns creep into the domain. Reads that need shaping,
/// filtering, or paging do not go through repositories at all — they use the CQRS query side, which bypasses the
/// domain model entirely.
/// </para>
/// </remarks>
public interface IRepository<TAggregate, in TId>
    where TAggregate : class, IAggregateRoot
    where TId : notnull
{
    /// <summary>
    /// Loads an aggregate by identity, or <see langword="null"/> if it does not exist.
    /// </summary>
    /// <remarks>
    /// Implementations load the <i>whole</i> aggregate — root plus everything inside the boundary — because a
    /// partially-loaded aggregate cannot enforce its own invariants. This is the strongest practical argument
    /// for keeping aggregates small.
    /// </remarks>
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new aggregate. The write is staged, not committed — see the remarks on this interface.
    /// </summary>
    Task<TAggregate> AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an existing aggregate as modified.
    /// </summary>
    /// <remarks>
    /// Frequently a no-op with EF Core, which already tracks changes to a loaded aggregate. It stays on the
    /// interface so the calling code reads the same regardless of implementation, and so a non-tracking
    /// implementation remains possible.
    /// </remarks>
    void Update(TAggregate aggregate);
}
