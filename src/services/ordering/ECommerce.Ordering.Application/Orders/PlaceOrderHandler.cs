using ECommerce.Common.Exceptions;
using ECommerce.Common.SeedWork;
using ECommerce.Contracts.Ordering;
using ECommerce.Ordering.Domain.Orders;
using ECommerce.Outbox;

using Microsoft.Extensions.Logging;

namespace ECommerce.Ordering.Application.Orders;

/// <summary>
/// Turns a basket into an order.
/// </summary>
/// <remarks>
/// <para>
/// The single most important handler in the system, and the one worth reading closely. It shows what an
/// application layer is actually for: <b>orchestration, and nothing else</b>. It fetches, it re-prices,
/// it asks the aggregate to do the work, it writes the outbox, it commits. Every rule about what a valid
/// order is lives in <see cref="Order"/>, not here — which is what stops a second entry point (an admin
/// tool, an import, a retry job) quietly obeying a different set of rules.
/// </para>
/// <para>
/// The sequence, and why it is in this order:
/// </para>
/// <list type="number">
///   <item><description>Read the basket. No basket, no order.</description></item>
///   <item><description>
///     <b>Re-price every line from Catalog.</b> The security step — see <see cref="ICatalogService"/>.
///   </description></item>
///   <item><description>
///     Ask the aggregate to build the order. All the invariants apply here, in one place.
///   </description></item>
///   <item><description>
///     Write the order <b>and</b> the integration event in ONE transaction. The outbox.
///   </description></item>
///   <item><description>
///     Clear the basket afterwards, outside the transaction, and tolerate failure — see below.
///   </description></item>
/// </list>
/// </remarks>
public sealed class PlaceOrderHandler(
    IRepository<Order, Guid> orders,
    IBasketService baskets,
    ICatalogService catalog,
    IOutboxWriter outbox,
    IOrderingUnitOfWork unitOfWork,
    ILogger<PlaceOrderHandler> logger)
{
    public async Task<Order> HandleAsync(
        string buyerId,
        string buyerName,
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        BasketSnapshot basket = await baskets.GetBasketAsync(buyerId, cancellationToken).ConfigureAwait(false)
                                ?? throw new DomainException("Your basket is empty.");

        if (basket.Items.Count == 0)
        {
            throw new DomainException("Your basket is empty.");
        }

        IReadOnlyList<OrderLineRequest> lines =
            await RepriceAsync(basket, request.Currency, cancellationToken).ConfigureAwait(false);

        var shippingAddress = new ShippingAddress(
            request.ShippingAddress.Recipient,
            request.ShippingAddress.Line1,
            request.ShippingAddress.Line2,
            request.ShippingAddress.City,
            request.ShippingAddress.Postcode,
            request.ShippingAddress.Country);

        // The aggregate builds and validates itself. Note that nothing here checks the total, the line
        // count or the currency - those are the aggregate's job, and duplicating them here is how two
        // versions of a rule start to disagree.
        Order order = Order.Submit(buyerId, buyerName, shippingAddress, request.Currency, lines);

        await orders.AddAsync(order, cancellationToken).ConfigureAwait(false);

        // --- The outbox, in the same transaction as the order ------------------------------------
        //
        // The aggregate raised OrderSubmittedDomainEvent internally. That is an INTERNAL fact, free to
        // carry domain types. Here it is translated into the published contract, which carries only
        // primitives. Those few lines of translation are the seam that lets the aggregate be refactored
        // without breaking three other services.
        outbox.Add(new OrderSubmittedIntegrationEvent
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            BuyerId = order.BuyerId,
            Total = order.Total.Amount,
            Currency = order.Total.Currency,
            Lines = order.Items.Select(item => new OrderLineContract
            {
                ProductId = item.ProductId,
                Sku = item.Sku,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice.Amount,
            }).ToArray(),
        });

        // ONE commit for both. Either the order and its event exist, or neither does. There is no
        // window in which an order was created and nobody was told.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        order.ClearDomainEvents();

        // --- After the commit -------------------------------------------------------------------
        //
        // Clearing the basket is deliberately NOT part of the transaction, and cannot be: it is a
        // different service with a different database. If it fails, the order still exists and is
        // correct - the customer merely sees stale items in their basket, which is an annoyance rather
        // than a lost order. Letting this failure roll back a committed, paid-for order would be
        // strictly worse.
        //
        // A production system would make this a compensating step in the saga rather than a
        // fire-and-forget; naming that as a known limitation is more honest than a silent catch.
        try
        {
            await baskets.ClearBasketAsync(buyerId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Order {OrderNumber} was placed but the basket could not be cleared. "
                + "The order is unaffected.",
                order.OrderNumber);
        }

        logger.LogInformation(
            "Order {OrderNumber} placed for {BuyerId}: {Units} units, {Total}.",
            order.OrderNumber,
            buyerId,
            order.TotalUnits,
            order.Total);

        return order;
    }

    /// <summary>
    /// Replaces every basket price with Catalog's current price.
    /// </summary>
    /// <remarks>
    /// The basket's own prices are used for nothing here. They exist so the UI can show a total before
    /// checkout; the money that changes hands is derived from the source of truth, at the moment of the
    /// transaction.
    /// </remarks>
    private async Task<IReadOnlyList<OrderLineRequest>> RepriceAsync(
        BasketSnapshot basket,
        string currency,
        CancellationToken cancellationToken)
    {
        Guid[] productIds = basket.Items.Select(item => item.ProductId).Distinct().ToArray();

        IReadOnlyDictionary<Guid, CatalogPrice> prices =
            await catalog.GetPricesAsync(productIds, cancellationToken).ConfigureAwait(false);

        var lines = new List<OrderLineRequest>(basket.Items.Count);

        foreach (BasketLineSnapshot item in basket.Items)
        {
            if (!prices.TryGetValue(item.ProductId, out CatalogPrice? price))
            {
                // The product has been withdrawn since it was added. Named specifically, because
                // "something went wrong" leaves the customer with no idea which item to remove.
                throw new DomainException(
                    $"'{item.ProductName}' is no longer available. Please remove it from your basket.");
            }

            if (!price.IsAvailable)
            {
                throw new DomainException(
                    $"'{price.Name}' is not currently available. Please remove it from your basket.");
            }

            if (!string.Equals(price.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException(
                    $"'{price.Name}' is priced in {price.Currency}, but this order is in {currency}.");
            }

            if (price.UnitPrice != item.UnitPrice)
            {
                // Logged rather than rejected. A price that has moved between basket and checkout is
                // normal, and blocking the order would be worse for everyone; the customer is shown the
                // real total on the confirmation. Logging it means a suspicious pattern - the same
                // buyer repeatedly checking out at prices that do not match - is visible.
                logger.LogInformation(
                    "Repriced {Sku} for {BuyerId}: basket had {BasketPrice}, catalog says {CatalogPrice}.",
                    price.Sku,
                    basket.BuyerId,
                    item.UnitPrice,
                    price.UnitPrice);
            }

            lines.Add(new OrderLineRequest(
                price.ProductId,
                price.Sku,
                // The name is taken from Catalog too, so a renamed product shows its real name on the
                // invoice rather than whatever the client happened to send.
                price.Name,
                new Money(price.UnitPrice, price.Currency),
                item.Quantity));
        }

        return lines;
    }
}

/// <summary>
/// Commits the current unit of work.
/// </summary>
/// <remarks>
/// A one-method interface over <c>DbContext.SaveChangesAsync</c>, so the application layer can commit
/// without referencing EF Core. Not an abstraction over persistence in general — the repository is
/// already that — but the seam that keeps the layering assertion in
/// tests/unit/ECommerce.Architecture.Tests true.
/// </remarks>
public interface IOrderingUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
