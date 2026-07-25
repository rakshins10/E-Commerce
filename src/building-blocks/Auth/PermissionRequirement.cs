using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace ECommerce.Auth;

/// <summary>
/// Requires the caller to hold a named permission.
/// </summary>
/// <param name="Permission">A value from <see cref="Permissions"/>.</param>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Policy-based authorization with a custom requirement.
/// See <c>docs/authorization-model.md</c>.
/// </para>
/// <para>
/// <b>Why not <c>[Authorize(Roles = "admin,order-manager")]</c>?</b> Because that encodes a <i>job title</i>
/// where the code means a <i>capability</i>. When the business decides support agents may now issue refunds,
/// the role-based version requires finding and editing every attribute that should include them — and the
/// ones you miss keep working the old way, silently, with no error anywhere. Authorization bugs that fail
/// permissively are the worst kind, because nothing reports them.
/// </para>
/// <para>
/// Requiring <c>order:refund</c> instead means that endpoint's code <b>never changes again</b>. Which roles
/// hold that permission is configuration in Keycloak, expressed as a composite role, and granting it to
/// support agents becomes a settings change with no deployment.
/// </para>
/// </remarks>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Approves a <see cref="PermissionRequirement"/> when the token carries the matching permission claim.
/// </summary>
public sealed class PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Deliberately NOT calling context.Fail(). Simply not succeeding lets other handlers for the same
        // requirement have their say - which is what makes an OR relationship possible (for example
        // "order:read OR order:read:own for this specific order"). context.Fail() is a hard veto that no
        // other handler can override, and using it here would break that composition.
        logger.LogDebug(
            "Permission {Permission} not held by {User}.",
            requirement.Permission,
            context.User.Identity?.Name ?? "(anonymous)");

        return Task.CompletedTask;
    }
}
