using Microsoft.EntityFrameworkCore;
using SpecTen.Web.Data;
using Testcontainers.PostgreSql;

namespace SpecTen.Tests;

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
            .WithDatabase("specten_tests")
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
