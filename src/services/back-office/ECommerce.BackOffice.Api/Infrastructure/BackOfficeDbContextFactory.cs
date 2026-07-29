using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ECommerce.BackOffice.Api.Infrastructure;

/// <summary>Lets the EF tools build a context without booting the application.</summary>
public sealed class BackOfficeDbContextFactory : IDesignTimeDbContextFactory<BackOfficeDbContext>
{
    public BackOfficeDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BackOfficeDbContext>()
            .UseNpgsql("Host=localhost;Database=backoffice;Username=postgres;Password=design_time_only")
            .Options);
}
