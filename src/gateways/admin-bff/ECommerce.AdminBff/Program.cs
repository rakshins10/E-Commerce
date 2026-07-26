using ECommerce.Auth;
using ECommerce.Observability;

// -----------------------------------------------------------------------------
//  admin-bff
// -----------------------------------------------------------------------------
//  The back-office gateway. A SECOND BFF, deliberately separate from the
//  storefront's - see the .csproj for why.
//
//  Every route here requires a permission at the EDGE as well as at the service.
//  The storefront BFF only asks "are you signed in?" and leaves authorization to
//  each service; this one is stricter, because the blast radius of a mistake is
//  larger. Defence in depth, not a replacement for the service's own checks.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("admin-bff");

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

string[] allowedOrigins =
    builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

// The admin apps only, never the storefront origins. An explicit allow-list rather than
// AllowAnyOrigin: with credentials in play, reflecting any origin lets any page on the internet make
// authenticated admin calls on a signed-in manager's behalf.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
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
    service = "admin-bff",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

app.MapReverseProxy();

await app.RunAsync();
