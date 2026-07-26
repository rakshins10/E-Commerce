using System.Data;
using ECommerce.Auth;
using ECommerce.Common.SeedWork;
using ECommerce.Contracts.Ordering;
using ECommerce.Contracts.Saga;
using ECommerce.EventBus;
using ECommerce.EventBus.RabbitMQ;
using ECommerce.Observability;
using ECommerce.Ordering.Api.Features;
using ECommerce.Ordering.Api.Handlers;
using ECommerce.Ordering.Application.Orders;
using ECommerce.Ordering.Domain.Orders;
using ECommerce.Ordering.Infrastructure;
using ECommerce.Ordering.Infrastructure.Orders;
using ECommerce.Ordering.Infrastructure.Services;
using ECommerce.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// -----------------------------------------------------------------------------
//  ordering
// -----------------------------------------------------------------------------
//  The core subdomain: the Order aggregate, CQRS, and the transactional outbox.
//
//  The composition root is deliberately the ONLY place that knows about
//  infrastructure. Everything below is wiring; no business logic lives here.
//  See docs/services/ordering.md.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("ordering");

string connectionString =
    builder.Configuration.GetConnectionString("OrderingDb")
    ?? throw new InvalidOperationException("ConnectionStrings:OrderingDb is not configured.");

builder.Services.AddDbContext<OrderingDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

// The read side gets a raw connection, not the DbContext. That is the CQRS boundary made physical:
// OrderQueries has no way to reach a domain type because it never sees one.
builder.Services.AddScoped<IDbConnection>(_ => new NpgsqlConnection(connectionString));
builder.Services.AddScoped<OrderQueries>();

builder.Services.AddScoped<IRepository<Order, Guid>, OrderRepository>();
builder.Services.AddScoped<IOrderingUnitOfWork, OrderingUnitOfWork>();
builder.Services.AddScoped<PlaceOrderHandler>();
builder.Services.AddScoped<CancelOrderHandler>();
builder.Services.AddScoped<AdvanceOrderHandler>();

// Consumes the saga's transition commands. Ordering is both a publisher and a consumer, which is the
// normal shape - a service that only publishes is a source, and one that only consumes is a sink.
builder.Services.AddScoped<AdvanceOrderCommandHandler>();

// --- Service-to-service HTTP -------------------------------------------------------------------
//
// Resilience lives here, in the composition root, rather than inside each client. The standard
// handler bundles two policies that solve different problems:
//
//   Retry with exponential backoff AND JITTER. Backoff handles a brief blip. Jitter is what stops
//   fifty instances that all failed at the same moment retrying in lockstep and re-creating the load
//   spike that caused the failure - the "thundering herd". Without jitter, retries synchronise.
//
//   Circuit breaker. After enough consecutive failures it stops calling for a while. Retrying a
//   service that is genuinely down wastes the caller's threads and connections and delays its
//   recovery; failing fast is kinder to both ends.
builder.Services.AddHttpClient<IBasketService, HttpBasketService>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["Services:Basket"]
            ?? throw new InvalidOperationException("Services:Basket is not configured."));

        // Bounded, because checkout is a request a person is waiting on. Without a timeout, a hung
        // Basket holds this request - and its thread and connection - indefinitely.
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient<ICatalogService, HttpCatalogService>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["Services:Catalog"]
            ?? throw new InvalidOperationException("Services:Catalog is not configured."));

        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddStandardResilienceHandler();

// --- Messaging ---------------------------------------------------------------------------------
// The subscription client name becomes the queue name prefix, so each service gets its OWN queue
// bound to the shared exchange. That is what makes competing consumers work: three replicas of
// Ordering share one queue and split the messages, while Inventory has a separate queue and receives
// its own copy of every event.
builder.Services.AddRabbitMqEventBus(builder.Configuration, "ordering");

// The outbox publisher, scanning the contracts assembly for event types. Only types in that assembly
// can ever be deserialised from the outbox table - see IOutboxEventResolver.
builder.Services.AddOutbox<OrderingDbContext>(
    builder.Configuration,
    typeof(OrderSubmittedIntegrationEvent).Assembly);

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services
    .AddDefaultHealthChecks()
    .AddNpgSql(connectionString, name: "ordering-db", tags: ["ready"]);

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
    service = "ordering",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

app.MapOrderEndpoints();

// Simplified for this repo: production would not migrate from application startup, because several
// replicas would race and a failed migration would crash every instance rather than one deployment
// step. See docs/operations/deployment.md.
using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.MigrateAsync();
}

// Subscriptions come AFTER the migration, so a consumer never handles a message against a schema that
// does not exist yet.
IEventBus bus = app.Services.GetRequiredService<IEventBus>();

await bus.SubscribeAsync<AdvanceOrderCommand, AdvanceOrderCommandHandler>();

await app.RunAsync();
