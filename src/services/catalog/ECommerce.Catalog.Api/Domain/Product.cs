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
        string? imageUrl = null)
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
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Stock Keeping Unit — the identifier Catalog and Inventory both agree on.
    /// </summary>
    /// <remarks>
    /// The one string shared across a context boundary, which makes it the integration key between the two.
    /// Unique, and enforced by a database index rather than by hoping.
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

    /// <summary>Applies a stock figure received from Inventory. Never sets it authoritatively.</summary>
    public void SyncStock(int stockOnHand)
    {
        StockOnHand = stockOnHand < 0 ? 0 : stockOnHand;
        Touch();
    }

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
