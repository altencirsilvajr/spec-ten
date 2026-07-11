using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpecTen.Web.Data;
using SpecTen.Web.Options;
using SpecTen.Web.Scraping;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class PhoneImportNormalizationTests
{
    [Fact]
    public async Task ImportRecordAsync_Merges_Noisy_OfficialSamsungVariants_Into_CanonicalModel()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddDbContextFactory<CatalogDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<SpecFactResolver>();
        services.AddSingleton<PhoneClassifier>();
        services.AddSingleton<IOptions<ScrapingOptions>>(Options.Create(new ScrapingOptions()));
        services.AddScoped(_ => new PhoneImportService(
            _.GetRequiredService<IDbContextFactory<CatalogDbContext>>(),
            Array.Empty<IPhoneSourceAdapter>(),
            _.GetRequiredService<SpecFactResolver>(),
            _.GetRequiredService<PhoneClassifier>(),
            _.GetRequiredService<IOptions<ScrapingOptions>>(),
            _.GetRequiredService<IMemoryCache>(),
            NullLogger<PhoneImportService>.Instance));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
        var collectedAt = DateTimeOffset.Parse("2026-07-09T12:00:00Z");

        await importer.ImportRecordAsync(
            BuildOfficialSamsungRecord(
                "Galaxy A57 5G",
                "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a57-5g-awesome-navy-256gb-sm-a576bdbdeub/",
                collectedAt,
                [
                    new SourceVariantClaim("Awesome Navy", null, null, "Awesome Navy"),
                    new SourceVariantClaim("8 GB / 256 GB", 8, 256, null),
                    new SourceVariantClaim("256 GB - Awesome Navy", null, 256, "Awesome Navy"),
                ]),
            "test:canonical",
            CancellationToken.None);

        await importer.ImportRecordAsync(
            BuildOfficialSamsungRecord(
                "Galaxy A57 5G 128GB (Unlocked)",
                "https://www.samsung.com/us/smartphones/galaxy-a/galaxy-a57-5g-128gb-unlocked/",
                collectedAt.AddMinutes(1),
                [
                    new SourceVariantClaim("128 GB", null, 128, null),
                    new SourceVariantClaim("8 GB - Awesome Navy", null, 8, "Awesome Navy"),
                ]),
            "test:unlocked",
            CancellationToken.None);

        await importer.ImportRecordAsync(
            BuildOfficialSamsungRecord(
                "Galaxy A57 5G Awesome Gray 256GB and Fit3 Bundle",
                "https://www.samsung.com/ph/smartphones/galaxy-a/galaxy-a57-5g-awesome-gray-256gb-and-fit3-bundle/",
                collectedAt.AddMinutes(2),
                [
                    new SourceVariantClaim("256 GB - Awesome Gray", null, 256, "Awesome Gray"),
                ]),
            "test:bundle",
            CancellationToken.None);

        await importer.NormalizeCatalogAsync(CancellationToken.None);

        await using var verificationDb = await dbFactory.CreateDbContextAsync();
        var samsungPhones = await verificationDb.PhoneModels
            .Include(phone => phone.Brand)
            .Include(phone => phone.Variants)
            .Where(phone => phone.Brand.Slug == "samsung")
            .ToListAsync();

        var canonical = Assert.Single(samsungPhones);
        Assert.Equal("Galaxy A57 5G", canonical.Name);
        Assert.Equal("galaxy-a57-5g", canonical.Slug);
        Assert.Contains(canonical.Variants, variant => variant.StorageGb == 128);
        Assert.Contains(canonical.Variants, variant => variant.StorageGb == 256);
        Assert.DoesNotContain(canonical.Variants, variant => variant.StorageGb == 8);
        Assert.DoesNotContain(canonical.Variants, variant => variant.RamGb is null && variant.StorageGb is null);
        Assert.DoesNotContain(canonical.Variants, variant => variant.Name.Contains("Unlocked", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(canonical.Variants, variant => variant.Name.Contains("Bundle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NormalizeCatalogAsync_Sanitizes_Persisted_SamsungMarketingNoise()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddDbContextFactory<CatalogDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<SpecFactResolver>();
        services.AddSingleton<PhoneClassifier>();
        services.AddSingleton<IOptions<ScrapingOptions>>(Options.Create(new ScrapingOptions()));
        services.AddScoped(_ => new PhoneImportService(
            _.GetRequiredService<IDbContextFactory<CatalogDbContext>>(),
            Array.Empty<IPhoneSourceAdapter>(),
            _.GetRequiredService<SpecFactResolver>(),
            _.GetRequiredService<PhoneClassifier>(),
            _.GetRequiredService<IOptions<ScrapingOptions>>(),
            _.GetRequiredService<IMemoryCache>(),
            NullLogger<PhoneImportService>.Instance));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();

            var brand = new Brand
            {
                Name = "Samsung",
                Slug = "samsung",
                OfficialDomain = "samsung.com",
            };

            var phone = new PhoneModel
            {
                Brand = brand,
                Name = "Galaxy A17 5G",
                Slug = "galaxy-a17-5g",
                Specs =
                [
                    new SpecFact
                    {
                        Group = "Construcao",
                        Key = "build",
                        DisplayName = "Construcao",
                        DisplayValue = "Gorilla Glass Victus means you can focus on what matters",
                        NormalizedValue = PhoneSearchText.Normalize("Gorilla Glass Victus means you can focus on what matters"),
                        SourceName = "Samsung Official",
                        SourceUrl = "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a17-5g-black-128gb-sm-a176bzkaeub/",
                        Confidence = 0.98,
                    },
                    new SpecFact
                    {
                        Group = "Bateria",
                        Key = "charging",
                        DisplayName = "Carregamento",
                        DisplayValue = "80 W",
                        NormalizedValue = PhoneSearchText.Normalize("80 W"),
                        SourceName = "Samsung Official",
                        SourceUrl = "https://www.samsung.com/uk/smartphones/galaxy-a/galaxy-a17-5g-black-128gb-sm-a176bzkaeub/",
                        Confidence = 0.98,
                    },
                ],
            };

            db.PhoneModels.Add(phone);
            await db.SaveChangesAsync();
        }

        var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
        await importer.NormalizeCatalogAsync(CancellationToken.None);

        await using var verificationDb = await dbFactory.CreateDbContextAsync();
        var persistedPhone = await verificationDb.PhoneModels
            .Include(phone => phone.Specs)
            .Include(phone => phone.Brand)
            .SingleAsync();

        var build = Assert.Single(persistedPhone.Specs, spec => spec.Key == "build");
        Assert.Equal("Gorilla Glass Victus", build.DisplayValue);
        Assert.DoesNotContain(persistedPhone.Specs, spec => spec.Key == "charging");
    }

    [Fact]
    public async Task NormalizeCatalogAsync_Preserves_Parenthetical_Model_Names()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddDbContextFactory<CatalogDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<SpecFactResolver>();
        services.AddSingleton<PhoneClassifier>();
        services.AddSingleton<IOptions<ScrapingOptions>>(Options.Create(new ScrapingOptions()));
        services.AddScoped(_ => new PhoneImportService(
            _.GetRequiredService<IDbContextFactory<CatalogDbContext>>(),
            Array.Empty<IPhoneSourceAdapter>(),
            _.GetRequiredService<SpecFactResolver>(),
            _.GetRequiredService<PhoneClassifier>(),
            _.GetRequiredService<IOptions<ScrapingOptions>>(),
            _.GetRequiredService<IMemoryCache>(),
            NullLogger<PhoneImportService>.Instance));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();

            db.PhoneModels.Add(new PhoneModel
            {
                Brand = new Brand
                {
                    Name = "Nokia",
                    Slug = "nokia",
                },
                Name = "3210 (1999",
                Slug = "3210-1999",
            });

            await db.SaveChangesAsync();
        }

        var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
        await importer.NormalizeCatalogAsync(CancellationToken.None);

        await using var verificationDb = await dbFactory.CreateDbContextAsync();
        var phone = await verificationDb.PhoneModels.SingleAsync();
        Assert.Equal("3210 (1999)", phone.Name);
        Assert.Equal("3210-1999", phone.Slug);
    }

    private static SourcePhoneRecord BuildOfficialSamsungRecord(
        string modelName,
        string sourceUrl,
        DateTimeOffset collectedAt,
        IReadOnlyList<SourceVariantClaim> variants)
    {
        return new SourcePhoneRecord(
            "Samsung Official",
            sourceUrl,
            "Allowed",
            true,
            true,
            "Samsung",
            "samsung.com",
            modelName,
            $"{modelName} com ficha oficial.",
            new DateTimeOffset(2026, 05, 25, 0, 0, 0, TimeSpan.Zero),
            null,
            "https://images.example.test/galaxy-a57.png",
            "https://images.example.test/galaxy-a57.png",
            [
                Spec("Performance", "chipset", "Chipset", "Exynos 1580", true, collectedAt),
                Spec("Tela", "display_size", "Tamanho da tela", "6.7 in", true, collectedAt),
                Spec("Camera", "main_camera", "Camera principal", "50 MP", true, collectedAt),
                Spec("Bateria", "battery", "Bateria", "5000 mAh", true, collectedAt),
            ],
            variants,
            []);
    }

    private static SourceSpecClaim Spec(
        string group,
        string key,
        string displayName,
        string displayValue,
        bool critical,
        DateTimeOffset collectedAt)
    {
        return new SourceSpecClaim(
            "Samsung Official",
            "https://www.samsung.com/",
            true,
            group,
            key,
            displayName,
            displayValue,
            PhoneSearchText.Normalize(displayValue),
            displayValue,
            null,
            critical,
            0.98,
            collectedAt);
    }
}
