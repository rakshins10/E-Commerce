using ECommerce.Auth;
using ECommerce.Contracts.Ordering;
using ECommerce.EventBus;
using ECommerce.EventBus.RabbitMQ;
using ECommerce.Notification.Api.Handlers;
using ECommerce.Notification.Api.Infrastructure;
using ECommerce.Observability;
using ECommerce.Outbox;

using Microsoft.EntityFrameworkCore;

// -----------------------------------------------------------------------------
//  notification
// -----------------------------------------------------------------------------
//  Emails the customer when something happens to their order. The one service
//  where a duplicate message is genuinely unrecoverable - an email cannot be
//  un-sent - so it deduplicates explicitly with processed_messages, in the same
//  transaction as the notification row.
//  See docs/services/notification.md.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddObservability("notification");

string connectionString =
    builder.Configuration.GetConnectionString("NotificationDb")
    ?? throw new InvalidOperationException("ConnectionStrings:NotificationDb is not configured.");

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

builder.Services.AddRabbitMqEventBus(builder.Configuration, "notification");
builder.Services.AddOutbox<NotificationDbContext>(
    builder.Configuration, typeof(OrderSubmittedIntegrationEvent).Assembly);

builder.Services.AddScoped<OrderSubmittedNotificationHandler>();
builder.Services.AddScoped<OrderPaidNotificationHandler>();
builder.Services.AddScoped<OrderShippedNotificationHandler>();
builder.Services.AddScoped<OrderCancelledNotificationHandler>();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

builder.Services
    .AddDefaultHealthChecks()
    .AddNpgSql(connectionString, name: "notification-db", tags: ["ready"]);

WebApplication app = builder.Build();

app.UseObservability();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultHealthChecks();

app.MapGet("/", () => Results.Ok(new
{
    service = "notification",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

IEventBus bus = app.Services.GetRequiredService<IEventBus>();

await bus.SubscribeAsync<OrderSubmittedIntegrationEvent, OrderSubmittedNotificationHandler>();
await bus.SubscribeAsync<OrderPaidIntegrationEvent, OrderPaidNotificationHandler>();
await bus.SubscribeAsync<OrderShippedIntegrationEvent, OrderShippedNotificationHandler>();
await bus.SubscribeAsync<OrderCancelledIntegrationEvent, OrderCancelledNotificationHandler>();

await app.RunAsync();
