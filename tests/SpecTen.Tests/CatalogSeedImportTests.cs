using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpecTen.Web.Data;
using SpecTen.Web.Options;
using SpecTen.Web.Scraping;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class CatalogSeedImportTests
{
    [Fact]
    public async Task JsonCatalogSeedAdapter_LoadsPhonesFromLocalFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-catalog-seed.json");
        await File.WriteAllTextAsync(tempFile,
            """
            {
              "phones": [
                {
                  "sourceName": "Seed feed",
                  "sourceUrl": "https://seed.example.test/oneplus-13",
                  "policyStatus": "ManualFeed",
                  "robotsAllowed": true,
                  "isOfficial": false,
                  "brandName": "OnePlus",
                  "officialDomain": "oneplus.com",
                  "modelName": "13",
                  "summary": "Flagship externo carregado por feed JSON.",
                  "releasedAt": "2025-01-07T00:00:00Z",
                  "launchPriceUsd": 899,
                  "imageUrl": "https://seed.example.test/images/oneplus-13.png",
                  "specs": [
                    {
                      "group": "Performance",
                      "key": "chipset",
                      "displayName": "Chipset",
                      "value": "Snapdragon 8 Elite",
                      "isCritical": true,
                      "confidence": 0.93
                    },
                    {
                      "group": "Bateria",
                      "key": "battery",
                      "displayName": "Bateria",
                      "value": "6000 mAh",
                      "unit": "mAh",
                      "isCritical": true,
                      "confidence": 0.91
                    }
                  ],
                  "variants": [
                    {
                      "name": "12 GB / 256 GB",
                      "ramGb": 12,
                      "storageGb": 256
                    }
                  ],
                  "benchmarks": [
                    {
                      "benchmarkName": "AnTuTu",
                      "score": 2450000,
                      "sourceName": "Geekbench Browser"
                    }
                  ]
                }
              ]
            }
            """);

        try
        {
            var adapter = new JsonCatalogSeedAdapter(
                new StubHttpClientFactory(),
                new TestHostEnvironment(Path.GetDirectoryName(tempFile)!),
                Options.Create(new CatalogSeedOptions
                {
                    Enabled = true,
                    FilePath = tempFile,
                    DefaultSourceName = "Catalog seed",
                }),
                NullLogger<JsonCatalogSeedAdapter>.Instance);

            var records = await adapter.FetchAsync(CancellationToken.None);

            var phone = Assert.Single(records);
            Assert.Equal("OnePlus", phone.BrandName);
            Assert.Equal("13", phone.ModelName);
            Assert.Equal("oneplus.com", phone.OfficialDomain);
            Assert.Equal(2, phone.Specs.Count);
            Assert.Contains(phone.Specs, spec => spec.Key == "chipset" && spec.DisplayValue == "Snapdragon 8 Elite");
            Assert.Contains(phone.Variants, variant => variant.Name == "12 GB / 256 GB");
            Assert.Contains(phone.Benchmarks, benchmark => benchmark.BenchmarkName == "AnTuTu" && benchmark.Score == 2_450_000);
            Assert.Contains(phone.Benchmarks, benchmark => benchmark.SourceName == "Geekbench Browser");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task PhoneImportService_Skips_WhenNoAdapterReturnsRecords()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbFactory = new TestCatalogDbContextFactory(dbOptions);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var importer = new PhoneImportService(
            dbFactory,
            [new EmptyPhoneSourceAdapter()],
            new SpecFactResolver(),
            new PhoneClassifier(),
            Options.Create(new ScrapingOptions
            {
                Enabled = true,
                UseFixtureAdapters = false,
                PerDomainDelayMilliseconds = 0,
            }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<PhoneImportService>.Instance);

        var run = await importer.RunImportAsync("empty-adapter-test", CancellationToken.None);

        Assert.Equal(ImportRunStatus.Skipped, run.Status);
        Assert.Equal("No active source adapters returned records.", run.Message);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        Assert.Equal(0, await verifyDb.PhoneModels.CountAsync());
    }

    [Fact]
    public async Task JsonCatalogSeedAdapter_LoadsBundledSeedFile()
    {
        var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SpecTen.Web"));
        var adapter = new JsonCatalogSeedAdapter(
            new StubHttpClientFactory(),
            new TestHostEnvironment(contentRoot),
            Options.Create(new CatalogSeedOptions
            {
                Enabled = true,
                FilePath = "Data/catalog-seed.json",
            }),
            NullLogger<JsonCatalogSeedAdapter>.Instance);

        var records = await adapter.FetchAsync(CancellationToken.None);
        var modelNames = records.Select(record => $"{record.BrandName} {record.ModelName}").ToList();

        Assert.Contains("Xiaomi 15", modelNames);
        Assert.Contains("OnePlus 13", modelNames);
        Assert.Contains("Google Pixel 9a", modelNames);
        Assert.Contains("Nothing Phone (3a) Pro", modelNames);
        Assert.Contains(records, record =>
            record.BrandName == "Google" &&
            record.ModelName == "Pixel 9a" &&
            record.Benchmarks.Any(benchmark => benchmark.SourceName == "NanoReview" && benchmark.Score == 1_321_069));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "SpecTen.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class EmptyPhoneSourceAdapter : IPhoneSourceAdapter
    {
        public string SourceName => "Empty";
        public string PolicyStatus => "Disabled";
        public bool RobotsAllowed => true;
        public bool IsOfficialSource => false;

        public Task<IReadOnlyList<SourcePhoneRecord>> FetchAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SourcePhoneRecord>>([]);
        }
    }

    private sealed class TestCatalogDbContextFactory(DbContextOptions<CatalogDbContext> options) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() => new(options);

        public ValueTask<CatalogDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CatalogDbContext(options));
        }
    }
}
