namespace ECommerce.Common.SeedWork;

/// <summary>
/// Base class for a <b>value object</b>: an object with no identity, defined entirely by its attributes,
/// compared structurally, and immutable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Value Object (DDD tactical). See <c>docs/concept-map.md</c>.
/// </para>
/// <para>
/// <b>Why value objects matter more than they look.</b> The instinct is to model money as <c>decimal</c> and an
/// address as five loose strings on the parent entity. Both lose information the type system could have kept:
/// </para>
/// <list type="bullet">
///   <item><description><c>decimal + decimal</c> compiles even when the two amounts are in different currencies.
///   <c>Money + Money</c> can refuse.</description></item>
///   <item><description>Five loose strings can be half-populated. An <c>Address</c> constructed through one
///   constructor that validates is either complete or does not exist.</description></item>
///   <item><description>Loose primitives spread validation across every call site. A value object validates once,
///   in its constructor, and is thereafter trustworthy — the "parse, don't validate" idea.</description></item>
/// </list>
/// <para>
/// <b>Immutability is the point, not a detail.</b> Because two equal value objects are interchangeable, sharing
/// an instance must be safe. A mutable value object shared between two orders means editing one order's address
/// silently edits the other's. Derived types therefore expose <c>init</c>-only members and return new instances
/// from any "change" operation.
/// </para>
/// <para>
/// <b>Why not just use a C# <c>record</c>?</b> Often you should — records give structural equality for free and
/// are the right choice for simple cases. This base class exists for value objects that need a hand-written
/// equality contract: normalising before comparing (a currency code compared case-insensitively), or excluding a
/// cached/derived member from equality. Deriving from this makes that contract explicit in one place
/// (<see cref="GetEqualityComponents"/>) instead of scattered across overridden record members.
/// </para>
/// </remarks>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>
    /// The components that define this value's identity, in a stable order.
    /// </summary>
    /// <remarks>
    /// Yield exactly the members that make two instances interchangeable. Normalise here rather than at the
    /// comparison site — for example <c>yield return Currency.ToUpperInvariant();</c>.
    /// </remarks>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) =>
        other is not null
        && GetType() == other.GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GetType());

        foreach (object? component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
