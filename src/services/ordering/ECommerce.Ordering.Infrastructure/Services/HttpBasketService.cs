using System.Net;
using System.Net.Http.Json;

using ECommerce.Ordering.Application.Orders;

using Microsoft.Extensions.Logging;

namespace ECommerce.Ordering.Infrastructure.Services;

/// <summary>
/// Reads a basket from the Basket service over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Calls the <c>/internal</c> routes, which are not exposed through any BFF. Those endpoints take a
/// buyer id in the path — which would be a serious flaw on a public route and is fine here, because the
/// only way to reach them is from inside the container network.
/// </para>
/// <para>
/// <b>That is network-level trust, and worth being honest about.</b> It is adequate for this repo and
/// insufficient for a real deployment: anything that gets into the network can call them. The
/// production answer is mutual TLS or a service mesh, or a service-to-service token with its own
/// audience. Recorded here rather than left as an unexamined assumption.
/// </para>
/// <para>
/// Resilience — retry with jitter, and a circuit breaker — is configured on the typed client in
/// <c>Program.cs</c> rather than written here, so the policy is visible in the composition root next to
/// every other cross-cutting concern.
/// </para>
/// </remarks>
public sealed class HttpBasketService(HttpClient client, ILogger<HttpBasketService> logger)
    : IBasketService
{
    public async Task<BasketSnapshot?> GetBasketAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await client
            .GetAsync(new Uri($"/internal/basket/{Uri.EscapeDataString(buyerId)}", UriKind.Relative),
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // No basket is a legitimate answer, not a failure. The handler turns it into "your basket is
            // empty", which is what the customer needs to be told.
            return null;
        }

        response.EnsureSuccessStatusCode();

        BasketResponse? basket = await response.Content
            .ReadFromJsonAsync<BasketResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (basket is null)
        {
            return null;
        }

        return new BasketSnapshot(
            basket.BuyerId,
            basket.Items.Select(item => new BasketLineSnapshot(
                item.ProductId,
                item.Sku,
                item.ProductName,
                item.UnitPrice,
                item.Currency,
                item.Quantity,
                item.Size,
                item.ColourName)).ToArray());
    }

    public async Task ClearBasketAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await client
            .DeleteAsync(new Uri($"/internal/basket/{Uri.EscapeDataString(buyerId)}", UriKind.Relative),
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Logged, not thrown. The caller already has a committed order; failing here would be
            // reporting an error for something that succeeded.
            logger.LogWarning(
                "Clearing the basket for {BuyerId} returned {StatusCode}.", buyerId, response.StatusCode);
        }
    }

    // Ordering's own view of Basket's JSON, deliberately not Basket's class. A field added for the
    // basket UI must not recompile Ordering.
    private sealed record BasketResponse(string BuyerId, IReadOnlyList<BasketItemResponse> Items);

    private sealed record BasketItemResponse(
        Guid ProductId,
        string Sku,
        string ProductName,
        string? Size,
        string? ColourName,
        decimal UnitPrice,
        string Currency,
        int Quantity);
}
