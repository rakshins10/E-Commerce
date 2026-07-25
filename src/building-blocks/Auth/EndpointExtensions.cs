using Microsoft.AspNetCore.Builder;

namespace ECommerce.Auth;

/// <summary>
/// Endpoint helpers that make the permission an endpoint requires visible at the route definition.
/// </summary>
/// <remarks>
/// <para>
/// The goal is that a reader can scan the route table and see the entire authorization surface of a service
/// without opening a single handler:
/// </para>
/// <code>
/// group.MapGet("/products",         GetProducts).RequirePermission(Permissions.Catalog.Read);
/// group.MapPost("/products",        CreateProduct).RequirePermission(Permissions.Catalog.Write);
/// group.MapDelete("/products/{id}", DeleteProduct).RequirePermission(Permissions.Catalog.Delete);
/// </code>
/// <para>
/// That property matters more than it looks. Authorization spread through handler bodies cannot be audited
/// by reading — you have to trust that every path checked. Declared on the route, an unprotected endpoint is
/// visible as an <i>absence</i>, and absences are much easier to spot in review than a missing line buried
/// three levels into a method.
/// </para>
/// </remarks>
public static class EndpointExtensions
{
    /// <summary>
    /// Requires the caller to hold <paramref name="permission"/>.
    /// </summary>
    /// <remarks>
    /// The policy name <i>is</i> the permission string, and every one is registered from
    /// <see cref="Permissions.All"/> by <see cref="AuthenticationExtensions.AddPermissionPolicies"/> — so
    /// referencing a permission that was never registered fails fast at startup rather than silently
    /// allowing the request.
    /// </remarks>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(permission);

    /// <summary>
    /// Requires the caller to hold <b>any one</b> of the given permissions.
    /// </summary>
    /// <remarks>
    /// For endpoints reachable by two different capabilities — an order detail page readable either by staff
    /// with <c>order:read</c> or by the customer who placed it with <c>order:read:own</c>. The ownership half
    /// still needs a resource check inside the handler; this only gets the request through the door.
    /// See <see cref="ResourceOwnerRequirement"/>.
    /// </remarks>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy =>
        {
            policy.RequireAuthenticatedUser();

            // A single assertion rather than several PermissionRequirements, because multiple requirements
            // on one policy are ANDed - which is the opposite of what is wanted here.
            policy.RequireAssertion(context =>
                permissions.Any(p => context.User.HasClaim(Permissions.ClaimType, p)));
        });

    /// <summary>
    /// Requires authentication only, with no particular permission.
    /// </summary>
    public static TBuilder RequireAuthenticated<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(AuthPolicies.Authenticated);
}
