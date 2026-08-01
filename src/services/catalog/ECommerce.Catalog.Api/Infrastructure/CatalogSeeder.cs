using ECommerce.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Catalog.Api.Infrastructure;

/// <summary>
/// Applies migrations and seeds demo data on startup.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes <c>docker compose up</c> produce a browsable shop rather than an empty one. A demo you
/// have to populate by hand is a demo nobody runs.
/// </para>
/// <para>
/// <b>Idempotent</b> — it checks before inserting, so a restart does not duplicate anything.
/// </para>
/// <para>
/// <b>Simplified for this repo:</b> production would not migrate from application startup. Several replicas
/// starting at once would race each other, and a failed migration would crash every instance rather than one
/// deployment step. Production runs migrations as a separate job that must succeed before the new version is
/// rolled out — see <c>docs/operations/deployment.md</c>.
/// </para>
/// </remarks>
public static class CatalogSeeder
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, ILogger logger, bool seedDemoData)
    {
        using IServiceScope scope = services.CreateScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        logger.LogInformation("Applying Catalog migrations…");
        await db.Database.MigrateAsync();

        if (!seedDemoData)
        {
            logger.LogInformation("SEED_DEMO_DATA is off; skipping catalog seed.");
            return;
        }

        if (await db.Products.AnyAsync())
        {
            logger.LogInformation("Catalog already seeded; nothing to do.");
            return;
        }

        logger.LogInformation("Seeding catalog demo data…");

        var clothing = new Category("Clothing", "clothing");
        var tshirts = new Category("T-shirts", "t-shirts", clothing.Id);
        var hoodies = new Category("Hoodies", "hoodies", clothing.Id);
        var accessories = new Category("Accessories", "accessories");
        var drinkware = new Category("Drinkware", "drinkware", accessories.Id);
        var stationery = new Category("Stationery", "stationery", accessories.Id);

        var northwind = new Brand("Northwind", "northwind");
        var contoso = new Brand("Contoso", "contoso");
        var fabrikam = new Brand("Fabrikam", "fabrikam");

        db.Categories.AddRange(clothing, tshirts, hoodies, accessories, drinkware, stationery);
        db.Brands.AddRange(northwind, contoso, fabrikam);

        // Prices span the payment simulator's decline threshold (5000) on purpose, so the saga's
        // compensation path can be demonstrated without editing anything.
        Product[] products =
        [
            new("NW-TS-001", "Classic Cotton T-shirt", "Heavyweight combed cotton, ribbed collar, pre-shrunk.", 18.00m, "GBP", tshirts.Id, northwind.Id, "/img/tshirt-classic.svg", Audience.Men),
            new("NW-TS-002", "Long Sleeve T-shirt", "Same cotton, longer sleeves, slightly relaxed fit.", 24.00m, "GBP", tshirts.Id, northwind.Id, "/img/tshirt-long.svg", Audience.Women),
            new("CT-TS-003", "Graphic Print T-shirt", "Water-based print that stays soft after washing.", 22.50m, "GBP", tshirts.Id, contoso.Id, "/img/tshirt-graphic.svg", Audience.Unisex),
            new("NW-HD-001", "Pullover Hoodie", "Brushed fleece lining, kangaroo pocket, drawstring hood.", 45.00m, "GBP", hoodies.Id, northwind.Id, "/img/hoodie-pullover.svg", Audience.Men),
            new("CT-HD-002", "Zip-through Hoodie", "Full-length YKK zip, side pockets.", 52.00m, "GBP", hoodies.Id, contoso.Id, "/img/hoodie-zip.svg", Audience.Women),
            new("FB-HD-003", "Heavyweight Hoodie", "450gsm loopback cotton. Substantial.", 68.00m, "GBP", hoodies.Id, fabrikam.Id, "/img/hoodie-heavy.svg", Audience.Unisex),
            new("NW-DW-001", "Enamel Mug", "Speckled enamel, stainless rim, 350ml.", 12.00m, "GBP", drinkware.Id, northwind.Id, "/img/mug-enamel.svg"),
            new("CT-DW-002", "Insulated Bottle", "Double-walled steel, keeps drinks cold 24h.", 28.00m, "GBP", drinkware.Id, contoso.Id, "/img/bottle-insulated.svg"),
            new("FB-DW-003", "Ceramic Mug", "Glazed stoneware, dishwasher safe, 400ml.", 14.50m, "GBP", drinkware.Id, fabrikam.Id, "/img/mug-ceramic.svg"),
            new("NW-ST-001", "Dot Grid Notebook", "A5, 160gsm paper, lay-flat binding.", 16.00m, "GBP", stationery.Id, northwind.Id, "/img/notebook-dot.svg"),
            new("CT-ST-002", "Fineliner Set", "Six pens, 0.4mm, pigment ink.", 9.50m, "GBP", stationery.Id, contoso.Id, "/img/pens-fineliner.svg"),
            new("FB-ST-003", "Leather Portfolio", "Full-grain leather, fits A4, ages well.", 5200.00m, "GBP", stationery.Id, fabrikam.Id, "/img/portfolio-leather.svg"),
        ];

        SeedVariants(products);

        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        int variantCount = products.Sum(product => product.Variants.Count);

        logger.LogInformation(
            "Seeded {ProductCount} products with {VariantCount} variants, {CategoryCount} categories, {BrandCount} brands.",
            products.Length,
            variantCount,
            6,
            3);
    }

    /// <summary>The four clothing sizes, in the order a human expects to see them — never alphabetical.</summary>
    private static readonly string[] ClothingSizes = ["S", "M", "L", "XL"];

    /// <summary>
    /// One colourway of one style, and how many of each size are on the shelf.
    /// </summary>
    /// <remarks>
    /// <c>Stock</c> holds one figure per entry in <see cref="ClothingSizes"/> when <c>Sized</c> is true, and a
    /// single figure otherwise. A null <c>Colour</c> means the product has no colour axis either.
    /// </remarks>
    private sealed record Colourway(string? Colour, string? Hex, bool Sized, int[] Stock);

    /// <summary>
    /// The whole variant plan, as data.
    /// </summary>
    /// <remarks>
    /// A static table rather than a sequence of calls with array literals in them, because the analyzers
    /// reject the latter (CA1861 — a constant array argument is reallocated on every call). Reading better
    /// than the calls did is a bonus: the entire seeded catalogue is one thing you can scan.
    /// </remarks>
    private static readonly Dictionary<string, Colourway[]> VariantPlan = new(StringComparer.Ordinal)
    {
        // --- Clothing: size × colour --------------------------------------------------------------
        ["NW-TS-001"] =
        [
            new("Navy", "#1e3a8a", true, [200, 200, 200, 200]),
            new("White", "#f8fafc", true, [200, 200, 200, 200]),
        ],
        ["NW-TS-002"] =
        [
            new("Charcoal", "#334155", true, [200, 200, 200, 200]),
            new("Sage", "#84a98c", true, [200, 200, 200, 200]),
        ],

        // Low in S and sold out in XL while the product itself is in stock. This is the row that makes a
        // per-size count mean something.
        ["CT-TS-003"] =
        [
            new("Black", "#0f172a", true, [2, 14, 10, 0]),
            new("Ecru", "#e7e5e4", true, [8, 6, 5, 1]),
        ],
        ["NW-HD-001"] =
        [
            new("Navy", "#1e3a8a", true, [200, 200, 200, 200]),
            new("Grey Marl", "#9ca3af", true, [200, 200, 200, 200]),
        ],
        // Total 2, deliberately. Product-level stock is the SUM across variants, so the low-stock
        // CARD state needs a product that is low in total - not merely low in one size. Without this
        // every card reads "In stock" and a whole branch of the UI is unreachable from a clean seed.
        ["CT-HD-002"] = [new("Forest", "#166534", true, [1, 1, 0, 0])],

        // Sold out in every size. The product page must still render, and say so.
        ["FB-HD-003"] = [new("Oatmeal", "#d6d3d1", true, [0, 0, 0, 0])],

        // --- Drinkware: colour, no size -----------------------------------------------------------
        // A mug does not come in a size, and saying it comes in "one size" is a different and wronger
        // statement — so Size stays null and the UI omits the selector entirely.
        ["NW-DW-001"] =
        [
            new("Speckled White", "#f1f5f9", false, [200]),
            new("Speckled Blue", "#60a5fa", false, [200]),
        ],
        ["CT-DW-002"] =
        [
            new("Brushed Steel", "#cbd5e1", false, [200]),
            new("Matte Black", "#1f2937", false, [200]),
        ],
        ["FB-DW-003"] =
        [
            new("White", "#f8fafc", false, [200]),
            new("Charcoal", "#334155", false, [200]),
        ],

        // --- Stationery: neither axis -------------------------------------------------------------
        // One variant, no selectors, and the same code path as everything above.
        ["NW-ST-001"] = [new(null, null, false, [200])],
        ["CT-ST-002"] = [new(null, null, false, [0])],
        ["FB-ST-003"] = [new(null, null, false, [200])],
    };

    /// <summary>
    /// Gives every product its sellable variants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stock is spread on purpose, not randomly.</b> Every state the UI can render has to be reachable
    /// from a clean <c>docker compose up</c>, or the states nobody seeds are the states nobody tests:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>a product in stock in every size,</description></item>
    ///   <item><description>a product low in one size and fine in the others — the case that justifies
    ///     per-variant stock existing at all,</description></item>
    ///   <item><description>a size that is sold out while the product is not,</description></item>
    ///   <item><description>a product sold out entirely,</description></item>
    ///   <item><description>a product with colours but no sizes, and one with neither.</description></item>
    /// </list>
    /// <para>
    /// Products the checkout specs buy hold <b>200 of every variant</b>. A paid order keeps its stock
    /// reservation until it ships and nothing here ships automatically, so every demo run permanently
    /// consumes stock; a realistic figure on a spec-bought SKU drains within a day of testing and the saga
    /// specs then fail with a perfectly correct "Out of stock" — see the gotcha table in CLAUDE.md.
    /// </para>
    /// <para>
    /// <b>These figures are mirrored exactly in <c>InventorySeeder</c></b>, which holds the authoritative
    /// numbers. Duplicated across the service boundary on purpose: a shared seed library would couple two
    /// services that are supposed to own their own data. The cost is that the two lists must be changed
    /// together, which is why both say so.
    /// </para>
    /// </remarks>
    private static void SeedVariants(IReadOnlyList<Product> products)
    {
        foreach (Product product in products)
        {
            foreach (Colourway colourway in VariantPlan[product.Sku])
            {
                if (colourway.Sized)
                {
                    for (int i = 0; i < ClothingSizes.Length; i++)
                    {
                        product
                            .AddVariant(
                                $"{product.Sku}-{ClothingSizes[i]}-{Abbreviate(colourway.Colour)}",
                                ClothingSizes[i],
                                colourway.Colour,
                                colourway.Hex)
                            .SyncStock(colourway.Stock[i]);
                    }
                }
                else
                {
                    // With no colour either, the variant SKU equals the style code. A coincidence of there
                    // being one variant, not a rule — nothing anywhere parses or compares the two.
                    string sku = colourway.Colour is null
                        ? product.Sku
                        : $"{product.Sku}-{Abbreviate(colourway.Colour)}";

                    product
                        .AddVariant(sku, size: null, colourway.Colour, colourway.Hex)
                        .SyncStock(colourway.Stock[0]);
                }
            }

            // The product-level figure is the sum, and it is written once here rather than maintained.
            product.SyncStock(product.TotalStock());
        }
    }

    /// <summary>"Grey Marl" -> "GREMAR". Readable on a picking list, and short enough for a SKU.</summary>
    private static string Abbreviate(string? colour)
    {
        ArgumentNullException.ThrowIfNull(colour);

        string[] words = colour.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length == 1
            ? words[0][..Math.Min(3, words[0].Length)].ToUpperInvariant()
            : string.Concat(words.Select(word => word[..Math.Min(3, word.Length)])).ToUpperInvariant();
    }
}
