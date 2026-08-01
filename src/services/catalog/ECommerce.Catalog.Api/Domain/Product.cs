using ECommerce.Common.Guards;

namespace ECommerce.Catalog.Api.Domain;

/// <summary>
/// A sellable item as <i>merchandising</i> understands it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately simple.</b> Catalog is a <i>supporting</i> subdomain
/// (<c>docs/domain/bounded-contexts.md</c>): necessary, but not where the business differentiates. It gets
/// plain entities with guarded constructors — no aggregate roots, no domain events, no four-layer split.
/// </para>
/// <para>
/// Compare with <c>Ordering</c>, which is the <b>core</b> subdomain and does get the full treatment. Spending
/// equal design effort everywhere is the most reliable way to run out of budget before the core domain is
/// right, so the asymmetry is the point rather than an inconsistency.
/// </para>
/// <para>
/// <b>What this deliberately does not own:</b> the authoritative stock level (Inventory) and the price a
/// customer actually paid (Ordering). <see cref="StockOnHand"/> below is a cached display figure, not the
/// truth.
/// </para>
/// </remarks>
public class Product
{
    // EF Core needs a parameterless constructor to materialise entities. `private` keeps it away from
    // application code, so the guarded constructor below is the only way to create a Product in our code.
    private Product()
    {
    }

    public Product(
        string sku,
        string name,
        string description,
        decimal price,
        string currency,
        Guid categoryId,
        Guid brandId,
        string? imageUrl = null,
        Audience audience = Audience.Unisex)
    {
        Id = Guid.CreateVersion7();
        Sku = Guard.AgainstNullOrWhiteSpace(sku);
        Name = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(name), 200);
        Description = description ?? string.Empty;
        Price = Guard.AgainstNegative(price);
        Currency = Guard.AgainstNullOrWhiteSpace(currency).ToUpperInvariant();
        CategoryId = Guard.AgainstEmpty(categoryId);
        BrandId = Guard.AgainstEmpty(brandId);
        ImageUrl = imageUrl;
        Audience = audience;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The <b>style code</b> — <c>NW-TS-001</c>. Unique, enforced by a database index rather than by hoping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is no longer the sellable SKU.</b> It identifies the style; what a customer buys is a
    /// <see cref="ProductVariant"/>, and the variant owns the SKU that Inventory, Basket and Ordering key on
    /// ([ADR-0020](../../../docs/adr/0020-product-variants.md)).
    /// </para>
    /// <para>
    /// Both indexes exist and they guarantee different things — unique styles here, unique sellable units on
    /// <c>product_variants.sku</c>. Reading only one of them leads to the wrong conclusion.
    /// </para>
    /// </remarks>
    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// The advertised list price. <b>Not necessarily the price paid.</b>
    /// </summary>
    /// <remarks>
    /// When an order is placed, Ordering copies this onto the order line as a snapshot. A price change next
    /// week must not retroactively alter last week's order — see <c>docs/architecture.md §6</c>.
    /// </remarks>
    public decimal Price { get; private set; }

    /// <summary>
    /// ISO 4217 code. Stored alongside the amount because an amount without a currency is not a price.
    /// </summary>
    public string Currency { get; private set; } = "GBP";

    public Guid CategoryId { get; private set; }

    public Category? Category { get; private set; }

    public Guid BrandId { get; private set; }

    public Brand? Brand { get; private set; }

    public string? ImageUrl { get; private set; }

    /// <summary>
    /// Who the product is made for. An attribute, not a category — see <see cref="Domain.Audience"/>.
    /// </summary>
    public Audience Audience { get; private set; } = Audience.Unisex;

    /// <summary>
    /// The sellable units: sizes and colours. <b>Never empty.</b>
    /// </summary>
    /// <remarks>
    /// A product with no size and no colour axis still has exactly one variant, so there is no "simple
    /// product" code path to diverge from the real one.
    /// </remarks>
    public List<ProductVariant> Variants { get; private set; } = [];

    /// <summary>
    /// A <b>cached, eventually-consistent</b> stock figure for display only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inventory owns the truth. Catalog keeps this copy, updated by subscribing to Inventory's stock events
    /// (Phase 7), purely so a product page can say "3 left" without a synchronous call on every page view.
    /// </para>
    /// <para>
    /// <b>It is allowed to be wrong,</b> and that is the interesting part. A product page a few seconds stale
    /// is fine, because the authoritative check happens when stock is reserved during checkout. The place you
    /// must be exactly right is the reservation, not the browse page — which is the whole lesson about
    /// choosing where eventual consistency is acceptable.
    /// </para>
    /// </remarks>
    public int StockOnHand { get; private set; }

    /// <summary>
    /// Soft delete. Deactivating rather than deleting keeps historic orders referencing this SKU meaningful.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public void UpdateDetails(string name, string description, string? imageUrl)
    {
        Name = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(name), 200);
        Description = description ?? string.Empty;
        ImageUrl = imageUrl;
        Touch();
    }

    /// <summary>
    /// Changes the advertised price.
    /// </summary>
    /// <remarks>
    /// In Phase 6 this also raises <c>ProductPriceChanged</c>, which Basket consumes to update and
    /// <b>flag</b> affected lines — rather than silently repricing what a customer thought they were buying.
    /// </remarks>
    public void ChangePrice(decimal newPrice)
    {
        Price = Guard.AgainstNegative(newPrice);
        Touch();
    }

    /// <summary>
    /// Moves the product to a different category or brand.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="UpdateDetails"/> because it changes where the product APPEARS rather than
    /// what it says - a merchandiser reorganising the taxonomy and one fixing a typo are doing different
    /// jobs, and keeping them apart makes the intent readable in a log.
    ///
    /// The ids are checked against the taxonomy by the caller, not here: the domain project references
    /// nothing outside the BCL, so it cannot query for them. That is the layering working as intended
    /// rather than a gap.
    /// </remarks>
    public void MoveTo(Guid categoryId, Guid brandId)
    {
        CategoryId = Guard.AgainstEmpty(categoryId);
        BrandId = Guard.AgainstEmpty(brandId);
        Touch();
    }

    /// <summary>Changes who the product is sold to.</summary>
    public void SetAudience(Audience audience)
    {
        Audience = audience;
        Touch();
    }

    /// <summary>Applies a stock figure received from Inventory. Never sets it authoritatively.</summary>
    public void SyncStock(int stockOnHand)
    {
        StockOnHand = stockOnHand < 0 ? 0 : stockOnHand;
        Touch();
    }

    /// <summary>
    /// Adds a sellable size/colour combination.
    /// </summary>
    /// <remarks>
    /// Refuses a duplicate combination rather than creating a second row for the same thing: two variants
    /// both meaning "Medium, Navy" would split one stock figure across two SKUs, and the shop would then
    /// report less stock than it has while a picker finds the item in one bin.
    /// </remarks>
    public ProductVariant AddVariant(string sku, string? size, string? colourName, string? colourHex)
    {
        if (Variants.Any(existing =>
                existing.IsActive
                && string.Equals(existing.Size, size, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.ColourName, colourName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateVariantException(Sku, size, colourName);
        }

        var variant = new ProductVariant(Id, sku, size, colourName, colourHex);
        Variants.Add(variant);
        Touch();

        return variant;
    }

    /// <summary>
    /// The display stock figure: the sum across active variants.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, for the same reason an order total is — a stored copy is a second source
    /// of truth that drifts the first time somebody updates one number and not the other. Callers that have
    /// loaded the variants should prefer this; <see cref="StockOnHand"/> remains for the read model, which
    /// computes the same sum in SQL.
    /// </remarks>
    public int TotalStock() => Variants.Where(variant => variant.IsActive).Sum(variant => variant.StockOnHand);

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
