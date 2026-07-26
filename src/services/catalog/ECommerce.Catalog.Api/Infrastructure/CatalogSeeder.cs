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
            new("NW-TS-001", "Classic Cotton T-shirt", "Heavyweight combed cotton, ribbed collar, pre-shrunk.", 18.00m, "GBP", tshirts.Id, northwind.Id, "/img/tshirt-classic.svg"),
            new("NW-TS-002", "Long Sleeve T-shirt", "Same cotton, longer sleeves, slightly relaxed fit.", 24.00m, "GBP", tshirts.Id, northwind.Id, "/img/tshirt-long.svg"),
            new("CT-TS-003", "Graphic Print T-shirt", "Water-based print that stays soft after washing.", 22.50m, "GBP", tshirts.Id, contoso.Id, "/img/tshirt-graphic.svg"),
            new("NW-HD-001", "Pullover Hoodie", "Brushed fleece lining, kangaroo pocket, drawstring hood.", 45.00m, "GBP", hoodies.Id, northwind.Id, "/img/hoodie-pullover.svg"),
            new("CT-HD-002", "Zip-through Hoodie", "Full-length YKK zip, side pockets.", 52.00m, "GBP", hoodies.Id, contoso.Id, "/img/hoodie-zip.svg"),
            new("FB-HD-003", "Heavyweight Hoodie", "450gsm loopback cotton. Substantial.", 68.00m, "GBP", hoodies.Id, fabrikam.Id, "/img/hoodie-heavy.svg"),
            new("NW-DW-001", "Enamel Mug", "Speckled enamel, stainless rim, 350ml.", 12.00m, "GBP", drinkware.Id, northwind.Id, "/img/mug-enamel.svg"),
            new("CT-DW-002", "Insulated Bottle", "Double-walled steel, keeps drinks cold 24h.", 28.00m, "GBP", drinkware.Id, contoso.Id, "/img/bottle-insulated.svg"),
            new("FB-DW-003", "Ceramic Mug", "Glazed stoneware, dishwasher safe, 400ml.", 14.50m, "GBP", drinkware.Id, fabrikam.Id, "/img/mug-ceramic.svg"),
            new("NW-ST-001", "Dot Grid Notebook", "A5, 160gsm paper, lay-flat binding.", 16.00m, "GBP", stationery.Id, northwind.Id, "/img/notebook-dot.svg"),
            new("CT-ST-002", "Fineliner Set", "Six pens, 0.4mm, pigment ink.", 9.50m, "GBP", stationery.Id, contoso.Id, "/img/pens-fineliner.svg"),
            new("FB-ST-003", "Leather Portfolio", "Full-grain leather, fits A4, ages well.", 5200.00m, "GBP", stationery.Id, fabrikam.Id, "/img/portfolio-leather.svg"),
        ];

        // A spread of stock levels so the UI's in-stock, low-stock and out-of-stock states are all
        // reachable without editing the database.
        int[] stock = [42, 18, 7, 25, 3, 0, 60, 12, 33, 21, 0, 2];
        for (int i = 0; i < products.Length; i++)
        {
            products[i].SyncStock(stock[i]);
        }

        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeded {ProductCount} products, {CategoryCount} categories, {BrandCount} brands.",
            products.Length,
            6,
            3);
    }
}
