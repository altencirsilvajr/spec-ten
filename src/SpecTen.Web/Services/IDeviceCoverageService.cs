namespace SpecTen.Web.Services;

public interface IDeviceCoverageService
{
    Task WarmupAsync(CancellationToken cancellationToken);
    Task<CoveragePhoneResult?> GetBySlugAsync(string brandSlug, string slug, CancellationToken cancellationToken);
    Task<IReadOnlyList<CoveragePhoneResult>> SearchAsync(string? query, string? brandSlug, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<CoveragePhoneResult>> BrowseByBrandAsync(string brandSlug, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<CoverageBrandOption>> GetBrandOptionsAsync(CancellationToken cancellationToken);
    Task<CoverageHydrationResult?> EnsureCatalogEntryAsync(string brandSlug, string slug, CancellationToken cancellationToken);
}

public sealed record CoverageBrandOption(
    string Name,
    string Slug,
    int Count);

public sealed record CoveragePhoneResult(
    string Brand,
    string BrandSlug,
    string Name,
    string Slug,
    string SourceName,
    string? SourceUrl);

public sealed record CoverageHydrationResult(
    int PhoneId,
    string BrandSlug,
    string Slug,
    string SourceName,
    string? SourceUrl);
