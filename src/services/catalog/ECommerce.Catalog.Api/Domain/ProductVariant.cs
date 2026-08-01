using ECommerce.Common.Exceptions;
using ECommerce.Common.Guards;

namespace ECommerce.Catalog.Api.Domain;

/// <summary>
/// The product already sells this size-and-colour combination.
/// </summary>
/// <remarks>
/// Names all three parts, because "duplicate variant" without saying which one forces whoever reads it to go
/// and look. <c>DomainException</c> is translated to RFC 7807 by <c>DomainExceptionHandler</c>, so this
/// reaches the admin panel as a readable 400 rather than a 500.
/// </remarks>
public sealed class DuplicateVariantException(string productSku, string? size, string? colour)
    : DomainException(
        $"{productSku} already has a variant in {Describe(size, colour)}.")
{
    private static string Describe(string? size, string? colour) => (size, colour) switch
    {
        (null, null) => "the default option",
        (not null, null) => $"size {size}",
        (null, not null) => colour!,
        _ => $"size {size}, {colour}",
    };
}

/// <summary>
/// One sellable unit of a <see cref="Product"/> — a specific size and colour.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the thing a customer actually buys</b> ([ADR-0020](../../../docs/adr/0020-product-variants.md)).
/// A <see cref="Product"/> is the style — the name, the description, the price, the photograph. A variant is
/// the medium navy one, of which there are three left.
/// </para>
/// <para>
/// <b>The SKU lives here, and that is the important part.</b> SKU is the string that crosses every context
/// boundary in this system: Inventory keys its stock on it, Ordering snapshots it onto an order line, Basket
/// carries it. Moving it down from <see cref="Product"/> to the variant meant no downstream service had to
/// learn what a size is — Inventory simply has more rows. The boundaries were drawn at the right place.
/// </para>
/// <para>
/// <b>Every product has at least one variant</b>, including a leather portfolio that comes in one size and
/// one colour. A nullable relationship would mean two rendering paths and two query paths, and the simple
/// one is the one that would quietly rot.
/// </para>
/// </remarks>
public class ProductVariant
{
    private ProductVariant()
    {
        // EF Core.
    }

    public ProductVariant(
        Guid productId,
        string sku,
        string? size,
        string? colourName,
        string? colourHex)
    {
        Id = Guid.CreateVersion7();
        ProductId = Guard.AgainstEmpty(productId);
        Sku = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(sku), 64).ToUpperInvariant();
        Size = Normalise(size, 20);
        ColourName = Normalise(colourName, 40);
        ColourHex = Normalise(colourHex, 7);
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    public Product? Product { get; private set; }

    /// <summary>
    /// The sellable SKU — unique across the whole catalogue, not just within the product.
    /// </summary>
    /// <remarks>
    /// Conventionally <c>{style code}-{size}-{colour}</c>, e.g. <c>NW-TS-001-M-NAV</c>, but the convention is
    /// a courtesy to whoever reads a picking list rather than a rule the code depends on. Nothing parses it.
    /// </remarks>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>
    /// <c>"S"</c>, <c>"M"</c>, <c>"L"</c>… or <b>null</b> when the product has no size axis.
    /// </summary>
    /// <remarks>
    /// Null rather than <c>"One size"</c>: null means "this product is not sized", which lets the UI omit the
    /// size selector entirely instead of showing a control with one disabled option. A mug does not come in a
    /// size, and saying it comes in one size is a different, wronger statement.
    /// </remarks>
    public string? Size { get; private set; }

    /// <summary>The colour as a customer reads it — <c>"Navy"</c>. Null when the product has no colour axis.</summary>
    public string? ColourName { get; private set; }

    /// <summary>
    /// The colour as a swatch — <c>"#1e3a8a"</c>.
    /// </summary>
    /// <remarks>
    /// Stored beside the name, never instead of it. A swatch alone fails WCAG 1.4.1 — colour is the only
    /// channel carrying the information — and it is unreadable to anyone who cannot distinguish the two
    /// shades. The name is the accessible fact; the hex is decoration on top of it.
    /// </remarks>
    public string? ColourHex { get; private set; }

    /// <summary>
    /// A cached, eventually-consistent count for display. Inventory owns the truth.
    /// </summary>
    /// <remarks>
    /// Exactly the same contract as <see cref="Product.StockOnHand"/> had, one level further down: it is
    /// allowed to be a few seconds stale, because the authoritative check happens when the saga reserves
    /// stock at checkout. The place you must be exactly right is the reservation, not the browse page.
    /// </remarks>
    public int StockOnHand { get; private set; }

    /// <summary>Soft delete, so historic orders referencing this SKU stay meaningful.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>How this variant reads in a basket line: <c>"M · Navy"</c>.</summary>
    public string Describe() => (Size, ColourName) switch
    {
        (null, null) => string.Empty,
        (not null, null) => Size!,
        (null, not null) => ColourName!,
        _ => $"{Size} · {ColourName}",
    };

    /// <summary>Applies a stock figure received from Inventory. Never sets it authoritatively.</summary>
    public void SyncStock(int stockOnHand)
    {
        StockOnHand = stockOnHand < 0 ? 0 : stockOnHand;
        Touch();
    }

    public void UpdateOptions(string? size, string? colourName, string? colourHex)
    {
        Size = Normalise(size, 20);
        ColourName = Normalise(colourName, 40);
        ColourHex = Normalise(colourHex, 7);
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

    /// <summary>Empty and whitespace both mean "no value on this axis", so both become null.</summary>
    private static string? Normalise(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.AgainstTooLong(value.Trim(), maxLength);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
