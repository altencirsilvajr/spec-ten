namespace SpecTen.Web.Scraping;

public interface IPhoneSourceAdapter
{
    string SourceName { get; }
    string PolicyStatus { get; }
    bool RobotsAllowed { get; }
    bool IsOfficialSource { get; }
    Task<IReadOnlyList<SourcePhoneRecord>> FetchAsync(CancellationToken cancellationToken);
}

public sealed record SourcePhoneRecord(
    string SourceName,
    string SourceUrl,
    string PolicyStatus,
    bool RobotsAllowed,
    bool IsOfficial,
    string BrandName,
    string? OfficialDomain,
    string ModelName,
    string? Summary,
    DateTimeOffset? ReleasedAt,
    decimal? LaunchPriceUsd,
    string? ImageUrl,
    string? ImageSourceUrl,
    IReadOnlyList<SourceSpecClaim> Specs,
    IReadOnlyList<SourceVariantClaim> Variants,
    IReadOnlyList<SourceBenchmarkClaim> Benchmarks);

public sealed record SourceSpecClaim(
    string SourceName,
    string? SourceUrl,
    bool IsOfficial,
    string Group,
    string Key,
    string DisplayName,
    string RawValue,
    string NormalizedValue,
    string DisplayValue,
    string? Unit,
    bool IsCritical,
    double Confidence,
    DateTimeOffset CollectedAt);

public sealed record SourceVariantClaim(
    string Name,
    int? RamGb,
    int? StorageGb,
    string? Color);

public sealed record SourceBenchmarkClaim(
    string BenchmarkName,
    int Score,
    string SourceName,
    string? SourceUrl,
    DateTimeOffset RecordedAt);
