using ECommerce.Auth;
using ECommerce.Common.Pagination;

namespace ECommerce.Catalog.Api.Features.Products;

/// <summary>
/// Catalog's HTTP surface.
/// </summary>
/// <remarks>
/// <para>
/// The permission each endpoint requires is declared <b>on the route</b>, so a reader can audit this
/// service's entire authorization surface by scanning the table below — and an unprotected endpoint shows up
/// as an <i>absence</i>, which is far easier to spot in review than a missing check inside a method.
/// See <c>docs/authorization-model.md</c>.
/// </para>
/// <para>
/// <b>Browsing is deliberately anonymous.</b> A shop nobody can look at without an account is a shop nobody
/// buys from. Writes are permission-gated and arrive in Phase 9.
/// </para>
/// </remarks>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/catalog").WithTags("Catalog");

        group
            .MapGet("/products", SearchProducts)
            .WithName("SearchProducts")
            .WithSummary("Browse products with search, filtering, sorting and paging")
            .Produces<PagedResult<ProductSummaryDto>>();

        group
            .MapGet("/products/{id:guid}", GetProductById)
            .WithName("GetProductById")
            .WithSummary("Fetch one product")
            .Produces<ProductDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group
            .MapGet("/categories", GetCategories)
            .WithName("GetCategories")
            .WithSummary("Category tree with product counts, for the filter panel")
            .Produces<IReadOnlyList<CategoryDto>>();

        group
            .MapGet("/brands", GetBrands)
            .WithName("GetBrands")
            .WithSummary("Brands with product counts, for the filter panel")
            .Produces<IReadOnlyList<BrandDto>>();

        // ---------------------------------------------------------------------------------------
        //  Internal: authoritative pricing for checkout.
        // ---------------------------------------------------------------------------------------
        //  Deliberately OUTSIDE the /api/catalog group, so it is not exposed through the storefront
        //  BFF - that route whitelists /api/catalog only.
        //
        //  Ordering calls this when placing an order, because the price in a basket came from a
        //  client and may be a month old. Trusting it would mean anyone who can send an HTTP request
        //  can set their own prices. Anything that reaches a ledger is derived server-side.
        //
        //  One call for the whole basket rather than one per line: twenty lines meaning twenty round
        //  trips would make checkout latency a function of basket size.
        app.MapGet("/internal/catalog/prices", GetPrices)
            .WithTags("Catalog")
            .WithSummary("Internal: current prices for a set of products.")
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> GetPrices(
        string ids,
        ProductQueries queries,
        CancellationToken cancellationToken)
    {
        Guid[] productIds = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Guid.TryParse(part, out Guid id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            // Bounded, because this is an IN clause built from a query string. Without a cap, one
            // request could ask for every product in the catalogue and make the database do it.
            .Distinct()
            .Take(100)
            .ToArray();

        if (productIds.Length == 0)
        {
            return Results.Ok(Array.Empty<ProductPriceDto>());
        }

        return Results.Ok(await queries.GetPricesAsync(productIds, cancellationToken));
    }

    /// <remarks>
    /// Filters arrive as individual query parameters rather than a bound object so the OpenAPI document
    /// describes each one — which is what makes a generated client, and the Swagger UI, actually usable.
    /// </remarks>
    private static async Task<IResult> SearchProducts(
        ProductQueries queries,
        CancellationToken cancellationToken,
        string? search = null,
        string? category = null,
        string? brand = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool inStockOnly = false,
        string? sortBy = null,
        bool sortDescending = false,
        int page = 1,
        int pageSize = 12)
    {
        // PageRequest.Normalise() clamps pageSize. An unclamped page size is a denial-of-service vector:
        // ?pageSize=10000000 is a free way to exhaust server memory.
        var query = new ProductQuery(
            search, category, brand, minPrice, maxPrice, inStockOnly, sortBy, sortDescending, page, pageSize);

        PagedResult<ProductSummaryDto> result = await queries.SearchAsync(query, cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetProductById(
        Guid id,
        ProductQueries queries,
        CancellationToken cancellationToken)
    {
        ProductDetailDto? product = await queries.GetByIdAsync(id, cancellationToken);

        return product is null
            // ProblemDetails (RFC 9457), not a bare 404, so every client parses errors the same way.
            ? Results.Problem(
                title: "Product not found",
                detail: $"No active product with id {id}.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(product);
    }

    private static async Task<IResult> GetCategories(ProductQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetCategoriesAsync(cancellationToken));

    private static async Task<IResult> GetBrands(ProductQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetBrandsAsync(cancellationToken));
}
