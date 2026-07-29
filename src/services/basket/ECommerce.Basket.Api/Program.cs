using ECommerce.Auth;
using ECommerce.Basket.Api.Features;
using ECommerce.Basket.Api.Infrastructure;
using ECommerce.Observability;

using StackExchange.Redis;

// -----------------------------------------------------------------------------
//  basket
// -----------------------------------------------------------------------------
//  A customer's basket, stored in Redis.
//
//  The composition root is deliberately the ONLY place that knows about
//  infrastructure. Everything below is wiring; no business logic lives here.
//  See docs/architecture.md and docs/services/basket.md.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("basket");

string redisConnection =
    builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");

// A SINGLETON, and this matters. ConnectionMultiplexer is designed to be shared for the lifetime of the
// application: it multiplexes every command over a small number of sockets. Creating one per request -
// the reflex when it is called "Connection" - opens a socket per request, exhausts the connection pool
// under load, and is the single most common way to make Redis look slow.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnection));

builder.Services.AddScoped<BasketRepository>();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services
    .AddDefaultHealthChecks()
    .AddRedis(redisConnection, name: "basket-redis", tags: ["ready"]);

builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Correlation must be first: a request that fails inside exception handling should still be correlated.
app.UseObservability();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultHealthChecks();
app.MapOpenApi();

// A minimal identity endpoint. Useful when you have thirty containers running and want to confirm which
// service is answering on a port.
app.MapGet("/", () => Results.Ok(new
{
    service = "basket",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

app.MapBasketEndpoints();

await app.RunAsync();
