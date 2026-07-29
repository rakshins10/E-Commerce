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
        // LEFT JOIN so a category with no products still appears, with a count of zero. An INNER JOIN would
        // make empty categories vanish from the filter list, which looks like a bug to a user.
        const string sql = """
            SELECT  c.id                       AS Id,
                    c.name                     AS Name,
                    c.slug                     AS Slug,
                    parent.slug                AS ParentSlug,
                    COUNT(p.id)::int           AS ProductCount
            FROM categories c
            LEFT JOIN categories parent ON parent.id = c.parent_id
            LEFT JOIN products   p      ON p.category_id = c.id AND p.is_active = TRUE
            GROUP BY c.id, c.name, c.slug, parent.slug
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
