using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ECommerce.Catalog.Api.Infrastructure;

/// <summary>
/// Creates a <see cref="CatalogDbContext"/> for the <c>dotnet ef</c> tooling.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>dotnet ef migrations add</c> needs a <c>DbContext</c> instance to read the
/// model from. By default it tries to build the application's host, which means running
/// <c>Program.cs</c> — and that throws here, because the composition root requires a real connection string
/// that is not present at design time:
/// </para>
/// <code>
/// Error: ConnectionStrings:CatalogDb is not configured.
/// Unable to create a 'DbContext' of type 'CatalogDbContext'.
/// </code>
/// <para>
/// <see cref="IDesignTimeDbContextFactory{TContext}"/> is the supported way out: when this type exists,
/// the tooling uses it instead of building the host.
/// </para>
/// <para>
/// <b>The connection string below is never used to connect.</b> Generating a migration only needs the
/// provider, so that EF Core knows to emit PostgreSQL DDL rather than SQL Server DDL. Nothing here reaches a
/// database, which is why a placeholder is safe — and why it is not a committed secret.
/// </para>
/// <para>
/// To generate a migration against a real database (for <c>dotnet ef database update</c>), set
/// <c>ConnectionStrings__CatalogDb</c> in the environment and it is picked up below.
/// </para>
/// </remarks>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__CatalogDb")
            ?? "Host=localhost;Port=15433;Database=catalog;Username=ecom;Password=dev_only_pg_pw";

        DbContextOptions<CatalogDbContext> options =
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new CatalogDbContext(options);
    }
}
