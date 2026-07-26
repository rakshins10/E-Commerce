using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Extensions.Options;

namespace ECommerce.BackOffice.Api.Infrastructure;

/// <summary>A user, as the admin panel sees them.</summary>
public sealed record KeycloakUser
{
    public required string Id { get; init; }

    public required string Username { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public required bool Enabled { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>Options for reaching the Keycloak Admin API.</summary>
public sealed class KeycloakAdminOptions
{
    public const string SectionName = "KeycloakAdmin";

    public string BaseUrl { get; set; } = "http://keycloak:8080";

    public string Realm { get; set; } = "ecommerce";

    /// <summary>
    /// The confidential client this service authenticates as.
    /// </summary>
    /// <remarks>
    /// A dedicated service account with <b>only</b> the user-management roles it needs, never the
    /// Keycloak master admin. If this service is compromised, the blast radius should be "can disable
    /// accounts in one realm", not "owns the identity provider".
    /// </remarks>
    public string ClientId { get; set; } = "back-office";

    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Talks to the Keycloak Admin API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Client credentials, not the signed-in user's token.</b> The admin panel's user has a token for
/// <c>ecommerce-api</c>, which Keycloak's own admin endpoints do not accept — so this service
/// authenticates as itself with a service account.
/// </para>
/// <para>
/// That means the caller's permission check has already happened, at the route, before this is reached.
/// The service account is powerful; the route is what decides who gets to use it. Losing that ordering —
/// calling this before checking the permission — would hand every signed-in user the service account's
/// privileges.
/// </para>
/// </remarks>
public sealed class KeycloakAdminClient(
    HttpClient client,
    IOptions<KeycloakAdminOptions> options,
    ILogger<KeycloakAdminClient> logger)
{
    private readonly KeycloakAdminOptions _options = options.Value;

    /// <summary>
    /// The cached service-account token, and when it expires.
    /// </summary>
    /// <remarks>
    /// Cached because fetching a token per request doubles every call's latency and hammers Keycloak.
    /// Refreshed 30 seconds early, so a token never expires mid-flight — the classic off-by-one that
    /// produces a 401 on one request in a thousand and is miserable to reproduce.
    /// </remarks>
    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    private string AdminBase => $"{_options.BaseUrl}/admin/realms/{_options.Realm}";

    public async Task<IReadOnlyList<KeycloakUser>> SearchUsersAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        await AuthenticateAsync(cancellationToken);

        // Bounded. Without max, a realm with 100,000 users returns all of them into a page that shows
        // twenty.
        string query = string.IsNullOrWhiteSpace(search)
            ? "?max=50"
            : $"?max=50&search={Uri.EscapeDataString(search)}";

        List<KeycloakUserResponse>? users = await client
            .GetFromJsonAsync<List<KeycloakUserResponse>>(
                new Uri($"{AdminBase}/users{query}"), cancellationToken);

        return (users ?? []).Select(Map).ToArray();
    }

    public async Task<KeycloakUser?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        await AuthenticateAsync(cancellationToken);

        HttpResponseMessage response = await client.GetAsync(
            new Uri($"{AdminBase}/users/{Uri.EscapeDataString(userId)}"), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        KeycloakUserResponse? user =
            await response.Content.ReadFromJsonAsync<KeycloakUserResponse>(cancellationToken);

        if (user is null)
        {
            return null;
        }

        // Roles come from a second call. Keycloak does not include them on the user representation, and
        // an admin panel that cannot show what somebody can do is not much of an admin panel.
        List<RoleResponse>? roles = await client.GetFromJsonAsync<List<RoleResponse>>(
            new Uri($"{AdminBase}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm"),
            cancellationToken);

        return Map(user) with
        {
            Roles = (roles ?? []).Select(role => role.Name).OrderBy(name => name).ToArray(),
        };
    }

    public async Task SetEnabledAsync(
        string userId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await AuthenticateAsync(cancellationToken);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"{AdminBase}/users/{Uri.EscapeDataString(userId)}"),
            new { enabled },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task AssignRealmRoleAsync(
        string userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        await AuthenticateAsync(cancellationToken);

        RoleResponse? representation = await client.GetFromJsonAsync<RoleResponse>(
            new Uri($"{AdminBase}/roles/{Uri.EscapeDataString(role)}"), cancellationToken);

        if (representation is null)
        {
            return;
        }

        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri($"{AdminBase}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm"),
            new[] { representation },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveRealmRoleAsync(
        string userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        await AuthenticateAsync(cancellationToken);

        RoleResponse? representation = await client.GetFromJsonAsync<RoleResponse>(
            new Uri($"{AdminBase}/roles/{Uri.EscapeDataString(role)}"), cancellationToken);

        if (representation is null)
        {
            return;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"{AdminBase}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm"))
        {
            Content = JsonContent.Create(new[] { representation }),
        };

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return;
        }

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
        });

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"{_options.BaseUrl}/realms/{_options.Realm}/protocol/openid-connect/token"),
            form,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Deliberately does NOT log the secret or the response body, which can echo configuration.
            logger.LogError(
                "Keycloak service-account authentication failed with {StatusCode}. "
                + "Check KeycloakAdmin:ClientSecret and that the '{ClientId}' client has the "
                + "realm-management roles it needs.",
                response.StatusCode,
                _options.ClientId);

            response.EnsureSuccessStatusCode();
        }

        TokenResponse? token =
            await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

        _token = token?.AccessToken;

        // 30 seconds early - see the field comment.
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds((token?.ExpiresIn ?? 60) - 30);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    private static KeycloakUser Map(KeycloakUserResponse user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Enabled = user.Enabled,
    };

    // Keycloak's JSON is camelCase; the client is configured with web defaults so these map directly.
    private sealed record KeycloakUserResponse
    {
        public string Id { get; init; } = string.Empty;

        public string Username { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? FirstName { get; init; }

        public string? LastName { get; init; }

        public bool Enabled { get; init; }
    }

    private sealed record RoleResponse
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;
    }

    private sealed record TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
