using ECommerce.Common.Guards;

namespace ECommerce.Catalog.Api.Domain;

/// <summary>
/// The set of sizes a category is sold in, and the order they belong in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Configuration, not code</b> ([ADR-0021](../../../docs/adr/0021-category-defined-options-and-contextual-facets.md)).
/// The clothing sizes used to be an <c>ARRAY['S','M','L','XL']</c> literal inside two queries, which said in
/// the database layer that this shop sells clothing. Adding shoes meant editing SQL; a merchandiser could
/// not do it at all.
/// </para>
/// <para>
/// <b>Position is the whole point.</b> Sizes have an order that is neither alphabetical nor numeric -
/// <c>S, M, L, XL</c> sorts to <c>L, M, S, XL</c> either way - so the order has to be stated. Storing it as
/// data means the SQL can join for it instead of hard-coding it, and means a new scale arrives correctly
/// ordered without a deployment.
/// </para>
/// <para>
/// <b>This is not a generic attribute system.</b> A variant still has typed <c>Size</c> and
/// <c>ColourName</c> columns; what is configurable is which values are legal and how they sort. The
/// difference is that the UI knows what a size *is*, which it could not if this were a key/value bag.
/// </para>
/// </remarks>
public class SizeScale
{
    private SizeScale()
    {
        // EF Core.
    }

    public SizeScale(string name, string slug)
    {
        Id = Guid.CreateVersion7();
        Name = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(name), 100);
        Slug = Guard.AgainstNullOrWhiteSpace(slug).ToLowerInvariant();
    }

    public Guid Id { get; private set; }

    /// <summary>How a merchandiser refers to it: "UK clothing", "EU shoe".</summary>
    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public List<SizeScaleValue> Values { get; private set; } = [];

    /// <summary>
    /// Adds a size at the end of the scale.
    /// </summary>
    /// <remarks>
    /// Position is assigned rather than passed, so a caller cannot create two sizes claiming the same place
    /// or leave a gap. Appending is the only order-changing operation there is, which is enough for a scale
    /// that is defined once and rarely revisited.
    /// </remarks>
    public SizeScaleValue Add(string value)
    {
        string normalised = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(value), 20).Trim();

        if (Values.Any(existing => string.Equals(existing.Value, normalised, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateSizeException(Name, normalised);
        }

        var size = new SizeScaleValue(Id, normalised, Values.Count);
        Values.Add(size);

        return size;
    }

    /// <summary>The sizes in the order they should be shown.</summary>
    public IEnumerable<string> Ordered() =>
        Values.OrderBy(value => value.Position).Select(value => value.Value);
}

/// <summary>One size within a scale, and where it sits in the order.</summary>
public class SizeScaleValue
{
    private SizeScaleValue()
    {
        // EF Core.
    }

    internal SizeScaleValue(Guid sizeScaleId, string value, int position)
    {
        Id = Guid.CreateVersion7();
        SizeScaleId = sizeScaleId;
        Value = value;
        Position = position;
    }

    public Guid Id { get; private set; }

    public Guid SizeScaleId { get; private set; }

    public SizeScale? SizeScale { get; private set; }

    /// <summary>The size as a customer reads it: <c>M</c>, <c>42</c>, <c>350ml</c>.</summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>Zero-based. Sorting by this is what makes S come before M before L before XL.</summary>
    public int Position { get; private set; }
}

/// <summary>The scale already contains that size.</summary>
public sealed class DuplicateSizeException(string scaleName, string value)
    : Common.Exceptions.DomainException($"The {scaleName} scale already contains the size '{value}'.");
