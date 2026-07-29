using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ECommerce.Auth;

/// <summary>
/// The caller of the current request: who they are and what they may do.
/// </summary>
/// <remarks>
/// <para>
/// Injected wherever the caller's identity is needed, rather than reaching into
/// <c>HttpContext.User.FindFirst("sub")</c> at each call site. Three reasons that matters:
/// </para>
/// <list type="number">
///   <item><description><b>Testable.</b> A handler that depends on <see cref="ICurrentUser"/> can be unit
///   tested with a stub. One that reads <c>HttpContext</c> needs an HTTP context faked into existence.</description></item>
///   <item><description><b>One place to be wrong.</b> The mapping from claim names to concepts is subtle —
///   <c>sub</c> vs <c>preferred_username</c> vs <c>email</c> — and scattering it means fixing it in a dozen
///   places when the identity provider changes.</description></item>
///   <item><description><b>It keeps the application layer free of ASP.NET.</b> A command handler asking
///   "who is this?" should not need a reference to the web framework.</description></item>
/// </list>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>
    /// The Keycloak <c>sub</c> claim — the stable, opaque identifier for this user.
    /// <see langword="null"/> when the request is anonymous.
    /// </summary>
    /// <remarks>
    /// <b>This is the only identifier that may be stored as a foreign key against business data.</b> Email
    /// and username are mutable and reassignable; keying on either guarantees an eventual orphaning
    /// incident when someone changes their email. See <c>docs/adr/0004-identity-vs-profile-data-split.md</c>.
    /// </remarks>
    string? UserId { get; }

    string? UserName { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    /// <summary>Realm roles held by the caller — job titles, for display.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Permissions held by the caller — capabilities, what authorization actually checks.</summary>
    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Whether the caller holds a permission.</summary>
    /// <remarks>
    /// Prefer declaring the requirement on the endpoint (<c>.RequirePermission(...)</c>) so it is visible in
    /// the routing table. Use this only where the decision is genuinely conditional — for example choosing
    /// between a full and a redacted response shape.
    /// </remarks>
    bool HasPermission(string permission);
}

/// <inheritdoc />
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? Principal?.FindFirstValue("sub");

    public string? UserName => Principal?.FindFirstValue("preferred_username");

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email)
                            ?? Principal?.FindFirstValue("email");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public IReadOnlyCollection<string> Permissions =>
        Principal?.FindAll(ECommerce.Auth.Permissions.ClaimType).Select(c => c.Value).ToArray() ?? [];

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(ECommerce.Auth.Permissions.ClaimType, permission) ?? false;
}

/// <summary>Convenience over <see cref="ICurrentUser"/>.</summary>
public static class CurrentUserExtensions
{
    /// <summary>
    /// The caller's Keycloak <c>sub</c>, or an exception if there is not one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every <c>/me</c> route needs this and none of them can proceed without it, so the alternative is
    /// the same four-line null check copied into every handler in every service — where it will
    /// eventually be written as <c>?? string.Empty</c> by someone in a hurry, silently turning "no
    /// subject" into "the customer whose id is the empty string".
    /// </para>
    /// <para>
    /// It throws rather than returning null because the situation is genuinely impossible on a route
    /// that requires a permission: authentication has already succeeded, so a token without a subject
    /// means a misconfigured identity provider, not a bad request. A 500 is the honest response, and the
    /// stack trace points at the configuration.
    /// </para>
    /// </remarks>
    public static string RequireSubject(this ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.UserId
               ?? throw new InvalidOperationException(
                   "Authenticated request carries no 'sub' claim. Check the identity provider's "
                   + "token configuration - this should be impossible on an authorized route.");
    }
}
