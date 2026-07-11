using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpecTen.Web.Data;
using SpecTen.Web.Options;
using SpecTen.Web.Scraping;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class OfficialCoverageFallbackTests
{
    [Fact]
    public async Task CatalogService_Hydrates_OfficialVivoMatch_WhenBroadIndexMissesRecentDevice()
    {
        await using var harness = await CreateHarnessAsync();

        var results = await harness.Catalog.SearchAsync(
            "vivo x300 ultra",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);

        var match = Assert.Single(results, phone =>
            phone.Brand == "Vivo" &&
            phone.Slug == "x300-ultra" &&
            phone.HasFullCatalogEntry);

        Assert.Equal(results[0].Id, match.Id);
        Assert.Equal("Top de linha", match.Tier);
        Assert.Equal("Snapdragon 8 Elite Gen 5", match.Chipset);
        Assert.Equal("6600 mAh", match.Battery);
        Assert.Equal("6.82 in", match.Display);
        Assert.Equal("200 MP", match.MainCamera);
        Assert.Equal("https://fixtures.test/vivo/x300-ultra-black.png", match.ImageUrl);

        var details = await harness.Catalog.GetPhoneBySlugAsync("vivo", "x300-ultra", CancellationToken.None);

        Assert.NotNull(details);
        Assert.True(details!.HasFullCatalogEntry);
        Assert.Equal("Vivo X300 Ultra", details.FullName);
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "charging" && spec.DisplayValue == "100 W");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "ip_rating" && spec.DisplayValue == "IP68 & IP69");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "wifi" && spec.DisplayValue == "2.4G/5G/6G");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "bluetooth" && spec.DisplayValue == "Bluetooth 6.0");
        Assert.Contains(details.Variants, variant => variant.RamGb == 16 && variant.StorageGb == 512);
    }

    [Fact]
    public async Task CatalogService_Merges_CoverageBrands_IntoPublicFilterOptions()
    {
        await using var harness = await CreateHarnessAsync();

        var brands = await harness.Catalog.GetBrandOptionsAsync(CancellationToken.None);

        var vivo = Assert.Single(brands, brand => brand.Slug == "vivo");
        Assert.Equal("Vivo", vivo.Name);
        Assert.True(vivo.Count >= 3);
    }

    [Fact]
    public async Task CatalogService_Refreshes_IncompleteExistingOfficialRecord_OnExactSearch()
    {
        await using var harness = await CreateHarnessAsync(seedIncompleteVivoRecord: true);

        var results = await harness.Catalog.SearchAsync(
            "vivo x300 ultra",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);

        var match = Assert.Single(results, phone =>
            phone.Brand == "Vivo" &&
            phone.Slug == "x300-ultra" &&
            phone.HasFullCatalogEntry);

        Assert.Equal("6.82 in", match.Display);
        Assert.Equal("https://fixtures.test/vivo/x300-ultra-black.png", match.ImageUrl);

        var details = await harness.Catalog.GetPhoneBySlugAsync("vivo", "x300-ultra", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Contains(details!.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "display_size" && spec.DisplayValue == "6.82 in");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "dimensions" && spec.DisplayValue == "162.98 x 76.81 x 8.19 mm");
    }

    [Fact]
    public async Task CatalogService_Hydrates_GsmArenaMatch_WhenQueryOmits_TrailingVariantNoise()
    {
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
            ["https://www.gsmarena.com/honor_400_pro_5g-99999.php"] = BuildHonor400ProPage(),
        };

        await using var harness = await CreateHarnessAsync(extraResponses: responses);

        var results = await harness.Catalog.SearchAsync(
            "honor 400 pro",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);

        var match = Assert.Single(results, phone =>
            phone.Brand == "Honor" &&
            phone.Slug == "400-pro" &&
            phone.HasFullCatalogEntry);

        Assert.Equal(match.Id, results[0].Id);
        Assert.Equal("6.7 in", match.Display);
        Assert.Equal("5300 mAh", match.Battery);
        Assert.Equal("200 MP", match.MainCamera);
        Assert.Equal("https://fixtures.test/honor-400-pro.jpg", match.ImageUrl);

        var details = await harness.Catalog.GetPhoneBySlugAsync("honor", "400-pro", CancellationToken.None);

        Assert.NotNull(details);
        Assert.True(details!.HasFullCatalogEntry);
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "charging" && spec.DisplayValue == "100W wired");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "wireless_charging" && spec.DisplayValue == "50W wireless");
    }

    [Fact]
    public async Task CatalogService_Hydrates_OfficialSamsungMatch_WhenBroadIndexMissesRecentDevice()
    {
        var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://example.test/gsmarena/sitemap.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url>
                    <loc>https://www.gsmarena.com/samsung_galaxy_a37_5g-99997.php</loc>
                  </url>
                </urlset>
                """,
            ["https://www.gsmarena.com/samsung_galaxy_a37_5g-99997.php"] = BuildSamsungA37GsmArenaPage(),
            ["https://www.samsung.com/br/smartphones/all-smartphones/"] = BuildSamsungCatalogPage(),
            ["https://www.samsung.com/br/smartphones/galaxy-a/galaxy-a37-5g-awesome-charcoal-128gb-sm-a376ezkazto/"] = BuildSamsungA37Page(),
        };

        await using var harness = await CreateHarnessAsync(extraResponses: responses);

        var results = await harness.Catalog.SearchAsync(
            "galaxy a37 5g",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);

        var match = Assert.Single(results, phone =>
            phone.Brand == "Samsung" &&
            phone.Slug == "galaxy-a37-5g" &&
            phone.HasFullCatalogEntry);

        Assert.Equal("Fonte oficial", match.TrustLabel);
        Assert.Equal("Galaxy A37 5G", match.Name);
        Assert.Equal("Intermediario", match.Tier);
        Assert.Equal("Exynos 1580 (4 nm)", match.Chipset);
        Assert.Equal("5000 mAh", match.Battery);
        Assert.Equal("6.7 in", match.Display);
        Assert.Equal("50 MP", match.MainCamera);
        Assert.Equal("https://images.samsung.com/is/image/samsung/p6pim/br/sm-a376ezkazto/gallery/br-galaxy-a37-5g-sm-a376-000001-sm-a376ezkazto-front.png", match.ImageUrl);

        var details = await harness.Catalog.GetPhoneBySlugAsync("samsung", "galaxy-a37-5g", CancellationToken.None);

        Assert.NotNull(details);
        Assert.True(details!.HasFullCatalogEntry);
        Assert.True(details.IsPublicReady);
        Assert.Equal("Samsung Galaxy A37 5G", details.FullName);
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "charging" && spec.DisplayValue == "45 W");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "ip_rating" && spec.DisplayValue == "IP67");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "chipset" && spec.DisplayValue == "Exynos 1580 (4 nm)");
    }

    [Fact]
    public async Task CatalogService_Uses_OfficialSamsungPages_AcrossRegions_WhenOneRegionHasManyVariants()
    {
        const string usProductUrl = "https://www.samsung.com/us/smartphones/galaxy-a57-5g/";
        var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://www.samsung.com/uk/smartphones/all-smartphones/"] = BuildSamsungA57CatalogPage("uk", 13),
            ["https://www.samsung.com/us/smartphones/all-smartphones/"] = BuildSamsungA57CatalogPage("us", 1),
            [usProductUrl] = BuildSamsungA57ChipsetPage(),
        };

        foreach (var url in BuildSamsungA57ProductUrls("uk", 13))
        {
            responses[url] = BuildSamsungA57Page(includeChipset: false);
        }

        await using var harness = await CreateHarnessAsync(extraResponses: responses);

        var results = await harness.Catalog.SearchAsync(
            "galaxy a57 5g",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);

        var match = Assert.Single(results, phone =>
            phone.Brand == "Samsung" &&
            phone.Slug == "galaxy-a57-5g" &&
            phone.HasFullCatalogEntry);

        Assert.Equal("Exynos 1680", match.Chipset);
        Assert.Equal("Intermediario", match.Tier);

        var details = await harness.Catalog.GetPhoneBySlugAsync("samsung", "galaxy-a57-5g", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Contains(
            details!.SpecGroups.SelectMany(group => group.Specs),
            spec => spec.Key == "chipset" && spec.DisplayValue == "Exynos 1680");
    }

    [Fact]
    public async Task CatalogService_Reconciles_Stale_OfficialSamsungSlug_OnExactSearch()
    {
        var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://www.samsung.com/br/smartphones/all-smartphones/"] = BuildSamsungCatalogPage(),
            ["https://www.samsung.com/br/smartphones/galaxy-a/galaxy-a37-5g-awesome-charcoal-128gb-sm-a376ezkazto/"] = BuildSamsungA37Page(),
        };

        await using var harness = await CreateHarnessAsync(
            seedStaleSamsungRecord: true,
            extraResponses: responses);

        var results = await harness.Catalog.SearchAsync(
            "galaxy a37 5g",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);

        var refreshed = Assert.Single(results, phone =>
            phone.Brand == "Samsung" &&
            phone.Slug == "galaxy-a37-5g" &&
            phone.HasFullCatalogEntry);

        Assert.Equal("Galaxy A37 5G", refreshed.Name);
        Assert.Equal("Intermediario", refreshed.Tier);
        Assert.DoesNotContain(results, phone => phone.Slug == "galaxy-a37-5g-128gb");

        var details = await harness.Catalog.GetPhoneBySlugAsync("samsung", "galaxy-a37-5g", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Samsung Galaxy A37 5G", details!.FullName);
        Assert.Equal("Intermediario", details.Classification.Label);
    }

    [Fact]
    public async Task CatalogService_Hydrates_OfficialSamsungUkMatch_WithEnglishSections()
    {
        var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://www.samsung.com/uk/smartphones/all-smartphones/"] = BuildSamsungUkCatalogPage(),
            ["https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a17-5g-black-128gb-sm-a176bzkaeub/"] = BuildSamsungA17Page(),
        };

        await using var harness = await CreateHarnessAsync(extraResponses: responses);

        var results = await harness.Catalog.SearchAsync(
            "galaxy a17 5g",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);

        var match = Assert.Single(results, phone =>
            phone.Brand == "Samsung" &&
            phone.Slug == "galaxy-a17-5g" &&
            phone.HasFullCatalogEntry);

        Assert.Equal("Exynos 1330", match.Chipset);
        Assert.Equal("5000 mAh", match.Battery);
        Assert.Equal("6.7 in", match.Display);
        Assert.Equal("50 MP", match.MainCamera);

        var details = await harness.Catalog.GetPhoneBySlugAsync("samsung", "galaxy-a17-5g", CancellationToken.None);

        Assert.NotNull(details);
        Assert.True(details!.IsPublicReady);
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "ip_rating" && spec.DisplayValue == "IP54");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "storage_base" && spec.DisplayValue == "128 GB");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "ram" && spec.DisplayValue == "4 GB");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "wifi" && spec.DisplayValue.Contains("802.11a/b/g/n/ac", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CatalogService_Hydrates_OfficialSamsungIndonesiaMatch_WithLocalizedStorageSection()
    {
        var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://www.samsung.com/id/smartphones/all-smartphones/"] = BuildSamsungIndonesiaCatalogPage(),
            ["https://www.samsung.com/id/smartphones/galaxy-a/galaxy-a07-5g-light-violet-128gb-sm-a076blvcxid/"] = BuildSamsungA07FiveGPage(),
        };

        await using var harness = await CreateHarnessAsync(extraResponses: responses);

        var results = await harness.Catalog.SearchAsync(
            "galaxy a07 5g",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);

        var match = Assert.Single(results, phone =>
            phone.Brand == "Samsung" &&
            phone.Slug == "galaxy-a07-5g" &&
            phone.HasFullCatalogEntry);

        Assert.Equal("Dimensity 6300", match.Chipset);
        Assert.Equal("5000 mAh", match.Battery);
        Assert.Equal("6.7 in", match.Display);
        Assert.Equal("50 MP", match.MainCamera);

        var details = await harness.Catalog.GetPhoneBySlugAsync("samsung", "galaxy-a07-5g", CancellationToken.None);

        Assert.NotNull(details);
        Assert.True(details!.IsPublicReady);
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "ram" && spec.DisplayValue == "6 GB");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "storage_base" && spec.DisplayValue == "128 GB");
        Assert.Contains(details.SpecGroups.SelectMany(group => group.Specs), spec => spec.Key == "network" && spec.DisplayValue.Contains("5G Sub6", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<TestHarness> CreateHarnessAsync(
        bool seedIncompleteVivoRecord = false,
        bool seedStaleSamsungRecord = false,
        IReadOnlyDictionary<string, string>? extraResponses = null)
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/SpecTen.Tests/Fixtures/official/vivo"));

        var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://example.test/gsmarena/sitemap.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url>
                    <loc>https://www.gsmarena.com/legacy_phone-123.php</loc>
                  </url>
                </urlset>
                """,
            ["https://www.vivo.com/in/products"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "products.html")),
            ["https://www.vivo.com/in/products/x300-ultra"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "x300-ultra-overview.html")),
            ["https://www.vivo.com/in/products/param/x300-ultra"] = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "x300-ultra-parameter.html")),
        };

        if (extraResponses is not null)
        {
            foreach (var response in extraResponses)
            {
                responses[response.Key] = response.Value;
            }
        }

        var services = new ServiceCollection();
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContextFactory<CatalogDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(responses));
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(AppContext.BaseDirectory));
        services.AddSingleton<GsmArenaPageParser>();
        services.AddSingleton<SpecFactResolver>();
        services.AddSingleton<PhoneClassifier>();
        services.AddSingleton<IOfficialCoverageProvider, SamsungOfficialCoverageProvider>();
        services.AddSingleton<IOfficialCoverageProvider, VivoOfficialCoverageProvider>();
        services.AddSingleton<IDeviceCoverageService, DeviceCoverageService>();
        services.AddScoped<PhoneImportService>();
        services.AddScoped<CatalogService>();
        services.AddSingleton(Options.Create(new CoverageOptions
        {
            Enabled = true,
            DataUrl = "https://example.test/gsmarena/sitemap.xml",
            SourceName = "GSMArena",
            SourceUrl = "https://www.gsmarena.com/",
            SnapshotFilePath = "",
            RefreshHours = 1,
            MinimumQueryLength = 2,
            OnDemandHydrationEnabled = true,
            ExactHydrationLimit = 1,
        }));
        services.AddSingleton(Options.Create(new ScrapingOptions
        {
            Enabled = true,
            UseFixtureAdapters = false,
            PerDomainDelayMilliseconds = 0,
        }));

        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        if (seedIncompleteVivoRecord)
        {
            var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
            await importer.ImportRecordAsync(BuildIncompleteVivoRecord(), "seed:incomplete-vivo-x300-ultra", CancellationToken.None);
        }

        if (seedStaleSamsungRecord)
        {
            var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
            await importer.ImportRecordAsync(BuildStaleSamsungRecord(), "seed:stale-samsung-a37", CancellationToken.None);
        }

        var catalog = scope.ServiceProvider.GetRequiredService<CatalogService>();
        return new TestHarness(provider, connection, catalog);
    }

    private static SourcePhoneRecord BuildIncompleteVivoRecord()
    {
        var collectedAt = new DateTimeOffset(2026, 7, 8, 0, 0, 0, TimeSpan.Zero);

        return new SourcePhoneRecord(
            "vivo Official",
            "https://www.vivo.com/in/products/param/x300-ultra",
            "OfficialCatalog",
            true,
            true,
            "Vivo",
            "vivo.com",
            "X300 Ultra",
            "Registro parcial para validar refresh sob demanda.",
            null,
            null,
            "https://fixtures.test/vivo/x300-ultra-black.png",
            "https://www.vivo.com/in/products/x300-ultra",
            [
                new SourceSpecClaim("vivo Official", "https://www.vivo.com/in/products/param/x300-ultra", true, "Performance", "chipset", "Chipset", "Snapdragon 8 Elite Gen 5", "snapdragon8elitegen5", "Snapdragon 8 Elite Gen 5", null, true, 0.98, collectedAt),
                new SourceSpecClaim("vivo Official", "https://www.vivo.com/in/products/param/x300-ultra", true, "Camera", "main_camera", "Camera principal", "200 MP", "200mp", "200 MP", "MP", true, 0.98, collectedAt),
                new SourceSpecClaim("vivo Official", "https://www.vivo.com/in/products/param/x300-ultra", true, "Bateria", "battery", "Bateria", "6600 mAh", "6600mah", "6600 mAh", "mAh", true, 0.98, collectedAt),
            ],
            [new SourceVariantClaim("16 GB / 512 GB", 16, 512, null)],
            []);        
    }

    private static SourcePhoneRecord BuildStaleSamsungRecord()
    {
        var collectedAt = new DateTimeOffset(2026, 7, 8, 0, 0, 0, TimeSpan.Zero);
        const string sourceUrl = "https://www.samsung.com/br/smartphones/galaxy-a/galaxy-a37-5g-awesome-charcoal-128gb-sm-a376ezkazto/";

        return new SourcePhoneRecord(
            "Samsung Official",
            sourceUrl,
            "OfficialCatalog",
            true,
            true,
            "Samsung",
            "samsung.com",
            "Galaxy A37 5G (128GB)",
            "Registro oficial antigo com slug poluido por armazenamento.",
            new DateTimeOffset(2026, 3, 19, 0, 0, 0, TimeSpan.Zero),
            null,
            "https://images.samsung.com/is/image/samsung/p6pim/br/sm-a376bzajzto/gallery/br-galaxy-a37-5g-sm-a376-sm-a376bzajzto-551722947?$624_468_PNG$",
            "https://images.samsung.com/is/image/samsung/p6pim/br/sm-a376bzajzto/gallery/br-galaxy-a37-5g-sm-a376-sm-a376bzajzto-551722947?$624_468_PNG$",
            [
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Performance", "cpu", "CPU", "Octa Core / 2.9GHz, 2.6GHz, 1.9GHz", "octacore29ghz26ghz19ghz", "Octa Core / 2.9GHz, 2.6GHz, 1.9GHz", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Armazenamento", "storage_base", "Armazenamento base", "128 GB", "128gb", "128 GB", "GB", true, 0.98, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Tela", "display_size", "Tamanho da tela", "6.7 in", "67in", "6.7 in", "in", true, 0.98, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Tela", "display_type", "Painel", "Super AMOLED", "superamoled", "Super AMOLED", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Tela", "resolution", "Resolucao", "1080 x 2340", "1080x2340", "1080 x 2340", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Tela", "refresh_rate", "Taxa de atualizacao", "120 Hz", "120hz", "120 Hz", "Hz", false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Camera", "main_camera", "Camera principal", "50 MP", "50mp", "50 MP", "MP", true, 0.98, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Camera", "ultrawide_camera", "Ultra-wide", "12 MP", "12mp", "12 MP", "MP", false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Camera", "selfie_camera", "Camera frontal", "12 MP", "12mpfront", "12 MP", "MP", false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Camera", "main_camera_video", "Video principal", "UHD 4K (3840 x 2160) @30fps", "uhd4k3840x216030fps", "UHD 4K (3840 x 2160) @30fps", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Bateria", "battery", "Bateria", "5000 mAh", "5000mah", "5000 mAh", "mAh", true, 0.98, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Construcao", "dimensions", "Dimensoes", "161.5 x 76.8 x 6.9 mm", "1615x768x69mm", "161.5 x 76.8 x 6.9 mm", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Construcao", "weight", "Peso", "179 g", "179g", "179 g", "g", false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Construcao", "ip_rating", "Resistencia", "IP67", "ip67", "IP67", null, true, 0.97, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Conectividade", "sim", "SIM / eSIM", "Dual Nano-SIM", "dualnanosim", "Dual Nano-SIM", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Conectividade", "wifi", "Wi-Fi", "802.11ax 2.4GHz+5GHz", "80211ax24ghz5ghz", "802.11ax 2.4GHz+5GHz", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Conectividade", "bluetooth", "Bluetooth", "Bluetooth v5.3", "bluetoothv53", "Bluetooth v5.3", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Conectividade", "usb", "USB", "USB - Tipo C / USB 2.0", "usbtipocusb20", "USB - Tipo C / USB 2.0", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Conectividade", "network", "Rede", "2G GSM, 3G WCDMA, 4G LTE, 5G Sub6", "2ggsm3gwcdma4glte5gsub6", "2G GSM, 3G WCDMA, 4G LTE, 5G Sub6", null, false, 0.95, collectedAt),
                new SourceSpecClaim("Samsung Official", sourceUrl, true, "Software", "os", "Sistema", "Android", "android", "Android", null, false, 0.95, collectedAt),
            ],
            [new SourceVariantClaim("128 GB - Awesome Charcoal", null, 128, "Awesome Charcoal")],
            []);
    }

    private static string BuildHonor400ProPage()
    {
        return """
            <html>
            <head><title>Honor 400 Pro</title></head>
            <body>
                <script>
                    var HISTORY_ITEM_NAME = "Honor 400 Pro";
                    var HISTORY_ITEM_IMAGE = "https://fixtures.test/honor-400-pro.jpg";
                </script>
                <h1 class="section nobor">Honor 400 Pro</h1>

                <table>
                    <tr><th colspan="2">Launch</th></tr>
                    <tr><td class="ttl">Status</td><td class="nfo" data-spec="released-hl">2025, May 23</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Display</th></tr>
                    <tr><td class="ttl">Type</td><td class="nfo" data-spec="displaytype">OLED, 120Hz, 5000 nits</td></tr>
                    <tr><td class="ttl">Size</td><td class="nfo" data-spec="displaysize">6.7 inches</td></tr>
                    <tr><td class="ttl">Resolution</td><td class="nfo" data-spec="displayresolution">1280 x 2800 pixels</td></tr>
                    <tr><td class="ttl">Protection</td><td class="nfo" data-spec="displayprotection">Honor Shield Glass</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Platform</th></tr>
                    <tr><td class="ttl">OS</td><td class="nfo" data-spec="os">Android 15</td></tr>
                    <tr><td class="ttl">Chipset</td><td class="nfo" data-spec="chipset">Qualcomm SM8650-AB Snapdragon 8 Gen 3 (4 nm)</td></tr>
                    <tr><td class="ttl">CPU</td><td class="nfo" data-spec="cpu">Octa-core</td></tr>
                    <tr><td class="ttl">GPU</td><td class="nfo" data-spec="gpu">Adreno 750</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Memory</th></tr>
                    <tr><td class="ttl">Card slot</td><td class="nfo" data-spec="memoryslot">No</td></tr>
                    <tr><td class="ttl">Internal</td><td class="nfo" data-spec="internalmemory">256GB 12GB RAM, 512GB 12GB RAM</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Main Camera</th></tr>
                    <tr><td class="ttl">Triple</td><td class="nfo" data-spec="cam1modules">200 MP, f/1.9, (wide)<br>12 MP, f/2.2, (ultrawide)<br>50 MP, f/2.4, (telephoto)</td></tr>
                    <tr><td class="ttl">Features</td><td class="nfo" data-spec="cam1features">LED flash, HDR</td></tr>
                    <tr><td class="ttl">Video</td><td class="nfo" data-spec="cam1video">4K</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Selfie camera</th></tr>
                    <tr><td class="ttl">Single</td><td class="nfo" data-spec="cam2modules">50 MP</td></tr>
                    <tr><td class="ttl">Video</td><td class="nfo" data-spec="cam2video">4K</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Battery</th></tr>
                    <tr><td class="ttl">Type</td><td class="nfo" data-spec="batdescription1">5300 mAh</td></tr>
                    <tr><td class="ttl">Charging</td><td class="nfo">100W wired<br>50W wireless</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Body</th></tr>
                    <tr><td class="ttl">Dimensions</td><td class="nfo" data-spec="dimensions">160.8 x 76.1 x 8.1 mm</td></tr>
                    <tr><td class="ttl">Weight</td><td class="nfo" data-spec="weight">205 g</td></tr>
                    <tr><td class="ttl">Build</td><td class="nfo" data-spec="build">Glass front, aluminum frame</td></tr>
                    <tr><td class="ttl">SIM</td><td class="nfo" data-spec="sim">Nano-SIM + eSIM</td></tr>
                    <tr><td class="ttl">Other</td><td class="nfo" data-spec="bodyother">IP68</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Comms</th></tr>
                    <tr><td class="ttl">WLAN</td><td class="nfo" data-spec="wlan">Wi-Fi 7</td></tr>
                    <tr><td class="ttl">Bluetooth</td><td class="nfo" data-spec="bluetooth">5.4</td></tr>
                    <tr><td class="ttl">Positioning</td><td class="nfo" data-spec="gps">GPS, GALILEO</td></tr>
                    <tr><td class="ttl">NFC</td><td class="nfo" data-spec="nfc">Yes</td></tr>
                    <tr><td class="ttl">USB</td><td class="nfo" data-spec="usb">USB Type-C 2.0</td></tr>
                </table>
            </body>
            </html>
            """;
    }

    private static string BuildSamsungCatalogPage()
    {
        return """
            <html>
            <body>
                <script type="application/ld+json">
                {
                  "@context": "https://schema.org",
                  "@type": "ItemList",
                  "itemListElement": [
                    {
                      "@type": "ListItem",
                      "position": "1",
                      "item": {
                        "@type": "Product",
                        "@id": "https://www.samsung.com/br/smartphones/galaxy-a/galaxy-a37-5g-awesome-charcoal-128gb-sm-a376ezkazto/",
                        "name": "Galaxy A37 5G (128GB)",
                        "description": "Camera. Display. Processador : 2.9GHz, 2.6GHz, 1.9GHz",
                        "url": "https://www.samsung.com/br/smartphones/galaxy-a/galaxy-a37-5g-awesome-charcoal-128gb-sm-a376ezkazto/",
                        "image": "//images.samsung.com/is/image/samsung/p6pim/br/sm-a376ezkazto/gallery/br-galaxy-a37-5g-sm-a376-000001-sm-a376ezkazto-thumb-000000001"
                      }
                    },
                    {
                      "@type": "ListItem",
                      "position": "2",
                      "item": {
                        "@type": "Product",
                        "@id": "https://www.samsung.com/br/smartphones/galaxy-a/galaxy-a37-5g-awesome-charcoal-256gb-sm-a376ezkgzto/",
                        "name": "Galaxy A37 5G (Exclusiva Samsung.com)",
                        "description": "Camera. Display. Processador : 2.9GHz, 2.6GHz, 1.9GHz",
                        "url": "https://www.samsung.com/br/smartphones/galaxy-a/galaxy-a37-5g-awesome-charcoal-256gb-sm-a376ezkgzto/",
                        "image": "//images.samsung.com/is/image/samsung/p6pim/br/sm-a376ezkgzto/gallery/br-galaxy-a37-5g-sm-a376-000002-sm-a376ezkgzto-thumb-000000002"
                      }
                    }
                  ]
                }
                </script>
            </body>
            </html>
            """;
    }

    private static string BuildSamsungA57CatalogPage(string region, int variantCount)
    {
        var entries = BuildSamsungA57ProductUrls(region, variantCount)
            .Select((url, index) => $$"""
                {
                  "@type": "ListItem",
                  "position": "{{index + 1}}",
                  "item": {
                    "@type": "Product",
                    "@id": "{{url}}",
                    "name": "Galaxy A57 5G",
                    "description": "Galaxy A57 5G official product",
                    "url": "{{url}}",
                    "image": "//images.samsung.com/is/image/samsung/p6pim/{{region}}/sm-a576/gallery/galaxy-a57-5g-{{index + 1}}"
                  }
                }
                """);

        return $$"""
            <html>
            <body>
                <script type="application/ld+json">
                {
                  "@context": "https://schema.org",
                  "@type": "ItemList",
                  "itemListElement": [
                    {{string.Join(",", entries)}}
                  ]
                }
                </script>
            </body>
            </html>
            """;
    }

    private static IReadOnlyList<string> BuildSamsungA57ProductUrls(string region, int variantCount)
    {
        if (region.Equals("us", StringComparison.OrdinalIgnoreCase))
        {
            return ["https://www.samsung.com/us/smartphones/galaxy-a57-5g/buy/galaxy-a57-5g-128gb-unlocked-sku-sm-a576udbaxaa"];
        }

        return Enumerable.Range(1, variantCount)
            .Select(index => $"https://www.samsung.com/{region}/smartphones/galaxy-a/galaxy-a57-5g-awesome-navy-{128 + (index % 3 * 128)}gb-sm-a576b{index:D2}eub/")
            .ToList();
    }

    private static string BuildSamsungA57Page(bool includeChipset)
    {
        var chipset = includeChipset ? "<p>Powered by an Exynos 1680 processor.</p>" : string.Empty;
        return BuildSamsungA37Page()
            .Replace("Galaxy A37 5G", "Galaxy A57 5G", StringComparison.Ordinal)
            .Replace("sm-a376", "sm-a576", StringComparison.OrdinalIgnoreCase)
            .Replace("2026-03-11", "2026-03-25", StringComparison.Ordinal)
            .Replace("<body>", $"<body>{chipset}", StringComparison.Ordinal);
    }

    private static string BuildSamsungA57ChipsetPage()
    {
        return """
            <html>
            <head><title>Galaxy A57 5G | Samsung US</title></head>
            <body><p>Galaxy A57 5G delivers responsive power with an Exynos 1680 processor.</p></body>
            </html>
            """;
    }

    private static string BuildSamsungUkCatalogPage()
    {
        return """
            <html>
            <body>
                <script type="application/ld+json">
                {
                  "@context": "https://schema.org",
                  "@type": "ItemList",
                  "itemListElement": [
                    {
                      "@type": "ListItem",
                      "position": "1",
                      "item": {
                        "@type": "Product",
                        "@id": "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a17-5g-black-128gb-sm-a176bzkaeub/",
                        "name": "Galaxy A17 5G",
                        "description": "6.7-inch Super AMOLED display. Exynos 1330. IP54 durability.",
                        "url": "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a17-5g-black-128gb-sm-a176bzkaeub/",
                        "image": "//images.samsung.com/is/image/samsung/p6pim/uk/sm-a176bzkaeub/gallery/uk-galaxy-a17-5g-sm-a176-sm-a176bzkaeub-thumb-548472757"
                      }
                    },
                    {
                      "@type": "ListItem",
                      "position": "2",
                      "item": {
                        "@type": "Product",
                        "@id": "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a17-5g-black-128gb-enterprise-edition-sm-a176bzkaeeb/",
                        "name": "Galaxy A17 5G Enterprise Edition",
                        "description": "6.7-inch Super AMOLED display. Exynos 1330. IP54 durability.",
                        "url": "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a17-5g-black-128gb-enterprise-edition-sm-a176bzkaeeb/",
                        "image": "//images.samsung.com/is/image/samsung/p6pim/uk/sm-a176bzkaeeb/gallery/uk-galaxy-a17-5g-sm-a176-561010-sm-a176bzkaeeb-thumb-548558663"
                      }
                    }
                  ]
                }
                </script>
            </body>
            </html>
            """;
    }

    private static string BuildSamsungIndonesiaCatalogPage()
    {
        return """
            <html>
            <body>
                <script type="application/ld+json">
                {
                  "@context": "https://schema.org",
                  "@type": "ItemList",
                  "itemListElement": [
                    {
                      "@type": "ListItem",
                      "position": "1",
                      "item": {
                        "@type": "Product",
                        "@id": "https://www.samsung.com/id/smartphones/galaxy-a/galaxy-a07-5g-light-violet-128gb-sm-a076blvcxid/",
                        "name": "Galaxy A07 5G",
                        "description": "Display. Durability. Processor : Dimensity 6300",
                        "url": "https://www.samsung.com/id/smartphones/galaxy-a/galaxy-a07-5g-light-violet-128gb-sm-a076blvcxid/",
                        "image": "//images.samsung.com/is/image/samsung/p6pim/id/sm-a076blvcxid/gallery/id-galaxy-a07-5g-sm-a076-sm-a076blvcxid-thumb-550763318"
                      }
                    },
                    {
                      "@type": "ListItem",
                      "position": "2",
                      "item": {
                        "@type": "Product",
                        "@id": "https://www.samsung.com/id/smartphones/galaxy-a/galaxy-a07-green-128gb-sm-a075fzggxid/",
                        "name": "Galaxy A07",
                        "description": "Display. Durability. Processor : 2.2GHz, 2GHz",
                        "url": "https://www.samsung.com/id/smartphones/galaxy-a/galaxy-a07-green-128gb-sm-a075fzggxid/",
                        "image": "//images.samsung.com/is/image/samsung/p6pim/id/sm-a075fzggxid/gallery/id-galaxy-a07-sm-a075-sm-a075fzggxid-thumb-548603072"
                      }
                    }
                  ]
                }
                </script>
            </body>
            </html>
            """;
    }

    private static string BuildSamsungA37Page()
    {
        return """
            <html>
            <head>
                <title>Galaxy A37 5G 128 GB | Samsung Brasil</title>
                <meta name="date" content="2026-03-11" />
                <link rel="preload" as="image" href="https://images.samsung.com/is/image/samsung/p6pim/br/sm-a376ezkazto/gallery/br-galaxy-a37-5g-sm-a376-000001-sm-a376ezkazto-front.png" />
            </head>
            <body>
                <p>O Galaxy A37 5G traz desempenho otimizado para jogos e multitarefa. O Galaxy A37 5G suporta carregamento super-rápido de 45 W. Com IP67, ele resiste a agua e poeira. Estrutura de metal com Gorilla Glass Victus+.</p>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Processador<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Velocidade do Processador</p>
                      <p class="pdd32-product-spec__content-item-desc">2.9GHz, 2.6GHz, 1.9GHz</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Tipo de Processador</p>
                      <p class="pdd32-product-spec__content-item-desc">Octa Core</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Tela<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Tamanho (Tela Principal)</p>
                      <p class="pdd32-product-spec__content-item-desc">170.1mm (6.7" retângulo cheio) / 165.5mm (6.5" cantos arredondados)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Resolução (Tela Principal)</p>
                      <p class="pdd32-product-spec__content-item-desc">1080 x 2340 (FHD+)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Tecnologia (Tela Principal)</p>
                      <p class="pdd32-product-spec__content-item-desc">Super AMOLED</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Taxa de Atualização Máxima (Tela Principal)</p>
                      <p class="pdd32-product-spec__content-item-desc">120 Hz</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Camera<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Cameras Traseiras (Multiplas) - Resolucao</p>
                      <p class="pdd32-product-spec__content-item-desc">50.0 MP + 8.0 MP + 5.0 MP</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Camera Frontal - Resolucao</p>
                      <p class="pdd32-product-spec__content-item-desc">13.0 MP</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Resolucao de Gravacao de Videos</p>
                      <p class="pdd32-product-spec__content-item-desc">UHD 4K (3840 x 2160) @30fps</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Armazenamento/Memoria<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Memoria_(GB)</p>
                      <p class="pdd32-product-spec__content-item-desc">8 GB</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Armazenamento (GB)</p>
                      <p class="pdd32-product-spec__content-item-desc">128 GB*</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Rede / Bandas<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Tipo de Chip (SIM Card)</p>
                      <p class="pdd32-product-spec__content-item-desc">Nano-SIM (4FF), Embedded-SIM</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Tipo de Slot de Chip</p>
                      <p class="pdd32-product-spec__content-item-desc">Chip 1 + Chip 2 / Chip 1 + eSIM / Dual eSIM</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Conexoes</p>
                      <p class="pdd32-product-spec__content-item-desc">2G GSM, 3G WCDMA, 4G LTE FDD, 5G Sub6 FDD</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Conectividade<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">USB Interface</p>
                      <p class="pdd32-product-spec__content-item-desc">USB - Tipo C</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Versao de USB</p>
                      <p class="pdd32-product-spec__content-item-desc">USB 2.0</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Localizacao</p>
                      <p class="pdd32-product-spec__content-item-desc">GPS, Glonass, Beidou, Galileo</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Wi-Fi</p>
                      <p class="pdd32-product-spec__content-item-desc">802.11a/b/g/n/ac/ax 2.4GHz+5GHz</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Versao de Bluetooth</p>
                      <p class="pdd32-product-spec__content-item-desc">Bluetooth v5.3</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Sistema Operacional<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-desc">Android 16</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Sensores<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-desc">Acelerometro, Sensor de Impressao Digital, Giroscopio, Sensor de Luz</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Especificacoes Fisicas<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Dimensoes (AxLxP, mm)</p>
                      <p class="pdd32-product-spec__content-item-desc">162.2 x 77.5 x 7.4</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Peso (g)</p>
                      <p class="pdd32-product-spec__content-item-desc">198</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Bateria<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Tempo de Reproducao de Video (Horas)</p>
                      <p class="pdd32-product-spec__content-item-desc">ate 29</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Capacidade da Bateria (mAh, Typical)</p>
                      <p class="pdd32-product-spec__content-item-desc">5000</p>
                    </li>
                  </div>
                </div>
            </body>
            </html>
            """;
    }

    private static string BuildSamsungA17Page()
    {
        return """
            <html>
            <head>
                <title>Galaxy A17 5G | Samsung UK</title>
                <meta name="date" content="2026-09-01" />
                <link rel="preload" as="image" href="https://images.samsung.com/is/image/samsung/p6pim/uk/sm-a176bzkaeub/gallery/uk-galaxy-a17-5g-sm-a176-sm-a176bzkaeub-front.png" />
            </head>
            <body>
                <p>Galaxy A17 5G is powered by Exynos 1330 and keeps going with a 5,000 mAh battery. Rated IP54 for everyday durability.</p>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Processor<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">CPU Speed</p>
                      <p class="pdd32-product-spec__content-item-desc">2.4GHz, 2GHz</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">CPU Type</p>
                      <p class="pdd32-product-spec__content-item-desc">Octa-Core</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Display<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Size (Main_Display)</p>
                      <p class="pdd32-product-spec__content-item-desc">169.1mm (6.7&quot; full rectangle) / 164.5mm (6.5&quot; rounded corners)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Resolution (Main Display)</p>
                      <p class="pdd32-product-spec__content-item-desc">1080 x 2340 (FHD+)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Technology (Main Display)</p>
                      <p class="pdd32-product-spec__content-item-desc">Super AMOLED</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Max Refresh Rate (Main Display)</p>
                      <p class="pdd32-product-spec__content-item-desc">90 Hz</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Camera<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Rear Camera - Resolution (Multiple)</p>
                      <p class="pdd32-product-spec__content-item-desc">50.0 MP + 5.0 MP + 2.0 MP</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Front Camera - Resolution</p>
                      <p class="pdd32-product-spec__content-item-desc">13.0 MP</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Video Recording Resolution</p>
                      <p class="pdd32-product-spec__content-item-desc">FHD (1920 x 1080)@30fps</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Storage/Memory<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Memory_(GB)</p>
                      <p class="pdd32-product-spec__content-item-desc">4</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Storage (GB)</p>
                      <p class="pdd32-product-spec__content-item-desc">128</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Network/Bearer<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Number of SIM</p>
                      <p class="pdd32-product-spec__content-item-desc">Dual-SIM</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">SIM size</p>
                      <p class="pdd32-product-spec__content-item-desc">Nano-SIM (4FF)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">SIM Slot Type</p>
                      <p class="pdd32-product-spec__content-item-desc">SIM 1 + Hybrid (SIM or MicroSD)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Infra</p>
                      <p class="pdd32-product-spec__content-item-desc">2G GSM, 3G WCDMA, 4G LTE FDD, 4G LTE TDD, 5G Sub6 FDD, 5G Sub6 TDD</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Connectivity<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">USB Interface</p>
                      <p class="pdd32-product-spec__content-item-desc">USB Type-C</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">USB Version</p>
                      <p class="pdd32-product-spec__content-item-desc">USB 2.0</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Location Technology</p>
                      <p class="pdd32-product-spec__content-item-desc">GPS, Glonass, Beidou, Galileo, QZSS</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Wi-Fi</p>
                      <p class="pdd32-product-spec__content-item-desc">802.11a/b/g/n/ac 2.4GHz+5GHz, VHT80</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Bluetooth Version</p>
                      <p class="pdd32-product-spec__content-item-desc">Bluetooth v5.3</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">OS<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-desc">Android 16</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Physical specification<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Dimension (HxWxD, mm)</p>
                      <p class="pdd32-product-spec__content-item-desc">164.4 x 77.9 x 7.5</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Weight (g)</p>
                      <p class="pdd32-product-spec__content-item-desc">192</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Battery<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Battery Capacity (mAh, Typical)</p>
                      <p class="pdd32-product-spec__content-item-desc">5000</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Sensors<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-desc">Fingerprint Sensor, Accelerometer, Gyro Sensor</p>
                    </li>
                  </div>
                </div>
            </body>
            </html>
            """;
    }

    private static string BuildSamsungA07FiveGPage()
    {
        return """
            <html>
            <head>
                <title>Galaxy A07 5G | Samsung Indonesia</title>
                <meta name="date" content="2026-08-12" />
                <link rel="preload" as="image" href="https://images.samsung.com/is/image/samsung/p6pim/id/sm-a076blvcxid/gallery/id-galaxy-a07-5g-sm-a076-sm-a076blvcxid-front.png" />
            </head>
            <body>
                <p>Galaxy A07 5G menghadirkan Dimensity 6300, baterai 5000 mAh, dan layar 6.7-inch untuk pemakaian harian.</p>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Processor<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">CPU Speed</p>
                      <p class="pdd32-product-spec__content-item-desc">2.4GHz, 2GHz</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">CPU Type</p>
                      <p class="pdd32-product-spec__content-item-desc">Octa-Core</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Display<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Size (Main_Display)</p>
                      <p class="pdd32-product-spec__content-item-desc">171.3mm (6.7&quot; full rectangle) / 167.3mm (6.6&quot; rounded corners)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Resolution (Main Display)</p>
                      <p class="pdd32-product-spec__content-item-desc">720 x 1600 (HD+)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Technology (Main Display)</p>
                      <p class="pdd32-product-spec__content-item-desc">PLS LCD</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Camera<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Rear Camera - Resolution (Multiple)</p>
                      <p class="pdd32-product-spec__content-item-desc">50.0 MP + 2.0 MP</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Front Camera - Resolution</p>
                      <p class="pdd32-product-spec__content-item-desc">8.0 MP</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Video Recording Resolution</p>
                      <p class="pdd32-product-spec__content-item-desc">FHD (1920 x 1080) @30fps</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Penyimpanan/Memori<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Memori_(GB)</p>
                      <p class="pdd32-product-spec__content-item-desc">6</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Penyimpanan (GB)</p>
                      <p class="pdd32-product-spec__content-item-desc">128</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Network/Bearer<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Number of SIM</p>
                      <p class="pdd32-product-spec__content-item-desc">Dual-SIM</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">SIM size</p>
                      <p class="pdd32-product-spec__content-item-desc">Nano-SIM (4FF)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">SIM Slot Type</p>
                      <p class="pdd32-product-spec__content-item-desc">SIM 1 + Hybrid (SIM or MicroSD)</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Infra</p>
                      <p class="pdd32-product-spec__content-item-desc">2G GSM, 3G WCDMA, 4G LTE FDD, 4G LTE TDD, 5G Sub6 FDD, 5G Sub6 TDD</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Connectivity<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">USB Interface</p>
                      <p class="pdd32-product-spec__content-item-desc">USB Type-C</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">USB Version</p>
                      <p class="pdd32-product-spec__content-item-desc">USB 2.0</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Location Technology</p>
                      <p class="pdd32-product-spec__content-item-desc">GPS, Glonass, Beidou, Galileo</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Wi-Fi</p>
                      <p class="pdd32-product-spec__content-item-desc">802.11a/b/g/n/ac 2.4GHz+5GHz</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Bluetooth Version</p>
                      <p class="pdd32-product-spec__content-item-desc">Bluetooth v5.3</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">OS<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-desc">Android 16</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Physical specification<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Dimension (HxWxD, mm)</p>
                      <p class="pdd32-product-spec__content-item-desc">167.3 x 77.3 x 8.0</p>
                    </li>
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Weight (g)</p>
                      <p class="pdd32-product-spec__content-item-desc">199</p>
                    </li>
                  </div>
                </div>

                <div class="pdd32-product-spec__item">
                  <button class="pdd32-product-spec__toggle-cta">Battery<svg></svg></button>
                  <div class="pdd32-product-spec__content-wrap">
                    <li class="pdd32-product-spec__content-item">
                      <p class="pdd32-product-spec__content-item-title">Battery Capacity (mAh, Typical)</p>
                      <p class="pdd32-product-spec__content-item-desc">5000</p>
                    </li>
                  </div>
                </div>
            </body>
            </html>
            """;
    }

    private static string BuildSamsungA37GsmArenaPage()
    {
        return """
            <html>
            <head><title>Samsung Galaxy A37 5G</title></head>
            <body>
                <script>
                    var HISTORY_ITEM_NAME = "Samsung Galaxy A37 5G";
                    var HISTORY_ITEM_IMAGE = "https://fdn2.gsmarena.com/vv/bigpic/samsung-galaxy-a37-5g.jpg";
                </script>
                <h1 class="section nobor">Samsung Galaxy A37 5G</h1>

                <table>
                    <tr><th colspan="2">Launch</th></tr>
                    <tr><td class="ttl">Status</td><td class="nfo" data-spec="released-hl">2026, March 19</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Display</th></tr>
                    <tr><td class="ttl">Type</td><td class="nfo" data-spec="displaytype">Super AMOLED, 120Hz</td></tr>
                    <tr><td class="ttl">Size</td><td class="nfo" data-spec="displaysize">6.7 inches</td></tr>
                    <tr><td class="ttl">Resolution</td><td class="nfo" data-spec="displayresolution">1080 x 2340 pixels</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Platform</th></tr>
                    <tr><td class="ttl">OS</td><td class="nfo" data-spec="os">Android 16</td></tr>
                    <tr><td class="ttl">Chipset</td><td class="nfo" data-spec="chipset">Exynos 1580 (4 nm)</td></tr>
                    <tr><td class="ttl">CPU</td><td class="nfo" data-spec="cpu">Octa-core</td></tr>
                    <tr><td class="ttl">GPU</td><td class="nfo" data-spec="gpu">Xclipse 540</td></tr>
                    <tr><td class="ttl">Benchmarks</td><td class="nfo" data-spec="tbench">AnTuTu: 780,000<br>GeekBench: 2,050</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Memory</th></tr>
                    <tr><td class="ttl">Internal</td><td class="nfo" data-spec="internalmemory">128GB 8GB RAM, 256GB 8GB RAM</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Main Camera</th></tr>
                    <tr><td class="ttl">Triple</td><td class="nfo" data-spec="cam1modules">50 MP, f/1.8, (wide)<br>8 MP, f/2.2, (ultrawide)<br>5 MP, f/2.4, (macro)</td></tr>
                    <tr><td class="ttl">Video</td><td class="nfo" data-spec="cam1video">4K</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Selfie camera</th></tr>
                    <tr><td class="ttl">Single</td><td class="nfo" data-spec="cam2modules">13 MP</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Battery</th></tr>
                    <tr><td class="ttl">Type</td><td class="nfo" data-spec="batdescription1">5000 mAh</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Body</th></tr>
                    <tr><td class="ttl">Dimensions</td><td class="nfo" data-spec="dimensions">162.2 x 77.5 x 7.4 mm</td></tr>
                    <tr><td class="ttl">Weight</td><td class="nfo" data-spec="weight">198 g</td></tr>
                    <tr><td class="ttl">SIM</td><td class="nfo" data-spec="sim">Nano-SIM + eSIM</td></tr>
                    <tr><td class="ttl">Other</td><td class="nfo" data-spec="bodyother">IP67</td></tr>
                </table>

                <table>
                    <tr><th colspan="2">Comms</th></tr>
                    <tr><td class="ttl">WLAN</td><td class="nfo" data-spec="wlan">Wi-Fi 6</td></tr>
                    <tr><td class="ttl">Bluetooth</td><td class="nfo" data-spec="bluetooth">5.3</td></tr>
                    <tr><td class="ttl">Positioning</td><td class="nfo" data-spec="gps">GPS, GALILEO</td></tr>
                    <tr><td class="ttl">USB</td><td class="nfo" data-spec="usb">USB Type-C 2.0</td></tr>
                </table>
            </body>
            </html>
            """;
    }

    private sealed record TestHarness(
        ServiceProvider Provider,
        SqliteConnection Connection,
        CatalogService Catalog) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class StubHttpClientFactory(IReadOnlyDictionary<string, string> responses) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FixtureHttpMessageHandler(responses), disposeHandler: true);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SpecTen.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }

    private sealed class FixtureHttpMessageHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!responses.TryGetValue(request.RequestUri!.ToString(), out var payload))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
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
}
