using System.Data;

using ECommerce.Auth;
using ECommerce.Contracts.Inventory;
using ECommerce.Contracts.Ordering;
using ECommerce.Contracts.Payment;
using ECommerce.EventBus;
using ECommerce.EventBus.RabbitMQ;
using ECommerce.Observability;
using ECommerce.OrderingSaga.Api.Features;
using ECommerce.OrderingSaga.Api.Handlers;
using ECommerce.OrderingSaga.Api.Infrastructure;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

using Npgsql;

// -----------------------------------------------------------------------------
//  ordering-saga
// -----------------------------------------------------------------------------
//  The checkout orchestrator. It listens to what the participants say, decides
//  what happens next, and - when a step fails - issues the compensating actions.
//
//  It owns no business rules: the saga decides WHEN a transition happens, the
//  aggregate decides WHETHER it is allowed. See docs/services/ordering-saga.md.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("ordering-saga");

string connectionString =
    builder.Configuration.GetConnectionString("SagaDb")
    ?? throw new InvalidOperationException("ConnectionStrings:SagaDb is not configured.");

builder.Services.AddDbContext<SagaDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

builder.Services.AddRabbitMqEventBus(builder.Configuration, "ordering-saga");
builder.Services.AddOutbox<SagaDbContext>(
    builder.Configuration, typeof(OrderSubmittedIntegrationEvent).Assembly);

// Handlers are scoped, because each resolves a DbContext. The event bus creates a scope per message, so
// a handler never shares a change tracker with another message being processed concurrently.
builder.Services.AddScoped<OrderSubmittedHandler>();
builder.Services.AddScoped<StockReservedHandler>();
builder.Services.AddScoped<StockRejectedHandler>();
builder.Services.AddScoped<PaymentSucceededHandler>();
builder.Services.AddScoped<PaymentFailedHandler>();

// The read side gets a raw connection, not the DbContext - the same CQRS boundary as every other
// service here.
builder.Services.AddScoped<IDbConnection>(_ => new NpgsqlConnection(connectionString));
builder.Services.AddScoped<SagaQueries>();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services
    .AddDefaultHealthChecks()
    .AddNpgSql(connectionString, name: "saga-db", tags: ["ready"]);

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
    service = "ordering-saga",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

app.MapSagaEndpoints();

using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<SagaDbContext>().Database.MigrateAsync();
}

// Subscriptions come AFTER the migration, so a consumer never handles a message against a schema that
// does not exist yet. Each SubscribeAsync declares this service's own queue bound to the shared topic
// exchange: that is what gives it a copy of every event it cares about, while several replicas of it
// compete for the same messages rather than each getting one.
IEventBus bus = app.Services.GetRequiredService<IEventBus>();

await bus.SubscribeAsync<OrderSubmittedIntegrationEvent, OrderSubmittedHandler>();
await bus.SubscribeAsync<StockReservedIntegrationEvent, StockReservedHandler>();
await bus.SubscribeAsync<StockRejectedIntegrationEvent, StockRejectedHandler>();
await bus.SubscribeAsync<PaymentSucceededIntegrationEvent, PaymentSucceededHandler>();
await bus.SubscribeAsync<PaymentFailedIntegrationEvent, PaymentFailedHandler>();

await app.RunAsync();
