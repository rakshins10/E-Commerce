using System.Data;

using Dapper;

using ECommerce.Auth;

namespace ECommerce.OrderingSaga.Api.Features;

/// <summary>
/// Read-only access to saga state.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no endpoint that changes a saga.</b> A saga advances only in response to what really
/// happened, and an endpoint that let someone push it forward by hand would let the saga's record
/// disagree with reality — which is the one thing it exists to prevent.
/// </para>
/// <para>
/// The read side answers two questions. The customer's storefront asks "what happened to my order?" so
/// the timeline can show that stock was reserved and then released. Operations asks "which sagas are
/// stuck?", which in a choreographed design has no single answer at all.
/// </para>
/// </remarks>
public static class SagaEndpoints
{
    public static IEndpointRouteBuilder MapSagaEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/saga").WithTags("Saga");

        // Either permission: a customer reading their own order's timeline, or staff reading anyone's.
        // Ownership is checked in the query, not here - the same shape as the Ordering endpoints.
        group.MapGet("/orders/{orderId:guid}", GetSagaForOrder)
            .RequireAnyPermission(Permissions.Order.Read, Permissions.Order.ReadOwn)
            .WithSummary("The saga timeline for one order.");

        group.MapGet("/stuck", GetStuckSagas)
            .RequirePermission(Permissions.Order.Read)
            .WithSummary("Sagas that started a while ago and have not finished.");

        return app;
    }

    private static async Task<IResult> GetSagaForOrder(
        Guid orderId,
        ICurrentUser user,
        SagaQueries queries,
        CancellationToken cancellationToken)
    {
        string? buyerFilter = user.HasPermission(Permissions.Order.Read)
            ? null
            : user.RequireSubject();

        SagaTimelineDto? timeline = await queries.GetTimelineAsync(orderId, buyerFilter, cancellationToken);

        // 404 for someone else's saga as well as for one that does not exist - distinguishing the two
        // would confirm an id is real.
        return timeline is null ? Results.NotFound() : Results.Ok(timeline);
    }

    private static async Task<IResult> GetStuckSagas(
        SagaQueries queries,
        CancellationToken cancellationToken,
        int olderThanMinutes = 5) =>
        Results.Ok(await queries.GetStuckAsync(olderThanMinutes, cancellationToken));
}

/// <summary>The saga read side. Dapper, like every other read side in this repo.</summary>
public sealed class SagaQueries(IDbConnection connection)
{
    public async Task<SagaTimelineDto?> GetTimelineAsync(
        Guid orderId,
        string? buyerId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.order_id        AS OrderId,
                   s.order_number    AS OrderNumber,
                   s.state           AS State,
                   s.stock_reserved  AS StockReserved,
                   s.failure_reason  AS FailureReason,
                   s.started_at      AS StartedAt,
                   s.completed_at    AS CompletedAt
            FROM   order_sagas s
            WHERE  s.order_id = @OrderId
              AND  (@BuyerId IS NULL OR s.buyer_id = @BuyerId);

            SELECT st.name        AS Name,
                   st.detail      AS Detail,
                   st.occurred_at AS OccurredAt
            FROM   saga_steps st
            WHERE  st.order_id = @OrderId
            -- By sequence, not by time. Two steps recorded in the same millisecond come back in an
            -- arbitrary order otherwise, and a timeline showing compensation before the failure it
            -- compensates is worse than no timeline at all.
            ORDER  BY st.sequence;
            """;

        var command = new CommandDefinition(
            sql, new { OrderId = orderId, BuyerId = buyerId }, cancellationToken: cancellationToken);

        using SqlMapper.GridReader reader = await connection.QueryMultipleAsync(command);

        SagaRow? saga = await reader.ReadSingleOrDefaultAsync<SagaRow>();

        if (saga is null)
        {
            return null;
        }

        IEnumerable<SagaStepDto> steps = await reader.ReadAsync<SagaStepDto>();

        return new SagaTimelineDto
        {
            OrderId = saga.OrderId,
            OrderNumber = saga.OrderNumber,
            State = StateName(saga.State),
            StockReserved = saga.StockReserved,
            FailureReason = saga.FailureReason,
            StartedAt = saga.StartedAt,
            CompletedAt = saga.CompletedAt,
            Steps = steps.ToArray(),
        };
    }

    /// <summary>
    /// Sagas that started a while ago and never finished.
    /// </summary>
    /// <remarks>
    /// <b>The operational payoff of orchestration.</b> In a choreographed saga this query cannot be
    /// written: "where is order 12345 stuck?" is spread across four services' logs and exists nowhere as
    /// a fact. Here it is one <c>SELECT</c>, and it is the thing an on-call engineer actually needs.
    /// </remarks>
    public async Task<IReadOnlyList<StuckSagaDto>> GetStuckAsync(
        int olderThanMinutes,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.order_id     AS OrderId,
                   s.order_number AS OrderNumber,
                   s.state        AS State,
                   s.started_at   AS StartedAt
            FROM   order_sagas s
            WHERE  s.completed_at IS NULL
              AND  s.started_at < NOW() - (@Minutes || ' minutes')::interval
            ORDER  BY s.started_at;
            """;

        var command = new CommandDefinition(
            sql, new { Minutes = olderThanMinutes }, cancellationToken: cancellationToken);

        IEnumerable<StuckRow> rows = await connection.QueryAsync<StuckRow>(command);

        return rows.Select(row => new StuckSagaDto
        {
            OrderId = row.OrderId,
            OrderNumber = row.OrderNumber,
            State = StateName(row.State),
            StartedAt = row.StartedAt,
        }).ToArray();
    }

    /// <remarks>
    /// The read side must not reference the domain enum - that is the CQRS boundary. The duplication is
    /// small and explicit rather than a hidden coupling.
    /// </remarks>
    private static string StateName(int state) => state switch
    {
        10 => "AwaitingStock",
        20 => "AwaitingPayment",
        30 => "Completed",
        90 => "Compensated",
        _ => "Unknown",
    };

    private sealed record SagaRow
    {
        public Guid OrderId { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public int State { get; init; }

        public bool StockReserved { get; init; }

        public string? FailureReason { get; init; }

        public DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }
    }

    private sealed record StuckRow
    {
        public Guid OrderId { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public int State { get; init; }

        public DateTimeOffset StartedAt { get; init; }
    }
}

public sealed record SagaTimelineDto
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string State { get; init; }

    public required bool StockReserved { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required IReadOnlyList<SagaStepDto> Steps { get; init; }
}

public sealed record SagaStepDto
{
    public string Name { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }
}

public sealed record StuckSagaDto
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required string State { get; init; }

    public required DateTimeOffset StartedAt { get; init; }
}
