using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpecTen.Web.Data;
using SpecTen.Web.Options;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class DeviceCoverageSnapshotTests
{
    [Fact]
    public async Task SearchAsync_MatchesPocoSubbrandInXiaomiSnapshot()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Xiaomi",
                    "xiaomi",
                    "X6",
                    "x6",
                    "xiaomi",
                    "x6",
                    "xiaomix6",
                    "x6",
                    28,
                    "GSMArena",
                    "https://www.gsmarena.com/xiaomi_poco_x6-12723.php"),
            ]));

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var results = await harness.Service.SearchAsync("Poco X6", null, 5, CancellationToken.None);

            var match = Assert.Single(results);
            Assert.Equal("Xiaomi", match.Brand);
            Assert.Equal("Poco X6", match.Name);
            Assert.Equal("x6", match.Slug);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_MatchesRedmiSubbrandAndCommonBrandTypos()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Xiaomi",
                    "xiaomi",
                    "Note 7",
                    "note-7",
                    "xiaomi",
                    "note7",
                    "xiaominote7",
                    "note7",
                    28,
                    "GSMArena",
                    "https://www.gsmarena.com/xiaomi_redmi_note_7-9513.php"),
                new SnapshotEntry(
                    "Xiaomi",
                    "xiaomi",
                    "15",
                    "15",
                    "xiaomi",
                    "15",
                    "xiaomi15",
                    "15",
                    28,
                    "GSMArena",
                    "https://www.gsmarena.com/xiaomi_15-13472.php"),
            ]));

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var redmiResults = await harness.Service.SearchAsync("Redmi Note 7", null, 5, CancellationToken.None);
            var transposedBrandResults = await harness.Service.SearchAsync("Xioami 15", null, 5, CancellationToken.None);
            var omittedBrandLetterResults = await harness.Service.SearchAsync("Xaumi 15", null, 5, CancellationToken.None);

            Assert.Contains(redmiResults, result => result.Slug == "note-7");
            Assert.Contains(transposedBrandResults, result => result.Slug == "15");
            Assert.Contains(omittedBrandLetterResults, result => result.Slug == "15");
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_UsesSnapshot_WhenLiveIndexIsUnavailable()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Honor",
                    "honor",
                    "400 Pro",
                    "400-pro",
                    "honor",
                    "400pro",
                    "honor400pro",
                    "400pro",
                    28,
                    "GSMArena",
                    "https://www.gsmarena.com/honor_400_pro_5g-99999.php"),
            ]));

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var results = await harness.Service.SearchAsync("honor 400 pro", null, 5, CancellationToken.None);

            var match = Assert.Single(results);
            Assert.Equal("Honor", match.Brand);
            Assert.Equal("400-pro", match.Slug);
            Assert.Equal("Cobertura inicial", CatalogServiceLabel(match));
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WarmupAsync_RefreshesStaleSnapshot_FromLiveIndex()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Legacy",
                    "legacy",
                    "Archive Phone",
                    "archive-phone",
                    "legacy",
                    "archivephone",
                    "legacyarchivephone",
                    "archivephone",
                    2,
                    "GSMArena",
                    "https://www.gsmarena.com/archive_phone-1.php"),
            ],
            generatedAt: DateTimeOffset.UtcNow.AddDays(-5)));

            var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["https://example.test/gsmarena/sitemap.xml"] = """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                      <url>
                        <loc>https://www.gsmarena.com/honor_400_pro_5g-99999.php</loc>
                      </url>
                    </urlset>
                    """,
            };

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                responses);

            await harness.Service.WarmupAsync(CancellationToken.None);

            var results = await harness.Service.SearchAsync("honor 400 pro", null, 5, CancellationToken.None);

            var match = Assert.Single(results);
            Assert.Equal("Honor", match.Brand);
            Assert.Equal("400-pro-5g", match.Slug);

            var snapshotJson = await File.ReadAllTextAsync(snapshotPath);
            Assert.Contains("\"brand\":\"Honor\"", snapshotJson, StringComparison.Ordinal);
            Assert.DoesNotContain("Archive Phone", snapshotJson, StringComparison.Ordinal);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_UsesCurrentMakerPage_WhenSitemapSnapshotMissesRecentModel()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Apple",
                    "apple",
                    "iPhone 16 Pro",
                    "iphone-16-pro",
                    "apple",
                    "iphone16pro",
                    "appleiphone16pro",
                    "iphone16pro",
                    32,
                    "GSMArena",
                    "https://www.gsmarena.com/apple_iphone_16_pro-13315.php"),
            ]));

            var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["https://www.gsmarena.com/makers.php3"] = """
                    <ul class="links"><li><a href="apple-phones-48.php">Apple</a></li></ul>
                    """,
                ["https://www.gsmarena.com/apple-phones-48.php"] = """
                    <ul><li><a href="apple_iphone_17_pro-14049.php"><strong><span>iPhone 17 Pro</span></strong></a></li></ul>
                    """,
            };

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                responses);

            var results = await harness.Service.SearchAsync("iPhone 17 Pro", null, 5, CancellationToken.None);

            var match = Assert.Single(results, result => result.Slug == "iphone-17-pro");
            Assert.Equal("Apple", match.Brand);
            Assert.Equal("https://www.gsmarena.com/apple_iphone_17_pro-14049.php", match.SourceUrl);
            Assert.Equal(match, results[0]);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_FollowsMakerPagination_WhenRecentModelIsOnSecondPage()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Xiaomi",
                    "xiaomi",
                    "Xiaomi 14T Pro",
                    "14t-pro",
                    "xiaomi",
                    "xiaomi14tpro",
                    "xiaomixiaomi14tpro",
                    "xiaomi14tpro",
                    32,
                    "GSMArena",
                    "https://www.gsmarena.com/xiaomi_14t_pro-13328.php"),
            ]));

            var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["https://www.gsmarena.com/makers.php3"] = """
                    <ul class="links"><li><a href="xiaomi-phones-80.php">Xiaomi</a></li></ul>
                    """,
                ["https://www.gsmarena.com/xiaomi-phones-80.php"] = """
                    <ul><li><a href="xiaomi_17t_pro_5g-14651.php"><strong><span>Xiaomi 17T Pro 5G</span></strong></a></li></ul>
                    <a href="xiaomi-phones-f-80-0-p2.php" class="prevnextbutton">Next</a>
                    """,
                ["https://www.gsmarena.com/xiaomi-phones-f-80-0-p2.php"] = """
                    <ul><li><a href="xiaomi_15t_pro_5g-14178.php"><strong><span>Xiaomi 15T Pro 5G</span></strong></a></li></ul>
                    """,
            };

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                responses);

            var results = await harness.Service.SearchAsync("Xiaomi 15T Pro", null, 5, CancellationToken.None);

            var match = Assert.Single(results, result => result.Slug == "15t-pro-5g");
            Assert.Equal("Xiaomi", match.Brand);
            Assert.Equal("https://www.gsmarena.com/xiaomi_15t_pro_5g-14178.php", match.SourceUrl);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_UsesDirectSourceSearch_BeforeMakerPagination()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Xiaomi",
                    "xiaomi",
                    "Xiaomi 14T Pro",
                    "14t-pro",
                    "xiaomi",
                    "xiaomi14tpro",
                    "xiaomixiaomi14tpro",
                    "xiaomi14tpro",
                    32,
                    "GSMArena",
                    "https://www.gsmarena.com/xiaomi_14t_pro-13328.php"),
            ]));

            var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["https://www.gsmarena.com/results.php3?sQuickSearch=yes&sName=Xiaomi 15T Pro"] = """
                    <ul><li><a href=xiaomi_15t_pro_5g-14178.php><strong>Xiaomi<br>15T Pro</strong></a></li></ul>
                    """,
            };

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                responses);

            var results = await harness.Service.SearchAsync("Xiaomi 15T Pro", null, 5, CancellationToken.None);

            var match = Assert.Single(results, result => result.Slug == "15t-pro-5g");
            Assert.Equal("https://www.gsmarena.com/xiaomi_15t_pro_5g-14178.php", match.SourceUrl);
            Assert.DoesNotContain(
                harness.HttpClientFactory.RequestUris,
                uri => uri.EndsWith("/makers.php3", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_UsesDirectSearch_WhenSnapshotOnlyHasRelatedModel()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Xiaomi",
                    "xiaomi",
                    "Redmi 12",
                    "redmi-12",
                    "xiaomi",
                    "redmi12",
                    "xiaomiredmi12",
                    "redmi12",
                    40,
                    "GSMArena",
                    "https://www.gsmarena.com/xiaomi_redmi_12-12345.php"),
            ]));

            var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["https://www.gsmarena.com/results.php3?sQuickSearch=yes&sName=Xiaomi 12"] = """
                    <ul><li><a href=xiaomi_12-11728.php><strong>Xiaomi<br>12</strong></a></li></ul>
                    """,
            };

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                responses);

            var results = await harness.Service.SearchAsync("Xiaomi 12", null, 5, CancellationToken.None);

            Assert.Equal("Xiaomi", results[0].Brand);
            Assert.Equal("12", results[0].Name);
            Assert.Contains(
                harness.HttpClientFactory.RequestUris,
                uri => uri.Contains("results.php3?sQuickSearch=yes", StringComparison.Ordinal));
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_DiscoversMakerPage_FromKnownPhone_WhenCompactDirectoryOmitsBrand()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("coverage-snapshot-tests");
        try
        {
            var snapshotPath = Path.Combine(workingDirectory.FullName, "coverage-index.snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, BuildSnapshotJson(
            [
                new SnapshotEntry(
                    "Acme",
                    "acme",
                    "Phone 1",
                    "phone-1",
                    "acme",
                    "phone1",
                    "acmephone1",
                    "phone1",
                    10,
                    "GSMArena",
                    "https://www.gsmarena.com/acme_phone_1-100.php"),
            ]));

            var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["https://www.gsmarena.com/makers.php3"] = """
                    <ul><li><a href="apple-phones-48.php">Apple</a></li></ul>
                    """,
                ["https://www.gsmarena.com/acme_phone_1-100.php"] = """
                    <nav><a href="acme-phones-999.php">Acme phones</a></nav>
                    """,
                ["https://www.gsmarena.com/acme-phones-999.php"] = """
                    <ul><li><a href="acme_phone_99_pro-99999.php"><strong><span>Phone 99 Pro</span></strong></a></li></ul>
                    """,
            };

            await using var harness = await CreateHarnessAsync(
                workingDirectory.FullName,
                snapshotPath,
                responses);

            var results = await harness.Service.SearchAsync("Acme Phone 99 Pro", null, 5, CancellationToken.None);

            var match = Assert.Single(results, result => result.Slug == "phone-99-pro");
            Assert.Equal("Acme", match.Brand);
            Assert.Equal("https://www.gsmarena.com/acme_phone_99_pro-99999.php", match.SourceUrl);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    private static async Task<TestHarness> CreateHarnessAsync(
        string contentRootPath,
        string snapshotPath,
        IReadOnlyDictionary<string, string> responses)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContextFactory<CatalogDbContext>(options => options.UseSqlite(connection));
        var httpClientFactory = new StubHttpClientFactory(responses);
        services.AddSingleton<IHttpClientFactory>(httpClientFactory);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRootPath));
        services.AddSingleton<GsmArenaPageParser>();
        services.AddSingleton<IDeviceCoverageService, DeviceCoverageService>();
        services.AddSingleton(Options.Create(new CoverageOptions
        {
            Enabled = true,
            DataUrl = "https://example.test/gsmarena/sitemap.xml",
            SourceName = "GSMArena",
            SourceUrl = "https://www.gsmarena.com/",
            SnapshotFilePath = snapshotPath,
            RefreshHours = 1,
            MinimumQueryLength = 2,
            OnDemandHydrationEnabled = true,
            ExactHydrationLimit = 1,
            MakerPageDelayMilliseconds = 0,
        }));

        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IDeviceCoverageService>();
        return new TestHarness(provider, connection, service, httpClientFactory);
    }

    private static string BuildSnapshotJson(IReadOnlyList<SnapshotEntry> entries, DateTimeOffset? generatedAt = null)
    {
        return JsonSerializer.Serialize(new
        {
            version = 1,
            generatedAt = generatedAt ?? DateTimeOffset.UtcNow,
            entries = entries,
        });
    }

    private static string CatalogServiceLabel(CoveragePhoneResult result)
    {
        return string.IsNullOrWhiteSpace(result.SourceName) ? string.Empty : "Cobertura inicial";
    }

    private sealed record SnapshotEntry(
        string Brand,
        string BrandSlug,
        string Name,
        string Slug,
        string NormalizedBrand,
        string NormalizedName,
        string NormalizedFullName,
        string ComparableName,
        int QualityScore,
        string SourceName,
        string? SourceUrl);

    private sealed record TestHarness(
        ServiceProvider Provider,
        SqliteConnection Connection,
        IDeviceCoverageService Service,
        StubHttpClientFactory HttpClientFactory) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class StubHttpClientFactory(IReadOnlyDictionary<string, string> responses) : IHttpClientFactory
    {
        public ConcurrentQueue<string> RequestUris { get; } = new();

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FixtureHttpMessageHandler(responses, RequestUris), disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(5),
            };
        }
    }

    private sealed class FixtureHttpMessageHandler(
        IReadOnlyDictionary<string, string> responses,
        ConcurrentQueue<string> requestUris) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestUris.Enqueue(request.RequestUri!.ToString());

            if (!responses.TryGetValue(request.RequestUri.ToString(), out var payload))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    RequestMessage = request,
                });
            }

            var contentType = request.RequestUri.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                ? "application/xml"
                : "text/html";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(payload)
                {
                    Headers =
                    {
                        ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType),
                    },
                },
            });
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SpecTen.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
