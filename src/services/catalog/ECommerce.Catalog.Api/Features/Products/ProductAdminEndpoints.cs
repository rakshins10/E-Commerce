using ECommerce.Auth;
using ECommerce.Catalog.Api.Domain;
using ECommerce.Catalog.Api.Infrastructure;
using ECommerce.Common.Exceptions;

using Microsoft.EntityFrameworkCore;

namespace ECommerce.Catalog.Api.Features.Products;

/// <summary>
/// Catalogue writes. Staff only.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a separate file and a separate route group from the public read endpoints, so a reader
/// can see the entire write surface at once. Mixing them means a reviewer has to filter twelve routes to
/// find the four that change anything.
/// </para>
/// <para>
/// <b>Three permissions, not one.</b> Editing a product description, changing a price, and withdrawing an
/// item from sale are different powers held by different people. A merchandiser writes copy; changing what
/// customers are charged is the sort of thing an organisation wants separately grantable and separately
/// auditable.
/// </para>
/// </remarks>
public static class ProductAdminEndpoints
{
    public static IEndpointRouteBuilder MapProductAdminEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/catalog").WithTags("Catalog (admin)");

        group.MapPost("/products", CreateProduct)
            .RequirePermission(Permissions.Catalog.Write)
            .WithSummary("Adds a product.")
            .Produces<ProductDetailDto>(StatusCodes.Status201Created);

        group.MapPut("/products/{id:guid}", UpdateProduct)
            .RequirePermission(Permissions.Catalog.Write)
            .WithSummary("Edits a product's name, description, image, category or brand.");

        // A DIFFERENT permission. Writing copy and setting what customers are charged are not the same
        // power, and an organisation that cannot separate them cannot let a junior merchandiser near the
        // catalogue at all.
        group.MapPut("/products/{id:guid}/price", ChangePrice)
            .RequirePermission(Permissions.Catalog.PriceOverride)
            .WithSummary("Changes the advertised price.");

        // DELETE, and it does NOT delete. See WithdrawProduct.
        group.MapDelete("/products/{id:guid}", WithdrawProduct)
            .RequirePermission(Permissions.Catalog.Delete)
            .WithSummary("Withdraws a product from sale. Soft delete - the row survives.");

        group.MapPost("/products/{id:guid}/restore", RestoreProduct)
            .RequirePermission(Permissions.Catalog.Delete)
            .WithSummary("Puts a withdrawn product back on sale.");

        // Withdrawn products are invisible to the storefront, so the admin panel needs its own way to
        // see them - otherwise withdrawing something makes it vanish from the only screen that could
        // bring it back.
        group.MapGet("/products/withdrawn", GetWithdrawn)
            .RequirePermission(Permissions.Catalog.Write)
            .WithSummary("Products currently withdrawn from sale.");

        return app;
    }

    private static async Task<IResult> CreateProduct(
        CreateProductRequest request,
        CatalogDbContext db,
        ProductQueries queries,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Checked here for a clean 400, AND enforced by a unique index. The check alone loses a race
        // between two concurrent creates; the index alone produces a 500 with a Postgres constraint
        // name in it. Both, and the common case gets a sentence a human can act on.
        bool skuTaken = await db.Products
            .AnyAsync(p => p.Sku == request.Sku, cancellationToken);

        if (skuTaken)
        {
            throw new DomainException($"SKU '{request.Sku}' is already in use.");
        }

        await AssertTaxonomyExistsAsync(db, request.CategoryId, request.BrandId, cancellationToken);

        var product = new Product(
            request.Sku,
            request.Name,
            request.Description,
            request.Price,
            request.Currency,
            request.CategoryId,
            request.BrandId,
            request.ImageUrl,
            ParseAudience(request.Audience));

        // A product must be sellable the moment it is created, so it gets one variant with no size and
        // no colour - the same shape a leather portfolio has. Adding sizes afterwards is a separate
        // action, and this way there is never a product that exists but cannot be bought.
        product.AddVariant(request.Sku, size: null, colourName: null, colourHex: null);

        db.Products.Add(product);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The race the check above cannot close. Translated rather than surfaced raw, because
            // "23505: duplicate key value violates unique constraint ix_products_sku" is not a message
            // for a merchandiser.
            throw new DomainException($"SKU '{request.Sku}' is already in use.");
        }

        loggerFactory.CreateLogger("Catalog").LogInformation(
            "Product {Sku} created: {Name} at {Price} {Currency}.",
            product.Sku,
            product.Name,
            product.Price,
            product.Currency);

        ProductDetailDto? created = await queries.GetByIdAsync(product.Id, cancellationToken);

        return Results.Created($"/api/catalog/products/{product.Id}", created);
    }

    private static async Task<IResult> UpdateProduct(
        Guid id,
        UpdateProductRequest request,
        CatalogDbContext db,
        ProductQueries queries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Product? product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return Results.NotFound();
        }

        await AssertTaxonomyExistsAsync(db, request.CategoryId, request.BrandId, cancellationToken);

        // The aggregate validates. Nothing here re-checks the name length or trims the description,
        // because a second copy of a rule is a rule that eventually disagrees with itself.
        product.UpdateDetails(request.Name, request.Description, request.ImageUrl);
        product.MoveTo(request.CategoryId, request.BrandId);
        product.SetAudience(ParseAudience(request.Audience));

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(await queries.GetByIdAsync(id, cancellationToken));
    }

    /// <remarks>
    /// A separate endpoint from <see cref="UpdateProduct"/> rather than a field on it, so the price
    /// permission can be checked on the route. Folding price into the general update would mean anyone
    /// who can fix a typo can also reprice the shop.
    /// </remarks>
    private static async Task<IResult> ChangePrice(
        Guid id,
        ChangePriceRequest request,
        CatalogDbContext db,
        ProductQueries queries,
        ICurrentUser user,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Product? product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return Results.NotFound();
        }

        decimal previous = product.Price;

        product.ChangePrice(request.Price);

        await db.SaveChangesAsync(cancellationToken);

        // Logged at Information with the OLD and NEW value and who did it. A price change is the sort
        // of thing somebody asks about three weeks later, and "it says 45 now" is not an answer.
        loggerFactory.CreateLogger("Catalog").LogInformation(
            "Price for {Sku} changed from {Previous} to {New} {Currency} by {Actor}.",
            product.Sku,
            previous,
            product.Price,
            product.Currency,
            user.UserName ?? "unknown");

        return Results.Ok(await queries.GetByIdAsync(id, cancellationToken));
    }

    /// <summary>
    /// Withdraws a product from sale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The route is DELETE and the row survives.</b> That is not a compromise — it is the correct
    /// behaviour, and the verb is kept because withdrawing is what "delete" means to the person clicking
    /// it.
    /// </para>
    /// <para>
    /// Hard-deleting a product breaks history. Orders copy the product name and price onto their own
    /// lines precisely so an old invoice still reads correctly — but the <c>product_id</c> on those lines
    /// would dangle, the admin panel could not link from an order to what was bought, and any report
    /// joining orders to products would silently lose rows.
    /// </para>
    /// <para>
    /// So <c>IsActive</c> goes false: the storefront stops showing it, checkout refuses it by name
    /// ("'X' is not currently available"), and everything historical keeps working. Restoring is one
    /// click rather than a database recovery.
    /// </para>
    /// </remarks>
    private static async Task<IResult> WithdrawProduct(
        Guid id,
        CatalogDbContext db,
        ICurrentUser user,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        Product? product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return Results.NotFound();
        }

        product.Deactivate();
        await db.SaveChangesAsync(cancellationToken);

        loggerFactory.CreateLogger("Catalog").LogInformation(
            "Product {Sku} withdrawn from sale by {Actor}.", product.Sku, user.UserName ?? "unknown");

        return Results.NoContent();
    }

    private static async Task<IResult> RestoreProduct(
        Guid id,
        CatalogDbContext db,
        ProductQueries queries,
        CancellationToken cancellationToken)
    {
        Product? product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return Results.NotFound();
        }

        product.Activate();
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(await queries.GetByIdAsync(id, cancellationToken));
    }

    private static async Task<IResult> GetWithdrawn(
        ProductQueries queries,
        CancellationToken cancellationToken) =>
        Results.Ok(await queries.GetWithdrawnAsync(cancellationToken));

    /// <remarks>
    /// A category or brand id that does not exist would otherwise fail as a foreign-key violation — a
    /// 500 naming a constraint. Checked up front so the caller is told which one is wrong.
    /// </remarks>
    /// <summary>
    /// Reads an audience from the wire.
    /// </summary>
    /// <remarks>
    /// Falls back to <see cref="Audience.Unisex"/> rather than rejecting an unknown value. A product that
    /// arrives without an audience is not an error - most of the catalogue is unisex, and the field was
    /// added long after the endpoint - so defaulting is the honest behaviour rather than a 400 telling a
    /// client to send something it has never had to send before.
    /// </remarks>
    private static Audience ParseAudience(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out Audience audience) ? audience : Audience.Unisex;

    private static async Task AssertTaxonomyExistsAsync(
        CatalogDbContext db,
        Guid categoryId,
        Guid brandId,
        CancellationToken cancellationToken)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new DomainException("That category does not exist.");
        }

        if (!await db.Brands.AnyAsync(b => b.Id == brandId, cancellationToken))
        {
            throw new DomainException("That brand does not exist.");
        }
    }

    /// <remarks>
    /// PostgreSQL reports a unique-constraint violation as SQLSTATE 23505. Matching on the code rather
    /// than the message text, because messages are localised and change between versions.
    /// </remarks>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}

public sealed record CreateProductRequest
{
    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public required decimal Price { get; init; }

    public string Currency { get; init; } = "GBP";

    public required Guid CategoryId { get; init; }

    public required Guid BrandId { get; init; }

    public string? ImageUrl { get; init; }

    /// <summary>"Men", "Women" or "Unisex". Defaults to Unisex when absent or unrecognised.</summary>
    public string? Audience { get; init; }
}

public sealed record UpdateProductRequest
{
    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public required Guid CategoryId { get; init; }

    public required Guid BrandId { get; init; }

    public string? ImageUrl { get; init; }

    /// <summary>"Men", "Women" or "Unisex". Defaults to Unisex when absent or unrecognised.</summary>
    public string? Audience { get; init; }
}

/// <remarks>
/// The SKU is deliberately absent from both update requests. It is what the warehouse picks by and what
/// historic order lines record, so renaming one silently decouples an order from the thing that was
/// shipped. Withdraw and create a new one instead.
/// </remarks>
public sealed record ChangePriceRequest
{
    public required decimal Price { get; init; }
}
