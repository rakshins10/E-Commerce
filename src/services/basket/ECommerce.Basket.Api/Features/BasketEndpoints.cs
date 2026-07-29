using ECommerce.Auth;
using ECommerce.Basket.Api.Infrastructure;
using ECommerce.Basket.Api.Model;
using ECommerce.Common.Exceptions;

namespace ECommerce.Basket.Api.Features;

/// <summary>
/// The basket API.
/// </summary>
/// <remarks>
/// <para>
/// Every route is <c>/me</c>, and "me" is resolved server-side from the <c>sub</c> claim. There is no
/// <c>/baskets/{buyerId}</c> route, so there is no id to tamper with — the same reasoning as the
/// profile endpoints in Phase 5. See docs/authorization-model.md.
/// </para>
/// <para>
/// <b>Adding to a basket requires being signed in.</b> An anonymous basket is a real product feature and
/// a real complication: it needs a cookie-scoped identity, and a merge strategy for the moment a guest
/// signs in holding items. Out of scope here, and worth naming as a decision rather than leaving as an
/// apparent oversight.
/// </para>
/// </remarks>
public static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/basket").WithTags("Basket");

        group.MapGet("/me", GetMyBasket)
            .RequirePermission(Permissions.Basket.ReadOwn)
            .WithSummary("The signed-in customer's basket.");

        group.MapPost("/me/items", AddItem)
            .RequirePermission(Permissions.Basket.WriteOwn)
            .WithSummary("Adds an item, or increases the quantity if it is already there.");

        group.MapPut("/me/items/{productId:guid}", SetQuantity)
            .RequirePermission(Permissions.Basket.WriteOwn)
            .WithSummary("Sets the quantity of a line. Quantity 0 removes it.");

        group.MapDelete("/me/items/{productId:guid}", RemoveItem)
            .RequirePermission(Permissions.Basket.WriteOwn)
            .WithSummary("Removes a line.");

        group.MapDelete("/me", ClearBasket)
            .RequirePermission(Permissions.Basket.WriteOwn)
            .WithSummary("Empties the basket.");

        // Not exposed through the storefront BFF. Called by Ordering, service to service, when placing
        // an order. Keeping it off the public route table means a customer cannot read or clear
        // someone else's basket even if they discover the path.
        app.MapGet("/internal/basket/{buyerId}", GetForBuyer)
            .WithTags("Basket")
            .WithSummary("Internal: another service reading a basket during checkout.")
            .ExcludeFromDescription();

        app.MapDelete("/internal/basket/{buyerId}", DeleteForBuyer)
            .WithTags("Basket")
            .WithSummary("Internal: clearing a basket once its order exists.")
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> GetMyBasket(ICurrentUser user, BasketRepository baskets)
    {
        // An absent basket is an empty basket, not a 404. "You have no basket yet" and "your basket is
        // empty" are the same thing to a customer, and returning 404 would force every client to write
        // the same special case.
        CustomerBasket basket = await baskets.GetAsync(user.RequireSubject()).ConfigureAwait(false)
                                ?? new CustomerBasket { BuyerId = user.RequireSubject() };

        return Results.Ok(basket);
    }

    private static async Task<IResult> AddItem(
        AddBasketItemRequest request,
        ICurrentUser user,
        BasketRepository baskets)
    {
        if (request.Quantity is < 1 or > CustomerBasket.MaxQuantityPerItem)
        {
            throw new DomainException(
                $"Quantity must be between 1 and {CustomerBasket.MaxQuantityPerItem}.");
        }

        CustomerBasket basket = await baskets.GetAsync(user.RequireSubject()).ConfigureAwait(false)
                                ?? new CustomerBasket { BuyerId = user.RequireSubject() };

        BasketItem? existing = basket.Items.FirstOrDefault(item => item.ProductId == request.ProductId);

        if (existing is not null)
        {
            // Clamped rather than rejected. Someone who already has 95 and adds 10 more clearly wants
            // "as many as I can have", and an error message about a limit they did not know existed is
            // a worse outcome than quietly giving them the maximum.
            existing.Quantity = Math.Min(
                existing.Quantity + request.Quantity, CustomerBasket.MaxQuantityPerItem);

            // The price is refreshed on re-add, so a basket that has sat for a month shows today's
            // price rather than a stale one. It is still re-derived at checkout - this is display only.
            existing.UnitPrice = request.UnitPrice;
        }
        else
        {
            if (basket.Items.Count >= CustomerBasket.MaxItems)
            {
                throw new DomainException(
                    $"A basket may not hold more than {CustomerBasket.MaxItems} different products.");
            }

            basket.Items.Add(new BasketItem
            {
                ProductId = request.ProductId,
                Sku = request.Sku,
                ProductName = request.ProductName,
                ImageUrl = request.ImageUrl,
                UnitPrice = request.UnitPrice,
                Currency = request.Currency,
                Quantity = request.Quantity,
            });
        }

        return Results.Ok(await baskets.SaveAsync(basket).ConfigureAwait(false));
    }

    private static async Task<IResult> SetQuantity(
        Guid productId,
        SetQuantityRequest request,
        ICurrentUser user,
        BasketRepository baskets)
    {
        if (request.Quantity is < 0 or > CustomerBasket.MaxQuantityPerItem)
        {
            throw new DomainException(
                $"Quantity must be between 0 and {CustomerBasket.MaxQuantityPerItem}.");
        }

        CustomerBasket? basket = await baskets.GetAsync(user.RequireSubject()).ConfigureAwait(false);

        if (basket is null)
        {
            return Results.NotFound();
        }

        BasketItem? item = basket.Items.FirstOrDefault(i => i.ProductId == productId);

        if (item is null)
        {
            return Results.NotFound();
        }

        // Zero removes. A quantity stepper that reaches 0 should empty the line rather than leave a
        // row displaying "0", which every client would then have to filter out.
        if (request.Quantity == 0)
        {
            basket.Items.Remove(item);
        }
        else
        {
            item.Quantity = request.Quantity;
        }

        return Results.Ok(await baskets.SaveAsync(basket).ConfigureAwait(false));
    }

    private static async Task<IResult> RemoveItem(
        Guid productId,
        ICurrentUser user,
        BasketRepository baskets)
    {
        CustomerBasket? basket = await baskets.GetAsync(user.RequireSubject()).ConfigureAwait(false);

        if (basket is null)
        {
            return Results.NotFound();
        }

        basket.Items.RemoveAll(item => item.ProductId == productId);

        return Results.Ok(await baskets.SaveAsync(basket).ConfigureAwait(false));
    }

    private static async Task<IResult> ClearBasket(ICurrentUser user, BasketRepository baskets)
    {
        await baskets.DeleteAsync(user.RequireSubject()).ConfigureAwait(false);

        // The empty basket, not 204. Every mutating endpoint returns the resulting basket so the client
        // never has to guess what changed or make a second call to find out.
        return Results.Ok(new CustomerBasket { BuyerId = user.RequireSubject() });
    }

    private static async Task<IResult> GetForBuyer(string buyerId, BasketRepository baskets)
    {
        CustomerBasket? basket = await baskets.GetAsync(buyerId).ConfigureAwait(false);

        return basket is null ? Results.NotFound() : Results.Ok(basket);
    }

    private static async Task<IResult> DeleteForBuyer(string buyerId, BasketRepository baskets)
    {
        await baskets.DeleteAsync(buyerId).ConfigureAwait(false);
        return Results.NoContent();
    }
}

/// <summary>Adding a product to the basket.</summary>
/// <remarks>
/// The client supplies the name and price it is displaying. That is safe <i>here</i> because nothing in
/// a basket is binding — and it is emphatically not safe at checkout, where every price is re-derived
/// from Catalog. A client-supplied price that reached the ledger would be a discount anyone could grant
/// themselves.
/// </remarks>
public sealed record AddBasketItemRequest
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string ProductName { get; init; }

    public string? ImageUrl { get; init; }

    public required decimal UnitPrice { get; init; }

    public string Currency { get; init; } = "GBP";

    public int Quantity { get; init; } = 1;
}

public sealed record SetQuantityRequest
{
    public required int Quantity { get; init; }
}
