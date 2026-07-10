using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Specpen.Web.Infrastructure;

namespace Specpen.Web.Data;

public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=specpen;Username=postgres;Password=postgres";

        var builder = new DbContextOptionsBuilder<CatalogDbContext>();
        builder.UseNpgsql(ConfigurationHelpers.NormalizePostgresConnectionString(connectionString));
        return new CatalogDbContext(builder.Options);
    }
}
