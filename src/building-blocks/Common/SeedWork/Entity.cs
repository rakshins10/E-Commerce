namespace ECommerce.Common.SeedWork;

/// <summary>
/// Base class for a domain <b>entity</b>: an object defined by its <i>identity</i> rather than its attributes.
/// </summary>
/// <typeparam name="TId">The identity type. Usually <see cref="Guid"/> here.</typeparam>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Entity (DDD tactical). See <c>docs/concept-map.md</c>.
/// </para>
/// <para>
/// <b>Entity vs value object</b> — the distinction that decides which base class to use:
/// two entities with identical attributes are still <i>different things</i> if their ids differ. An order
/// whose every field matches another order is not that order. By contrast a
/// <see cref="ValueObject"/> has no identity: two <c>Money(10, "GBP")</c> instances are interchangeable.
/// The test: <i>if I change every attribute, is it still the same thing?</i> Yes → entity. No → value object.
/// </para>
/// <para>
/// Equality here is therefore <b>identity equality</b>, not structural equality — which is exactly why an
/// entity must not be a C# <c>record</c>: records give value-based equality, which is precisely wrong for an
/// entity and produces subtle bugs when entities land in sets or dictionaries.
/// </para>
/// </remarks>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// The entity's identity. Set once, by the constructor, and never mutated.
    /// </summary>
    public TId Id { get; protected init; } = default!;

    /// <summary>
    /// Domain events raised by this entity and not yet dispatched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exposed as a read-only view so no caller outside the aggregate can add to it. Events are added only via
    /// <see cref="RaiseDomainEvent"/>, which aggregate methods call while enforcing their invariants.
    /// </para>
    /// <para>
    /// <b>Why collect rather than publish immediately?</b> Publishing inside the aggregate would mean a handler
    /// running before the change is committed — and if the transaction then rolls back, the handler has acted on
    /// something that never happened. Collecting lets the infrastructure dispatch them at the right moment:
    /// after <c>SaveChanges</c> has staged the changes but inside the same transaction. It also keeps the
    /// aggregate ignorant of dispatching, which is what allows it to be unit-tested with no infrastructure.
    /// </para>
    /// </remarks>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Records that something domain-significant happened. Called from inside aggregate behaviour.
    /// </summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears collected events. Called by the infrastructure once they have been dispatched, so that a
    /// long-lived tracked instance cannot dispatch the same event twice.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// True when this entity has never been persisted — its identity is still the type's default.
    /// </summary>
    public bool IsTransient() => EqualityComparer<TId>.Default.Equals(Id, default!);

    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Different concrete types are never equal even if their ids collide, and two transient entities are
        // equal only by reference — they have no identity yet to compare.
        if (GetType() != other.GetType() || IsTransient() || other.IsTransient())
        {
            return false;
        }

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => obj is Entity<TId> entity && Equals(entity);

    public override int GetHashCode() => IsTransient() ? base.GetHashCode() : HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
