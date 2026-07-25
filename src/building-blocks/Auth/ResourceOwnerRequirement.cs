using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ECommerce.Auth;

/// <summary>
/// A resource that belongs to a specific user.
/// </summary>
/// <remarks>
/// Implemented by anything subject to an ownership check — an order, a basket, a profile, an address. The
/// owner is always the Keycloak <c>sub</c>, never an email or username, for the reasons in
/// <see cref="ICurrentUser.UserId"/>.
/// </remarks>
public interface IOwnedResource
{
    /// <summary>The <c>sub</c> of the user who owns this.</summary>
    string OwnerId { get; }
}

/// <summary>
/// Requires that the caller either owns the resource, or holds a permission that overrides ownership.
/// </summary>
/// <param name="OwnerPermission">Permission meaning "may act on your own" — e.g. <c>order:read:own</c>.</param>
/// <param name="OverridePermission">Permission meaning "may act on anyone's" — e.g. <c>order:read</c>.
/// Optional: pass <see langword="null"/> for resources nobody may access on another's behalf.</param>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Resource-based authorization.
/// See <c>docs/authorization-model.md</c> and
/// <see href="https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased"/>.
/// </para>
/// <para>
/// <b>Why this cannot be an ordinary policy.</b> Every requirement so far is answerable from the token
/// alone: <i>does this token contain <c>catalog:write</c>?</i> — yes or no, decided before the endpoint
/// runs. But <i>"a customer may read only their own orders"</i> cannot be. The token says who you are; it
/// says nothing about who owns order #12345. The decision needs <b>the resource</b>, and the resource is
/// only known after it has been loaded from the database.
/// </para>
/// <para>
/// That is exactly why ASP.NET Core has a second, imperative authorization API. The check happens
/// <i>inside</i> the handler, after the load:
/// </para>
/// <code>
/// Order? order = await repository.GetByIdAsync(id, ct);
/// if (order is null) return Results.NotFound();
///
/// AuthorizationResult result = await authorizationService.AuthorizeAsync(
///     user, order, OrderPolicies.ReadOrder);
///
/// if (!result.Succeeded) return Results.Forbid();
/// </code>
/// <para>
/// <b>A subtlety worth getting right:</b> return <c>404 Not Found</c> rather than <c>403 Forbidden</c> when
/// a customer requests someone else's order. A 403 confirms the order exists, which leaks information — an
/// attacker can enumerate valid order ids by watching which give 403 and which give 404. Returning 404 for
/// both "does not exist" and "not yours" reveals nothing. Staff endpoints, where existence is not sensitive,
/// can legitimately return 403.
/// </para>
/// </remarks>
public sealed record ResourceOwnerRequirement(string OwnerPermission, string? OverridePermission = null)
    : IAuthorizationRequirement;

/// <summary>
/// Approves a <see cref="ResourceOwnerRequirement"/> against a concrete <see cref="IOwnedResource"/>.
/// </summary>
public sealed class ResourceOwnerAuthorizationHandler
    : AuthorizationHandler<ResourceOwnerRequirement, IOwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement,
        IOwnedResource resource)
    {
        // Staff override first: someone holding "read any order" needs no ownership check at all.
        if (requirement.OverridePermission is not null
            && context.User.HasClaim(Permissions.ClaimType, requirement.OverridePermission))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? context.User.FindFirstValue("sub");

        // Both conditions are required: the caller must hold the "own" permission AND actually be the
        // owner. Checking only ownership would let a user with no order permissions at all read their own
        // orders; checking only the permission would let any customer read every customer's orders.
        bool holdsOwnerPermission = context.User.HasClaim(Permissions.ClaimType, requirement.OwnerPermission);
        bool isOwner = !string.IsNullOrEmpty(userId)
                       && string.Equals(userId, resource.OwnerId, StringComparison.Ordinal);

        if (holdsOwnerPermission && isOwner)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
