using System.Data;
using System.Text;
using Dapper;
using ECommerce.Common.Pagination;

namespace ECommerce.Catalog.Api.Features.Products;

/// <summary>What a product looks like in a list. Only the fields a card actually renders.</summary>
public sealed record ProductSummaryDto(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    string CategoryName,
    string CategorySlug,
    string BrandName,
    string BrandSlug,
    string? ImageUrl,
    int StockOnHand);

/// <summary>The full product, for a detail page.</summary>
public sealed record ProductDetailDto(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string CategoryName,
    string CategorySlug,
    string BrandName,
    string BrandSlug,
    string? ImageUrl,
    int StockOnHand);

/// <summary>
/// What a product costs right now, for checkout.
/// </summary>
/// <remarks>
/// Deliberately minimal. Ordering needs a price, a name and whether the product can still be sold;
/// sending the description and the image as well would be shipping a detail page to a service that
/// renders nothing.
/// </remarks>
public sealed record ProductPriceDto(
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    string Currency,
    bool IsAvailable);

public sealed record CategoryDto(Guid Id, string Name, string Slug, string? ParentSlug, int ProductCount);

public sealed record BrandDto(Guid Id, string Name, string Slug, int ProductCount);

/// <summary>Everything the browse endpoint can be filtered and sorted by.</summary>
public sealed record ProductQuery(
    string? Search = null,
    string? Category = null,
    string? Brand = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool InStockOnly = false,
    string? SortBy = null,
    bool SortDescending = false,
    int Page = 1,
    int PageSize = 12);

/// <summary>
/// The CQRS <b>read side</b> for Catalog.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> CQRS — separate read and write paths.
/// See <c>docs/adr/0012-cqrs-with-mediatr.md</c>.
/// </para>
/// <para>
/// <b>Dapper, not EF Core, and that is the point.</b> Rendering a grid of twelve product cards through the
/// write model would load twelve full <c>Product</c> entities plus their <c>Category</c> and <c>Brand</c>
/// navigations, attach every one to the change tracker, and then throw the lot away. Here the SQL selects
/// exactly the columns a card draws.
/// </para>
/// <para>
/// It is also a <b>hard boundary</b>. <c>AsNoTracking()</c> would remove the tracking cost but keep you in
/// the entity's shape, which quietly invites navigation properties added <i>for reads</i> — and those
/// corrupt the write model. Returning DTOs from hand-written SQL makes it impossible for a query to touch a
/// domain type.
/// </para>
/// <para>
/// The honest cost: this SQL is not refactoring-safe. Rename a column and nothing breaks until a test runs,
/// which is exactly why the read side needs integration tests against a real database rather than a mock.
/// </para>
/// </remarks>
public sealed class ProductQueries(IDbConnection connection)
{
    // Every query reads from this projection. One place to change if the shape changes, and it keeps each
    // query below down to its WHERE and ORDER BY.
    private const string BaseSelect = """
        SELECT  p.id            AS Id,
                p.sku           AS Sku,
                p.name          AS Name,
                p.price         AS Price,
                p.currency      AS Currency,
                c.name          AS CategoryName,
                c.slug          AS CategorySlug,
                b.name          AS BrandName,
                b.slug          AS BrandSlug,
                p.image_url    AS ImageUrl,
                p.stock_on_hand AS StockOnHand
        FROM products p
        JOIN categories c ON c.id = p.category_id
        JOIN brands     b ON b.id = p.brand_id
        """;

    public async Task<PagedResult<ProductSummaryDto>> SearchAsync(
        ProductQuery query,
        CancellationToken cancellationToken = default)
    {
        PageRequest page = new PageRequest(query.Page, query.PageSize).Normalise();

        var where = new StringBuilder(" WHERE p.is_active = TRUE");
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ILIKE is Postgres's case-insensitive LIKE. Parameterised, never concatenated - string
            // concatenation here is how SQL injection happens, and Dapper gives us no protection we do not
            // ask for.
            where.Append(" AND (p.name ILIKE @Search OR p.sku ILIKE @Search OR p.description ILIKE @Search)");
            parameters.Add("Search", $"%{query.Search.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            // Matches the category OR any of its children, so /products?category=clothing returns t-shirts
            // and hoodies too. Without this, a top-level category would look empty.
            where.Append(" AND (c.slug = @Category OR c.parent_id IN (SELECT id FROM categories WHERE slug = @Category))");
            parameters.Add("Category", query.Category.Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            where.Append(" AND b.slug = @Brand");
            parameters.Add("Brand", query.Brand.Trim().ToLowerInvariant());
        }

        if (query.MinPrice is not null)
        {
            where.Append(" AND p.price >= @MinPrice");
            parameters.Add("MinPrice", query.MinPrice);
        }

        if (query.MaxPrice is not null)
        {
            where.Append(" AND p.price <= @MaxPrice");
            parameters.Add("MaxPrice", query.MaxPrice);
        }

        if (query.InStockOnly)
        {
            where.Append(" AND p.stock_on_hand > 0");
        }

        string orderBy = BuildOrderBy(query.SortBy, query.SortDescending);

        parameters.Add("Limit", page.PageSize);
        parameters.Add("Offset", page.Skip);

        string sql = $"""
            {BaseSelect}
            {where}
            {orderBy}
            LIMIT @Limit OFFSET @Offset;

            SELECT COUNT(*)
            FROM products p
            JOIN categories c ON c.id = p.category_id
            JOIN brands     b ON b.id = p.brand_id
            {where};
            """;

        // Two result sets in ONE round trip. Paging needs both the page and the total, and issuing them
        // separately doubles the latency for no benefit.
        await using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<ProductSummaryDto>()).ToList();
        long total = await multi.ReadSingleAsync<long>();

        return new PagedResult<ProductSummaryDto>(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// Current prices for a set of products.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>= ANY(@Ids)</c> rather than a generated <c>IN (...)</c> list. PostgreSQL treats the array as
    /// a single parameter, so one plan is cached however many ids arrive; building the list into the
    /// SQL text produces a different statement per basket size and defeats the plan cache entirely.
    /// It is also parameterised, so there is nothing to inject.
    /// </para>
    /// <para>
    /// Withdrawn products are returned with <c>IsAvailable = false</c> rather than omitted, so the
    /// caller can name the item in its error message. Silently dropping a line would produce an order
    /// missing something the customer thought they were buying, which is far worse.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ProductPriceDto>> GetPricesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT  p.id            AS ProductId,
                    p.sku           AS Sku,
                    p.name          AS Name,
                    p.price         AS UnitPrice,
                    p.currency      AS Currency,
                    p.is_active     AS IsAvailable
            FROM products p
            WHERE p.id = ANY(@Ids);
            """;

        var command = new CommandDefinition(
            sql,
            new { Ids = productIds.ToArray() },
            cancellationToken: cancellationToken);

        return (await connection.QueryAsync<ProductPriceDto>(command)).ToArray();
    }

    /// <summary>
    /// Products currently withdrawn from sale.
    /// </summary>
    /// <remarks>
    /// The admin panel needs this because every other query filters on <c>is_active = TRUE</c> - so
    /// withdrawing something makes it vanish from the only screen that could bring it back.
    /// </remarks>
    public async Task<IReadOnlyList<ProductSummaryDto>> GetWithdrawnAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT  p.id            AS Id,
                    p.sku           AS Sku,
                    p.name          AS Name,
                    p.price         AS Price,
                    p.currency      AS Currency,
                    c.name          AS CategoryName,
                    c.slug          AS CategorySlug,
                    b.name          AS BrandName,
                    b.slug          AS BrandSlug,
                    p.image_url     AS ImageUrl,
                    p.stock_on_hand AS StockOnHand
            FROM products p
            JOIN categories c ON c.id = p.category_id
            JOIN brands     b ON b.id = p.brand_id
            WHERE p.is_active = FALSE
            ORDER BY p.name;
            """;

        return (await connection.QueryAsync<ProductSummaryDto>(
            new CommandDefinition(sql, cancellationToken: cancellationToken))).ToArray();
    }

    public async Task<ProductDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT  p.id            AS Id,
                    p.sku           AS Sku,
                    p.name          AS Name,
                    p.description   AS Description,
                    p.price         AS Price,
                    p.currency      AS Currency,
                    c.name          AS CategoryName,
                    c.slug          AS CategorySlug,
                    b.name          AS BrandName,
                    b.slug          AS BrandSlug,
                    p.image_url    AS ImageUrl,
                    p.stock_on_hand AS StockOnHand
            FROM products p
            JOIN categories c ON c.id = p.category_id
            JOIN brands     b ON b.id = p.brand_id
            WHERE p.id = @Id AND p.is_active = TRUE;
            """;

        return await connection.QuerySingleOrDefaultAsync<ProductDetailDto>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        // The count INCLUDES child categories, because filtering does.
        //
        // This used to count direct members only, which meant every top-level category advertised
        // "0 products" while selecting it returned six - Clothing has no products of its own, it has
        // T-shirts and Hoodies. A count that disagrees with what clicking it returns is worse than no
        // count at all, and it looked like an empty shop on the storefront's category tiles.
        //
        // The subquery mirrors the filter's predicate in BuildWhere EXACTLY - `own OR direct child`.
        // If one gains a level of nesting the other must, and they should be changed together.
        //
        // A category with genuinely no products still appears, with a count of zero. Hiding it would
        // make the taxonomy look different from the one the back office edits.
        const string sql = """
            SELECT  c.id                       AS Id,
                    c.name                     AS Name,
                    c.slug                     AS Slug,
                    parent.slug                AS ParentSlug,
                    (
                        SELECT COUNT(*)::int
                        FROM products p
                        JOIN categories pc ON pc.id = p.category_id
                        WHERE p.is_active = TRUE
                          AND (pc.id = c.id OR pc.parent_id = c.id)
                    )                          AS ProductCount
            FROM categories c
            LEFT JOIN categories parent ON parent.id = c.parent_id
            ORDER BY parent.slug NULLS FIRST, c.name;
            """;

        return (await connection.QueryAsync<CategoryDto>(
            new CommandDefinition(sql, cancellationToken: cancellationToken))).ToList();
    }

    public async Task<IReadOnlyList<BrandDto>> GetBrandsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT  b.id        AS Id,
                    b.name      AS Name,
                    b.slug      AS Slug,
                    COUNT(p.id)::int AS ProductCount
            FROM brands b
            LEFT JOIN products p ON p.brand_id = b.id AND p.is_active = TRUE
            GROUP BY b.id, b.name, b.slug
            ORDER BY b.name;
            """;

        return (await connection.QueryAsync<BrandDto>(
            new CommandDefinition(sql, cancellationToken: cancellationToken))).ToList();
    }

    /// <summary>
    /// Maps a sort key to a column.
    /// </summary>
    /// <remarks>
    /// <b>An allow-list, never string interpolation.</b> ORDER BY cannot be parameterised, so a caller-supplied
    /// sort field concatenated into SQL is a direct injection route — one of the few places Dapper's
    /// parameterisation cannot save you. Anything unrecognised falls back to a safe default rather than
    /// erroring, because a bad sort key is not worth a 400.
    /// </remarks>
    private static string BuildOrderBy(string? sortBy, bool descending)
    {
        string direction = descending ? "DESC" : "ASC";

        string column = sortBy?.Trim().ToLowerInvariant() switch
        {
            "price" => "p.price",
            "name" => "p.name",
            "brand" => "b.name",
            "newest" => "p.created_at",
            _ => "p.name",
        };

        // The id tiebreaker makes paging deterministic. Without it, two products with the same name can swap
        // places between page 1 and page 2, so one is shown twice and another never - a subtle paging bug
        // that only appears with duplicate sort values.
        return $" ORDER BY {column} {direction}, p.id ASC";
    }
}
