using ECommerce.Auth;
using ECommerce.BackOffice.Api.Infrastructure;

namespace ECommerce.BackOffice.Api.Features;

/// <summary>
/// User administration, backed by Keycloak.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keycloak stays the source of truth.</b> This service does not keep its own copy of users; it calls
/// the Keycloak Admin API and passes the answers through. A local mirror would need syncing, would drift,
/// and would produce the situation where the admin panel says an account is enabled and the login page
/// disagrees.
/// </para>
/// <para>
/// <b>What that costs, honestly:</b> the admin panel cannot show users when Keycloak is down. Since
/// nobody can sign in either, that is not much of a loss.
/// </para>
/// <para>
/// <b>Every write here is audited</b>, because every one is a human decision about someone else's access.
/// Disabling an account and assigning a role are exactly the actions somebody will later need to explain.
/// </para>
/// </remarks>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/admin/users").WithTags("Users");

        group.MapGet("/", SearchUsers)
            .RequirePermission(Permissions.Users.Read)
            .WithSummary("Search users by name or email.");

        group.MapGet("/{userId}", GetUser)
            .RequirePermission(Permissions.Users.Read)
            .WithSummary("One user, with their roles.");

        // A distinct permission from Users.Read, because seeing who exists and changing whether they can
        // log in are very different powers. support-agent holds the first and not the second.
        group.MapPost("/{userId}/enable", EnableUser)
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Re-enables a disabled account.");

        group.MapPost("/{userId}/disable", DisableUser)
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Disables an account. The user cannot sign in.");

        // Finer still: assigning roles is granting POWER, which is a bigger deal than turning an account
        // off. Somebody who can assign roles can grant themselves anything.
        group.MapPost("/{userId}/roles", AssignRole)
            .RequirePermission(Permissions.Users.ManageRoles)
            .WithSummary("Grants a realm role.");

        group.MapDelete("/{userId}/roles/{role}", RemoveRole)
            .RequirePermission(Permissions.Users.ManageRoles)
            .WithSummary("Removes a realm role.");

        return app;
    }

    private static async Task<IResult> SearchUsers(
        KeycloakAdminClient keycloak,
        CancellationToken cancellationToken,
        string? search = null) =>
        Results.Ok(await keycloak.SearchUsersAsync(search, cancellationToken));

    private static async Task<IResult> GetUser(
        string userId,
        KeycloakAdminClient keycloak,
        CancellationToken cancellationToken)
    {
        KeycloakUser? user = await keycloak.GetUserAsync(userId, cancellationToken);

        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static Task<IResult> EnableUser(
        string userId,
        ICurrentUser actor,
        KeycloakAdminClient keycloak,
        AuditWriter audit,
        CancellationToken cancellationToken) =>
        SetEnabledAsync(userId, enabled: true, actor, keycloak, audit, cancellationToken);

    private static Task<IResult> DisableUser(
        string userId,
        ICurrentUser actor,
        KeycloakAdminClient keycloak,
        AuditWriter audit,
        CancellationToken cancellationToken) =>
        SetEnabledAsync(userId, enabled: false, actor, keycloak, audit, cancellationToken);

    private static async Task<IResult> SetEnabledAsync(
        string userId,
        bool enabled,
        ICurrentUser actor,
        KeycloakAdminClient keycloak,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        KeycloakUser? user = await keycloak.GetUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        // Refused rather than allowed, and deliberately so. An administrator who disables their own
        // account is locked out of the tool that could re-enable it, and somebody has to go into
        // Keycloak directly to undo it. Cheap to prevent, tedious to recover from.
        if (!enabled && string.Equals(user.Id, actor.RequireSubject(), StringComparison.Ordinal))
        {
            return Results.Problem(
                "You cannot disable your own account.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await keycloak.SetEnabledAsync(userId, enabled, cancellationToken);

        await audit.RecordAsync(
            actor.RequireSubject(),
            actor.UserName ?? "unknown",
            enabled ? "user.enabled" : "user.disabled",
            user.Username,
            cancellationToken: cancellationToken);

        return Results.Ok(await keycloak.GetUserAsync(userId, cancellationToken));
    }

    private static async Task<IResult> AssignRole(
        string userId,
        AssignRoleRequest request,
        ICurrentUser actor,
        KeycloakAdminClient keycloak,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        // An allow-list, not whatever string arrives. Without it, a request naming a Keycloak built-in
        // such as `realm-admin` would grant control of the identity provider itself - a privilege
        // escalation delivered by JSON.
        if (!Roles.All.Contains(request.Role, StringComparer.Ordinal))
        {
            return Results.Problem(
                $"'{request.Role}' is not an assignable role.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        KeycloakUser? user = await keycloak.GetUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        await keycloak.AssignRealmRoleAsync(userId, request.Role, cancellationToken);

        await audit.RecordAsync(
            actor.RequireSubject(),
            actor.UserName ?? "unknown",
            "user.role.granted",
            user.Username,
            request.Role,
            cancellationToken);

        return Results.Ok(await keycloak.GetUserAsync(userId, cancellationToken));
    }

    private static async Task<IResult> RemoveRole(
        string userId,
        string role,
        ICurrentUser actor,
        KeycloakAdminClient keycloak,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        KeycloakUser? user = await keycloak.GetUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        await keycloak.RemoveRealmRoleAsync(userId, role, cancellationToken);

        await audit.RecordAsync(
            actor.RequireSubject(),
            actor.UserName ?? "unknown",
            "user.role.revoked",
            user.Username,
            role,
            cancellationToken);

        return Results.Ok(await keycloak.GetUserAsync(userId, cancellationToken));
    }
}

public sealed record AssignRoleRequest
{
    public required string Role { get; init; }
}
