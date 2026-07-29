using ECommerce.Auth;
using ECommerce.Common.Pagination;
using ECommerce.Ordering.Application.Orders;
using ECommerce.Ordering.Domain.Orders;

namespace ECommerce.Ordering.Api.Features;

/// <summary>
/// The Ordering API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read the route table to audit the service.</b> Every endpoint declares its permission on the
/// route, so the entire authorization surface is visible in one screen — and an unprotected endpoint
/// shows up as an <i>absence</i>, which is far easier to spot in review than a missing check buried in a
/// method body.
/// </para>
/// <para>
/// <b>Two kinds of authorization here.</b> The permission answers "may this kind of user do this kind of
/// thing". Ownership answers "to whose order" — and that cannot be checked until the order is loaded, so
/// it happens in the handler and in the query's WHERE clause. Both are needed:
/// <c>order:read:own</c> without an ownership check would let any customer read any order.
/// </para>
/// </remarks>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapPost("/", PlaceOrder)
            .RequirePermission(Permissions.Order.Write)
            .WithSummary("Turns the caller's basket into an order.")
            .Produces<OrderDto>(StatusCodes.Status201Created);

        // Either permission. "My orders" is always filtered to the caller's own `sub` server-side, so
        // someone holding order:read - the right to read ANY order - can self-evidently read their own.
        // Requiring only :own 403'd every member of staff who bought something from their own shop,
        // which is a real scenario rather than a hypothetical one.
        group.MapGet("/me", GetMyOrders)
            .RequireAnyPermission(Permissions.Order.Read, Permissions.Order.ReadOwn)
            .WithSummary("The caller's orders, newest first.")
            .Produces<PagedResult<OrderSummaryDto>>();

        // EITHER permission opens the door; which one you hold decides what you see. A customer holds
        // order:read:own and the query filters to their own rows; staff hold order:read and the filter
        // is dropped. Requiring only :own would 403 every member of staff, because reading ANY order and
        // reading YOUR OWN order are deliberately different permissions - see docs/authorization-model.md.
        group.MapGet("/{orderId:guid}", GetOrder)
            .RequireAnyPermission(Permissions.Order.Read, Permissions.Order.ReadOwn)
            .WithSummary("One order. Customers see only their own; staff see any.")
            .Produces<OrderDto>()
            .Produces(StatusCodes.Status404NotFound);

        // Same shape: a customer cancels their own (order:read:own plus the ownership check in the
        // handler); staff cancel anyone's (order:cancel).
        group.MapPost("/{orderId:guid}/cancel", CancelOrder)
            .RequireAnyPermission(Permissions.Order.Cancel, Permissions.Order.ReadOwn)
            .WithSummary("Cancels an order, if it has not been dispatched.")
            .Produces<OrderDto>();

        // -------------------------------------------------------------------------------------------
        //  Fulfilment. Staff only.
        // -------------------------------------------------------------------------------------------
        //  In Phase 7 the saga drives these transitions from payment and inventory events. They exist
        //  as endpoints now so the full lifecycle can be exercised end to end - and they will remain
        //  afterwards, because staff genuinely need to move an order along when something goes wrong
        //  with the automated path.
        //
        //  The same aggregate methods serve both, which is only possible because the rules live in the
        //  aggregate rather than in these handlers.
        group.MapPost("/{orderId:guid}/confirm-stock", ConfirmStock)
            .RequirePermission(Permissions.Inventory.Adjust)
            .WithSummary("Staff: confirms stock is reserved.");

        group.MapPost("/{orderId:guid}/pay", MarkPaid)
            .RequirePermission(Permissions.Order.Write)
            .WithSummary("Staff: records a payment. Phase 7 replaces this with the payment saga.");

        group.MapPost("/{orderId:guid}/ship", MarkShipped)
            .RequirePermission(Permissions.Order.Cancel)
            .WithSummary("Staff: marks the order dispatched.");

        group.MapPost("/{orderId:guid}/deliver", MarkDelivered)
            .RequirePermission(Permissions.Order.Cancel)
            .WithSummary("Staff: marks the order delivered.");

        return app;
    }

    private static async Task<IResult> PlaceOrder(
        PlaceOrderRequest request,
        ICurrentUser user,
        PlaceOrderHandler handler,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        Order order = await handler.HandleAsync(
            user.RequireSubject(),
            user.UserName ?? "Customer",
            request,
            cancellationToken);

        // Read back through the query side rather than mapping the aggregate. It costs one extra round
        // trip and guarantees the client sees exactly what a later GET will return - so a bug in the
        // read model surfaces at checkout rather than the first time someone opens their order history.
        OrderDto? dto = await queries.GetOrderAsync(order.Id, order.BuyerId, cancellationToken);

        return Results.Created($"/api/orders/{order.Id}", dto);
    }

    private static async Task<IResult> GetMyOrders(
        ICurrentUser user,
        OrderQueries queries,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        PagedResult<OrderSummaryDto> result =
            await queries.GetMyOrdersAsync(user.RequireSubject(), page, pageSize, cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetOrder(
        Guid orderId,
        ICurrentUser user,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        // Staff pass null, which removes the buyer filter from the query. A customer passes their own
        // subject, so the database - not a later `if` - is what stops them reading someone else's order.
        string? buyerFilter = user.HasPermission(Permissions.Order.Read)
            ? null
            : user.RequireSubject();

        OrderDto? order = await queries.GetOrderAsync(orderId, buyerFilter, cancellationToken);

        // 404 rather than 403 when it belongs to someone else. Distinguishing the two would confirm to
        // an attacker that the id is real, which is what makes enumeration worth attempting.
        return order is null ? Results.NotFound() : Results.Ok(order);
    }

    private static async Task<IResult> CancelOrder(
        Guid orderId,
        ICurrentUser user,
        CancelOrderHandler handler,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        bool isStaff = user.HasPermission(Permissions.Order.Cancel);

        Order order = await handler.HandleAsync(
            orderId, user.RequireSubject(), isStaff, cancellationToken);

        return Results.Ok(await queries.GetOrderAsync(order.Id, null, cancellationToken));
    }

    private static async Task<IResult> ConfirmStock(
        Guid orderId,
        AdvanceOrderHandler handler,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        Order order = await handler.ConfirmStockAsync(orderId, cancellationToken);
        return Results.Ok(await queries.GetOrderAsync(order.Id, null, cancellationToken));
    }

    private static async Task<IResult> MarkPaid(
        Guid orderId,
        AdvanceOrderHandler handler,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        // A placeholder reference until Phase 7 provides a real one from the payment service.
        Order order = await handler.MarkPaidAsync(
            orderId, $"manual-{Guid.CreateVersion7():N}", cancellationToken);

        return Results.Ok(await queries.GetOrderAsync(order.Id, null, cancellationToken));
    }

    private static async Task<IResult> MarkShipped(
        Guid orderId,
        AdvanceOrderHandler handler,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        Order order = await handler.MarkShippedAsync(orderId, cancellationToken);
        return Results.Ok(await queries.GetOrderAsync(order.Id, null, cancellationToken));
    }

    private static async Task<IResult> MarkDelivered(
        Guid orderId,
        AdvanceOrderHandler handler,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        Order order = await handler.MarkDeliveredAsync(orderId, cancellationToken);
        return Results.Ok(await queries.GetOrderAsync(order.Id, null, cancellationToken));
    }
}
