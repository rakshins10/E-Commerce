using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ECommerce.Ordering.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct a context without booting the application.
/// </summary>
/// <remarks>
/// Without this, the EF tools start the whole host to find a DbContext - which means they need every
/// connection string, every environment variable and a reachable Keycloak, just to generate a C# file.
/// The connection string here is a design-time placeholder and is never used to connect.
/// </remarks>
public sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<OrderingDbContext> options =
            new DbContextOptionsBuilder<OrderingDbContext>()
                .UseNpgsql("Host=localhost;Database=ordering;Username=postgres;Password=design_time_only")
                .Options;

        return new OrderingDbContext(options);
    }
}
