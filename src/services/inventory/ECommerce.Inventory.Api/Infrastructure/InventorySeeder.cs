using ECommerce.Inventory.Api.Model;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Inventory.Api.Infrastructure;

/// <summary>
/// Seeds stock for the demo catalogue — one row per sellable <b>variant</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stock is held per variant SKU, and always was.</b> <c>StockItem</c> keys on a SKU string, so when
/// Catalog gained sizes and colours ([ADR-0020](../../../../docs/adr/0020-product-variants.md)) this service
/// needed no schema change at all — just more rows. Inventory does not know what a size is. That is not
/// luck; it is what drawing the context boundary at the SKU bought us.
/// </para>
/// <para>
/// <b>These numbers match Catalog's cached <c>stock_on_hand</c> exactly</b>, and the two lists have to be
/// changed together. Catalog keeps its copy so a product page can say "Only 2 left" without calling
/// Inventory on every page view; Inventory holds the authoritative figure. Duplicating the seed across the
/// boundary is deliberate — a shared seed library would couple two services that are supposed to own their
/// own data — and the cost is exactly this paragraph.
/// </para>
/// <para>
/// In a complete implementation Catalog would subscribe to a <c>StockLevelChanged</c> event and update its
/// copy. That is left out on purpose: it is the same outbox-and-consumer pattern already demonstrated three
/// times, and a fourth copy would be repetition rather than teaching. The gap is named here rather than
/// left for a reader to discover.
/// </para>
/// <para>
/// The spread is chosen so every UI state is reachable without editing the database: in stock in every
/// size, <b>low in one size while fine in the others</b>, one size sold out while the product is not, and a
/// product sold out entirely. <c>FB-ST-003</c> — the £5,200 Leather Portfolio — is stocked precisely so the
/// <b>payment failure and compensation path</b> can be triggered from the storefront: it reserves
/// successfully and is then declined for exceeding the payment limit.
/// </para>
/// </remarks>
public static class InventorySeeder
{
    /// <summary>The sizes, in the same order Catalog seeds them.</summary>
    private static readonly string[] Sizes = ["S", "M", "L", "XL"];

    /// <summary>A colourway: its SKU suffix and the stock in each size (or one figure when unsized).</summary>
    private sealed record Colourway(string? Suffix, bool Sized, int[] Stock);

    /// <summary>
    /// The same plan Catalog seeds, expressed as stock rather than as merchandising.
    /// </summary>
    /// <remarks>
    /// Suffixes are Catalog's colour abbreviations — first three letters of each word, upper-cased. They are
    /// spelled out here rather than derived, because deriving them would mean copying an algorithm across a
    /// service boundary and quietly depending on it never changing. Copying the OUTPUT is honest; copying
    /// the function would look like sharing without being it.
    /// </remarks>
    private static readonly (string StyleCode, string Name, Colourway[] Colourways)[] Plan =
    [
        // --- Spec-bought: 200 of every variant. A paid order holds its reservation until it ships and
        //     nothing here ships automatically, so every demo run permanently consumes stock.
        ("NW-TS-001", "Classic Cotton T-shirt", [new("NAV", true, [200, 200, 200, 200]), new("WHI", true, [200, 200, 200, 200])]),
        ("NW-TS-002", "Long Sleeve T-shirt", [new("CHA", true, [200, 200, 200, 200]), new("SAG", true, [200, 200, 200, 200])]),
        ("NW-HD-001", "Pullover Hoodie", [new("NAV", true, [200, 200, 200, 200]), new("GREMAR", true, [200, 200, 200, 200])]),
        ("NW-DW-001", "Enamel Mug", [new("SPEWHI", false, [200]), new("SPEBLU", false, [200])]),
        ("CT-DW-002", "Insulated Bottle", [new("BRUSTE", false, [200]), new("MATBLA", false, [200])]),
        ("FB-DW-003", "Ceramic Mug", [new("WHI", false, [200]), new("CHA", false, [200])]),
        ("NW-ST-001", "Dot Grid Notebook", [new(null, false, [200])]),

        // The £5,200 item: it reserves successfully and is then declined by the payment simulator, which
        // is how the compensation path is demonstrated. 200 so repeated runs do not exhaust it.
        ("FB-ST-003", "Leather Portfolio", [new(null, false, [200])]),

        // --- Deliberately scarce, so low-stock and out-of-stock are reachable. No spec buys these.
        //
        // CT-TS-003 is the one that justifies per-variant stock existing at all: 2 left in Black S, none
        // in Black XL, and the product itself still comfortably in stock.
        ("CT-TS-003", "Graphic Print T-shirt", [new("BLA", true, [2, 14, 10, 0]), new("ECR", true, [8, 6, 5, 1])]),
        ("CT-HD-002", "Zip-through Hoodie", [new("FOR", true, [1, 4, 3, 2])]),
        ("FB-HD-003", "Heavyweight Hoodie", [new("OAT", true, [0, 0, 0, 0])]),
        ("CT-ST-002", "Fineliner Set", [new(null, false, [0])]),
    ];

    public static async Task SeedAsync(InventoryDbContext db, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!enabled || await db.StockItems.AnyAsync())
        {
            return;
        }

        var items = new List<StockItem>();

        foreach ((string styleCode, string name, Colourway[] colourways) in Plan)
        {
            foreach (Colourway colourway in colourways)
            {
                if (colourway.Sized)
                {
                    for (int i = 0; i < Sizes.Length; i++)
                    {
                        items.Add(new StockItem(
                            $"{styleCode}-{Sizes[i]}-{colourway.Suffix}",
                            name,
                            colourway.Stock[i]));
                    }
                }
                else
                {
                    // No colour either: the variant SKU is the style code. Matches Catalog's rule for a
                    // product with neither axis.
                    string sku = colourway.Suffix is null ? styleCode : $"{styleCode}-{colourway.Suffix}";
                    items.Add(new StockItem(sku, name, colourway.Stock[0]));
                }
            }
        }

        db.StockItems.AddRange(items);

        await db.SaveChangesAsync();
    }
}
