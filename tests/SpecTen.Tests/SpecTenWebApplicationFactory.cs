using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpecTen.Web.Data;
using SpecTen.Web.Options;
using SpecTen.Web.Scraping;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class SpecTenWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection? _connection;
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

    public SpecTenWebApplicationFactory()
        : this(null)
    {
    }

    internal SpecTenWebApplicationFactory(IReadOnlyDictionary<string, string?>? configurationOverrides)
    {
        _configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Scraping:UseFixtureAdapters"] = "true",
                ["Scraping:PerDomainDelayMilliseconds"] = "0",
                ["Coverage:Enabled"] = "true",
                ["CatalogSeed:Enabled"] = "false",
                ["PublicApi:RateLimitPermitLimit"] = "1000",
                ["PublicApi:RateLimitWindowSeconds"] = "60",
            };

            foreach (var overridePair in _configurationOverrides)
            {
                settings[overridePair.Key] = overridePair.Value;
            }

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextFactory<CatalogDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CatalogDbContext>>();
            services.RemoveAll<DbContextOptions<CatalogDbContext>>();
            services.RemoveAll<DbContextOptions>();

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContextFactory<CatalogDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            services.RemoveAll<IDeviceCoverageService>();
            services.AddSingleton<IDeviceCoverageService, TestDeviceCoverageService>();
            services.AddScoped<IPhoneSourceAdapter, OfficialFixtureAdapter>();
            services.AddScoped<IPhoneSourceAdapter, GsmArenaFixtureAdapter>();
            services.AddScoped<IPhoneSourceAdapter, KimovilFixtureAdapter>();
            services.AddScoped<IPhoneSourceAdapter, TudoCelularFixtureAdapter>();
        });
    }

    public async Task InitializeAsync()
    {
        _ = CreateClient();
        await using var scope = Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
        var run = await importer.RunImportAsync("test-seed", CancellationToken.None);
        if (run.Status == ImportRunStatus.Failed)
        {
            throw new InvalidOperationException(run.Message);
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
