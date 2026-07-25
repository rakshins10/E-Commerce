using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.EventBus.RabbitMQ;

/// <summary>
/// Composition-root wiring for the RabbitMQ event bus.
/// </summary>
/// <remarks>
/// <b>This is the only place a service is allowed to know RabbitMQ exists.</b> Everything else depends on
/// <see cref="IEventBus"/> from the transport-agnostic <c>ECommerce.EventBus</c> package. An architecture test
/// asserts that no service project references <c>RabbitMQ.Client</c> directly
/// (<c>docs/adr/0008-monorepo.md</c>) — which is what makes the swap in
/// <c>docs/adr/0016-rabbitmq-behind-ieventbus.md</c> a real option rather than an aspiration.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the RabbitMQ event bus and its options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root; the <c>EventBus</c> section is bound to
    /// <see cref="RabbitMqOptions"/>.</param>
    /// <param name="subscriptionClientName">This service's name, used to prefix its queues. Passed explicitly
    /// rather than read from configuration because getting it wrong silently makes two services share a queue
    /// and steal each other's messages — a failure that looks like "events randomly go missing".</param>
    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        string subscriptionClientName)
    {
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Configure(options => options.SubscriptionClientName = subscriptionClientName)
            .ValidateDataAnnotations()

            // Validate at startup, not on first use. A missing host name should stop the process immediately
            // with a message naming the property, rather than surfacing minutes later inside a background
            // service where nobody is watching.
            .ValidateOnStart();

        // Singletons: one AMQP connection per process, and one bus holding the consumer channels for the
        // process lifetime. Handlers are resolved per message from a scope created inside the bus.
        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddSingleton<IEventBusSubscriptionManager, InMemoryEventBusSubscriptionManager>();
        services.AddSingleton<IEventBus, RabbitMqEventBus>();

        return services;
    }

    /// <summary>
    /// Registers an integration event handler so the bus can resolve it per message.
    /// </summary>
    /// <remarks>
    /// <b>Scoped, deliberately.</b> Handlers take scoped dependencies — a <c>DbContext</c>, the ambient
    /// transaction — and a singleton handler capturing a <c>DbContext</c> would share one change tracker across
    /// every message in the process. That is a genuinely nasty bug: it works under light load and corrupts data
    /// under concurrency.
    /// </remarks>
    public static IServiceCollection AddIntegrationEventHandler<THandler>(this IServiceCollection services)
        where THandler : class, IIntegrationEventHandler
    {
        services.AddScoped<THandler>();
        return services;
    }
}
