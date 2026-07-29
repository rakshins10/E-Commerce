using System.Data;
using ECommerce.Auth;
using ECommerce.Catalog.Api.Features.Products;
using ECommerce.Catalog.Api.Infrastructure;
using ECommerce.Observability;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// -----------------------------------------------------------------------------
//  Catalog service
// -----------------------------------------------------------------------------
//  Products, categories, brands, search and browse.
//
//  The composition root is deliberately the ONLY place that knows about
//  infrastructure. Everything below is wiring; no business logic lives here.
//  See docs/services/catalog.md.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("catalog");

string connectionString =
    builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException("ConnectionStrings:CatalogDb is not configured.");

// --- Write side: EF Core ------------------------------------------------------
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

// --- Read side: Dapper --------------------------------------------------------
// A separate, short-lived connection per request rather than reusing the EF
// context's. The two paths are independent by design (docs/adr/0012), and
// sharing a connection would couple them again through the transaction scope.
builder.Services.AddScoped<IDbConnection>(_ => new NpgsqlConnection(connectionString));
builder.Services.AddScoped<ProductQueries>();

// --- Auth ---------------------------------------------------------------------
// Registered even though browsing is anonymous: writes arrive in Phase 9, and a
// service that validates tokens from day one cannot forget to later.
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

// --- CORS ---------------------------------------------------------------------
// The storefronts call the BFF, not this service, so this is only for local
// debugging against the service directly. Origins come from configuration - a
// wildcard would be wrong even in development, because it trains the habit.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

// --- Health -------------------------------------------------------------------
builder.Services
    .AddDefaultHealthChecks()
    // Tagged `ready`, never `live`. A database blip must stop traffic being
    // routed here; it must NOT make the orchestrator restart the process,
    // because restarting does not fix a database.
    // See docs/operations/health-checks.md.
    .AddNpgSql(connectionString, name: "catalog-db", tags: ["ready"]);

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
    service = "catalog",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

app.MapProductEndpoints();

// Migrate and seed before serving. See CatalogSeeder for why production would
// do this as a separate deployment step instead.
await CatalogSeeder.MigrateAndSeedAsync(
    app.Services,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CatalogSeeder"),
    app.Configuration.GetValue("SeedDemoData", defaultValue: true));

await app.RunAsync();
