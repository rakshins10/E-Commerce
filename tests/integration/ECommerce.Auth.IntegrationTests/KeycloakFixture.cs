using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Testcontainers.Keycloak;

namespace ECommerce.Auth.IntegrationTests;

/// <summary>
/// Starts a real Keycloak container and imports the <b>same</b> realm file that
/// <c>docker compose</c> uses, so these tests exercise the actual production realm rather than a
/// simplified test double.
/// </summary>
/// <remarks>
/// <para>
/// That detail matters more than it sounds. If the tests used their own trimmed-down realm, they would pass
/// happily while the real realm had a missing audience mapper or a broken composite role — the exact class of
/// mistake they exist to catch. Importing the shipped file means <b>a realm change that breaks authorization
/// breaks the build</b>.
/// </para>
/// <para>
/// The container is started once and shared by every test class via a collection fixture, because Keycloak
/// takes 30–40 seconds to boot. Per-class would multiply that by the number of test classes for no benefit —
/// nothing here mutates the realm.
/// </para>
/// </remarks>
public sealed class KeycloakFixture : IAsyncLifetime
{
    private const string RealmName = "ecommerce";
    private const string TestClientId = "test-harness";
    private const string TestClientSecret = "dev_only_test_harness_secret";

    private KeycloakContainer _keycloak = null!;
    private HttpClient _http = null!;

    /// <summary>Base URL of the running Keycloak, e.g. <c>http://localhost:32768</c>.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>The issuer that tokens from this container will carry.</summary>
    public string Issuer => $"{BaseUrl}/realms/{RealmName}";

    /// <summary>OIDC discovery document, from which the JWKS signing keys are fetched.</summary>
    public string MetadataAddress => $"{Issuer}/.well-known/openid-configuration";

    public async ValueTask InitializeAsync()
    {
        string realmFile = LocateRealmExport();

        _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.4")
            // Mount the shipped realm and tell Keycloak to import it on boot.
            .WithResourceMapping(new FileInfo(realmFile), "/opt/keycloak/data/import/")
            .WithCommand("--import-realm")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    // Wait for the realm import specifically, not merely for the port to open. Keycloak
                    // accepts connections well before the realm exists, so a port check would let the first
                    // token request race the import and fail intermittently - the worst kind of test.
                    .UntilMessageIsLogged($"Realm '{RealmName}' imported"))
            .Build();

        await _keycloak.StartAsync();

        BaseUrl = _keycloak.GetBaseAddress().TrimEnd('/');
        _http = new HttpClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();

        if (_keycloak is not null)
        {
            await _keycloak.DisposeAsync();
        }
    }

    /// <summary>
    /// Obtains a genuine, Keycloak-signed access token for a seed user.
    /// </summary>
    /// <remarks>
    /// Uses the password grant via the <c>test-harness</c> client — the only client in the realm with direct
    /// access grants enabled, precisely so no real application client needs the deprecated grant. See
    /// <c>identity/keycloak/realm-export.json</c>.
    /// </remarks>
    public async Task<string> GetAccessTokenAsync(string username, string password = "Passw0rd!")
    {
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = TestClientId,
            ["client_secret"] = TestClientSecret,
            ["username"] = username,
            ["password"] = password,
        });

        HttpResponseMessage response = await _http.PostAsync(
            new Uri($"{Issuer}/protocol/openid-connect/token"), request);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Token request for '{username}' failed with {(int)response.StatusCode}: {body}");
        }

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("access_token").GetString()!;
    }

    /// <summary>Whether a login attempt succeeds at all — used to assert a disabled user cannot sign in.</summary>
    public async Task<bool> CanAuthenticateAsync(string username, string password = "Passw0rd!")
    {
        try
        {
            await GetAccessTokenAsync(username, password);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Finds <c>identity/keycloak/realm-export.json</c> by walking up from the test binaries to the
    /// repository root, so the tests work from the IDE, the CLI and CI alike.
    /// </summary>
    private static string LocateRealmExport()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ECommerce.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (ECommerce.slnx).");
        }

        string path = Path.Combine(directory.FullName, "identity", "keycloak", "realm-export.json");

        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"Realm export not found at {path}.");
    }
}

/// <summary>
/// Shares one Keycloak container across every test class in the assembly.
/// </summary>
[CollectionDefinition(Name)]
public sealed class KeycloakCollection : ICollectionFixture<KeycloakFixture>
{
    public const string Name = "keycloak";
}
