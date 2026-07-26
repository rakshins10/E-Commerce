using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ECommerce.Auth;

/// <summary>
/// Configuration for token validation, bound from the <c>Auth</c> section.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// The expected <c>iss</c> claim — the URL <b>the browser used</b> to reach Keycloak.
    /// </summary>
    /// <remarks>
    /// <b>The single most common Keycloak-in-Docker failure.</b> Inside the compose network Keycloak is
    /// <c>http://keycloak:8080</c>, but to the browser it is <c>http://localhost:8080</c>. The <c>iss</c>
    /// claim records whichever the client used, so a service validating against the internal hostname
    /// rejects every browser-issued token — while both are the same server. Pin one public URL and use it
    /// here. See <c>docs/getting-started.md#troubleshooting</c>.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Where to fetch the OIDC discovery document, and through it the JWKS signing keys.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Issuer"/>: this is fetched <i>server to server</i> over the
    /// internal network, so it uses the internal hostname, while the issuer that must match the token is the
    /// public one.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>
    /// The expected <c>aud</c> claim. Every service in this system expects <c>ecommerce-api</c>.
    /// </summary>
    /// <remarks>
    /// <b>Audience validation is the check most often omitted, and omitting it is a real vulnerability.</b>
    /// Without it, a token minted by the same Keycloak realm for a completely different application is
    /// accepted here. If that other application has a laxer login policy, it becomes a way in.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = "ecommerce-api";

    /// <summary>
    /// Allow HTTP metadata retrieval. <b>Development only.</b>
    /// </summary>
    public bool RequireHttpsMetadata { get; set; }
}

/// <summary>
/// One-call authentication and authorization setup, applied identically by every service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a shared building block.</b> Security must be uniform. Nine services each configuring
/// token validation by hand means nine chances to omit audience validation, nine clock-skew settings, and
/// nine subtly different claim mappings. When one drifts, it becomes the way in — and nothing reports it,
/// because the service still works.
/// </para>
/// <para>
/// <b>Defence in depth:</b> tokens are validated here <i>and</i> again at the BFF. The gateway is not a trust
/// boundary worth betting everything on — a misconfiguration, a future internal caller, or a compromised
/// container must still meet authentication at the service. See
/// <c>docs/adr/0005-keycloak-as-identity-provider.md</c>.
/// </para>
/// </remarks>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Adds JWT bearer authentication validating signature, issuer, lifetime and audience.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        AuthOptions options = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
                              ?? throw new InvalidOperationException(
                                  $"The '{AuthOptions.SectionName}' configuration section is missing.");

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                // The discovery document is fetched once at startup and cached, along with the JWKS signing
                // keys. Every subsequent token is then verified locally with no network call - which is what
                // makes JWT validation cheap enough to do on every request in every service.
                jwt.MetadataAddress = options.MetadataAddress;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.Audience = options.Audience;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,

                    ValidateAudience = true,
                    ValidAudience = options.Audience,

                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // Default is five minutes, which means an expired token keeps working for five more.
                    // Thirty seconds is ample for real clock drift between containers.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    // Keycloak puts the user id in `sub` and the display name in `preferred_username`.
                    // Without this mapping, User.Identity.Name is null and role checks find nothing.
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role,
                };

                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        // Keycloak nests realm roles under realm_access.roles, which the default claim
                        // handling does not understand. Flatten them to standard role claims so
                        // User.IsInRole works and roles show up where ASP.NET expects them.
                        //
                        // Permissions need no such work: a protocol mapper in the realm already emits them
                        // as a flat `permissions` array. See identity/keycloak/realm-export.json.
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            FlattenRealmRoles(identity);
                        }

                        return Task.CompletedTask;
                    },

                    OnChallenge = context =>
                    {
                        // Surface WHY a token was rejected in a response header. Without this the client
                        // sees a bare 401 and cannot tell an expired token (refresh and retry) from a
                        // wrong audience (a configuration bug). Header only, never the body, and only the
                        // error code - never the exception detail.
                        if (context.AuthenticateFailure is not null)
                        {
                            context.Response.Headers["x-auth-error"] =
                                context.AuthenticateFailure.GetType().Name;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    /// <summary>
    /// Registers one authorization policy per permission, plus the resource-based handler.
    /// </summary>
    /// <remarks>
    /// Registering them from <see cref="Permissions.All"/> rather than by hand means adding a permission is
    /// a one-line change in one file, and no endpoint can reference a policy that was never registered.
    /// </remarks>
    public static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();

        var builder = services.AddAuthorizationBuilder();

        foreach (string permission in Permissions.All)
        {
            builder.AddPolicy(permission, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new PermissionRequirement(permission));
            });
        }

        // Authenticated but unprivileged - for endpoints that only need to know who you are.
        builder.AddPolicy(AuthPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());

        return services;
    }

    private static void FlattenRealmRoles(ClaimsIdentity identity)
    {
        Claim? realmAccess = identity.FindFirst("realm_access");
        if (realmAccess is null)
        {
            return;
        }

        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(realmAccess.Value);

            if (document.RootElement.TryGetProperty("roles", out System.Text.Json.JsonElement roles)
                && roles.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (System.Text.Json.JsonElement role in roles.EnumerateArray())
                {
                    string? value = role.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Role, value));
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // A malformed realm_access claim is not worth failing authentication over - the token's
            // signature is already verified, so this is a shape problem, not a trust problem. The caller
            // simply ends up with no role claims, and any permission check then denies them.
        }
    }
}

/// <summary>Named policies that are not one-per-permission.</summary>
public static class AuthPolicies
{
    /// <summary>Authenticated, with no particular permission required.</summary>
    public const string Authenticated = "authenticated";
}
