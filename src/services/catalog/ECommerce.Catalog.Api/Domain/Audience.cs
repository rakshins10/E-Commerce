namespace ECommerce.Catalog.Api.Domain;

/// <summary>
/// Who a product is made for.
/// </summary>
/// <remarks>
/// <para>
/// An <b>attribute</b>, deliberately, and not a branch of the category tree
/// ([ADR-0020](../../../docs/adr/0020-product-variants.md)). The taxonomy answers <i>what is this thing</i>;
/// this answers <i>who is it for</i>. They vary independently, so they are two fields — modelling audience as
/// a category means "T-shirts" exists twice, a unisex product has to be duplicated to appear in both, and
/// adding "Kids" doubles the tree again.
/// </para>
/// <para>
/// Persisted as a <b>string</b>, not as its integer value. A database whose rows say <c>'Women'</c> can be
/// read by a human with psql and survives someone reordering the enum; one whose rows say <c>1</c> silently
/// reassigns every product the day a member is inserted in the middle.
/// </para>
/// </remarks>
public enum Audience
{
    /// <summary>Not gendered — drinkware, stationery, and clothing sold to everyone.</summary>
    Unisex = 0,

    Men = 1,

    Women = 2,
}
