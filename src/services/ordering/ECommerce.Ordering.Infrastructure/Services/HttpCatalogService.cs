using System.Net.Http.Json;

using ECommerce.Ordering.Application.Orders;

namespace ECommerce.Ordering.Infrastructure.Services;

/// <summary>
/// Fetches authoritative prices from the Catalog service.
/// </summary>
/// <remarks>
/// See <see cref="ICatalogService"/> for why this exists at all — it is the step that stops a client
/// choosing what it pays.
/// </remarks>
public sealed class HttpCatalogService(HttpClient client) : ICatalogService
{
    public async Task<IReadOnlyDictionary<Guid, CatalogPrice>> GetPricesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, CatalogPrice>();
        }

        string ids = string.Join(',', productIds);

        // Not the storefront BFF: the internal route, on the container network. The BFF whitelists GET
        // on /api/catalog only, and this is deliberately outside that.
        var uri = new Uri($"/internal/catalog/prices?ids={Uri.EscapeDataString(ids)}", UriKind.Relative);

        // No try/catch. A failure here MUST fail the checkout: the alternative is falling back to the
        // basket's own prices, which is precisely the vulnerability this call exists to close. A
        // fallback that silently trusts client data is worse than an error the customer can retry.
        IReadOnlyList<CatalogPriceResponse> prices =
            await client.GetFromJsonAsync<IReadOnlyList<CatalogPriceResponse>>(uri, cancellationToken)
                .ConfigureAwait(false)
            ?? [];

        return prices.ToDictionary(
            price => price.ProductId,
            price => new CatalogPrice(
                price.ProductId,
                price.Sku,
                price.Name,
                price.UnitPrice,
                price.Currency,
                price.IsAvailable));
    }

    private sealed record CatalogPriceResponse(
        Guid ProductId,
        string Sku,
        string Name,
        decimal UnitPrice,
        string Currency,
        bool IsAvailable);
}
