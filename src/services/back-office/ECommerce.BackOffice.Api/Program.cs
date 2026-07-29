using System.Data;

using ECommerce.Auth;
using ECommerce.BackOffice.Api.Features;
using ECommerce.BackOffice.Api.Infrastructure;
using ECommerce.Observability;

using Microsoft.EntityFrameworkCore;

using Npgsql;

// -----------------------------------------------------------------------------
//  back-office
// -----------------------------------------------------------------------------
//  Dashboard aggregates, user administration and the audit log.
//
//  This is the ONE service that reads another service's database, and it does so
//  read-only and only for aggregates. See Features/DashboardEndpoints.cs for the
//  argument and what keeps the exception bounded.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("back-office");

string ownConnection =
    builder.Configuration.GetConnectionString("BackOfficeDb")
    ?? throw new InvalidOperationException("ConnectionStrings:BackOfficeDb is not configured.");

builder.Services.AddDbContext<BackOfficeDbContext>(options =>
    options.UseNpgsql(ownConnection, npgsql => npgsql.EnableRetryOnFailure(3)));

builder.Services.AddScoped<AuditWriter>();

// --- Read-only connections to other services' databases -----------------------------------------
//
// KEYED, so a query cannot reach the wrong database by accident. An unkeyed IDbConnection with five
// registrations would silently resolve to whichever was registered last, and a dashboard reading
// inventory figures out of the ordering database would produce plausible nonsense rather than an error.
//
// The connection strings use accounts with SELECT only, so "read-only" is enforced by the database
// rather than by this comment. See deploy/docker-compose.yml.
static void AddReadOnly(WebApplicationBuilder builder, string key, string configKey)
{
    string connection =
        builder.Configuration.GetConnectionString(configKey)
        ?? throw new InvalidOperationException($"ConnectionStrings:{configKey} is not configured.");

    builder.Services.AddKeyedScoped<IDbConnection>(key, (_, _) => new NpgsqlConnection(connection));
}

AddReadOnly(builder, "backoffice", "BackOfficeDb");
AddReadOnly(builder, "ordering", "OrderingReadDb");
AddReadOnly(builder, "saga", "SagaReadDb");
AddReadOnly(builder, "inventory", "InventoryReadDb");

builder.Services.AddScoped<DashboardQueries>();
builder.Services.AddScoped<AuditQueries>();

// --- Keycloak Admin API -------------------------------------------------------------------------
builder.Services.Configure<KeycloakAdminOptions>(
    builder.Configuration.GetSection(KeycloakAdminOptions.SectionName));

builder.Services.AddHttpClient<KeycloakAdminClient>(client =>
    {
        // Bounded: an admin page waiting indefinitely on a hung identity provider is a page that looks
        // broken with no explanation.
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    .AddStandardResilienceHandler();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services
    .AddDefaultHealthChecks()
    .AddNpgSql(ownConnection, name: "backoffice-db", tags: ["ready"]);

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
    service = "back-office",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

app.MapDashboardEndpoints();
app.MapUserEndpoints();

using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<BackOfficeDbContext>().Database.MigrateAsync();
}

await app.RunAsync();
