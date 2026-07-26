using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ECommerce.UserProfile.Api.Infrastructure;

/// <summary>
/// Creates a <see cref="UserProfileDbContext"/> for the <c>dotnet ef</c> tooling, which otherwise tries to
/// build the application host and fails on the missing connection string. The placeholder below never
/// connects - generating a migration only needs the provider, so EF knows to emit PostgreSQL DDL.
/// </summary>
public sealed class UserProfileDbContextFactory : IDesignTimeDbContextFactory<UserProfileDbContext>
{
    public UserProfileDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__UserProfileDb")
            ?? "Host=localhost;Port=15435;Database=userprofile;Username=ecom;Password=dev_only_pg_pw";

        return new UserProfileDbContext(
            new DbContextOptionsBuilder<UserProfileDbContext>().UseNpgsql(connectionString).Options);
    }
}
