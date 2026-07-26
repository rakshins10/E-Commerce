using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Auth.IntegrationTests;

/// <summary>
/// Proves the authorization model actually works, against a real Keycloak and real signed tokens.
/// </summary>
/// <remarks>
/// <para>
/// The tests that matter most here are the <b>negative</b> ones. It is easy to confirm an admin can reach an
/// admin endpoint; the valuable assertion is that a customer <i>cannot</i>, and that the server enforces
/// that independently of whether any UI hid the button.
/// </para>
/// <para>
/// See <c>docs/authorization-model.md</c>.
/// </para>
/// </remarks>
[Collection(KeycloakCollection.Name)]
public class AuthorizationTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>
    /// A miniature API wired up exactly as a real service is — the same
    /// <see cref="AuthenticationExtensions.AddJwtAuthentication"/> and the same
    /// <c>RequirePermission</c> helper — so what is tested is the shared building block, not a re-creation
    /// of it.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Issuer"] = keycloak.Issuer,
            ["Auth:MetadataAddress"] = keycloak.MetadataAddress,
            ["Auth:Audience"] = "ecommerce-api",
            ["Auth:RequireHttpsMetadata"] = "false",
        });

        builder.Services.AddRouting();
        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.Services.AddPermissionPolicies();

        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapGet("/public", () => Results.Ok("public"));

        _app.MapGet("/whoami", (ICurrentUser user) => Results.Ok(new
        {
            user.UserId,
            user.UserName,
            roles = user.Roles,
            permissions = user.Permissions,
        })).RequireAuthenticated();

        _app.MapGet("/catalog", () => Results.Ok("read")).RequirePermission(Permissions.Catalog.Read);
        _app.MapPost("/catalog", () => Results.Ok("write")).RequirePermission(Permissions.Catalog.Write);
        _app.MapPost("/refund", () => Results.Ok("refunded")).RequirePermission(Permissions.Order.Refund);
        _app.MapGet("/users", () => Results.Ok("users")).RequirePermission(Permissions.Users.Manage);
        _app.MapGet("/audit", () => Results.Ok("audit")).RequirePermission(Permissions.Admin.AuditRead);

        _app.MapGet("/orders/any", () => Results.Ok("orders"))
            .RequireAnyPermission(Permissions.Order.Read, Permissions.Order.ReadOwn);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await _app.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<HttpResponseMessage> CallAsync(string path, string? username, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);

        if (username is not null)
        {
            string token = await keycloak.GetAccessTokenAsync(username);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // -------------------------------------------------------------------------
    //  Authentication
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_endpoint_with_no_requirement_is_reachable_anonymously()
    {
        HttpResponseMessage response = await CallAsync("/public", username: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_protected_endpoint_rejects_an_anonymous_caller_with_401()
    {
        HttpResponseMessage response = await CallAsync("/catalog", username: null);

        // 401, not 403: the caller has not identified themselves at all, so the correct answer is
        // "authenticate", not "you are not allowed".
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_garbage_token_is_rejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/catalog");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.real.token");

        HttpResponseMessage response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    //  The composite-role design produces the right permissions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_real_token_carries_the_permissions_its_composite_role_grants()
    {
        HttpResponseMessage response = await CallAsync("/whoami", "customer");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Never assigned directly to the user - these come from the `customer` realm role being a
        // COMPOSITE that grants client roles on ecommerce-api. This assertion is what proves that design
        // works end to end.
        body.Should().Contain("catalog:read");
        body.Should().Contain("order:read:own");
        body.Should().Contain("profile:write:own");
    }

    [Fact]
    public async Task Admin_inherits_permissions_through_nested_composite_roles()
    {
        HttpResponseMessage response = await CallAsync("/whoami", "administrator");
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // `admin` grants no catalog permissions directly. It is a composite of catalog-manager,
        // order-manager and support-agent, so these arrive two levels down. Adding a permission to
        // catalog-manager later automatically reaches admin, with no realm edit for admin at all.
        body.Should().Contain("catalog:write");
        body.Should().Contain("order:refund");
        body.Should().Contain("user:manage");
        body.Should().Contain("audit:read");
    }

    // -------------------------------------------------------------------------
    //  The negative cases - the ones that matter
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("customer", "/catalog", HttpStatusCode.OK)]          // customers may browse
    [InlineData("support", "/catalog", HttpStatusCode.OK)]
    [InlineData("catalogmgr", "/catalog", HttpStatusCode.OK)]
    public async Task Anyone_with_catalog_read_may_browse(string user, string path, HttpStatusCode expected)
    {
        HttpResponseMessage response = await CallAsync(path, user);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task A_customer_cannot_write_to_the_catalog()
    {
        HttpResponseMessage response = await CallAsync("/catalog", "customer", HttpMethod.Post);

        // 403, not 401: we know exactly who they are - they simply may not do this.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_support_agent_is_read_only_and_cannot_refund()
    {
        // Support agents can see orders. That deliberately does NOT extend to acting on them - the whole
        // point of separating `order:read` from `order:refund`.
        (await CallAsync("/orders/any", "support")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallAsync("/refund", "support", HttpMethod.Post)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_catalog_manager_cannot_touch_orders_or_users()
    {
        // Roles are scoped to a job, not stacked by seniority. Merchandising has no business refunding
        // orders or administering users, and the model enforces that rather than relying on convention.
        (await CallAsync("/catalog", "catalogmgr", HttpMethod.Post)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallAsync("/refund", "catalogmgr", HttpMethod.Post)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CallAsync("/users", "catalogmgr")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_order_manager_can_refund_but_cannot_edit_the_catalog_or_manage_users()
    {
        (await CallAsync("/refund", "ordermgr", HttpMethod.Post)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallAsync("/catalog", "ordermgr", HttpMethod.Post)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CallAsync("/users", "ordermgr")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_an_admin_may_manage_users_and_read_the_audit_log()
    {
        (await CallAsync("/users", "administrator")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallAsync("/audit", "administrator")).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (string user in new[] { "customer", "support", "catalogmgr", "ordermgr" })
        {
            (await CallAsync("/users", user)).StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                because: $"{user} must not be able to manage users");
        }
    }

    [Fact]
    public async Task RequireAnyPermission_admits_a_caller_holding_either_permission()
    {
        // A customer holds order:read:own; staff hold order:read. Both get through the door - after which
        // an ownership check decides which orders they may actually see. See ResourceOwnerRequirement.
        (await CallAsync("/orders/any", "customer")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallAsync("/orders/any", "ordermgr")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------
    //  Account state
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_disabled_user_cannot_obtain_a_token_at_all()
    {
        // Disabling happens in Keycloak, so the account fails at authentication - it never reaches our
        // authorization layer. That is the correct place for it to fail.
        (await keycloak.CanAuthenticateAsync("blocked")).Should().BeFalse();
        (await keycloak.CanAuthenticateAsync("customer")).Should().BeTrue();
    }

    [Fact]
    public async Task A_wrong_password_is_rejected()
    {
        (await keycloak.CanAuthenticateAsync("customer", "wrong-password")).Should().BeFalse();
    }
}
