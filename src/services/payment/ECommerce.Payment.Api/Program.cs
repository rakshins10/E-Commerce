using ECommerce.Auth;
using ECommerce.Contracts.Saga;
using ECommerce.EventBus;
using ECommerce.EventBus.RabbitMQ;
using ECommerce.Observability;
using ECommerce.Outbox;
using ECommerce.Payment.Api.Handlers;
using ECommerce.Payment.Api.Infrastructure;

using Microsoft.EntityFrameworkCore;

// -----------------------------------------------------------------------------
//  payment
// -----------------------------------------------------------------------------
//  A deterministic simulator behind a real event boundary. Swapping in an actual
//  provider changes RequestPaymentHandler.AuthoriseAsync and nothing else - the
//  saga, the outbox and the compensation path are all unaffected by where the
//  money comes from. See docs/services/payment.md.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("payment");

string connectionString =
    builder.Configuration.GetConnectionString("PaymentDb")
    ?? throw new InvalidOperationException("ConnectionStrings:PaymentDb is not configured.");

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

builder.Services.AddRabbitMqEventBus(builder.Configuration, "payment");
builder.Services.AddOutbox<PaymentDbContext>(
    builder.Configuration, typeof(RequestPaymentCommand).Assembly);

builder.Services.AddScoped<RequestPaymentHandler>();
builder.Services.AddScoped<RefundPaymentHandler>();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services
    .AddDefaultHealthChecks()
    .AddNpgSql(connectionString, name: "payment-db", tags: ["ready"]);

builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.UseObservability();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultHealthChecks();
app.MapOpenApi();

// The decline threshold is published on the identity endpoint deliberately: it is the one piece of
// configuration somebody demonstrating the compensation path needs to know, and hunting for it in a
// constant is a poor use of anybody's time.
app.MapGet("/", () => Results.Ok(new
{
    service = "payment",
    status = "up",
    environment = app.Environment.EnvironmentName,
    declineThreshold = RequestPaymentHandler.DeclineThreshold,
}));

using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
}

IEventBus bus = app.Services.GetRequiredService<IEventBus>();

await bus.SubscribeAsync<RequestPaymentCommand, RequestPaymentHandler>();
await bus.SubscribeAsync<RefundPaymentCommand, RefundPaymentHandler>();

await app.RunAsync();
