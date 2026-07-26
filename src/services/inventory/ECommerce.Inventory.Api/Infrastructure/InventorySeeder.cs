using ECommerce.Inventory.Api.Model;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Inventory.Api.Infrastructure;

/// <summary>
/// Seeds stock for the demo catalogue.
/// </summary>
/// <remarks>
/// <para>
/// <b>These numbers deliberately match Catalog's <c>stock_on_hand</c>.</b> Catalog keeps a cached copy so
/// the product grid can show "Only 2 left" without calling Inventory on every page load; Inventory holds
/// the authoritative figure. That duplication is a considered trade — see docs/services/catalog.md — and
/// seeding them to the same values means the demo starts consistent.
/// </para>
/// <para>
/// In a complete implementation Catalog would subscribe to a <c>StockLevelChanged</c> event and update
/// its copy. That is left out on purpose: it is the same outbox-and-consumer pattern already
/// demonstrated three times, and adding a fourth copy of it would be repetition rather than teaching.
/// The gap is named here rather than left for a reader to discover.
/// </para>
/// <para>
/// The spread of levels is chosen so every UI state is reachable without editing the database: plenty in
/// stock, low stock, and out of stock. <c>FB-ST-003</c> — the £5,200 Leather Portfolio — has stock
/// precisely so the <b>payment failure and compensation path</b> can be triggered from the storefront:
/// it reserves successfully and is then declined for exceeding the payment limit.
/// </para>
/// </remarks>
public static class InventorySeeder
{
    public static async Task SeedAsync(InventoryDbContext db, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!enabled || await db.StockItems.AnyAsync())
        {
            return;
        }

        (string Sku, string Name, int OnHand)[] items =
        [
            ("NW-TS-001", "Classic Cotton T-shirt", 42),
            ("NW-TS-002", "Long Sleeve T-shirt", 18),
            ("CT-TS-003", "Graphic Print T-shirt", 7),
            ("NW-HD-001", "Pullover Hoodie", 25),
            ("CT-HD-002", "Zip-through Hoodie", 3),
            ("FB-HD-003", "Heavyweight Hoodie", 0),
            ("NW-DW-001", "Enamel Mug", 60),
            ("CT-DW-002", "Insulated Bottle", 12),
            ("FB-DW-003", "Ceramic Mug", 33),
            ("NW-ST-001", "Dot Grid Notebook", 21),
            ("CT-ST-002", "Fineliner Set", 0),
            // In stock on purpose: this is the £5,200 item, and it is how the payment-failure and
            // stock-release path is demonstrated end to end.
            ("FB-ST-003", "Leather Portfolio", 2),
        ];

        db.StockItems.AddRange(items.Select(item => new StockItem(item.Sku, item.Name, item.OnHand)));

        await db.SaveChangesAsync();
    }
}
