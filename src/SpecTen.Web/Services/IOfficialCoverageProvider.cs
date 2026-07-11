using SpecTen.Web.Scraping;

namespace SpecTen.Web.Services;

public interface IOfficialCoverageProvider
{
    string Brand { get; }
    string BrandSlug { get; }

    Task<IReadOnlyList<CoveragePhoneResult>> SearchAsync(string? query, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<CoveragePhoneResult>> BrowseAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<CoverageBrandOption>> GetBrandOptionsAsync(CancellationToken cancellationToken);
    Task<CoveragePhoneResult?> GetBySlugAsync(string slug, CancellationToken cancellationToken);
    Task<SourcePhoneRecord?> FetchRecordAsync(string slug, CancellationToken cancellationToken);
}
