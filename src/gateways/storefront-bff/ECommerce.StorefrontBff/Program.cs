using ECommerce.Auth;
using ECommerce.Observability;

// -----------------------------------------------------------------------------
//  Storefront BFF
// -----------------------------------------------------------------------------
//  The single entry point for BOTH storefronts - React on :3000 and Angular on
//  :4200. One BFF, because a BFF exists per client EXPERIENCE, not per
//  framework (docs/adr/0006). Two apps with identical UX have identical data
//  needs, so splitting them would be duplication with no benefit - and sharing
//  one is what makes their parity provable: identical endpoints, identical
//  payloads, so any visible difference is a client-side bug.
//
//  Two jobs:
//    1. ROUTE  - proxy to the owning service, so the browser never learns the
//                internal topology and services can move without a client
//                release.
//    2. SHAPE  - aggregate several services into one payload per screen, so a
//                product page is one round trip instead of three.
//
//  What it must NEVER do is decide what is TRUE. A BFF may choose what to call
//  and how to shape the answer; the moment it decides whether an order may be
//  cancelled, the domain has leaked into the edge.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("storefront-bff");

// --- Reverse proxy ------------------------------------------------------------
// Routes come from configuration rather than code so a downstream address can
// change without a rebuild. See appsettings.json.
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// --- Auth ---------------------------------------------------------------------
// Validated HERE and again at each service. The gateway is not a trust boundary
// worth betting everything on: a misconfiguration, a future internal caller, or
// a compromised container must still meet authentication at the service itself.
// Defence in depth - docs/adr/0005.
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

// --- CORS ---------------------------------------------------------------------
// The storefronts are separate origins, so every browser call is cross-origin.
// Origins are listed explicitly: AllowAnyOrigin cannot be combined with
// credentials, and a wildcard would let any site on the internet call this
// gateway with the user's token attached.
string[] allowedOrigins =
    builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    // So the browser can read the correlation id off the response and a user can
    // quote it to support.
    .WithExposedHeaders("X-Correlation-Id")));

builder.Services.AddDefaultHealthChecks();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.UseObservability();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultHealthChecks();
app.MapOpenApi();

app.MapGet("/", () => Results.Ok(new
{
    service = "storefront-bff",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

// Proxied routes, defined in appsettings.json.
app.MapReverseProxy();

await app.RunAsync();
