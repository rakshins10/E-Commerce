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

    public ICollection<Product> Products { get; private set; } = [];
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
