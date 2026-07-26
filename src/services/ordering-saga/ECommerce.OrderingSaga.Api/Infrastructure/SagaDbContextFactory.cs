using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ECommerce.OrderingSaga.Api.Infrastructure;

/// <summary>Lets the EF tools build a context without booting the application.</summary>
public sealed class SagaDbContextFactory : IDesignTimeDbContextFactory<SagaDbContext>
{
    public SagaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SagaDbContext>()
            .UseNpgsql("Host=localhost;Database=saga;Username=postgres;Password=design_time_only")
            .Options);
}
