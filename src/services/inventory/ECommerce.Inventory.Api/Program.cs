using System.Data;

using ECommerce.Auth;
using ECommerce.Contracts.Ordering;
using ECommerce.Contracts.Saga;
using ECommerce.EventBus;
using ECommerce.EventBus.RabbitMQ;
using ECommerce.Inventory.Api.Features;
using ECommerce.Inventory.Api.Handlers;
using ECommerce.Inventory.Api.Infrastructure;
using ECommerce.Observability;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

using Npgsql;

// -----------------------------------------------------------------------------
//  inventory
// -----------------------------------------------------------------------------
//  Stock levels, reservations, and the compensating release. Reserving is
//  asynchronous because the customer is not waiting on it - they already have an
//  order number. See docs/services/inventory.md.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("inventory");

string connectionString =
    builder.Configuration.GetConnectionString("InventoryDb")
    ?? throw new InvalidOperationException("ConnectionStrings:InventoryDb is not configured.");

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

builder.Services.AddScoped<IDbConnection>(_ => new NpgsqlConnection(connectionString));
builder.Services.AddScoped<StockQueries>();

builder.Services.AddRabbitMqEventBus(builder.Configuration, "inventory");
builder.Services.AddOutbox<InventoryDbContext>(
    builder.Configuration, typeof(ReserveStockCommand).Assembly);

builder.Services.AddScoped<ReserveStockHandler>();
builder.Services.AddScoped<ReleaseStockHandler>();
builder.Services.AddScoped<OrderShippedHandler>();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services
    .AddDefaultHealthChecks()
    .AddNpgSql(connectionString, name: "inventory-db", tags: ["ready"]);

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
    service = "inventory",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

app.MapInventoryEndpoints();

using (IServiceScope scope = app.Services.CreateScope())
{
    InventoryDbContext db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.MigrateAsync();
    await InventorySeeder.SeedAsync(db, app.Configuration.GetValue("SeedDemoData", defaultValue: true));
}

// Subscriptions come AFTER the migration, so a consumer never handles a message against a schema that
// does not exist yet.
IEventBus bus = app.Services.GetRequiredService<IEventBus>();

await bus.SubscribeAsync<ReserveStockCommand, ReserveStockHandler>();
await bus.SubscribeAsync<ReleaseStockCommand, ReleaseStockHandler>();
await bus.SubscribeAsync<OrderShippedIntegrationEvent, OrderShippedHandler>();

await app.RunAsync();
