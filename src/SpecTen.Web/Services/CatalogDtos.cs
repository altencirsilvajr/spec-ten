using SpecTen.Web.Data;

namespace SpecTen.Web.Services;

public sealed record PhoneSearchResult(
    int Id,
    string Brand,
    string BrandSlug,
    string Name,
    string Slug,
    string? ImageUrl,
    string Tier,
    ClassificationTier TierKey,
    string? Chipset,
    string? Battery,
    string? Display,
    string? MainCamera,
    DateTimeOffset? ReleasedAt,
    decimal? LaunchPriceUsd,
    double MinConfidence,
    int SpecCount,
    bool HasFullCatalogEntry,
    bool IsPublicReady,
    string TrustLabel,
    string TrustSummary,
    string ReadinessNote,
    int SourceCount,
    bool HasOfficialSource,
    DateTimeOffset? UpdatedAt)
{
    public string DisplayBrand => PhoneNameFormatter.DisplayBrand(Brand, Name);
    public string FullName => PhoneNameFormatter.FullName(Brand, Name);
    public string ModelName => PhoneNameFormatter.ModelName(Brand, Name);
    public bool IsPartialCatalogEntry => HasFullCatalogEntry && !IsPublicReady;
    public string StateLabel => !HasFullCatalogEntry
        ? "Cobertura inicial"
        : IsPublicReady
            ? "Ficha completa"
            : "Ficha parcial";
    public string PrimaryBadgeLabel => IsPublicReady ? Tier : StateLabel;
}

public sealed record PhoneSuggestionDto(
    int Id,
    string Brand,
    string BrandSlug,
    string Name,
    string Slug,
    string Tier,
    string? Chipset,
    DateTimeOffset? ReleasedAt,
    bool HasFullCatalogEntry,
    bool IsPublicReady)
{
    public string DisplayBrand => PhoneNameFormatter.DisplayBrand(Brand, Name);
    public string FullName => PhoneNameFormatter.FullName(Brand, Name);
    public string ModelName => PhoneNameFormatter.ModelName(Brand, Name);
    public bool IsPartialCatalogEntry => HasFullCatalogEntry && !IsPublicReady;
    public string StateLabel => !HasFullCatalogEntry
        ? "Cobertura inicial"
        : IsPublicReady
            ? "Ficha completa"
            : "Ficha parcial";
    public string PrimaryBadgeLabel => IsPublicReady ? Tier : StateLabel;
}

public sealed record CatalogBrandOptionDto(string Name, string Slug, int Count);

public enum CatalogSortOption
{
    Relevance,
    Newest,
    Name,
    Confidence,
}

public sealed record PhoneDetailsDto(
    int Id,
    string Brand,
    string BrandSlug,
    string Name,
    string Slug,
    string? Summary,
    DateTimeOffset? ReleasedAt,
    decimal? LaunchPriceUsd,
    string? ImageUrl,
    string? ImageSourceUrl,
    ClassificationDto Classification,
    IReadOnlyList<PhoneVariantDto> Variants,
    IReadOnlyList<SpecGroupDto> SpecGroups,
    IReadOnlyList<BenchmarkDto> Benchmarks,
    double MinConfidence,
    bool HasFullCatalogEntry,
    bool IsPublicReady,
    string TrustLabel,
    string TrustSummary,
    string ReadinessNote,
    int SourceCount,
    bool HasOfficialSource,
    bool HasReviewFlags,
    string? AvailabilityNote,
    string? DiscoverySourceName,
    string? DiscoverySourceUrl,
    DateTimeOffset? UpdatedAt)
{
    public string DisplayBrand => PhoneNameFormatter.DisplayBrand(Brand, Name);
    public string FullName => PhoneNameFormatter.FullName(Brand, Name);
    public string ModelName => PhoneNameFormatter.ModelName(Brand, Name);
    public bool IsPartialCatalogEntry => HasFullCatalogEntry && !IsPublicReady;
    public string StateLabel => !HasFullCatalogEntry
        ? "Cobertura inicial"
        : IsPublicReady
            ? "Ficha completa"
            : "Ficha parcial";
    public string PrimaryBadgeLabel => IsPublicReady ? Classification.Label : StateLabel;
}

public sealed record PhoneVariantDto(string Name, int? RamGb, int? StorageGb, string? Color);

public sealed record SpecGroupDto(string Name, IReadOnlyList<SpecFactDto> Specs);

public sealed record SpecFactDto(
    string Key,
    string DisplayName,
    string DisplayValue,
    string? Unit,
    string SourceName,
    string? SourceUrl,
    double Confidence,
    SpecStatus Status,
    bool IsCritical,
    DateTimeOffset CollectedAt);

public sealed record BenchmarkDto(string Name, int Score, string SourceName, string? SourceUrl, DateTimeOffset RecordedAt);

public sealed record ClassificationDto(
    ClassificationTier Tier,
    string Label,
    int Score,
    string Basis,
    string Explanation);

public sealed record CompareResultDto(
    IReadOnlyList<PhoneDetailsDto> Phones,
    IReadOnlyList<CompareRowDto> Rows);

public sealed record ComparisonQuickStartDto(
    PhoneSearchResult Left,
    PhoneSearchResult Right,
    string Label,
    string Reason);

public sealed record CompareRowDto(
    string Group,
    string Key,
    string DisplayName,
    IReadOnlyDictionary<int, string> Values,
    int? WinnerPhoneId);

public sealed record CorrectionReportRequest(
    int PhoneModelId,
    string? FieldKey,
    string? ReporterEmail,
    string Message);

public sealed record SitemapPhoneDto(
    string BrandSlug,
    string Slug,
    DateTimeOffset UpdatedAt);
