using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SpecTen.Web.Data;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class TestDeviceCoverageService(IDbContextFactory<CatalogDbContext> dbContextFactory) : IDeviceCoverageService
{
    private static readonly ConcurrentQueue<string> EnsureCatalogEntryRequests = new();
    private static readonly IReadOnlyList<CoveragePhoneResult> Entries =
    [
        new("Vivo", "vivo", "X300 Ultra", "x300-ultra", "Test coverage", "https://example.test/device-models"),
        new("Vivo", "vivo", "X300", "x300", "Test coverage", "https://example.test/device-models"),
        new("Vivo", "vivo", "X300 Pro 卫星通信版", "x300-pro-satellite", "Test coverage", "https://example.test/device-models"),
        new("Nothing", "nothing", "Phone (2a) Plus", "phone-2a-plus", "Test coverage", "https://example.test/device-models"),
        new("Nothing", "nothing", "Phone 3a Pro", "phone-3a-pro", "Test coverage", "https://example.test/device-models"),
        new("Apple", "apple", "iPhone 14 Pro Max", "iphone-14-pro-max", "Test coverage", "https://example.test/device-models"),
        new("Google", "google", "Pixel 6", "pixel-6", "Test coverage", "https://example.test/device-models"),
        new("Xiaomi", "xiaomi", "Xiaomi 15", "xiaomi-15", "Test coverage", "https://example.test/device-models"),
        new("Xiaomi", "xiaomi", "Xiaomi 15 Ultra", "15-ultra", "Test coverage", "https://example.test/device-models"),
        new("Xiaomi", "xiaomi", "Mi A2 (Mi 6X)", "mi-a2-mi-6x", "Test coverage", "https://example.test/device-models"),
        new("LG", "lg", "G6", "g6", "Test coverage", "https://example.test/device-models"),
        new("LG", "lg", "LG-600", "lg-600", "Test coverage", "https://example.test/device-models"),
        new("Redmi", "redmi", "Redmi Note 12", "redmi-note-12", "Test coverage", "https://example.test/device-models"),
    ];
    private static readonly IReadOnlyDictionary<string, CoverageFixture> HydrationFixtures =
        new Dictionary<string, CoverageFixture>(StringComparer.OrdinalIgnoreCase)
        {
            ["nothing:phone-2a-plus"] = new CoverageFixture(
                "Nothing",
                "nothing",
                "Phone (2a) Plus",
                "phone-2a-plus",
                "Ficha publica criada sob demanda nos testes para validar hidratacao da cobertura.",
                "https://example.test/images/nothing-phone-2a-plus.png",
                new DateTimeOffset(2024, 7, 31, 0, 0, 0, TimeSpan.Zero),
                [
                    new CoverageSpec("Performance", "chipset", "Chipset", "Mediatek Dimensity 7350 Pro (4 nm)", true, 0.91),
                    new CoverageSpec("Memoria", "ram", "RAM", "12 GB", true, 0.9, "GB"),
                    new CoverageSpec("Tela", "display_size", "Tamanho da tela", "6.7 in", true, 0.89, "in"),
                    new CoverageSpec("Camera", "main_camera", "Camera principal", "50 MP", true, 0.88, "MP"),
                    new CoverageSpec("Bateria", "battery", "Bateria", "5000 mAh", true, 0.9, "mAh"),
                    new CoverageSpec("Bateria", "charging", "Carregamento", "50 W", true, 0.87, "W"),
                ]),
            ["vivo:x300-ultra"] = new CoverageFixture(
                "Vivo",
                "vivo",
                "X300 Ultra",
                "x300-ultra",
                "Ficha publica criada sob demanda nos testes para validar hidratacao de um aparelho recente fora do seed local.",
                "https://example.test/images/vivo-x300-ultra.png",
                new DateTimeOffset(2025, 4, 21, 0, 0, 0, TimeSpan.Zero),
                [
                    new CoverageSpec("Performance", "chipset", "Chipset", "Snapdragon 8 Elite Gen 5", true, 0.93),
                    new CoverageSpec("Memoria", "ram", "RAM", "16 GB", true, 0.91, "GB"),
                    new CoverageSpec("Tela", "display_size", "Tamanho da tela", "6.82 in", true, 0.9, "in"),
                    new CoverageSpec("Camera", "main_camera", "Camera principal", "200 MP", true, 0.9, "MP"),
                    new CoverageSpec("Bateria", "battery", "Bateria", "6600 mAh", true, 0.92, "mAh"),
                    new CoverageSpec("Bateria", "charging", "Carregamento", "100 W", true, 0.88, "W"),
                ],
                [
                    new CoverageBenchmark("AnTuTu", 2_084_500),
                ]),
            ["xiaomi:15-ultra"] = new CoverageFixture(
                "Xiaomi",
                "xiaomi",
                "15 Ultra",
                "15-ultra",
                "Ficha publica criada sob demanda nos testes para validar hidratacao de flagship recente.",
                "https://example.test/images/xiaomi-15-ultra.png",
                new DateTimeOffset(2025, 2, 27, 0, 0, 0, TimeSpan.Zero),
                [
                    new CoverageSpec("Performance", "chipset", "Chipset", "Qualcomm Snapdragon 8 Elite", true, 0.92),
                    new CoverageSpec("Memoria", "ram", "RAM", "16 GB", true, 0.9, "GB"),
                    new CoverageSpec("Tela", "display_size", "Tamanho da tela", "6.73 in", true, 0.89, "in"),
                    new CoverageSpec("Camera", "main_camera", "Camera principal", "50 MP", true, 0.89, "MP"),
                    new CoverageSpec("Bateria", "battery", "Bateria", "5410 mAh", true, 0.91, "mAh"),
                    new CoverageSpec("Bateria", "charging", "Carregamento", "90 W", true, 0.88, "W"),
                ]),
            ["xiaomi:mi-a2-mi-6x"] = new CoverageFixture(
                "Xiaomi",
                "xiaomi",
                "Mi A2 (Mi 6X)",
                "mi-a2-mi-6x",
                "Ficha legado criada sob demanda nos testes para validar buscas curtas com codigo de modelo.",
                "https://example.test/images/mi-a2-mi-6x.png",
                new DateTimeOffset(2018, 7, 24, 0, 0, 0, TimeSpan.Zero),
                [
                    new CoverageSpec("Performance", "chipset", "Chipset", "Qualcomm Snapdragon 660", true, 0.9),
                    new CoverageSpec("Memoria", "ram", "RAM", "4 GB", true, 0.88, "GB"),
                    new CoverageSpec("Armazenamento", "storage_base", "Armazenamento base", "64 GB", true, 0.87, "GB"),
                    new CoverageSpec("Tela", "display_size", "Tamanho da tela", "5.99 in", true, 0.88, "in"),
                    new CoverageSpec("Camera", "main_camera", "Camera principal", "12 MP", true, 0.87, "MP"),
                    new CoverageSpec("Bateria", "battery", "Bateria", "3010 mAh", true, 0.89, "mAh"),
                    new CoverageSpec("Bateria", "charging", "Carregamento", "18 W", true, 0.84, "W"),
                ]),
            ["apple:iphone-14-pro-max"] = new CoverageFixture(
                "Apple",
                "apple",
                "iPhone 14 Pro Max",
                "iphone-14-pro-max",
                "Ficha legado hidratada nos testes para validar classificacao publica coerente.",
                "https://example.test/images/iphone-14-pro-max.png",
                new DateTimeOffset(2022, 9, 16, 0, 0, 0, TimeSpan.Zero),
                [
                    new CoverageSpec("Performance", "chipset", "Chipset", "Apple A16 Bionic", true, 0.93),
                    new CoverageSpec("Memoria", "ram", "RAM", "6 GB", true, 0.89, "GB"),
                    new CoverageSpec("Tela", "display_size", "Tamanho da tela", "6.7 in", true, 0.9, "in"),
                    new CoverageSpec("Camera", "main_camera", "Camera principal", "48 MP", true, 0.89, "MP"),
                    new CoverageSpec("Bateria", "battery", "Bateria", "4323 mAh", true, 0.88, "mAh"),
                    new CoverageSpec("Bateria", "charging", "Carregamento", "27 W", true, 0.84, "W"),
                ],
                [
                    new CoverageBenchmark("AnTuTu", 955_884),
                    new CoverageBenchmark("GeekBench", 5_423),
                ]),
            ["google:pixel-6"] = new CoverageFixture(
                "Google",
                "google",
                "Pixel 6",
                "pixel-6",
                "Ficha legado hidratada nos testes para validar foco da busca em modelos especificos.",
                "https://example.test/images/google-pixel-6.png",
                new DateTimeOffset(2021, 10, 28, 0, 0, 0, TimeSpan.Zero),
                [
                    new CoverageSpec("Performance", "chipset", "Chipset", "Google Tensor", true, 0.92),
                    new CoverageSpec("Memoria", "ram", "RAM", "8 GB", true, 0.89, "GB"),
                    new CoverageSpec("Tela", "display_size", "Tamanho da tela", "6.4 in", true, 0.9, "in"),
                    new CoverageSpec("Camera", "main_camera", "Camera principal", "50 MP", true, 0.89, "MP"),
                    new CoverageSpec("Bateria", "battery", "Bateria", "4614 mAh", true, 0.9, "mAh"),
                    new CoverageSpec("Bateria", "charging", "Carregamento", "30 W", true, 0.84, "W"),
                ]),
            ["lg:g6"] = new CoverageFixture(
                "LG",
                "lg",
                "G6",
                "g6",
                "Ficha legado criada sob demanda para validar buscas especificas com codigo curto de modelo.",
                "https://example.test/images/lg-g6.png",
                new DateTimeOffset(2017, 4, 7, 0, 0, 0, TimeSpan.Zero),
                [
                    new CoverageSpec("Performance", "chipset", "Chipset", "Qualcomm Snapdragon 821", true, 0.9),
                    new CoverageSpec("Memoria", "ram", "RAM", "4 GB", true, 0.87, "GB"),
                    new CoverageSpec("Armazenamento", "storage_base", "Armazenamento base", "32 GB", true, 0.86, "GB"),
                    new CoverageSpec("Armazenamento", "storage_options", "Opcoes de armazenamento", "32 GB / 64 GB", false, 0.84),
                    new CoverageSpec("Tela", "display_size", "Tamanho da tela", "5.7 in", true, 0.88, "in"),
                    new CoverageSpec("Tela", "display_type", "Painel", "IPS LCD", false, 0.84),
                    new CoverageSpec("Tela", "resolution", "Resolucao", "2880 x 1440", false, 0.84),
                    new CoverageSpec("Camera", "main_camera", "Camera principal", "13 MP", true, 0.86, "MP"),
                    new CoverageSpec("Camera", "ultrawide_camera", "Ultra-wide", "13 MP", false, 0.82, "MP"),
                    new CoverageSpec("Camera", "selfie_camera", "Selfie", "5 MP", false, 0.82, "MP"),
                    new CoverageSpec("Bateria", "battery", "Bateria", "3300 mAh", true, 0.89, "mAh"),
                    new CoverageSpec("Bateria", "charging", "Carregamento", "18 W", true, 0.83, "W"),
                    new CoverageSpec("Construcao", "dimensions", "Dimensoes", "148.9 x 71.9 x 7.9 mm", false, 0.82),
                    new CoverageSpec("Construcao", "weight", "Peso", "163 g", false, 0.82, "g"),
                    new CoverageSpec("Construcao", "protection", "Protecao", "Gorilla Glass 3", false, 0.8),
                    new CoverageSpec("Construcao", "ip_rating", "IP", "IP68", false, 0.8),
                    new CoverageSpec("Construcao", "sim", "SIM / eSIM", "Nano-SIM", false, 0.82),
                    new CoverageSpec("Conectividade", "wifi", "Wi-Fi", "Wi-Fi 802.11 a/b/g/n/ac", false, 0.81),
                    new CoverageSpec("Conectividade", "bluetooth", "Bluetooth", "4.2", false, 0.81),
                    new CoverageSpec("Conectividade", "nfc", "NFC", "Sim", false, 0.8),
                    new CoverageSpec("Conectividade", "usb", "USB", "USB-C 3.1", false, 0.8),
                    new CoverageSpec("Conectividade", "network", "Rede", "GSM / HSPA / LTE", false, 0.82),
                    new CoverageSpec("Software", "os", "Sistema", "Android 7.0", false, 0.8),
                ]),
            ["lg:lg-600"] = new CoverageFixture(
                "LG",
                "lg",
                "LG-600",
                "lg-600",
                "Modelo antigo usado para garantir que consultas por G6 nao caiam em falsos positivos numericos.",
                "https://example.test/images/lg-600.png",
                new DateTimeOffset(2002, 1, 1, 0, 0, 0, TimeSpan.Zero),
                [
                    new CoverageSpec("Mercado", "network", "Rede", "GSM", true, 0.74),
                    new CoverageSpec("Tela", "display_type", "Painel", "Monochrome graphic", true, 0.73),
                    new CoverageSpec("Bateria", "battery", "Bateria", "600 mAh", true, 0.78, "mAh"),
                    new CoverageSpec("Construcao", "dimensions", "Dimensoes", "108 x 44 x 22 mm", false, 0.72),
                    new CoverageSpec("Construcao", "weight", "Peso", "112 g", false, 0.72, "g"),
                    new CoverageSpec("Construcao", "sim", "SIM / eSIM", "Mini-SIM", false, 0.72),
                    new CoverageSpec("Conectividade", "radio", "Radio", "No", false, 0.72),
                ]),
        };
    private static readonly PhoneClassifier Classifier = new();

    public Task WarmupAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<CoveragePhoneResult?> GetBySlugAsync(string brandSlug, string slug, CancellationToken cancellationToken)
    {
        var result = Entries.FirstOrDefault(entry =>
            entry.BrandSlug.Equals(Slugger.Slugify(brandSlug), StringComparison.OrdinalIgnoreCase) &&
            entry.Slug.Equals(Slugger.Slugify(slug), StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<CoveragePhoneResult>> SearchAsync(string? query, string? brandSlug, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<CoveragePhoneResult>>([]);
        }

        var normalizedQuery = PhoneSearchText.Normalize(query);
        var tokens = PhoneSearchText.Tokenize(query);
        var normalizedBrand = string.IsNullOrWhiteSpace(brandSlug) ? string.Empty : Slugger.Slugify(brandSlug);

        var results = Entries
            .Where(entry => normalizedBrand.Length == 0 || entry.BrandSlug.Equals(normalizedBrand, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new
            {
                Entry = entry,
                FullName = PhoneSearchText.Normalize($"{entry.Brand} {entry.Name}"),
                Name = PhoneSearchText.Normalize(entry.Name),
            })
            .Where(item =>
                item.FullName.Contains(normalizedQuery, StringComparison.Ordinal) ||
                item.Name.Contains(normalizedQuery, StringComparison.Ordinal) ||
                tokens.All(token => item.FullName.Contains(token, StringComparison.Ordinal)))
            .Take(limit)
            .Select(item => item.Entry)
            .ToList();

        return Task.FromResult<IReadOnlyList<CoveragePhoneResult>>(results);
    }

    public Task<IReadOnlyList<CoveragePhoneResult>> BrowseByBrandAsync(string brandSlug, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(brandSlug) || limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<CoveragePhoneResult>>([]);
        }

        var normalizedBrand = Slugger.Slugify(brandSlug);
        var results = Entries
            .Where(entry => entry.BrandSlug.Equals(normalizedBrand, StringComparison.OrdinalIgnoreCase))
            .Where(IsBrowseFriendly)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<CoveragePhoneResult>>(results);
    }

    public Task<IReadOnlyList<CoverageBrandOption>> GetBrandOptionsAsync(CancellationToken cancellationToken)
    {
        var results = Entries
            .Where(IsBrowseFriendly)
            .GroupBy(entry => entry.BrandSlug, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CoverageBrandOption(group.First().Brand, group.Key, group.Count()))
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<CoverageBrandOption>>(results);
    }

    public Task<CoverageHydrationResult?> EnsureCatalogEntryAsync(string brandSlug, string slug, CancellationToken cancellationToken)
    {
        EnsureCatalogEntryRequests.Enqueue($"{Slugger.Slugify(brandSlug)}:{Slugger.Slugify(slug)}");
        return EnsureCatalogEntryCoreAsync(brandSlug, slug, cancellationToken);
    }

    public static void ResetObservedRequests()
    {
        while (EnsureCatalogEntryRequests.TryDequeue(out _))
        {
        }
    }

    public static IReadOnlyList<string> ObservedRequests()
    {
        return EnsureCatalogEntryRequests.ToArray();
    }

    private static bool IsBrowseFriendly(CoveragePhoneResult entry)
    {
        return !entry.Name.Contains("卫星", StringComparison.Ordinal) &&
               !entry.Name.Contains("版", StringComparison.Ordinal) &&
               !entry.Name.Contains('(', StringComparison.Ordinal) &&
               !entry.Name.Contains(')', StringComparison.Ordinal);
    }

    private async Task<CoverageHydrationResult?> EnsureCatalogEntryCoreAsync(string brandSlug, string slug, CancellationToken cancellationToken)
    {
        var key = $"{Slugger.Slugify(brandSlug)}:{Slugger.Slugify(slug)}";
        if (!HydrationFixtures.TryGetValue(key, out var fixture))
        {
            return null;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.PhoneModels
            .Include(model => model.Brand)
            .FirstOrDefaultAsync(model =>
                model.Brand.Slug == fixture.BrandSlug &&
                model.Slug == fixture.Slug,
                cancellationToken);

        if (existing is not null)
        {
            await RefreshExistingPhoneAsync(db, existing, fixture, cancellationToken);
            return new CoverageHydrationResult(existing.Id, existing.Brand.Slug, existing.Slug, "Test coverage", $"https://example.test/{fixture.Slug}");
        }

        var brand = await db.Brands
            .FirstOrDefaultAsync(item => item.Slug == fixture.BrandSlug, cancellationToken);

        if (brand is null)
        {
            brand = new Brand
            {
                Name = fixture.Brand,
                Slug = fixture.BrandSlug,
            };
            db.Brands.Add(brand);
            await db.SaveChangesAsync(cancellationToken);
        }

        var phone = new PhoneModel
        {
            BrandId = brand.Id,
            Name = fixture.Name,
            Slug = fixture.Slug,
            Summary = fixture.Summary,
            ReleasedAt = fixture.ReleasedAt,
            ImageUrl = fixture.ImageUrl,
            ImageSourceUrl = $"https://example.test/{fixture.Slug}",
        };
        db.PhoneModels.Add(phone);
        await db.SaveChangesAsync(cancellationToken);

        var specs = fixture.Specs
            .Select(spec => new SpecFact
            {
                PhoneModelId = phone.Id,
                Group = spec.Group,
                Key = spec.Key,
                DisplayName = spec.DisplayName,
                NormalizedValue = spec.Value,
                DisplayValue = spec.Value,
                Unit = spec.Unit,
                SourceName = "Test coverage",
                SourceUrl = $"https://example.test/{fixture.Slug}",
                Confidence = spec.Confidence,
                Status = SpecStatus.Published,
                IsCritical = spec.IsCritical,
            })
            .ToList();

        db.SpecFacts.AddRange(specs);

        var benchmarkFixtures = fixture.Benchmarks ?? [];
        var benchmarks = benchmarkFixtures
            .Select(benchmark => new BenchmarkScore
            {
                PhoneModelId = phone.Id,
                BenchmarkName = benchmark.Name,
                Score = benchmark.Score,
                SourceName = "Test coverage",
                SourceUrl = $"https://example.test/{fixture.Slug}",
            })
            .ToList();

        if (benchmarks.Count > 0)
        {
            db.BenchmarkScores.AddRange(benchmarks);
        }

        var chipset = fixture.Specs.FirstOrDefault(spec => spec.Key == "chipset")?.Value;
        if (!string.IsNullOrWhiteSpace(chipset))
        {
            var classification = Classifier.Classify(
                chipset,
                benchmarkFixtures.Select(benchmark => new BenchmarkInput(benchmark.Name, benchmark.Score)),
                fixture.ReleasedAt);
            db.ClassificationSnapshots.Add(new ClassificationSnapshot
            {
                PhoneModelId = phone.Id,
                Tier = classification.Tier,
                Score = classification.Score,
                Basis = classification.Basis,
                Explanation = classification.Explanation,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new CoverageHydrationResult(phone.Id, fixture.BrandSlug, fixture.Slug, "Test coverage", $"https://example.test/{fixture.Slug}");
    }

    private static async Task RefreshExistingPhoneAsync(
        CatalogDbContext db,
        PhoneModel phone,
        CoverageFixture fixture,
        CancellationToken cancellationToken)
    {
        var needsRefresh = string.IsNullOrWhiteSpace(phone.ImageUrl) ||
                           phone.ReleasedAt is null;

        var existingSpecs = await db.SpecFacts
            .Where(spec => spec.PhoneModelId == phone.Id)
            .ToDictionaryAsync(spec => spec.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);
        needsRefresh |= fixture.Specs.Any(spec => !existingSpecs.ContainsKey(spec.Key));

        if (!needsRefresh)
        {
            return;
        }

        phone.Summary = fixture.Summary;
        phone.ReleasedAt = fixture.ReleasedAt;
        phone.ImageUrl = fixture.ImageUrl;
        phone.ImageSourceUrl = $"https://example.test/{fixture.Slug}";

        foreach (var fixtureSpec in fixture.Specs)
        {
            if (!existingSpecs.TryGetValue(fixtureSpec.Key, out var spec))
            {
                spec = new SpecFact
                {
                    PhoneModelId = phone.Id,
                    Key = fixtureSpec.Key,
                };
                db.SpecFacts.Add(spec);
            }

            spec.Group = fixtureSpec.Group;
            spec.DisplayName = fixtureSpec.DisplayName;
            spec.NormalizedValue = fixtureSpec.Value;
            spec.DisplayValue = fixtureSpec.Value;
            spec.Unit = fixtureSpec.Unit;
            spec.SourceName = "Test coverage";
            spec.SourceUrl = $"https://example.test/{fixture.Slug}";
            spec.Confidence = fixtureSpec.Confidence;
            spec.Status = SpecStatus.Published;
            spec.IsCritical = fixtureSpec.IsCritical;
        }

        var benchmarkFixtures = fixture.Benchmarks ?? [];
        var existingBenchmarks = await db.BenchmarkScores
            .Where(score => score.PhoneModelId == phone.Id)
            .ToDictionaryAsync(score => score.BenchmarkName, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var benchmarkFixture in benchmarkFixtures)
        {
            if (!existingBenchmarks.TryGetValue(benchmarkFixture.Name, out var benchmark))
            {
                benchmark = new BenchmarkScore
                {
                    PhoneModelId = phone.Id,
                    BenchmarkName = benchmarkFixture.Name,
                };
                db.BenchmarkScores.Add(benchmark);
            }

            benchmark.Score = benchmarkFixture.Score;
            benchmark.SourceName = "Test coverage";
            benchmark.SourceUrl = $"https://example.test/{fixture.Slug}";
        }

        var chipset = fixture.Specs.FirstOrDefault(spec => spec.Key == "chipset")?.Value;
        if (!string.IsNullOrWhiteSpace(chipset))
        {
            var classification = Classifier.Classify(
                chipset,
                benchmarkFixtures.Select(benchmark => new BenchmarkInput(benchmark.Name, benchmark.Score)),
                fixture.ReleasedAt);
            db.ClassificationSnapshots.Add(new ClassificationSnapshot
            {
                PhoneModelId = phone.Id,
                Tier = classification.Tier,
                Score = classification.Score,
                Basis = classification.Basis,
                Explanation = classification.Explanation,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record CoverageFixture(
        string Brand,
        string BrandSlug,
        string Name,
        string Slug,
        string Summary,
        string ImageUrl,
        DateTimeOffset ReleasedAt,
        IReadOnlyList<CoverageSpec> Specs,
        IReadOnlyList<CoverageBenchmark>? Benchmarks = null);

    private sealed record CoverageSpec(
        string Group,
        string Key,
        string DisplayName,
        string Value,
        bool IsCritical,
        double Confidence,
        string? Unit = null);

    private sealed record CoverageBenchmark(string Name, int Score);
}
