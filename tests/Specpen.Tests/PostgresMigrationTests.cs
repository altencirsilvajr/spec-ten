using Microsoft.EntityFrameworkCore;
using Specpen.Web.Data;
using Testcontainers.PostgreSql;

namespace Specpen.Tests;

public sealed class PostgresMigrationTests
{
    [Fact]
    public async Task MigrationsApplyOnPostgres_WhenOptedIn()
    {
        if (Environment.GetEnvironmentVariable("PHONE_CATALOG_RUN_POSTGRES_TESTS") != "1")
        {
            return;
        }

        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("specpen_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var db = new CatalogDbContext(options);
        await db.Database.MigrateAsync();

        Assert.True(await db.Database.CanConnectAsync());
    }
}
