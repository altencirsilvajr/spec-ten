using System.Text.Json;
using Microsoft.Extensions.Options;
using SpecTen.Web.Options;

namespace SpecTen.Web.Scraping;

public sealed class JsonCatalogSeedAdapter(
    IHttpClientFactory httpClientFactory,
    IHostEnvironment environment,
    IOptions<CatalogSeedOptions> options,
    ILogger<JsonCatalogSeedAdapter> logger) : IPhoneSourceAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly CatalogSeedOptions _options = options.Value;

    public string SourceName => _options.DefaultSourceName;
    public string PolicyStatus => _options.DefaultPolicyStatus;
    public bool RobotsAllowed => _options.RobotsAllowed;
    public bool IsOfficialSource => _options.IsOfficialSource;

    public async Task<IReadOnlyList<SourcePhoneRecord>> FetchAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        var payload = await LoadPayloadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var phones = DeserializePhones(payload);
        if (phones.Count == 0)
        {
            logger.LogWarning("Catalog seed adapter loaded an empty phone list.");
            return [];
        }

        var records = phones
            .Select(MapRecord)
            .Where(record => record is not null)
            .Cast<SourcePhoneRecord>()
            .ToList();

        logger.LogInformation("Catalog seed adapter produced {Count} phone records.", records.Count);
        return records;
    }

    private async Task<string?> LoadPayloadAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.FilePath))
        {
            var path = Path.IsPathRooted(_options.FilePath)
                ? _options.FilePath
                : Path.Combine(environment.ContentRootPath, _options.FilePath);

            if (!File.Exists(path))
            {
                logger.LogWarning("Catalog seed file was not found at {Path}.", path);
                return null;
            }

            return await File.ReadAllTextAsync(path, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(_options.DataUrl))
        {
            var client = httpClientFactory.CreateClient("catalog-seed");
            return await client.GetStringAsync(_options.DataUrl, cancellationToken);
        }

        logger.LogInformation("Catalog seed adapter is enabled but no file path or data URL was configured.");
        return null;
    }

    private static IReadOnlyList<SeedPhoneRecordDto> DeserializePhones(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<SeedPhoneRecordDto>>(document.RootElement.GetRawText(), JsonOptions) ?? [],
            JsonValueKind.Object when TryGetPhonesElement(document.RootElement, out var phonesElement) => JsonSerializer.Deserialize<List<SeedPhoneRecordDto>>(phonesElement.GetRawText(), JsonOptions) ?? [],
            _ => [],
        };
    }

    private SourcePhoneRecord? MapRecord(SeedPhoneRecordDto phone)
    {
        if (string.IsNullOrWhiteSpace(phone.BrandName) || string.IsNullOrWhiteSpace(phone.ModelName))
        {
            return null;
        }

        var sourceName = string.IsNullOrWhiteSpace(phone.SourceName) ? SourceName : phone.SourceName.Trim();
        var sourceUrl = string.IsNullOrWhiteSpace(phone.SourceUrl) ? _options.DefaultSourceUrl ?? "seed://catalog" : phone.SourceUrl.Trim();
        var policyStatus = string.IsNullOrWhiteSpace(phone.PolicyStatus) ? PolicyStatus : phone.PolicyStatus.Trim();
        var specs = phone.Specs?
            .Where(spec => !string.IsNullOrWhiteSpace(spec.Key) && !string.IsNullOrWhiteSpace(spec.DisplayName) && !string.IsNullOrWhiteSpace(spec.Value))
            .Select(spec =>
            {
                var group = string.IsNullOrWhiteSpace(spec.Group) ? "Outros" : spec.Group.Trim();
                var key = spec.Key!.Trim();
                var displayName = spec.DisplayName!.Trim();
                var rawValue = spec.Value!.Trim();
                var normalizedValue = string.IsNullOrWhiteSpace(spec.NormalizedValue) ? Normalize(rawValue) : spec.NormalizedValue.Trim();
                var displayValue = string.IsNullOrWhiteSpace(spec.DisplayValue) ? rawValue : spec.DisplayValue.Trim();
                var sourceValueUrl = string.IsNullOrWhiteSpace(spec.SourceUrl) ? sourceUrl : spec.SourceUrl.Trim();

                return new SourceSpecClaim(
                    sourceName,
                    sourceValueUrl,
                    spec.IsOfficial ?? phone.IsOfficial ?? IsOfficialSource,
                    group,
                    key,
                    displayName,
                    rawValue,
                    normalizedValue,
                    displayValue,
                    string.IsNullOrWhiteSpace(spec.Unit) ? null : spec.Unit.Trim(),
                    spec.IsCritical,
                    ClampConfidence(spec.Confidence, spec.IsOfficial ?? phone.IsOfficial ?? IsOfficialSource),
                    spec.CollectedAt ?? DateTimeOffset.UtcNow);
            })
            .ToList() ?? [];
        var variants = phone.Variants?
            .Where(variant => !string.IsNullOrWhiteSpace(variant.Name))
            .Select(variant => new SourceVariantClaim(
                variant.Name!.Trim(),
                variant.RamGb,
                variant.StorageGb,
                string.IsNullOrWhiteSpace(variant.Color) ? null : variant.Color.Trim()))
            .ToList() ?? [];
        var benchmarks = phone.Benchmarks?
            .Where(benchmark => !string.IsNullOrWhiteSpace(benchmark.BenchmarkName) && benchmark.Score > 0)
            .Select(benchmark => new SourceBenchmarkClaim(
                benchmark.BenchmarkName!.Trim(),
                benchmark.Score,
                string.IsNullOrWhiteSpace(benchmark.SourceName) ? sourceName : benchmark.SourceName.Trim(),
                string.IsNullOrWhiteSpace(benchmark.SourceUrl) ? sourceUrl : benchmark.SourceUrl.Trim(),
                benchmark.RecordedAt ?? DateTimeOffset.UtcNow))
            .ToList() ?? [];

        return new SourcePhoneRecord(
            sourceName,
            sourceUrl,
            policyStatus,
            phone.RobotsAllowed ?? RobotsAllowed,
            phone.IsOfficial ?? IsOfficialSource,
            phone.BrandName.Trim(),
            string.IsNullOrWhiteSpace(phone.OfficialDomain) ? null : phone.OfficialDomain.Trim(),
            phone.ModelName.Trim(),
            string.IsNullOrWhiteSpace(phone.Summary) ? null : phone.Summary.Trim(),
            phone.ReleasedAt,
            phone.LaunchPriceUsd,
            string.IsNullOrWhiteSpace(phone.ImageUrl) ? null : phone.ImageUrl.Trim(),
            string.IsNullOrWhiteSpace(phone.ImageSourceUrl) ? null : phone.ImageSourceUrl.Trim(),
            specs,
            variants,
            benchmarks);
    }

    private static double ClampConfidence(double? value, bool isOfficial)
    {
        var baseline = value ?? (isOfficial ? 0.95 : 0.8);
        return Math.Round(Math.Clamp(baseline, 0.1, 0.99), 2);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
    }

    private static bool TryGetPhonesElement(JsonElement element, out JsonElement phonesElement)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "phones", StringComparison.OrdinalIgnoreCase))
            {
                phonesElement = property.Value;
                return true;
            }
        }

        phonesElement = default;
        return false;
    }

    private sealed record SeedPhoneRecordDto(
        string? SourceName,
        string? SourceUrl,
        string? PolicyStatus,
        bool? RobotsAllowed,
        bool? IsOfficial,
        string? BrandName,
        string? OfficialDomain,
        string? ModelName,
        string? Summary,
        DateTimeOffset? ReleasedAt,
        decimal? LaunchPriceUsd,
        string? ImageUrl,
        string? ImageSourceUrl,
        List<SeedSpecDto>? Specs,
        List<SeedVariantDto>? Variants,
        List<SeedBenchmarkDto>? Benchmarks);

    private sealed record SeedSpecDto(
        string? Group,
        string? Key,
        string? DisplayName,
        string? Value,
        string? NormalizedValue,
        string? DisplayValue,
        string? Unit,
        bool IsCritical,
        double? Confidence,
        bool? IsOfficial,
        string? SourceUrl,
        DateTimeOffset? CollectedAt);

    private sealed record SeedVariantDto(
        string? Name,
        int? RamGb,
        int? StorageGb,
        string? Color);

    private sealed record SeedBenchmarkDto(
        string? BenchmarkName,
        int Score,
        string? SourceName,
        string? SourceUrl,
        DateTimeOffset? RecordedAt);
}
