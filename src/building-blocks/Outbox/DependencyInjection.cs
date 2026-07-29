using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Outbox;

/// <summary>Registers the outbox for a service.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Wires the outbox writer, the event-name registry and the background publisher.
    /// </summary>
    /// <typeparam name="TContext">The service's <see cref="DbContext"/>, which must expose the outbox sets.</typeparam>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="configuration">Configuration root, read for the <c>Outbox</c> section.</param>
    /// <param name="eventAssemblies">
    /// Assemblies to scan for integration events. Only types in these can ever be deserialised from the
    /// outbox — see <see cref="IOutboxEventResolver"/> for why that restriction matters.
    /// </param>
    public static IServiceCollection AddOutbox<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] eventAssemblies)
        where TContext : DbContext, IOutboxContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        // Resolved as the service's own context, so the outbox rows are written through the same change
        // tracker - and therefore the same transaction - as the business data.
        services.AddScoped<IOutboxContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        services.AddSingleton<IOutboxEventResolver>(
            new AssemblyScanningOutboxEventResolver(
                eventAssemblies.Length > 0 ? eventAssemblies : [Assembly.GetCallingAssembly()]));

        services.AddHostedService<OutboxPublisher<TContext>>();

        return services;
    }
}
