using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SpecTen.Web.Data;
using SpecTen.Web.Options;

namespace SpecTen.Web.Services;

public sealed partial class CatalogService(
    IDbContextFactory<CatalogDbContext> dbContextFactory,
    IDeviceCoverageService coverageService,
    IMemoryCache cache,
    IOptions<CoverageOptions> coverageOptions)
{
    private static readonly TimeSpan SearchCacheDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SuggestionCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BrandOptionsCacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan QuickStartCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RelatedPhonesCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PopularComparisonsCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PhoneDetailsCacheDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CompareCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SitemapCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DatabaseHealthCacheDuration = TimeSpan.FromSeconds(15);
    private const int PublicCoverageBrandLimit = 32;
    private static readonly IReadOnlyDictionary<string, int> GroupOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Mercado"] = 0,
        ["Performance"] = 1,
        ["Memoria"] = 2,
        ["Armazenamento"] = 3,
        ["Tela"] = 4,
        ["Camera"] = 5,
        ["Bateria"] = 6,
        ["Construcao"] = 7,
        ["Conectividade"] = 8,
        ["Software"] = 9,
    };

    private static readonly IReadOnlyDictionary<string, int> SpecOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["released_at"] = 0,
        ["launch_price"] = 1,
        ["chipset"] = 2,
        ["benchmark_score"] = 3,
        ["ram"] = 4,
        ["storage_base"] = 5,
        ["storage_options"] = 6,
        ["display_size"] = 7,
        ["display_type"] = 8,
        ["resolution"] = 9,
        ["refresh_rate"] = 10,
        ["main_camera"] = 11,
        ["ultrawide_camera"] = 12,
        ["telephoto_camera"] = 13,
        ["selfie_camera"] = 14,
        ["battery"] = 15,
        ["charging"] = 16,
        ["wireless_charging"] = 17,
        ["dimensions"] = 18,
        ["weight"] = 19,
        ["protection"] = 20,
        ["ip_rating"] = 21,
        ["sim"] = 22,
        ["wifi"] = 23,
        ["bluetooth"] = 24,
        ["nfc"] = 25,
        ["usb"] = 26,
        ["network"] = 27,
        ["os"] = 28,
    };
    private static readonly HashSet<string> ComparableNoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "5g",
        "4g",
        "lte",
        "wifi",
        "global",
        "row",
        "cn",
        "china",
        "emea",
        "latam",
        "uw",
        "edition",
        "version",
    };
    private readonly CoverageOptions _coverageOptions = coverageOptions.Value;

    public async Task<IReadOnlyList<PhoneSearchResult>> SearchAsync(
        string? query,
        ClassificationTier? tier,
        int limit,
        CancellationToken cancellationToken)
    {
        return await SearchAsync(query, tier, null, CatalogSortOption.Relevance, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<PhoneSearchResult>> SearchAsync(
        string? query,
        ClassificationTier? tier,
        string? brandSlug,
        CatalogSortOption sort,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var normalizedBrand = string.IsNullOrWhiteSpace(brandSlug) ? string.Empty : Slugger.Slugify(brandSlug);
        var cacheKey = $"catalog:search:{PhoneSearchText.Normalize(query)}:{tier?.ToString() ?? "all"}:{normalizedBrand}:{sort}:{limit}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SearchCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await SearchQuery(db).ToListAsync(cancellationToken);
            var localResults = BuildSearchResults(phones, query, tier, brandSlug, sort, Math.Max(limit, 80));
            var coverageResults = await SearchCoverageFallbackAsync(query, tier, brandSlug, limit, localResults, cancellationToken);

            if (await HydrateCoverageMatchesAsync(query, localResults, coverageResults, cancellationToken))
            {
                await using var refreshedDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                phones = await SearchQuery(refreshedDb).ToListAsync(cancellationToken);
                localResults = BuildSearchResults(phones, query, tier, brandSlug, sort, Math.Max(limit, 80));
                coverageResults = await SearchCoverageFallbackAsync(query, tier, brandSlug, limit, localResults, cancellationToken);
            }

            return (IReadOnlyList<PhoneSearchResult>)MergeSearchResults(localResults, coverageResults, query, limit);
        }) ?? [];
    }

    public async Task<IReadOnlyList<PhoneSearchResult>> SearchForLiveDiscoveryAsync(
        string? query,
        ClassificationTier? tier,
        string? brandSlug,
        CatalogSortOption sort,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var normalizedBrand = string.IsNullOrWhiteSpace(brandSlug) ? string.Empty : Slugger.Slugify(brandSlug);
        var cacheKey = $"catalog:live-search:{PhoneSearchText.Normalize(query)}:{tier?.ToString() ?? "all"}:{normalizedBrand}:{sort}:{limit}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SearchCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await SearchQuery(db).ToListAsync(cancellationToken);
            var localResults = BuildSearchResults(phones, query, tier, brandSlug, sort, Math.Max(limit, 80));
            var coverageResults = await SearchCoverageFallbackAsync(query, tier, brandSlug, limit, localResults, cancellationToken);

            return (IReadOnlyList<PhoneSearchResult>)MergeSearchResults(localResults, coverageResults, query, limit);
        }) ?? [];
    }

    public async Task<IReadOnlyList<PhoneSuggestionDto>> SuggestAsync(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var cacheKey = $"catalog:suggest:{PhoneSearchText.Normalize(query)}:{limit}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SuggestionCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await SearchQuery(db).ToListAsync(cancellationToken);
            var localResults = BuildSearchResults(phones, query, null, null, CatalogSortOption.Relevance, Math.Max(limit, 24));
            var coverageResults = await SearchCoverageFallbackAsync(query, null, null, limit, localResults, cancellationToken);

            if (await HydrateCoverageMatchesAsync(query, localResults, coverageResults, cancellationToken))
            {
                await using var refreshedDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                phones = await SearchQuery(refreshedDb).ToListAsync(cancellationToken);
                localResults = BuildSearchResults(phones, query, null, null, CatalogSortOption.Relevance, Math.Max(limit, 24));
                coverageResults = await SearchCoverageFallbackAsync(query, null, null, limit, localResults, cancellationToken);
            }

            var results = MergeSearchResults(localResults, coverageResults, query, limit);

            return (IReadOnlyList<PhoneSuggestionDto>)results
                .Where(result => result.HasFullCatalogEntry || IsSuggestionFriendlyCoverage(result))
                .Take(limit)
                .Select(result => new PhoneSuggestionDto(
                    result.Id,
                    result.Brand,
                    result.BrandSlug,
                    result.Name,
                    result.Slug,
                    result.Tier,
                    result.Chipset,
                    result.ReleasedAt,
                    result.HasFullCatalogEntry,
                    result.IsPublicReady))
                .ToList();
        }) ?? [];
    }

    public async Task<IReadOnlyList<PhoneSuggestionDto>> SuggestForLiveDiscoveryAsync(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var cacheKey = $"catalog:live-suggest:{PhoneSearchText.Normalize(query)}:{limit}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SuggestionCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await SearchQuery(db).ToListAsync(cancellationToken);
            var localResults = BuildSearchResults(phones, query, null, null, CatalogSortOption.Relevance, Math.Max(limit, 24));
            var coverageResults = await SearchCoverageFallbackAsync(query, null, null, limit, localResults, cancellationToken);
            var results = MergeSearchResults(localResults, coverageResults, query, limit);

            return (IReadOnlyList<PhoneSuggestionDto>)results
                .Where(result => result.HasFullCatalogEntry || IsSuggestionFriendlyCoverage(result))
                .Take(limit)
                .Select(result => new PhoneSuggestionDto(
                    result.Id,
                    result.Brand,
                    result.BrandSlug,
                    result.Name,
                    result.Slug,
                    result.Tier,
                    result.Chipset,
                    result.ReleasedAt,
                    result.HasFullCatalogEntry,
                    result.IsPublicReady))
                .ToList();
        }) ?? [];
    }

    public async Task<IReadOnlyList<CatalogBrandOptionDto>> GetBrandOptionsAsync(CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync("catalog:brand-options", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = BrandOptionsCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await db.PhoneModels
                .AsNoTracking()
                .Select(phone => new
                {
                    phone.Brand.Name,
                    phone.Brand.Slug,
                })
                .ToListAsync(cancellationToken);
            var localOptions = phones
                .GroupBy(phone => phone.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogBrandOptionDto(BrandNameFormatter.DisplayName(group.First().Name), group.Key, group.Count()))
                .ToList();
            var localSlugs = localOptions
                .Select(option => option.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var coverageOptions = (await coverageService.GetBrandOptionsAsync(cancellationToken))
                .Select(option => new CatalogBrandOptionDto(BrandNameFormatter.DisplayName(option.Name), option.Slug, option.Count))
                .Where(option => !localSlugs.Contains(option.Slug))
                .Where(IsDiscoverableCoverageBrandOption)
                .OrderByDescending(option => option.Count)
                .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .Take(PublicCoverageBrandLimit)
                .ToList();

            return (IReadOnlyList<CatalogBrandOptionDto>)localOptions
                .Concat(coverageOptions)
                .GroupBy(option => option.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(option => option.Count)
                    .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(brand => localSlugs.Contains(brand.Slug) ? 0 : 1)
                .ThenByDescending(brand => brand.Count)
                .ThenBy(brand => brand.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }) ?? [];
    }

    public async Task<IReadOnlyList<CatalogBrandOptionDto>> GetFeaturedBrandOptionsAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var cacheKey = $"catalog:featured-brand-options:{limit}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = BrandOptionsCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await db.PhoneModels
                .AsNoTracking()
                .Select(phone => new
                {
                    phone.Brand.Name,
                    phone.Brand.Slug,
                })
                .ToListAsync(cancellationToken);

            return (IReadOnlyList<CatalogBrandOptionDto>)phones
                .GroupBy(phone => phone.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogBrandOptionDto(BrandNameFormatter.DisplayName(group.First().Name), group.Key, group.Count()))
                .OrderByDescending(brand => brand.Count)
                .ThenBy(brand => brand.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }) ?? [];
    }

    public async Task<IReadOnlyList<PhoneSearchResult>> GetQuickStartPhonesAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var cacheKey = $"catalog:quick-start:{limit}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = QuickStartCacheDuration;

            var results = await SearchAsync(
                null,
                null,
                null,
                CatalogSortOption.Newest,
                Math.Max(limit * 2, 12),
                cancellationToken);

            return (IReadOnlyList<PhoneSearchResult>)results
                .Where(phone => phone.IsPublicReady)
                .Take(limit)
                .ToList();
        }) ?? [];
    }

    public async Task<IReadOnlyList<PhoneSearchResult>> GetRelatedPhonesAsync(
        int phoneId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var cacheKey = $"catalog:related:{phoneId}:{limit}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RelatedPhonesCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await SearchQuery(db).ToListAsync(cancellationToken);
            var current = phones.FirstOrDefault(phone => phone.Id == phoneId);
            if (current is null)
            {
                return Array.Empty<PhoneSearchResult>();
            }

            var currentTier = LatestTier(current);
            var currentChipset = current.Specs.FirstOrDefault(spec => spec.Key == "chipset")?.DisplayValue;

            return (IReadOnlyList<PhoneSearchResult>)phones
                .Where(phone => phone.Id != phoneId)
                .Select(phone =>
                {
                    var result = ToSearchResult(phone);
                    return new RelatedPhoneCandidate(
                        result,
                        RelatedScore(phone, current, currentTier, currentChipset),
                        PriceDistance(phone, current),
                        ReleaseDistance(phone, current));
                })
                .Where(candidate => candidate.Result.IsPublicReady)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.PriceDistance)
                .ThenBy(candidate => candidate.ReleaseDistanceDays)
                .ThenByDescending(candidate => candidate.Result.MinConfidence)
                .ThenByDescending(candidate => candidate.Result.ReleasedAt)
                .Take(limit)
                .Select(candidate => candidate.Result)
                .ToList();
        }) ?? [];
    }

    public async Task<IReadOnlyList<ComparisonQuickStartDto>> GetPopularComparisonsAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var cacheKey = $"catalog:popular-comparisons:{limit}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PopularComparisonsCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await SearchQuery(db).ToListAsync(cancellationToken);
            return BuildPopularComparisons(phones, limit);
        }) ?? [];
    }

    public async Task<PhoneDetailsDto?> GetPhoneBySlugAsync(
        string brandSlug,
        string slug,
        CancellationToken cancellationToken)
    {
        var normalizedBrand = Slugger.Slugify(brandSlug);
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var cacheKey = $"catalog:phone:slug:{normalizedBrand}:{normalizedSlug}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PhoneDetailsCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phone = await PhoneDetailsQuery(db)
                .FirstOrDefaultAsync(model => model.Brand.Slug == brandSlug && model.Slug == slug, cancellationToken);

            if (phone is not null)
            {
                var details = ToDetails(phone);
                if (!NeedsOnDemandRefresh(details))
                {
                    return details;
                }

                var refreshed = await coverageService.EnsureCatalogEntryAsync(brandSlug, slug, cancellationToken);
                if (refreshed is not null)
                {
                    await using var refreshedDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                    var refreshedPhone = await PhoneDetailsQuery(refreshedDb)
                        .FirstOrDefaultAsync(model => model.Id == refreshed.PhoneId, cancellationToken);

                    if (refreshedPhone is not null)
                    {
                        return ToDetails(refreshedPhone);
                    }
                }

                return details;
            }

            var hydrated = await coverageService.EnsureCatalogEntryAsync(brandSlug, slug, cancellationToken);
            if (hydrated is not null)
            {
                await using var hydratedDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var hydratedPhone = await PhoneDetailsQuery(hydratedDb)
                    .FirstOrDefaultAsync(model => model.Id == hydrated.PhoneId, cancellationToken);

                if (hydratedPhone is not null)
                {
                    return ToDetails(hydratedPhone);
                }
            }

            var coveragePhone = await coverageService.GetBySlugAsync(brandSlug, slug, cancellationToken);
            return coveragePhone is null ? null : ToCoverageDetails(coveragePhone);
        });
    }

    public async Task<PhoneDetailsDto?> GetPhoneByIdAsync(int id, CancellationToken cancellationToken)
    {
        var cacheKey = $"catalog:phone:id:{id}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PhoneDetailsCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phone = await PhoneDetailsQuery(db)
                .FirstOrDefaultAsync(model => model.Id == id, cancellationToken);

            return phone is null ? null : ToDetails(phone);
        });
    }

    public async Task<CompareResultDto> CompareAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        var cleanIds = ids.Where(id => id > 0).Distinct().Take(4).ToArray();
        if (cleanIds.Length == 0)
        {
            return new CompareResultDto([], []);
        }

        var cacheKey = $"catalog:compare:{string.Join(',', cleanIds)}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CompareCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await PhoneDetailsQuery(db)
                .Where(model => cleanIds.Contains(model.Id))
                .ToListAsync(cancellationToken);

            var orderedPhones = cleanIds
                .Select(id => phones.FirstOrDefault(phone => phone.Id == id))
                .Where(phone => phone is not null)
                .Cast<PhoneModel>()
                .Select(ToDetails)
                .Where(phone => phone.IsPublicReady)
                .ToList();

            var specsByPhone = orderedPhones.ToDictionary(
                phone => phone.Id,
                phone => phone.SpecGroups
                    .SelectMany(group => group.Specs.Select(spec => (group.Name, Spec: spec)))
                    .ToDictionary(item => item.Spec.Key, item => item, StringComparer.OrdinalIgnoreCase));

            var derivedRows = BuildDerivedComparisonRows(orderedPhones);
            var derivedKeys = derivedRows
                .Select(row => row.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var dataRows = specsByPhone.Values
                .SelectMany(map => map.Values)
                .GroupBy(item => item.Spec.Key, StringComparer.OrdinalIgnoreCase)
                .Where(group => !derivedKeys.Contains(group.Key))
                .Select(group =>
                {
                    var preferred = group
                        .OrderBy(item => SpecRank(item.Spec.Key))
                        .ThenBy(item => item.Spec.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .First();

                    var values = orderedPhones.ToDictionary(
                        phone => phone.Id,
                        phone => specsByPhone[phone.Id].TryGetValue(group.Key, out var spec)
                            ? spec.Spec.DisplayValue
                            : "-");

                    return new CompareRowDto(
                        preferred.Name,
                        preferred.Spec.Key,
                        preferred.Spec.DisplayName,
                        values,
                        ChooseWinner(preferred.Spec.Key, values));
                })
                .ToList();

            var rows = derivedRows
                .Concat(dataRows)
                .OrderBy(row => GroupRank(row.Group))
                .ThenBy(row => SpecRank(row.Key))
                .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new CompareResultDto(orderedPhones, rows);
        }) ?? new CompareResultDto([], []);
    }

    public async Task SubmitCorrectionReportAsync(CorrectionReportRequest request, CancellationToken cancellationToken)
    {
        var normalizedFieldKey = NormalizeReportFieldKey(request.FieldKey);
        var normalizedEmail = NormalizeReportEmail(request.ReporterEmail);
        var normalizedMessage = NormalizeReportMessage(request.Message);

        if (request.PhoneModelId <= 0 || string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new InvalidOperationException("Informe o aparelho e descreva o problema.");
        }

        if (normalizedMessage.Length < 8)
        {
            throw new InvalidOperationException("Descreva o problema com um pouco mais de detalhe.");
        }

        if (normalizedMessage.Length > 1000)
        {
            throw new InvalidOperationException("Mensagem muito longa. Resuma o problema em ate 1000 caracteres.");
        }

        if (normalizedFieldKey is { Length: > 80 })
        {
            throw new InvalidOperationException("Campo informado e invalido.");
        }

        if (normalizedEmail is { Length: > 240 } || (normalizedEmail is not null && !MailAddress.TryCreate(normalizedEmail, out _)))
        {
            throw new InvalidOperationException("Informe um email valido ou deixe esse campo em branco.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var phoneExists = await db.PhoneModels.AnyAsync(phone => phone.Id == request.PhoneModelId, cancellationToken);
        if (!phoneExists)
        {
            throw new InvalidOperationException("Aparelho nao encontrado.");
        }

        var duplicateCutoff = DateTimeOffset.UtcNow.AddHours(-6);
        var matchingReports = await db.CorrectionReports
            .AsNoTracking()
            .Where(report =>
                report.PhoneModelId == request.PhoneModelId &&
                report.FieldKey == normalizedFieldKey &&
                report.ReporterEmail == normalizedEmail &&
                report.Message == normalizedMessage)
            .Select(report => report.CreatedAt)
            .ToListAsync(cancellationToken);

        if (matchingReports.Any(createdAt => createdAt >= duplicateCutoff))
        {
            return;
        }

        db.CorrectionReports.Add(new CorrectionReport
        {
            PhoneModelId = request.PhoneModelId,
            FieldKey = normalizedFieldKey,
            ReporterEmail = normalizedEmail,
            Message = normalizedMessage,
            Status = ReviewStatus.Open,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SitemapPhoneDto>> GetSitemapPhonesAsync(CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync("catalog:sitemap-phones", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SitemapCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var phones = await SearchQuery(db)
                .ToListAsync(cancellationToken);

            return (IReadOnlyList<SitemapPhoneDto>)phones
                .Select(phone => new
                {
                    phone.Brand.Slug,
                    BrandName = phone.Brand.Name,
                    phone.Name,
                    phone.UpdatedAt,
                    Result = ToSearchResult(phone),
                })
                .Where(phone => phone.Result.IsPublicReady)
                .OrderByDescending(phone => phone.Result.ReleasedAt ?? DateTimeOffset.MinValue)
                .ThenBy(phone => phone.BrandName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(phone => phone.Name, StringComparer.OrdinalIgnoreCase)
                .Select(phone => new SitemapPhoneDto(phone.Slug, phone.Result.Slug, phone.UpdatedAt))
                .ToList();
        }) ?? [];
    }

    public async Task<bool> CanReachDatabaseAsync(CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync("catalog:db-connectivity", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = DatabaseHealthCacheDuration;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken);
        });
    }

    private static IQueryable<PhoneModel> SearchQuery(CatalogDbContext db)
    {
        return db.PhoneModels
            .AsNoTracking()
            .Include(phone => phone.Brand)
            .Include(phone => phone.Variants)
            .Include(phone => phone.Specs)
            .Include(phone => phone.Classifications)
            .AsSplitQuery();
    }

    private static string? NormalizeReportFieldKey(string? fieldKey)
        => string.IsNullOrWhiteSpace(fieldKey) ? null : fieldKey.Trim();

    private static string? NormalizeReportEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static string NormalizeReportMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return string.Join(" ", message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static IQueryable<PhoneModel> PhoneDetailsQuery(CatalogDbContext db)
    {
        return db.PhoneModels
            .AsNoTracking()
            .Include(phone => phone.Brand)
            .Include(phone => phone.Variants)
            .Include(phone => phone.Specs)
            .Include(phone => phone.Benchmarks)
            .Include(phone => phone.Classifications)
            .AsSplitQuery();
    }

    private async Task<IReadOnlyList<CoveragePhoneResult>> SearchCoverageFallbackAsync(
        string? query,
        ClassificationTier? tier,
        string? brandSlug,
        int limit,
        IReadOnlyList<PhoneSearchResult> localResults,
        CancellationToken cancellationToken)
    {
        if (tier is not null)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return string.IsNullOrWhiteSpace(brandSlug)
                ? []
                : await coverageService.BrowseByBrandAsync(brandSlug, limit, cancellationToken);
        }

        var shouldSearchCoverage = localResults.Count < limit ||
                                   ShouldExpandCoverageSearch(query, localResults);
        if (!shouldSearchCoverage)
        {
            return [];
        }

        return await coverageService.SearchAsync(query, brandSlug, limit, cancellationToken);
    }

    private async Task<bool> HydrateCoverageMatchesAsync(
        string? query,
        IReadOnlyList<PhoneSearchResult> localResults,
        IReadOnlyList<CoveragePhoneResult> coverageResults,
        CancellationToken cancellationToken)
    {
        if (!_coverageOptions.Enabled ||
            !_coverageOptions.OnDemandHydrationEnabled ||
            string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var exactHydrationLimit = Math.Max(1, _coverageOptions.ExactHydrationLimit);
        var candidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = localResults
            .Where(result => result.HasFullCatalogEntry &&
                             IsExactQueryMatch(result, query) &&
                             NeedsOnDemandRefresh(result))
            .Select(result => (result.BrandSlug, result.Slug))
            .Concat(coverageResults
                .Where(result => IsExactQueryMatch(ToCoverageSearchResult(result), query))
                .Select(result => (result.BrandSlug, result.Slug)))
            .Distinct()
            .Take(exactHydrationLimit)
            .ToList();

        foreach (var candidate in candidates)
        {
            candidateKeys.Add(BuildCoverageKey(candidate.BrandSlug, candidate.Slug));
        }

        if (ShouldHydrateTopCoverageMatches(query, localResults, coverageResults))
        {
            var additionalCoverageLimit = localResults.Count == 0 ? 3 : 2;
            foreach (var candidate in coverageResults
                         .Select(result => new
                         {
                             result.BrandSlug,
                             result.Slug,
                             Score = StrongQueryMatchScore(ToCoverageSearchResult(result), query),
                             IsCandidate = IsCoverageHydrationCandidate(result, query),
                         })
                         .Where(result => result.IsCandidate)
                         .OrderByDescending(result => result.Score)
                         .ThenBy(result => result.Slug, StringComparer.OrdinalIgnoreCase)
                         .Select(result => (result.BrandSlug, result.Slug))
                         .Distinct()
                         .Take(exactHydrationLimit + additionalCoverageLimit))
            {
                if (!candidateKeys.Add(BuildCoverageKey(candidate.BrandSlug, candidate.Slug)))
                {
                    continue;
                }

                candidates.Add(candidate);
                if (candidates.Count >= exactHydrationLimit + additionalCoverageLimit)
                {
                    break;
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var refreshedAny = false;
        foreach (var candidate in candidates)
        {
            if (await coverageService.EnsureCatalogEntryAsync(candidate.BrandSlug, candidate.Slug, cancellationToken) is not null)
            {
                refreshedAny = true;
            }
        }

        return refreshedAny;
    }

    private bool ShouldHydrateTopCoverageMatches(
        string query,
        IReadOnlyList<PhoneSearchResult> localResults,
        IReadOnlyList<CoveragePhoneResult> coverageResults)
    {
        if (coverageResults.Count == 0 || !LooksLikeSpecificDeviceQuery(query))
        {
            return false;
        }

        var hasReadyExactLocalMatch = localResults.Any(result =>
            result.HasFullCatalogEntry &&
            IsExactQueryMatch(result, query) &&
            !NeedsOnDemandRefresh(result));

        if (hasReadyExactLocalMatch)
        {
            return false;
        }

        var hasReadyStrongLocalMatch = localResults.Any(result =>
            result.HasFullCatalogEntry &&
            IsStrongQueryMatch(result, query) &&
            !NeedsOnDemandRefresh(result));
        var bestReadyLocalStrongScore = localResults
            .Where(result => result.HasFullCatalogEntry && !NeedsOnDemandRefresh(result))
            .Select(result => StrongQueryMatchScore(result, query))
            .DefaultIfEmpty(0)
            .Max();
        var bestCoverageStrongScore = coverageResults
            .Select(result => StrongQueryMatchScore(ToCoverageSearchResult(result), query))
            .DefaultIfEmpty(0)
            .Max();

        if (bestCoverageStrongScore > 0 && bestCoverageStrongScore > bestReadyLocalStrongScore)
        {
            return true;
        }

        if (hasReadyStrongLocalMatch)
        {
            return false;
        }

        return localResults.Count == 0 ||
               !localResults.Any(result => result.HasFullCatalogEntry) ||
               bestCoverageStrongScore > 0;
    }

    private static bool IsCoverageHydrationCandidate(CoveragePhoneResult result, string query)
    {
        var mapped = ToCoverageSearchResult(result);
        return IsExactQueryMatch(mapped, query) ||
               IsStrongQueryMatch(mapped, query) ||
               (LooksLikeSpecificDeviceQuery(query) && IsSuggestionFriendlyCoverage(mapped));
    }

    private static bool LooksLikeSpecificDeviceQuery(string query)
    {
        var tokens = PhoneSearchText.Tokenize(query);
        return tokens.Count >= 2 || query.Any(char.IsDigit);
    }

    private static string BuildCoverageKey(string brandSlug, string slug)
    {
        return $"{Slugger.Slugify(brandSlug)}:{Slugger.Slugify(slug)}";
    }

    private static IReadOnlyList<PhoneSearchResult> MergeSearchResults(
        IReadOnlyList<PhoneSearchResult> localResults,
        IReadOnlyList<CoveragePhoneResult> coverageResults,
        string? query,
        int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        var mappedCoverage = coverageResults
            .Where(coverage => !IsCoveredByFullCatalogResult(coverage, localResults))
            .Select(ToCoverageSearchResult)
            .ToList();
        var visiblePool = FocusSpecificQueryResults(
            localResults
                .Concat(mappedCoverage)
                .ToList(),
            query);
        var merged = new List<PhoneSearchResult>(limit);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shouldPromoteExactMatches = ShouldPromoteExactMatches(query);

        if (shouldPromoteExactMatches && query is not null)
        {
            foreach (var result in visiblePool)
            {
                if (!IsExactQueryMatch(result, query) ||
                    !seen.Add(BuildResultKey(result)))
                {
                    continue;
                }

                merged.Add(result);
                if (merged.Count >= limit)
                {
                    return merged;
                }
            }

            foreach (var result in visiblePool
                         .Select(result => new
                         {
                             Result = result,
                             Score = StrongQueryMatchScore(result, query),
                         })
                         .Where(item => item.Score > 0)
                         .OrderByDescending(item => item.Score)
                         .ThenByDescending(item => item.Result.IsPublicReady)
                         .ThenByDescending(item => item.Result.HasFullCatalogEntry)
                         .ThenByDescending(item => item.Result.ReleasedAt)
                         .ThenBy(item => item.Result.Brand, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Result.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(item => item.Result))
            {
                if (!seen.Add(BuildResultKey(result)))
                {
                    continue;
                }

                merged.Add(result);
                if (merged.Count >= limit)
                {
                    return merged;
                }
            }
        }

        foreach (var result in visiblePool)
        {
            if (seen.Add(BuildResultKey(result)))
            {
                merged.Add(result);
            }

            if (merged.Count >= limit)
            {
                return merged;
            }
        }

        return merged;
    }

    private static IReadOnlyList<PhoneSearchResult> FocusSpecificQueryResults(
        IReadOnlyList<PhoneSearchResult> results,
        string? query)
    {
        if (results.Count == 0 ||
            string.IsNullOrWhiteSpace(query) ||
            !ShouldPromoteExactMatches(query))
        {
            return results;
        }

        var focusedMatches = results
            .Where(result => IsFocusedSpecificMatch(result, query))
            .ToList();
        if (focusedMatches.Count > 0 && focusedMatches.Count != results.Count)
        {
            return OrderFocusedResults(focusedMatches, query);
        }

        var strongMatches = results
            .Where(result => StrongQueryMatchScore(result, query) > 0)
            .ToList();
        if (strongMatches.Count == 0 || strongMatches.Count == results.Count)
        {
            return results;
        }

        return OrderFocusedResults(strongMatches, query);
    }

    private static IReadOnlyList<PhoneSearchResult> OrderFocusedResults(
        IReadOnlyList<PhoneSearchResult> results,
        string query)
    {
        return results
            .OrderByDescending(result => IsExactQueryMatch(result, query))
            .ThenByDescending(result => result.IsPublicReady)
            .ThenByDescending(result => result.HasFullCatalogEntry)
            .ThenByDescending(result => result.ReleasedAt)
            .ThenBy(result => result.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsFocusedSpecificMatch(PhoneSearchResult result, string query)
    {
        if (IsExactQueryMatch(result, query))
        {
            return true;
        }

        var queryTokens = PhoneSearchText.Tokenize(query)
            .Where(token => !PhoneSearchText.IsSearchNoiseToken(token))
            .ToList();
        if (queryTokens.Count == 0)
        {
            return false;
        }

        foreach (var candidateTokens in BuildFocusedCandidateTokenSets(result))
        {
            if (MatchesFocusedTokenSequence(candidateTokens, queryTokens))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IReadOnlyList<string>> BuildFocusedCandidateTokenSets(PhoneSearchResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[]
                 {
                     result.ModelName,
                     result.Name,
                     result.FullName,
                     StripLeadingBrandRaw(result.ModelName, result.Brand),
                     StripLeadingBrandRaw(result.Name, result.Brand),
                     StripLeadingBrandRaw(result.FullName, result.Brand),
                 })
        {
            var tokens = PhoneSearchText.Tokenize(value)
                .Where(token => !PhoneSearchText.IsSearchNoiseToken(token))
                .ToList();
            if (tokens.Count == 0)
            {
                continue;
            }

            var key = string.Join('|', tokens);
            if (seen.Add(key))
            {
                yield return tokens;
            }
        }
    }

    private static bool MatchesFocusedTokenSequence(
        IReadOnlyList<string> candidateTokens,
        IReadOnlyList<string> queryTokens)
    {
        if (candidateTokens.Count < queryTokens.Count)
        {
            return false;
        }

        for (var start = 0; start <= candidateTokens.Count - queryTokens.Count; start++)
        {
            var matches = true;
            for (var index = 0; index < queryTokens.Count; index++)
            {
                var candidateToken = candidateTokens[start + index];
                var queryToken = queryTokens[index];
                if (!IsFocusedTokenMatch(candidateToken, queryToken))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFocusedTokenMatch(string candidateToken, string queryToken)
    {
        if (candidateToken.Equals(queryToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (queryToken.All(char.IsDigit))
        {
            return candidateToken.StartsWith(queryToken, StringComparison.OrdinalIgnoreCase);
        }

        if (queryToken.Length <= 2 ||
            (queryToken.Any(char.IsLetter) && queryToken.Any(char.IsDigit)))
        {
            return false;
        }

        return candidateToken.StartsWith(queryToken, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<PhoneSearchResult> BuildSearchResults(
        IReadOnlyList<PhoneModel> phones,
        string? query,
        ClassificationTier? tier,
        string? brandSlug,
        CatalogSortOption sort,
        int limit)
    {
        var effectiveSort = sort == CatalogSortOption.Relevance && string.IsNullOrWhiteSpace(query)
            ? CatalogSortOption.Newest
            : sort;
        var normalizedBrand = string.IsNullOrWhiteSpace(brandSlug) ? string.Empty : Slugger.Slugify(brandSlug);

        var candidates = phones
            .Where(phone => normalizedBrand.Length == 0 || phone.Brand.Slug.Equals(normalizedBrand, StringComparison.OrdinalIgnoreCase))
            .Select(phone =>
            {
                var result = ToSearchResult(phone);
                return new SearchCandidate(phone, result, MatchPhone(phone, query));
            })
            .Where(candidate => tier is null || candidate.Result.TierKey == tier.Value)
            .Where(candidate => string.IsNullOrWhiteSpace(query) || candidate.Match is not null)
            .ToList();

        var filtered = OrderSearchResults(candidates, effectiveSort)
            .Take(limit)
            .Select(candidate => candidate.Result)
            .ToList();

        return filtered;
    }

    private static IOrderedEnumerable<SearchCandidate> OrderSearchResults(
        IEnumerable<SearchCandidate> candidates,
        CatalogSortOption sort)
    {
        return sort switch
        {
            CatalogSortOption.Name => candidates
                .OrderByDescending(candidate => candidate.Result.IsPublicReady)
                .ThenBy(candidate => candidate.Result.Brand, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Result.Name, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(candidate => candidate.Result.ReleasedAt),
            CatalogSortOption.Confidence => candidates
                .OrderByDescending(candidate => candidate.Result.IsPublicReady)
                .ThenByDescending(candidate => candidate.Result.MinConfidence)
                .ThenByDescending(candidate => candidate.Result.SpecCount)
                .ThenByDescending(candidate => candidate.Result.ReleasedAt)
                .ThenBy(candidate => candidate.Result.Brand, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Result.Name, StringComparer.OrdinalIgnoreCase),
            CatalogSortOption.Newest => candidates
                .OrderByDescending(candidate => candidate.Result.IsPublicReady)
                .ThenByDescending(candidate => candidate.Result.ReleasedAt)
                .ThenByDescending(candidate => candidate.Match?.Score ?? 0)
                .ThenBy(candidate => candidate.Result.Brand, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Result.Name, StringComparer.OrdinalIgnoreCase),
            _ => candidates
                .OrderByDescending(candidate => candidate.Result.IsPublicReady)
                .ThenByDescending(candidate => candidate.Match?.Score ?? 0)
                .ThenByDescending(candidate => candidate.Result.ReleasedAt)
                .ThenBy(candidate => candidate.Result.Brand, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Result.Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static int RelatedScore(
        PhoneModel candidate,
        PhoneModel current,
        ClassificationTier currentTier,
        string? currentChipset)
    {
        var score = 0;

        if (candidate.Brand.Slug.Equals(current.Brand.Slug, StringComparison.OrdinalIgnoreCase))
        {
            score += 160;
        }

        if (LatestTier(candidate) == currentTier && currentTier != ClassificationTier.Undefined)
        {
            score += 90;
        }
        else if (currentTier != ClassificationTier.Undefined)
        {
            score -= 60;
        }

        if (PriceDistance(candidate, current) <= 150)
        {
            score += 45;
        }
        else if (PriceDistance(candidate, current) <= 300)
        {
            score += 24;
        }

        if (ReleaseDistance(candidate, current) <= 180)
        {
            score += 24;
        }

        var candidateChipset = candidate.Specs.FirstOrDefault(spec => spec.Key == "chipset")?.DisplayValue;
        if (SameChipVendor(currentChipset, candidateChipset))
        {
            score += 18;
        }

        return score;
    }

    private static decimal PriceDistance(PhoneModel phone, PhoneModel current)
    {
        if (phone.LaunchPriceUsd is null || current.LaunchPriceUsd is null)
        {
            return decimal.MaxValue;
        }

        return Math.Abs(phone.LaunchPriceUsd.Value - current.LaunchPriceUsd.Value);
    }

    private static int ReleaseDistance(PhoneModel phone, PhoneModel current)
    {
        if (phone.ReleasedAt is null || current.ReleasedAt is null)
        {
            return int.MaxValue;
        }

        return (int)Math.Abs((phone.ReleasedAt.Value - current.ReleasedAt.Value).TotalDays);
    }

    private static IReadOnlyList<ComparisonQuickStartDto> BuildPopularComparisons(
        IReadOnlyList<PhoneModel> phones,
        int limit)
    {
        var publishedPhones = phones
            .Where(phone => ToSearchResult(phone).IsPublicReady)
            .ToList();
        if (publishedPhones.Count < 2)
        {
            return [];
        }

        var resultsById = publishedPhones.ToDictionary(phone => phone.Id, ToSearchResult);
        var recentPhones = publishedPhones
            .OrderByDescending(phone => resultsById[phone.Id].ReleasedAt)
            .ThenByDescending(phone => resultsById[phone.Id].MinConfidence)
            .ThenBy(phone => phone.Brand.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(phone => phone.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(limit * 4, 12))
            .ToList();

        var comparisons = new List<ComparisonQuickStartDto>(limit);
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var current in recentPhones)
        {
            var currentResult = resultsById[current.Id];
            var currentTier = LatestTier(current);
            var currentChipset = current.Specs.FirstOrDefault(spec => spec.Key == "chipset")?.DisplayValue;

            var related = publishedPhones
                .Where(phone => phone.Id != current.Id)
                .Select(phone =>
                {
                    var result = resultsById[phone.Id];
                    return new RelatedPhoneCandidate(
                        result,
                        RelatedScore(phone, current, currentTier, currentChipset),
                        PriceDistance(phone, current),
                        ReleaseDistance(phone, current));
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.PriceDistance)
                .ThenBy(candidate => candidate.ReleaseDistanceDays)
                .ThenByDescending(candidate => candidate.Result.MinConfidence)
                .ThenByDescending(candidate => candidate.Result.ReleasedAt)
                .FirstOrDefault();

            if (related is null)
            {
                continue;
            }

            var pairKey = BuildPairKey(currentResult.Id, related.Result.Id);
            if (!seenPairs.Add(pairKey))
            {
                continue;
            }

            comparisons.Add(new ComparisonQuickStartDto(
                currentResult,
                related.Result,
                BuildComparisonLabel(currentResult, related.Result),
                BuildComparisonReason(currentResult, related.Result)));

            if (comparisons.Count >= limit)
            {
                break;
            }
        }

        return comparisons;
    }

    private static string BuildPairKey(int leftId, int rightId)
    {
        return leftId < rightId
            ? $"{leftId}:{rightId}"
            : $"{rightId}:{leftId}";
    }

    private static string BuildComparisonLabel(PhoneSearchResult left, PhoneSearchResult right)
    {
        if (left.Brand.Equals(right.Brand, StringComparison.OrdinalIgnoreCase))
        {
            return $"{left.Tier} da mesma marca";
        }

        if (left.Tier.Equals(right.Tier, StringComparison.OrdinalIgnoreCase))
        {
            return $"{left.Tier} na mesma faixa";
        }

        return "Comparacao pronta";
    }

    private static string BuildComparisonReason(PhoneSearchResult left, PhoneSearchResult right)
    {
        if (left.Brand.Equals(right.Brand, StringComparison.OrdinalIgnoreCase))
        {
            return "Mesma marca, geracao parecida e diferencas claras de tela, bateria e desempenho.";
        }

        if (left.Tier.Equals(right.Tier, StringComparison.OrdinalIgnoreCase))
        {
            return "Dois aparelhos da mesma faixa para decidir sem depender de tabela confusa.";
        }

        return "Bom atalho para comparar propostas diferentes que brigam pelo mesmo bolso.";
    }

    private static bool SameChipVendor(string? left, string? right)
    {
        var leftVendor = ChipVendor(left);
        var rightVendor = ChipVendor(right);
        return leftVendor.Length > 0 && leftVendor.Equals(rightVendor, StringComparison.Ordinal);
    }

    private static string ChipVendor(string? chipset)
    {
        var normalized = chipset?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            var value when value.Contains("snapdragon", StringComparison.Ordinal) => "snapdragon",
            var value when value.Contains("dimensity", StringComparison.Ordinal) => "dimensity",
            var value when value.Contains("apple", StringComparison.Ordinal) => "apple",
            var value when value.Contains("exynos", StringComparison.Ordinal) => "exynos",
            var value when value.Contains("helio", StringComparison.Ordinal) => "helio",
            _ => string.Empty,
        };
    }

    private static SearchMatch? MatchPhone(PhoneModel phone, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalizedQuery = PhoneSearchText.Normalize(query);
        var tokens = PhoneSearchText.Tokenize(query);
        if (normalizedQuery.Length == 0 || tokens.Count == 0)
        {
            return null;
        }

        var primaryFields = GetPrimarySearchFields(phone);
        var secondaryFields = GetSecondarySearchFields(phone);
        var fullName = PhoneSearchText.Normalize($"{phone.Brand.Name} {phone.Name}");
        var phoneName = PhoneSearchText.Normalize(phone.Name);
        var brand = PhoneSearchText.Normalize(phone.Brand.Name);
        var comparableQuery = NormalizeComparableName(query);
        var comparablePhoneName = NormalizeComparableName(phone.Name);
        var comparableFullName = NormalizeComparableName($"{phone.Brand.Name} {phone.Name}");

        var score = 0;

        if (phoneName == normalizedQuery || fullName == normalizedQuery)
        {
            score += 420;
        }
        else if (comparableQuery.Length > 0 &&
                 (comparablePhoneName == comparableQuery || comparableFullName == comparableQuery))
        {
            score += 380;
        }
        else if (phoneName.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
                 fullName.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            score += 320;
        }
        else if (phoneName.Contains(normalizedQuery, StringComparison.Ordinal) ||
                 fullName.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            score += 240;
        }
        else if (brand == normalizedQuery)
        {
            score += 180;
        }

        foreach (var token in tokens)
        {
            if (PhoneSearchText.IsSearchNoiseToken(token))
            {
                continue;
            }

            var comparableToken = NormalizeComparableName(token);
            var tokenScore = EvaluateTokenMatchScore(
                primaryFields,
                token,
                comparableToken,
                exactScore: 110,
                startsWithScore: 85,
                containsScore: 52);

            if (tokenScore == 0 && AllowsSecondarySearch(token))
            {
                tokenScore = EvaluateTokenMatchScore(
                    secondaryFields,
                    token,
                    comparableToken,
                    exactScore: 76,
                    startsWithScore: 56,
                    containsScore: 34);
            }

            if (tokenScore == 0)
            {
                return null;
            }

            score += tokenScore;
        }

        return new SearchMatch(score);
    }

    private static bool ShouldExpandCoverageSearch(string query, IReadOnlyList<PhoneSearchResult> localResults)
    {
        var tokens = PhoneSearchText.Tokenize(query);
        if (tokens.Count < 2)
        {
            return false;
        }

        return !localResults.Any(result => IsExactQueryMatch(result, query));
    }

    private static bool IsExactQueryMatch(PhoneSearchResult result, string query)
    {
        var queryForms = BuildExactMatchForms(result.Brand, query);
        if (queryForms.Count == 0)
        {
            return false;
        }

        var candidateForms = BuildExactMatchForms(
            result.Brand,
            result.ModelName,
            result.Name,
            result.FullName);

        return candidateForms.Overlaps(queryForms);
    }

    private static bool IsStrongQueryMatch(PhoneSearchResult result, string query)
    {
        return StrongQueryMatchScore(result, query) > 0;
    }

    private static int StrongQueryMatchScore(PhoneSearchResult result, string query)
    {
        if (IsExactQueryMatch(result, query))
        {
            return 10_000;
        }

        var queryForms = BuildStrongMatchForms(result.Brand, query)
            .Where(form => form.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (queryForms.Count == 0)
        {
            return 0;
        }

        var candidateForms = BuildStrongMatchForms(
            result.Brand,
            result.ModelName,
            result.Name,
            result.FullName)
            .Where(form => form.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bestScore = 0;
        foreach (var queryForm in queryForms)
        {
            foreach (var candidate in candidateForms)
            {
                if (PhoneSearchText.IsCompactModelToken(queryForm))
                {
                    if (candidate.StartsWith(queryForm, StringComparison.OrdinalIgnoreCase))
                    {
                        var overflow = Math.Max(0, candidate.Length - queryForm.Length);
                        if (overflow <= 2)
                        {
                            bestScore = Math.Max(bestScore, 3_800 - (overflow * 420));
                        }
                    }

                    continue;
                }

                if (candidate.StartsWith(queryForm, StringComparison.OrdinalIgnoreCase))
                {
                    var overflow = Math.Max(0, candidate.Length - queryForm.Length);
                    bestScore = Math.Max(bestScore, 4_000 - Math.Min(overflow, 1_500));
                    continue;
                }

                var containsIndex = candidate.IndexOf(queryForm, StringComparison.OrdinalIgnoreCase);
                if (containsIndex >= 0)
                {
                    var overflow = Math.Max(0, candidate.Length - queryForm.Length);
                    bestScore = Math.Max(bestScore, 2_200 - Math.Min(overflow, 900) - Math.Min(containsIndex, 300));
                }
            }
        }

        return bestScore;
    }

    private static bool ShouldPromoteExactMatches(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var tokens = PhoneSearchText.Tokenize(query);
        if (tokens.Count >= 2)
        {
            return true;
        }

        return query.Any(char.IsDigit);
    }

    private static string StripLeadingBrand(string value, string normalizedBrand)
    {
        if (value.Length == 0 || normalizedBrand.Length == 0)
        {
            return value;
        }

        return value.StartsWith(normalizedBrand, StringComparison.OrdinalIgnoreCase)
            ? value[normalizedBrand.Length..]
            : value;
    }

    private static HashSet<string> GetPrimarySearchFields(PhoneModel phone)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal)
        {
            PhoneSearchText.Normalize(phone.Brand.Name),
            PhoneSearchText.Normalize(phone.Name),
            PhoneSearchText.Normalize($"{phone.Brand.Name} {phone.Name}"),
            NormalizeComparableName(phone.Name),
            NormalizeComparableName($"{phone.Brand.Name} {phone.Name}"),
            PhoneSearchText.Normalize(PhoneClassifier.LabelFor(LatestTier(phone))),
        };

        foreach (var variant in phone.Variants)
        {
            fields.Add(PhoneSearchText.Normalize(variant.Name));
            if (variant.StorageGb is not null)
            {
                fields.Add(PhoneSearchText.Normalize($"{variant.StorageGb} gb"));
            }
        }

        AddTokenForms(fields, phone.Name);
        AddTokenForms(fields, $"{phone.Brand.Name} {phone.Name}");
        AddTokenForms(fields, StripLeadingBrandRaw(phone.Name, phone.Brand.Name));

        fields.RemoveWhere(string.IsNullOrWhiteSpace);
        return fields;
    }

    private static HashSet<string> GetSecondarySearchFields(PhoneModel phone)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var spec in phone.Specs)
        {
            fields.Add(PhoneSearchText.Normalize(spec.DisplayName));
            fields.Add(PhoneSearchText.Normalize(spec.DisplayValue));
        }

        fields.RemoveWhere(string.IsNullOrWhiteSpace);
        return fields;
    }

    private static int EvaluateTokenMatchScore(
        IEnumerable<string> fields,
        string token,
        string comparableToken,
        int exactScore,
        int startsWithScore,
        int containsScore)
    {
        var score = 0;
        var compactModelToken = PhoneSearchText.IsCompactModelToken(token) ||
                                PhoneSearchText.IsCompactModelToken(comparableToken);

        foreach (var field in fields)
        {
            if (field == token || (comparableToken.Length > 0 && field == comparableToken))
            {
                score = Math.Max(score, exactScore);
            }
            else if (compactModelToken)
            {
                if ((field.StartsWith(token, StringComparison.Ordinal) && field.Length - token.Length <= 2) ||
                    (comparableToken.Length > 0 &&
                     field.StartsWith(comparableToken, StringComparison.Ordinal) &&
                     field.Length - comparableToken.Length <= 2))
                {
                    score = Math.Max(score, startsWithScore);
                }
            }
            else if (field.StartsWith(token, StringComparison.Ordinal) ||
                     (comparableToken.Length > 0 && field.StartsWith(comparableToken, StringComparison.Ordinal)))
            {
                score = Math.Max(score, startsWithScore);
            }
            else if (field.Contains(token, StringComparison.Ordinal) ||
                     (comparableToken.Length > 0 && field.Contains(comparableToken, StringComparison.Ordinal)))
            {
                score = Math.Max(score, containsScore);
            }
        }

        return score;
    }

    private static bool AllowsSecondarySearch(string token)
    {
        return token.Length >= 3 ||
               (token.All(char.IsDigit) && token.Length >= 2);
    }

    private static ClassificationTier LatestTier(PhoneModel phone)
    {
        return phone.Classifications
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .FirstOrDefault()?.Tier ?? ClassificationTier.Undefined;
    }

    private static int GroupRank(string group)
    {
        return GroupOrder.TryGetValue(group, out var order) ? order : int.MaxValue;
    }

    private static int SpecRank(string key)
    {
        return SpecOrder.TryGetValue(key, out var order) ? order : int.MaxValue;
    }

    private static PhoneSearchResult ToSearchResult(PhoneModel phone)
    {
        var details = ToDetails(phone);
        var allSpecs = details.SpecGroups.SelectMany(group => group.Specs).ToList();
        var chipset = allSpecs.FirstOrDefault(spec => spec.Key == "chipset")?.DisplayValue;
        if (LooksLikeSuspiciousChipsetValue(chipset))
        {
            chipset = null;
        }

        return new PhoneSearchResult(
            phone.Id,
            BrandNameFormatter.DisplayName(phone.Brand.Name),
            phone.Brand.Slug,
            phone.Name,
            phone.Slug,
            phone.ImageUrl,
            details.Classification.Label,
            details.Classification.Tier,
            chipset,
            allSpecs.FirstOrDefault(spec => spec.Key == "battery")?.DisplayValue,
            allSpecs.FirstOrDefault(spec => spec.Key == "display_size")?.DisplayValue,
            allSpecs.FirstOrDefault(spec => spec.Key == "main_camera")?.DisplayValue,
            phone.ReleasedAt,
            phone.LaunchPriceUsd,
            details.MinConfidence,
            allSpecs.Count,
            true,
            details.IsPublicReady,
            details.TrustLabel,
            details.TrustSummary,
            details.ReadinessNote,
            details.SourceCount,
            details.HasOfficialSource,
            phone.UpdatedAt);
    }

    private bool NeedsOnDemandRefresh(PhoneSearchResult result)
    {
        return result.HasFullCatalogEntry &&
               (!result.IsPublicReady ||
                result.ReleasedAt is null ||
                IsCatalogEntryStale(result.UpdatedAt) ||
                LooksLikeSuspiciousChipsetValue(result.Chipset) ||
                HasMissingCriticalSearchSummary(result));
    }

    private bool NeedsOnDemandRefresh(PhoneDetailsDto details)
    {
        var valuesByKey = details.SpecGroups
            .SelectMany(group => group.Specs)
            .GroupBy(spec => spec.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().DisplayValue,
                StringComparer.OrdinalIgnoreCase);
        valuesByKey.TryGetValue("chipset", out var chipset);

        return details.HasFullCatalogEntry &&
               (!details.IsPublicReady ||
                details.ReleasedAt is null ||
                IsCatalogEntryStale(details.UpdatedAt) ||
                LooksLikeSuspiciousChipsetValue(chipset) ||
                HasMissingCriticalDetailSpecs(valuesByKey));
    }

    private bool IsCatalogEntryStale(DateTimeOffset? updatedAt)
    {
        if (updatedAt is null)
        {
            return true;
        }

        var refreshHours = Math.Max(1, _coverageOptions.CatalogEntryRefreshHours);
        return updatedAt.Value <= DateTimeOffset.UtcNow.AddHours(-refreshHours);
    }

    private static bool HasMissingCriticalSearchSummary(PhoneSearchResult result)
    {
        return IsMissingMeaningfulSpec(result.Chipset) ||
               IsMissingMeaningfulSpec(result.Battery) ||
               IsMissingMeaningfulSpec(result.Display) ||
               IsMissingMeaningfulSpec(result.MainCamera);
    }

    private static bool HasMissingCriticalDetailSpecs(IReadOnlyDictionary<string, string> valuesByKey)
    {
        return CriticalRefreshKeys.Any(key =>
            !valuesByKey.TryGetValue(key, out var value) ||
            IsMissingMeaningfulSpec(value));
    }

    private static bool IsMissingMeaningfulSpec(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim() is "-" or "?" or "N/A" or "n/a";
    }

    private static bool LooksLikeSuspiciousChipsetValue(string? chipset)
        => ChipsetText.IsSuspicious(chipset);

    private static readonly string[] CriticalRefreshKeys =
    [
        "chipset",
        "ram",
        "storage_base",
        "display_size",
        "main_camera",
        "battery",
        "os",
    ];

    private static PhoneSearchResult ToCoverageSearchResult(CoveragePhoneResult coverage)
    {
        return new PhoneSearchResult(
            CoverageId(coverage.BrandSlug, coverage.Slug),
            BrandNameFormatter.DisplayName(coverage.Brand),
            coverage.BrandSlug,
            coverage.Name,
            coverage.Slug,
            null,
            "Cobertura inicial",
            ClassificationTier.Undefined,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            false,
            false,
            "Cobertura inicial",
            "Modelo encontrado na busca ampla e ainda sem consolidacao publica de fontes.",
            "A ficha completa ainda esta em preparacao; por isso o resultado entra como cobertura inicial.",
            0,
            false,
            null);
    }

    private static PhoneDetailsDto ToDetails(PhoneModel phone)
    {
        var specGroups = phone.Specs
            .OrderBy(spec => GroupRank(spec.Group))
            .ThenBy(spec => SpecRank(spec.Key))
            .ThenBy(spec => spec.DisplayName, StringComparer.OrdinalIgnoreCase)
            .GroupBy(spec => spec.Group)
            .Select(group => new SpecGroupDto(
                group.Key,
                group.Select(spec => new SpecFactDto(
                        spec.Key,
                        spec.DisplayName,
                        spec.DisplayValue,
                        spec.Unit,
                        spec.SourceName,
                        spec.SourceUrl,
                        spec.Confidence,
                        spec.Status,
                        spec.IsCritical,
                        spec.CollectedAt))
                    .ToList()))
            .ToList();

        var allSpecs = specGroups.SelectMany(group => group.Specs).ToList();
        var minConfidence = allSpecs.Count == 0 ? 0 : allSpecs.Min(spec => spec.Confidence);
        var readiness = CatalogReadiness.Evaluate(phone.ImageUrl, phone.ReleasedAt, specGroups);
        var latestClass = phone.Classifications
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .FirstOrDefault();
        var classification = BuildDisplayClassification(latestClass, readiness);

        return new PhoneDetailsDto(
            phone.Id,
            BrandNameFormatter.DisplayName(phone.Brand.Name),
            phone.Brand.Slug,
            phone.Name,
            phone.Slug,
            phone.Summary,
            phone.ReleasedAt,
            phone.LaunchPriceUsd,
            phone.ImageUrl,
            phone.ImageSourceUrl,
            classification,
            phone.Variants
                .OrderBy(variant => variant.StorageGb)
                .ThenBy(variant => variant.RamGb)
                .Select(variant => new PhoneVariantDto(variant.Name, variant.RamGb, variant.StorageGb, variant.Color))
                .ToList(),
            specGroups,
            phone.Benchmarks
                .OrderByDescending(score => score.Score)
                .Select(score => new BenchmarkDto(score.BenchmarkName, score.Score, score.SourceName, score.SourceUrl, score.RecordedAt))
                .ToList(),
            Math.Round(minConfidence, 2),
            true,
            readiness.IsPublicReady,
            readiness.TrustLabel,
            readiness.TrustSummary,
            readiness.ReadinessNote,
            readiness.SourceCount,
            readiness.HasOfficialSource,
            readiness.HasReviewFlags,
            null,
            null,
            null,
            phone.UpdatedAt);
    }

    private static PhoneDetailsDto ToCoverageDetails(CoveragePhoneResult coverage)
    {
        return new PhoneDetailsDto(
            CoverageId(coverage.BrandSlug, coverage.Slug),
            BrandNameFormatter.DisplayName(coverage.Brand),
            coverage.BrandSlug,
            coverage.Name,
            coverage.Slug,
            $"{PhoneNameFormatter.FullName(BrandNameFormatter.DisplayName(coverage.Brand), coverage.Name)} foi encontrado na busca ampla do catalogo, mas ainda esta sem ficha tecnica consolidada.",
            null,
            null,
            null,
            null,
            new ClassificationDto(
                ClassificationTier.Undefined,
                "Cobertura inicial",
                0,
                "coverage-only",
                "Modelo encontrado numa base ampla de dispositivos. Ainda estamos consolidando specs verificadas, imagens e classificacao."),
            [],
            [],
            [],
            0,
            false,
            false,
            "Cobertura inicial",
            "Modelo localizado em base ampla, ainda sem consolidacao de fontes publicas.",
            "Ainda estamos consolidando specs verificadas, imagem e classificacao para esta ficha.",
            0,
            false,
            false,
            "Este modelo ja aparece na busca publica, mas a ficha completa com specs verificadas ainda esta em preparacao.",
            coverage.SourceName,
            coverage.SourceUrl,
            null);
    }

    private static ClassificationDto BuildDisplayClassification(
        ClassificationSnapshot? latestClass,
        CatalogReadinessEvaluation readiness)
    {
        if (latestClass is not null && latestClass.Tier != ClassificationTier.Undefined)
        {
            return new ClassificationDto(
                latestClass.Tier,
                PhoneClassifier.LabelFor(latestClass.Tier),
                latestClass.Score,
                latestClass.Basis,
                latestClass.Explanation);
        }

        if (readiness.UsesEntryFallbackTier)
        {
            return new ClassificationDto(
                ClassificationTier.Entry,
                PhoneClassifier.LabelFor(ClassificationTier.Entry),
                15,
                "legacy-profile",
                "Aparelho basico ou feature phone com ficha suficiente para navegacao publica; sem benchmark moderno, fica na faixa de entrada.");
        }

        return latestClass is null
            ? new ClassificationDto(ClassificationTier.Undefined, "Indefinido", 0, "missing-data", "Sem classificacao calculada.")
            : new ClassificationDto(
                latestClass.Tier,
                PhoneClassifier.LabelFor(latestClass.Tier),
                latestClass.Score,
                latestClass.Basis,
                latestClass.Explanation);
    }

    private static int? ChooseWinner(string key, IReadOnlyDictionary<int, string> values)
    {
        return key switch
        {
            "battery" => ChooseNumericWinner(values, "battery", lowerIsBetter: false),
            "charging" => ChooseNumericWinner(values, "charging", lowerIsBetter: false),
            "wireless_charging" => ChooseNumericWinner(values, "wireless_charging", lowerIsBetter: false),
            "ram" => ChooseNumericWinner(values, "ram", lowerIsBetter: false),
            "storage_base" => ChooseNumericWinner(values, "storage_base", lowerIsBetter: false),
            "display_size" => ChooseNumericWinner(values, "display_size", lowerIsBetter: false),
            "refresh_rate" => ChooseNumericWinner(values, "refresh_rate", lowerIsBetter: false),
            "resolution" => ChooseNumericWinner(values, "resolution", lowerIsBetter: false),
            "main_camera" => ChooseNumericWinner(values, "main_camera", lowerIsBetter: false),
            "ultrawide_camera" => ChooseNumericWinner(values, "ultrawide_camera", lowerIsBetter: false),
            "selfie_camera" => ChooseNumericWinner(values, "selfie_camera", lowerIsBetter: false),
            "weight" => ChooseNumericWinner(values, "weight", lowerIsBetter: true),
            "benchmark_score" => ChooseNumericWinner(values, "benchmark_score", lowerIsBetter: false),
            _ => null,
        };
    }

    private static IReadOnlyList<CompareRowDto> BuildDerivedComparisonRows(IReadOnlyList<PhoneDetailsDto> phones)
    {
        var rows = new List<CompareRowDto>();

        AddDerivedRow(
            rows,
            "Mercado",
            "released_at",
            "Lancamento",
            phones,
            phone => phone.ReleasedAt?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-");

        AddDerivedRow(
            rows,
            "Mercado",
            "launch_price",
            "Preco de lancamento",
            phones,
            phone => phone.LaunchPriceUsd is null
                ? "-"
                : $"US$ {phone.LaunchPriceUsd.Value:0}");

        AddDerivedRow(
            rows,
            "Armazenamento",
            "storage_options",
            "Opcoes de armazenamento",
            phones,
            phone => FormatStorageOptions(phone.Variants));

        var benchmarkNames = phones
            .Select(phone => NormalizeBenchmarkName(phone.Benchmarks.FirstOrDefault()?.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (benchmarkNames.Count == 1 && phones.Count(phone => phone.Benchmarks.Count > 0) >= 2)
        {
            var values = phones.ToDictionary(
                phone => phone.Id,
                phone => phone.Benchmarks
                    .OrderByDescending(score => score.Score)
                    .FirstOrDefault() is { } score
                        ? score.Score.ToString(CultureInfo.InvariantCulture)
                        : "-");

            rows.Add(new CompareRowDto(
                "Performance",
                "benchmark_score",
                benchmarkNames[0]!,
                values,
                ChooseWinner("benchmark_score", values)));
        }

        return rows;
    }

    private static string? NormalizeBenchmarkName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (name.Contains("antutu", StringComparison.OrdinalIgnoreCase))
        {
            return "AnTuTu";
        }

        if (name.Contains("geekbench", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("single", StringComparison.OrdinalIgnoreCase)
                ? "Geekbench single-core"
                : "Geekbench multi-core";
        }

        return name.Trim();
    }

    private static void AddDerivedRow(
        ICollection<CompareRowDto> rows,
        string group,
        string key,
        string displayName,
        IReadOnlyList<PhoneDetailsDto> phones,
        Func<PhoneDetailsDto, string> valueSelector)
    {
        var values = phones.ToDictionary(phone => phone.Id, valueSelector);
        if (values.Values.All(value => value == "-"))
        {
            return;
        }

        rows.Add(new CompareRowDto(group, key, displayName, values, null));
    }

    private static string FormatStorageOptions(IReadOnlyList<PhoneVariantDto> variants)
    {
        var storageOptions = variants
            .Select(variant => variant.StorageGb)
            .Where(storage => storage is not null)
            .Select(storage => storage!.Value)
            .Distinct()
            .OrderBy(storage => storage)
            .ToList();

        return storageOptions.Count == 0
            ? "-"
            : string.Join(" / ", storageOptions.Select(FormatStorageAmount));
    }

    private static string FormatStorageAmount(int storageGb)
    {
        return storageGb >= 1024 && storageGb % 1024 == 0
            ? $"{storageGb / 1024} TB"
            : $"{storageGb} GB";
    }

    private static int? ChooseNumericWinner(
        IReadOnlyDictionary<int, string> values,
        string key,
        bool lowerIsBetter)
    {
        var parsed = values
            .Select(pair => new
            {
                pair.Key,
                Value = TryParseComparableValue(key, pair.Value, out var comparableValue)
                    ? comparableValue
                    : (double?)null,
            })
            .Where(item => item.Value is not null)
            .Select(item => new { PhoneId = item.Key, Value = item.Value!.Value })
            .ToList();

        if (parsed.Count < 2)
        {
            return null;
        }

        var target = lowerIsBetter
            ? parsed.Min(item => item.Value)
            : parsed.Max(item => item.Value);

        return parsed.Count(item => Math.Abs(item.Value - target) < 0.01) == 1
            ? parsed.First(item => Math.Abs(item.Value - target) < 0.01).PhoneId
            : null;
    }

    private static bool TryParseComparableValue(string key, string value, out double comparableValue)
    {
        comparableValue = 0;
        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!TryExtractNumber(normalized, out var parsed))
        {
            return false;
        }

        switch (key)
        {
            case "battery":
                if (!normalized.Contains("mah", StringComparison.Ordinal))
                {
                    return false;
                }

                comparableValue = parsed;
                return true;

            case "charging":
            case "wireless_charging":
                if (!normalized.Contains('w'))
                {
                    return false;
                }

                comparableValue = parsed;
                return true;

            case "ram":
                if (!normalized.Contains("gb", StringComparison.Ordinal))
                {
                    return false;
                }

                comparableValue = parsed;
                return true;

            case "storage_base":
                if (normalized.Contains("tb", StringComparison.Ordinal))
                {
                    comparableValue = parsed * 1024;
                    return true;
                }

                if (!normalized.Contains("gb", StringComparison.Ordinal))
                {
                    return false;
                }

                comparableValue = parsed;
                return true;

            case "refresh_rate":
                if (!normalized.Contains("hz", StringComparison.Ordinal))
                {
                    return false;
                }

                comparableValue = parsed;
                return true;

            case "display_size":
                if (!normalized.Contains("in", StringComparison.Ordinal))
                {
                    return false;
                }

                comparableValue = parsed;
                return true;

            case "main_camera":
            case "ultrawide_camera":
            case "selfie_camera":
                if (!normalized.Contains("mp", StringComparison.Ordinal) ||
                    normalized.Contains('+', StringComparison.Ordinal) ||
                    normalized.Contains('/', StringComparison.Ordinal))
                {
                    return false;
                }

                comparableValue = parsed;
                return true;

            case "weight":
                if (!normalized.Contains('g'))
                {
                    return false;
                }

                comparableValue = parsed;
                return true;

            case "benchmark_score":
                comparableValue = parsed;
                return true;

            case "resolution":
                var pieces = normalized
                    .Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(part => new string(part.Where(char.IsDigit).ToArray()))
                    .Where(part => part.Length > 0)
                    .ToList();

                if (pieces.Count != 2 ||
                    !double.TryParse(pieces[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var width) ||
                    !double.TryParse(pieces[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var height))
                {
                    return false;
                }

                comparableValue = width * height;
                return true;

            default:
                return false;
        }
    }

    private static bool TryExtractNumber(string value, out double parsed)
    {
        parsed = 0;
        var start = value.IndexOfAny("0123456789".ToCharArray());
        if (start < 0)
        {
            return false;
        }

        var token = new string(value[start..]
            .TakeWhile(character => char.IsDigit(character) || character == '.' || character == ',')
            .ToArray());

        if (token.Length == 0)
        {
            return false;
        }

        var separatorCount = token.Count(character => character is '.' or ',');
        var normalized = separatorCount > 1
            ? new string(token.Where(char.IsDigit).ToArray())
            : token.Replace(',', '.');

        return double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);
    }

    private static int CoverageId(string brandSlug, string slug)
    {
        var hash = HashCode.Combine(
            brandSlug.ToLowerInvariant(),
            slug.ToLowerInvariant());

        return hash == int.MinValue ? int.MinValue + 1 : -Math.Abs(hash);
    }

    private static string BuildResultKey(PhoneSearchResult result)
    {
        var normalizedFullName = Slugger.Slugify(PhoneNameFormatter.FullName(result.Brand, result.Name));
        return $"{result.BrandSlug}:{normalizedFullName}";
    }

    private static bool IsCoveredByFullCatalogResult(
        CoveragePhoneResult coverage,
        IReadOnlyList<PhoneSearchResult> localResults)
    {
        var coverageForms = BuildComparableForms(coverage.Brand, coverage.Name, $"{coverage.Brand} {coverage.Name}");
        if (coverageForms.Count == 0)
        {
            return false;
        }

        var normalizedCoverageBrand = PhoneSearchText.Normalize(coverage.Brand);
        var normalizedCoverageFullName = PhoneSearchText.Normalize($"{coverage.Brand} {coverage.Name}");

        foreach (var local in localResults)
        {
            if (!local.HasFullCatalogEntry)
            {
                continue;
            }

            var localForms = BuildComparableForms(local.Brand, local.Name, local.FullName);
            if (!coverageForms.Overlaps(localForms))
            {
                continue;
            }

            if (local.BrandSlug.Equals(coverage.BrandSlug, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedLocalBrand = PhoneSearchText.Normalize(local.Brand);
            var normalizedLocalName = PhoneSearchText.Normalize(local.Name);
            var normalizedLocalFullName = PhoneSearchText.Normalize(local.FullName);

            if (normalizedLocalName.Contains(normalizedCoverageBrand, StringComparison.Ordinal) ||
                normalizedCoverageFullName.Contains(normalizedLocalBrand, StringComparison.Ordinal) ||
                normalizedLocalFullName.Contains(normalizedCoverageBrand, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildComparableForms(string brand, string name, string fullName)
    {
        return BuildExactMatchForms(brand, name, fullName);
    }

    private static HashSet<string> BuildStrongMatchForms(string brand, params string?[] values)
    {
        var forms = BuildExactMatchForms(brand, values);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var raw = value.Trim();
            AddTokenForms(forms, raw);
            AddTokenForms(forms, StripLeadingBrandRaw(raw, brand));
        }

        return forms;
    }

    private static HashSet<string> BuildExactMatchForms(string brand, params string?[] values)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedBrand = PhoneSearchText.Normalize(brand);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var raw = value.Trim();
            var normalized = PhoneSearchText.Normalize(raw);
            var strippedRaw = StripLeadingBrandRaw(raw, brand);
            var strippedNormalized = StripLeadingBrand(normalized, normalizedBrand);

            AddIfPresent(forms, normalized);
            AddIfPresent(forms, strippedNormalized);
            AddIfPresent(forms, NormalizeComparableName(raw));
            AddIfPresent(forms, NormalizeComparableName(strippedRaw));
        }

        return forms;
    }

    private static void AddIfPresent(ISet<string> forms, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            forms.Add(value);
        }
    }

    private static void AddTokenForms(ISet<string> forms, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var token in PhoneSearchText.Tokenize(value))
        {
            if (PhoneSearchText.IsSearchNoiseToken(token))
            {
                continue;
            }

            AddIfPresent(forms, token);
            AddIfPresent(forms, NormalizeComparableName(token));
        }
    }

    private static string StripLeadingBrandRaw(string value, string brand)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(brand))
        {
            return value;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(brand, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed[brand.Length..].TrimStart(' ', '-', '_', '/', '(', ')');
    }

    private static string NormalizeComparableName(string value)
    {
        var tokens = PhoneSearchText.Tokenize(value)
            .Where(token => !ComparableNoiseTokens.Contains(token))
            .ToList();

        return string.Concat(tokens);
    }

    private static bool IsSuggestionFriendlyCoverage(PhoneSearchResult result)
    {
        if (result.HasFullCatalogEntry)
        {
            return true;
        }

        if (HasUnsupportedParentheticalVariant(result.Name))
        {
            return false;
        }

        if (result.Name.Contains("卫星", StringComparison.Ordinal) || result.Name.Contains("版", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool HasUnsupportedParentheticalVariant(string name)
    {
        var parentheticalMatches = System.Text.RegularExpressions.Regex.Matches(name, @"\([^)]*\)");
        if (parentheticalMatches.Count == 0)
        {
            return false;
        }

        foreach (System.Text.RegularExpressions.Match match in parentheticalMatches)
        {
            var inner = match.Value[1..^1].Trim();
            if (inner.Length == 0 ||
                inner.Length > 4 ||
                inner.Any(character => !char.IsLetterOrDigit(character)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDiscoverableCoverageBrandOption(CatalogBrandOptionDto option)
    {
        if (option.Count < 3 ||
            option.Name.Length is < 2 or > 24 ||
            char.IsDigit(option.Name[0]))
        {
            return false;
        }

        return CoverageBrandNameRegex().IsMatch(option.Name);
    }

    [GeneratedRegex("""^[A-Za-z][A-Za-z0-9 .+&-]{1,23}$""", RegexOptions.CultureInvariant)]
    private static partial Regex CoverageBrandNameRegex();

    private sealed record SearchCandidate(PhoneModel Phone, PhoneSearchResult Result, SearchMatch? Match);
    private sealed record RelatedPhoneCandidate(PhoneSearchResult Result, int Score, decimal PriceDistance, int ReleaseDistanceDays);
    private sealed record SearchMatch(int Score);
}
