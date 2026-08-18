using ECommerce.Common.Guards;

namespace ECommerce.Catalog.Api.Domain;

/// <summary>
/// A browsable grouping of products. Categories nest one level (e.g. Clothing → T-shirts).
/// </summary>
public class Category
{
    private Category()
    {
    }

    public Category(string name, string slug, Guid? parentId = null)
    {
        Id = Guid.CreateVersion7();
        Name = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(name), 100);
        Slug = Guard.AgainstNullOrWhiteSpace(slug).ToLowerInvariant();
        ParentId = parentId;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// URL-friendly identifier, e.g. <c>t-shirts</c>.
    /// </summary>
    /// <remarks>
    /// Filtering uses the slug rather than the GUID so that <c>/products?category=t-shirts</c> is a readable,
    /// shareable, bookmarkable URL. Exposing an opaque id in a query string is a small usability tax paid on
    /// every link anyone ever shares.
    /// </remarks>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Null for a top-level category.</summary>
    public Guid? ParentId { get; private set; }

    public Category? Parent { get; private set; }

    /// <summary>
    /// Which sizes products in this category are sold in. <b>Null means the category is not sized.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null rather than an empty scale, and that is a real distinction: a notebook is not sold in sizes at
    /// all, which is a different claim from being sold in one size. The storefront omits the size selector
    /// entirely rather than showing a control with a single option
    /// ([ADR-0021](../../../docs/adr/0021-category-defined-options-and-contextual-facets.md)).
    /// </para>
    /// <para>
    /// <b>Inherited from the parent when null on a child.</b> Clothing declares "UK clothing" once; T-shirts
    /// and Hoodies do not repeat it. A child that declares its own overrides - which is how Shoes would sit
    /// under Clothing with a different scale. See <see cref="EffectiveSizeScaleId"/>.
    /// </para>
    /// </remarks>
    public Guid? SizeScaleId { get; private set; }

    public SizeScale? SizeScale { get; private set; }

    public ICollection<Product> Products { get; private set; } = [];

    /// <summary>
    /// This category's scale, or its parent's.
    /// </summary>
    /// <remarks>
    /// One level of inheritance, matching the taxonomy's one level of nesting. A third level would need this
    /// revisited along with everything else that assumes two, which is recorded in the ADR rather than
    /// generalised for a case that does not exist.
    ///
    /// Requires <see cref="Parent"/> to be loaded; callers that have not loaded it get this category's own
    /// scale, which is the safe direction to be wrong in - it under-reports rather than inventing sizes.
    /// </remarks>
    public Guid? EffectiveSizeScaleId => SizeScaleId ?? Parent?.SizeScaleId;

    /// <summary>Points the category at a size scale, or at none.</summary>
    public void UseSizeScale(Guid? sizeScaleId) => SizeScaleId = sizeScaleId;
}

/// <summary>
/// A manufacturer or label. Unlike categories, brands do not nest.
/// </summary>
public class Brand
{
    private Brand()
    {
    }

    public Brand(string name, string slug)
    {
        Id = Guid.CreateVersion7();
        Name = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(name), 100);
        Slug = Guard.AgainstNullOrWhiteSpace(slug).ToLowerInvariant();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public ICollection<Product> Products { get; private set; } = [];
}
