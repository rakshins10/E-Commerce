using ECommerce.Auth;
using ECommerce.Observability;
using ECommerce.UserProfile.Api.Features;
using ECommerce.UserProfile.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

// -----------------------------------------------------------------------------
//  User Profile service
// -----------------------------------------------------------------------------
//  Owns the customer data that does NOT belong in an identity provider: display
//  name, contact details, saved addresses, preferences and consent records.
//
//  It NEVER stores a password, a role or a group. Keycloak answers "who are you
//  and what may you do"; this answers "what do we know about you". The two are
//  joined only by the `sub` claim.
//
//  See docs/services/user-profile.md and docs/adr/0004.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("user-profile");

string connectionString =
    builder.Configuration.GetConnectionString("UserProfileDb")
    ?? throw new InvalidOperationException("ConnectionStrings:UserProfileDb is not configured.");

builder.Services.AddDbContext<UserProfileDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services
    .AddDefaultHealthChecks()
    .AddNpgSql(connectionString, name: "userprofile-db", tags: ["ready"]);

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
    service = "user-profile",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

app.MapProfileEndpoints();

// Migrate on startup. No seeding: profiles are created lazily on a user's first
// authenticated request, which is the point of the provisioning design.
using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<UserProfileDbContext>().Database.MigrateAsync();
}

await app.RunAsync();
