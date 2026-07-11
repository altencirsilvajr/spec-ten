using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpecTen.Web.Data;
using SpecTen.Web.Options;
using SpecTen.Web.Scraping;

namespace SpecTen.Web.Services;

public sealed partial class DeviceCoverageService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IServiceScopeFactory scopeFactory,
    GsmArenaPageParser pageParser,
    IEnumerable<IOfficialCoverageProvider> officialProviders,
    IOptions<CoverageOptions> options,
    IHostEnvironment hostEnvironment,
    ILogger<DeviceCoverageService> logger) : IDeviceCoverageService
{
    private const string CacheKey = "device-coverage:index";
    private const string MakerDirectoryCacheKey = "device-coverage:makers";
    private const int CoverageSnapshotSchemaVersion = 1;
    private static readonly TimeSpan MakerCatalogCacheDuration = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ExcludedTokens = new(StringComparer.Ordinal)
    {
        "tv",
        "box",
        "stick",
        "dongle",
        "projector",
        "monitor",
        "tablet",
        "tab",
        "pad",
        "router",
        "modem",
        "gateway",
        "stb",
        "speaker",
        "earbuds",
        "buds",
        "headset",
        "watch",
    };
    private static readonly HashSet<string> VariantNoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "eea",
        "row",
        "global",
        "cn",
        "latam",
        "emea",
        "verizon",
        "tmobile",
        "wifi",
        "lte",
        "4g",
        "5g",
        "edition",
    };
    private static readonly IReadOnlyList<(string Prefix, string Brand)> BrandPrefixes =
    [
        ("apple_", "Apple"),
        ("samsung_", "Samsung"),
        ("xiaomi_", "Xiaomi"),
        ("redmi_", "Xiaomi"),
        ("poco_", "Xiaomi"),
        ("google_", "Google"),
        ("motorola_", "Motorola"),
        ("nothing_", "Nothing"),
        ("oneplus_", "OnePlus"),
        ("realme_", "Realme"),
        ("oppo_", "Oppo"),
        ("vivo_", "Vivo"),
        ("iqoo_", "Vivo"),
        ("honor_", "Honor"),
        ("huawei_", "Huawei"),
        ("asus_", "Asus"),
        ("sony_", "Sony"),
        ("nubia_", "Nubia"),
        ("redmagic_", "Nubia"),
        ("infinix_", "Infinix"),
        ("tecno_", "Tecno"),
        ("lenovo_", "Lenovo"),
        ("zte_", "ZTE"),
        ("meizu_", "Meizu"),
        ("doogee_", "Doogee"),
        ("ulefone_", "Ulefone"),
        ("oukitel_", "Oukitel"),
        ("blackview_", "Blackview"),
        ("hmd_", "HMD"),
    ];
    private static readonly HashSet<string> UppercaseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "5g",
        "4g",
        "lte",
        "nfc",
        "fe",
        "se",
        "ce",
        "gt",
        "xl",
        "ii",
        "iii",
        "iv",
        "v",
        "vi",
        "vii",
        "viii",
        "ix",
        "x",
        "xi",
        "xii",
    };
    private static readonly IReadOnlyDictionary<string, string> QueryBrandAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["iphone"] = "apple",
            ["ipad"] = "apple",
            ["pixel"] = "google",
            ["galaxy"] = "samsung",
            ["redmi"] = "xiaomi",
            ["poco"] = "xiaomi",
            ["moto"] = "motorola",
            ["oneplus"] = "oneplus",
            ["nothing"] = "nothing",
            ["iqoo"] = "vivo",
            ["redmagic"] = "nubia",
        };

    private readonly CoverageOptions _options = options.Value;
    private readonly string? _snapshotFilePath = ResolveSnapshotPath(hostEnvironment.ContentRootPath, options.Value.SnapshotFilePath);
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _indexMutationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hydrateGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _makerLoadGates = new(StringComparer.OrdinalIgnoreCase);

    public async Task WarmupAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var currentIndex = await GetIndexAsync(cancellationToken);
        if (!ShouldRefreshIndexFromRemote(currentIndex))
        {
            return;
        }

        var refreshed = await LoadLiveIndexOrEmptyAsync(cancellationToken);
        if (refreshed.Entries.Count == 0)
        {
            return;
        }

        await TryPersistSnapshotAsync(refreshed, cancellationToken);
        CacheIndex(refreshed);
        logger.LogInformation("Refreshed public coverage index during warmup with {Count} entries.", refreshed.Entries.Count);
    }

    public async Task<CoveragePhoneResult?> GetBySlugAsync(string brandSlug, string slug, CancellationToken cancellationToken)
    {
        var index = await GetIndexAsync(cancellationToken);
        index.BySlug.TryGetValue(BuildKey(brandSlug, slug), out var entry);
        if (entry is not null)
        {
            return entry.ToResult();
        }

        return await GetOfficialBySlugAsync(brandSlug, slug, cancellationToken);
    }

    public async Task<IReadOnlyList<CoveragePhoneResult>> SearchAsync(
        string? query,
        string? brandSlug,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return [];
        }

        var normalizedQuery = PhoneSearchText.Normalize(query);
        if (normalizedQuery.Length < Math.Max(1, _options.MinimumQueryLength))
        {
            return [];
        }

        var tokens = PhoneSearchText.Tokenize(query);
        if (tokens.Count == 0)
        {
            return [];
        }

        var normalizedBrand = string.IsNullOrWhiteSpace(brandSlug) ? string.Empty : Slugger.Slugify(brandSlug);
        var index = await GetIndexAsync(cancellationToken);
        var indexResults = SearchIndexEntries(index, normalizedBrand, normalizedQuery, tokens, limit);

        if (indexResults.Count == 0 &&
            index.Source != CoverageIndexSource.Live &&
            LooksLikeSpecificDeviceQuery(query))
        {
            var liveIndex = await LoadLiveIndexOrEmptyAsync(cancellationToken);
            var liveResults = SearchIndexEntries(liveIndex, normalizedBrand, normalizedQuery, tokens, limit);
            if (liveResults.Count > 0)
            {
                await TryPersistSnapshotAsync(liveIndex, cancellationToken);
                CacheIndex(liveIndex);
                index = liveIndex;
                indexResults = liveResults;
                logger.LogInformation(
                    "Coverage search refreshed live index on demand for query {Query} and found {Count} entries.",
                    query,
                    liveResults.Count);
            }
        }

        if (LooksLikeSpecificDeviceQuery(query) &&
            !HasExactCoverageMatch(indexResults, query))
        {
            var directIndex = await LoadDirectSearchIndexAsync(query, cancellationToken);
            var directResults = SearchIndexEntries(directIndex, normalizedBrand, normalizedQuery, tokens, limit);
            if (directResults.Count > 0)
            {
                index = await MergeAndCacheIndexAsync(index, directIndex, cancellationToken);
                indexResults = MergeCoverageResults(directResults, indexResults, limit);
            }
        }

        if (LooksLikeSpecificDeviceQuery(query) &&
            !HasExactCoverageMatch(indexResults, query))
        {
            var makerIndex = await LoadMakerBrandIndexAsync(query, normalizedBrand, index, cancellationToken);
            var makerResults = SearchIndexEntries(makerIndex, normalizedBrand, normalizedQuery, tokens, limit);
            if (makerResults.Count > 0)
            {
                index = await MergeAndCacheIndexAsync(index, makerIndex, cancellationToken);
                indexResults = MergeCoverageResults(makerResults, indexResults, limit);
            }
        }

        var officialResults = await SearchOfficialFallbackAsync(query, brandSlug, limit, cancellationToken);
        return MergeCoverageResults(indexResults, officialResults, limit);
    }

    private async Task<CoverageIndex> LoadDirectSearchIndexAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = PhoneSearchText.Normalize(query);
        var cacheKey = $"device-coverage:direct-search:{normalizedQuery}";
        if (cache.TryGetValue<CoverageIndex>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var sourceBaseUri = new Uri(_options.SourceUrl, UriKind.Absolute);
            var searchUri = new Uri(
                sourceBaseUri,
                $"results.php3?sQuickSearch=yes&sName={Uri.EscapeDataString(query.Trim())}");
            var client = httpClientFactory.CreateClient("device-coverage");
            var html = await client.GetStringAsync(searchUri, cancellationToken);
            var entries = new Dictionary<string, CoverageEntry>(StringComparer.OrdinalIgnoreCase);
            AddMakerPhoneEntries(entries, searchUri.ToString(), html);

            var index = entries.Count == 0
                ? CoverageIndex.Empty
                : new CoverageIndex(entries.Values.ToArray(), entries, DateTimeOffset.UtcNow, CoverageIndexSource.Live);
            cache.Set(cacheKey, index, entries.Count > 0 ? MakerCatalogCacheDuration : TimeSpan.FromMinutes(5));
            return index;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to search GSMArena directly for {Query}.", query);
            return CoverageIndex.Empty;
        }
    }

    public async Task<IReadOnlyList<CoveragePhoneResult>> BrowseByBrandAsync(
        string brandSlug,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(brandSlug) || limit <= 0)
        {
            return [];
        }

        var normalizedBrand = Slugger.Slugify(brandSlug);
        var index = await GetIndexAsync(cancellationToken);
        var indexResults = index.Entries
            .Where(IsBrowseFriendly)
            .Where(entry => entry.BrandSlug.Equals(normalizedBrand, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.QualityScore)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(entry => entry.ToResult())
            .ToList();

        var officialResults = await BrowseOfficialFallbackAsync(brandSlug, limit, cancellationToken);
        return MergeCoverageResults(indexResults, officialResults, limit);
    }

    public async Task<IReadOnlyList<CoverageBrandOption>> GetBrandOptionsAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        var index = await GetIndexAsync(cancellationToken);
        var officialOptions = await GetOfficialBrandOptionsAsync(cancellationToken);
        var indexOptions = index.Entries
            .Where(IsBrowseFriendly)
            .GroupBy(entry => entry.BrandSlug, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CoverageBrandOption(group.First().Brand, group.Key, group.Count()))
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return indexOptions
            .Concat(officialOptions)
            .GroupBy(option => option.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(option => option.Count).ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase).First())
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CoverageHydrationResult?> EnsureCatalogEntryAsync(string brandSlug, string slug, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.OnDemandHydrationEnabled)
        {
            return null;
        }

        var normalizedKey = BuildKey(brandSlug, slug);
        var index = await GetIndexAsync(cancellationToken);

        var gate = _hydrateGates.GetOrAdd(normalizedKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var officialProvider = await FindOfficialProviderAsync(brandSlug, slug, cancellationToken);
            if (officialProvider is not null)
            {
                var officialCoverage = await officialProvider.GetBySlugAsync(slug, cancellationToken);
                if (officialCoverage is not null)
                {
                    var officialExisting = await FindExistingCatalogEntryAsync(officialCoverage, cancellationToken);
                    var isCanonicalOfficialMatch = officialExisting is not null &&
                                                   string.Equals(
                                                       BuildKey(officialExisting.BrandSlug, officialExisting.Slug),
                                                       normalizedKey,
                                                       StringComparison.OrdinalIgnoreCase);

                    if (officialExisting is not null &&
                        isCanonicalOfficialMatch &&
                        !await NeedsCoverageRefreshAsync(officialExisting.PhoneId, cancellationToken))
                    {
                        return officialExisting;
                    }

                    var officialRecord = await officialProvider.FetchRecordAsync(slug, cancellationToken);
                    if (officialRecord is not null)
                    {
                        await using var officialScope = scopeFactory.CreateAsyncScope();
                        var officialImporter = officialScope.ServiceProvider.GetRequiredService<PhoneImportService>();
                        var importedOfficial = await officialImporter.ImportRecordAsync(officialRecord, $"coverage:{normalizedKey}", cancellationToken);

                        if (importedOfficial is not null)
                        {
                            if (index.BySlug.TryGetValue(normalizedKey, out var supplementalEntry) &&
                                NeedsSupplementalBroadCoverage(officialRecord))
                            {
                                await ImportBroadCoverageRecordAsync(supplementalEntry, normalizedKey, cancellationToken);
                            }

                            var refreshedOfficial = await FindExistingCatalogEntryAsync(officialCoverage, cancellationToken);
                            if (refreshedOfficial is not null)
                            {
                                return refreshedOfficial;
                            }

                            return new CoverageHydrationResult(
                                importedOfficial.PhoneId,
                                importedOfficial.BrandSlug,
                                importedOfficial.Slug,
                                officialCoverage.SourceName,
                                officialCoverage.SourceUrl);
                        }
                    }

                    if (officialExisting is not null)
                    {
                        return officialExisting;
                    }
                }
            }

            if (index.BySlug.TryGetValue(normalizedKey, out var entry))
            {
                var existing = await FindExistingCatalogEntryAsync(entry, cancellationToken);
                if (existing is not null && !await NeedsCoverageRefreshAsync(existing.PhoneId, cancellationToken))
                {
                    return existing;
                }

                var client = httpClientFactory.CreateClient("device-coverage");
                var html = await client.GetStringAsync(entry.SourceUrl, cancellationToken);
                var record = pageParser.Parse(entry.SourceUrl!, html, DateTimeOffset.UtcNow, _options.SourceName);

                await using var gsmaScope = scopeFactory.CreateAsyncScope();
                var gsmaImporter = gsmaScope.ServiceProvider.GetRequiredService<PhoneImportService>();
                var imported = await gsmaImporter.ImportRecordAsync(record, $"coverage:{normalizedKey}", cancellationToken);

                return imported is null
                    ? null
                    : new CoverageHydrationResult(imported.PhoneId, imported.BrandSlug, imported.Slug, entry.SourceName, entry.SourceUrl);
            }
            
            return null;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out while hydrating coverage entry {Brand}/{Slug}.", brandSlug, slug);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to hydrate coverage entry {Brand}/{Slug}.", brandSlug, slug);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CoverageIndex> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return CoverageIndex.Empty;
        }

        if (cache.TryGetValue<CoverageIndex>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<CoverageIndex>(CacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var loaded = await LoadIndexAsync(cancellationToken);
            CacheIndex(loaded);
            return loaded;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task<CoverageIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        var snapshot = await TryLoadSnapshotAsync(cancellationToken);
        if (snapshot is not null && snapshot.Entries.Count > 0)
        {
            logger.LogInformation("Loaded {Count} coverage entries from local snapshot for public search fallback.", snapshot.Entries.Count);
            return snapshot;
        }

        var liveIndex = await LoadLiveIndexOrEmptyAsync(cancellationToken);
        if (liveIndex.Entries.Count > 0)
        {
            await TryPersistSnapshotAsync(liveIndex, cancellationToken);
        }

        return liveIndex;
    }

    private async Task<CoverageIndex> LoadLiveIndexOrEmptyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("device-coverage");
            await using var stream = await client.GetStreamAsync(_options.DataUrl, cancellationToken);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            var entries = new Dictionary<string, CoverageEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var location in document.Descendants().Where(element => element.Name.LocalName == "loc"))
            {
                var entry = TryBuildEntry(location.Value);
                if (entry is null)
                {
                    continue;
                }

                var key = BuildKey(entry.BrandSlug, entry.Slug);
                entries.TryAdd(key, entry);
            }

            logger.LogInformation("Loaded {Count} GSMArena coverage entries for public search fallback.", entries.Count);
            return new CoverageIndex(entries.Values.ToArray(), entries, DateTimeOffset.UtcNow, CoverageIndexSource.Live);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load GSMArena coverage index. Search fallback will stay limited for now.");
            return CoverageIndex.Empty;
        }
    }

    private async Task<CoverageIndex> LoadMakerBrandIndexAsync(
        string query,
        string normalizedBrand,
        CoverageIndex knownIndex,
        CancellationToken cancellationToken)
    {
        var directory = await LoadMakerDirectoryAsync(cancellationToken);
        var availableBrands = directory.Keys
            .Concat(knownIndex.Entries.Select(entry => entry.BrandSlug))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var makerBrand = ResolveMakerBrandSlug(query, normalizedBrand, availableBrands);
        if (makerBrand is null)
        {
            return CoverageIndex.Empty;
        }

        var makerUrl = directory.TryGetValue(makerBrand, out var directoryUrl)
            ? directoryUrl
            : await DiscoverMakerUrlAsync(makerBrand, knownIndex, cancellationToken);
        if (string.IsNullOrWhiteSpace(makerUrl))
        {
            return CoverageIndex.Empty;
        }

        var cacheKey = $"device-coverage:maker:{makerBrand}";
        if (cache.TryGetValue<CoverageIndex>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var gate = _makerLoadGates.GetOrAdd(makerBrand, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<CoverageIndex>(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var client = httpClientFactory.CreateClient("device-coverage");
            var html = await client.GetStringAsync(makerUrl, cancellationToken);
            var entries = new Dictionary<string, CoverageEntry>(StringComparer.OrdinalIgnoreCase);
            AddMakerPhoneEntries(entries, makerUrl, html);

            foreach (var pageUrl in GetMakerPaginationUrls(makerUrl, makerBrand, html))
            {
                if (_options.MakerPageDelayMilliseconds > 0)
                {
                    await Task.Delay(_options.MakerPageDelayMilliseconds, cancellationToken);
                }

                try
                {
                    var pageHtml = await client.GetStringAsync(pageUrl, cancellationToken);
                    AddMakerPhoneEntries(entries, pageUrl, pageHtml);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Failed to load maker catalog page {PageUrl}.", pageUrl);
                }
            }

            var index = entries.Count == 0
                ? CoverageIndex.Empty
                : new CoverageIndex(entries.Values.ToArray(), entries, DateTimeOffset.UtcNow, CoverageIndexSource.Live);
            cache.Set(
                cacheKey,
                index,
                entries.Count > 0 ? MakerCatalogCacheDuration : TimeSpan.FromMinutes(5));
            return index;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to load current GSMArena maker page for {BrandSlug}.", makerBrand);
            return CoverageIndex.Empty;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void AddMakerPhoneEntries(
        IDictionary<string, CoverageEntry> entries,
        string pageUrl,
        string html)
    {
        var baseUri = new Uri(pageUrl, UriKind.Absolute);
        foreach (Match match in MakerPhoneLinkRegex().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (!Uri.TryCreate(baseUri, href, out var phoneUri))
            {
                continue;
            }

            var entry = TryBuildEntry(phoneUri.ToString());
            if (entry is not null)
            {
                entries.TryAdd(BuildKey(entry.BrandSlug, entry.Slug), entry);
            }
        }
    }

    private IReadOnlyList<string> GetMakerPaginationUrls(string makerUrl, string makerBrand, string html)
    {
        var baseUri = new Uri(makerUrl, UriKind.Absolute);
        var pageLinks = MakerPaginationLinkRegex()
            .Matches(html)
            .Select(match => new
            {
                Url = WebUtility.HtmlDecode(match.Groups["url"].Value),
                Page = int.TryParse(match.Groups["page"].Value, out var page) ? page : 0,
            })
            .Where(link => link.Page >= 2)
            .Select(link => new
            {
                Uri = Uri.TryCreate(baseUri, link.Url, out var uri) ? uri : null,
                link.Page,
            })
            .Where(link => link.Uri is not null)
            .Where(link =>
            {
                var fileName = Path.GetFileName(link.Uri!.AbsolutePath);
                var marker = fileName.IndexOf("-phones-", StringComparison.OrdinalIgnoreCase);
                return marker > 0 &&
                       Slugger.Slugify(fileName[..marker]).Equals(makerBrand, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (pageLinks.Length == 0)
        {
            return [];
        }

        var template = pageLinks.OrderByDescending(link => link.Page).First();
        var maxPage = Math.Min(template.Page, Math.Max(1, _options.MakerPageLimit));
        return Enumerable.Range(2, Math.Max(0, maxPage - 1))
            .Select(page => MakerPageNumberRegex().Replace(template.Uri!.ToString(), $"-p{page}.php"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string?> DiscoverMakerUrlAsync(
        string makerBrand,
        CoverageIndex knownIndex,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"device-coverage:maker-url:{makerBrand}";
        if (cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var knownPhone = knownIndex.Entries
            .Where(entry => entry.BrandSlug.Equals(makerBrand, StringComparison.OrdinalIgnoreCase))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.SourceUrl))
            .OrderByDescending(entry => entry.QualityScore)
            .FirstOrDefault();
        if (knownPhone?.SourceUrl is null)
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient("device-coverage");
            var html = await client.GetStringAsync(knownPhone.SourceUrl, cancellationToken);
            var baseUri = new Uri(knownPhone.SourceUrl, UriKind.Absolute);

            foreach (Match match in MakerPageHrefRegex().Matches(html))
            {
                var href = WebUtility.HtmlDecode(match.Groups["url"].Value);
                var marker = href.IndexOf("-phones-", StringComparison.OrdinalIgnoreCase);
                if (marker <= 0 ||
                    !Slugger.Slugify(href[..marker]).Equals(makerBrand, StringComparison.OrdinalIgnoreCase) ||
                    !Uri.TryCreate(baseUri, href, out var makerUri))
                {
                    continue;
                }

                var resolved = makerUri.ToString();
                cache.Set(cacheKey, resolved, MakerCatalogCacheDuration);
                return resolved;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to discover GSMArena maker page from an existing {BrandSlug} phone.", makerBrand);
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadMakerDirectoryAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<IReadOnlyDictionary<string, string>>(MakerDirectoryCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var sourceBaseUri = new Uri(_options.SourceUrl, UriKind.Absolute);
            var makersUri = new Uri(sourceBaseUri, "makers.php3");
            var client = httpClientFactory.CreateClient("device-coverage");
            var html = await client.GetStringAsync(makersUri, cancellationToken);
            var makers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in MakerDirectoryLinkRegex().Matches(html))
            {
                var brand = WebUtility.HtmlDecode(match.Groups["name"].Value).Trim();
                var href = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
                var brandSlug = Slugger.Slugify(brand);
                if (brandSlug.Length == 0 || !Uri.TryCreate(makersUri, href, out var makerUri))
                {
                    continue;
                }

                makers.TryAdd(brandSlug, makerUri.ToString());
            }

            cache.Set(
                MakerDirectoryCacheKey,
                makers,
                makers.Count > 0 ? MakerCatalogCacheDuration : TimeSpan.FromMinutes(5));
            return makers;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to load GSMArena maker directory for recent-device discovery.");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<CoverageIndex?> TryLoadSnapshotAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_snapshotFilePath) || !File.Exists(_snapshotFilePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotFilePath);
            var snapshot = await JsonSerializer.DeserializeAsync<CoverageSnapshotFile>(stream, SnapshotJsonOptions, cancellationToken);
            if (snapshot is null || snapshot.Version != CoverageSnapshotSchemaVersion || snapshot.Entries.Count == 0)
            {
                return null;
            }

            var entries = new Dictionary<string, CoverageEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in snapshot.Entries)
            {
                var entry = item.ToCoverageEntry();
                var key = BuildKey(entry.BrandSlug, entry.Slug);
                entries.TryAdd(key, entry);
            }

            return entries.Count == 0
                ? null
                : new CoverageIndex(entries.Values.ToArray(), entries, snapshot.GeneratedAt, CoverageIndexSource.Snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load local coverage snapshot from {Path}.", _snapshotFilePath);
            return null;
        }
    }

    private async Task TryPersistSnapshotAsync(CoverageIndex index, CancellationToken cancellationToken)
    {
        if (index.Entries.Count == 0 || string.IsNullOrWhiteSpace(_snapshotFilePath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_snapshotFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = new CoverageSnapshotFile(
                CoverageSnapshotSchemaVersion,
                index.LoadedAt,
                index.Entries.Select(CoverageSnapshotEntry.FromCoverageEntry).ToArray());

            var tempPath = $"{_snapshotFilePath}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SnapshotJsonOptions, cancellationToken);
            }

            File.Move(tempPath, _snapshotFilePath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to persist local coverage snapshot to {Path}.", _snapshotFilePath);
        }
    }

    private void CacheIndex(CoverageIndex index)
    {
        cache.Set(CacheKey, index, DetermineCacheDuration(index));
    }

    private async Task<CoverageIndex> MergeAndCacheIndexAsync(
        CoverageIndex fallbackPrimary,
        CoverageIndex secondary,
        CancellationToken cancellationToken)
    {
        await _indexMutationGate.WaitAsync(cancellationToken);
        try
        {
            var primary = cache.TryGetValue<CoverageIndex>(CacheKey, out var current) && current is not null
                ? current
                : fallbackPrimary;
            var merged = MergeIndexes(primary, secondary);
            CacheIndex(merged);
            await TryPersistSnapshotAsync(merged, cancellationToken);
            return merged;
        }
        finally
        {
            _indexMutationGate.Release();
        }
    }

    private TimeSpan DetermineCacheDuration(CoverageIndex index)
    {
        if (index.Entries.Count == 0)
        {
            return TimeSpan.FromMinutes(2);
        }

        if (index.Source == CoverageIndexSource.Snapshot && !IsFresh(index))
        {
            return TimeSpan.FromMinutes(5);
        }

        return TimeSpan.FromHours(Math.Max(1, _options.RefreshHours));
    }

    private bool ShouldRefreshIndexFromRemote(CoverageIndex index)
    {
        return index.Source != CoverageIndexSource.Live &&
               (index.Entries.Count == 0 || !IsFresh(index));
    }

    private bool IsFresh(CoverageIndex index)
    {
        if (index.LoadedAt == DateTimeOffset.MinValue)
        {
            return false;
        }

        return index.LoadedAt >= DateTimeOffset.UtcNow.AddHours(-Math.Max(1, _options.RefreshHours));
    }

    private static string? ResolveSnapshotPath(string? contentRootPath, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(contentRootPath ?? AppContext.BaseDirectory, configuredPath));
    }

    private static CoverageEntry? TryBuildEntry(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var fileName = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".php", StringComparison.OrdinalIgnoreCase) ||
            !PhonePageRegex().IsMatch(fileName) ||
            fileName.Contains("-pictures-", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("-review-", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("-reviews-", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("-price-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var slugPart = fileName[..fileName.LastIndexOf('-')];
        var brand = ResolveBrand(slugPart);
        var modelName = CanonicalizeSubBrandModelName(brand, BuildModelName(slugPart, brand), uri.ToString());
        if (!LooksLikePhone(brand, modelName))
        {
            return null;
        }

        var brandSlug = Slugger.Slugify(brand);
        var slug = Slugger.Slugify(PhoneNameFormatter.ModelName(brand, modelName));
        if (brandSlug.Length == 0 || slug.Length == 0)
        {
            return null;
        }

        var normalizedBrand = PhoneSearchText.Normalize(brand);
        var normalizedName = PhoneSearchText.Normalize(modelName);
        var normalizedFullName = PhoneSearchText.Normalize($"{brand} {modelName}");
        var comparableName = NormalizeComparableText(modelName);
        var fields = BuildCoverageFields(brand, modelName, normalizedBrand, normalizedName, normalizedFullName, comparableName);

        return new CoverageEntry(
            brand,
            brandSlug,
            modelName,
            slug,
            normalizedBrand,
            normalizedName,
            normalizedFullName,
            comparableName,
            fields,
            QualityScore(modelName),
            "GSMArena",
            uri.ToString());
    }

    private async Task<CoverageHydrationResult?> FindExistingCatalogEntryAsync(CoverageEntry entry, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var directMatch = await db.PhoneModels
            .AsNoTracking()
            .Where(model => model.Brand.Slug == entry.BrandSlug && model.Slug == entry.Slug)
            .Select(model => new CoverageHydrationResult(model.Id, model.Brand.Slug, model.Slug, entry.SourceName, entry.SourceUrl))
            .FirstOrDefaultAsync(cancellationToken);

        if (directMatch is not null)
        {
            return directMatch;
        }

        if (string.IsNullOrWhiteSpace(entry.SourceUrl))
        {
            return null;
        }

        var existingPhoneId = await db.SourceDocuments
            .AsNoTracking()
            .Where(document => document.SourceUrl == entry.SourceUrl && document.PhoneModelId != null)
            .Select(document => document.PhoneModelId!.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingPhoneId == 0)
        {
            return null;
        }

        return await db.PhoneModels
            .AsNoTracking()
            .Where(model => model.Id == existingPhoneId)
            .Select(model => new CoverageHydrationResult(model.Id, model.Brand.Slug, model.Slug, entry.SourceName, entry.SourceUrl))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CoverageHydrationResult?> FindExistingCatalogEntryAsync(CoveragePhoneResult entry, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var directMatch = await db.PhoneModels
            .AsNoTracking()
            .Where(model => model.Brand.Slug == entry.BrandSlug && model.Slug == entry.Slug)
            .Select(model => new CoverageHydrationResult(model.Id, model.Brand.Slug, model.Slug, entry.SourceName, entry.SourceUrl))
            .FirstOrDefaultAsync(cancellationToken);

        if (directMatch is not null)
        {
            return directMatch;
        }

        if (string.IsNullOrWhiteSpace(entry.SourceUrl))
        {
            return null;
        }

        var existingPhoneId = await db.SourceDocuments
            .AsNoTracking()
            .Where(document => document.SourceUrl == entry.SourceUrl && document.PhoneModelId != null)
            .Select(document => document.PhoneModelId!.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingPhoneId == 0)
        {
            return null;
        }

        return await db.PhoneModels
            .AsNoTracking()
            .Where(model => model.Id == existingPhoneId)
            .Select(model => new CoverageHydrationResult(model.Id, model.Brand.Slug, model.Slug, entry.SourceName, entry.SourceUrl))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CoverageHydrationResult?> ImportBroadCoverageRecordAsync(
        CoverageEntry entry,
        string normalizedKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.SourceUrl))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient("device-coverage");
        var html = await client.GetStringAsync(entry.SourceUrl, cancellationToken);
        var record = pageParser.Parse(entry.SourceUrl, html, DateTimeOffset.UtcNow, _options.SourceName);

        await using var scope = scopeFactory.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
        var imported = await importer.ImportRecordAsync(record, $"coverage:{normalizedKey}", cancellationToken);

        return imported is null
            ? null
            : new CoverageHydrationResult(imported.PhoneId, imported.BrandSlug, imported.Slug, entry.SourceName, entry.SourceUrl);
    }

    private static bool NeedsSupplementalBroadCoverage(SourcePhoneRecord record)
    {
        var hasChipset = record.Specs.Any(spec =>
            spec.Key.Equals("chipset", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(spec.DisplayValue));

        return !hasChipset || record.Benchmarks.Count == 0;
    }

    private async Task<bool> NeedsCoverageRefreshAsync(int phoneId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var snapshot = await db.PhoneModels
                .AsNoTracking()
                .Where(model => model.Id == phoneId)
                .Select(model => new
                {
                    model.ImageUrl,
                    model.ReleasedAt,
                    model.UpdatedAt,
                    Specs = model.Specs
                        .Select(spec => new
                        {
                            spec.Key,
                            spec.DisplayValue,
                            spec.SourceName,
                            spec.Confidence,
                            spec.Status,
                            spec.IsCritical,
                        })
                        .ToList(),
                })
                .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return true;
        }

        var refreshHours = Math.Max(1, _options.CatalogEntryRefreshHours);
        var isStale = snapshot.UpdatedAt <= DateTimeOffset.UtcNow.AddHours(-refreshHours);

        var readiness = CatalogReadiness.Evaluate(
            snapshot.ImageUrl,
            snapshot.ReleasedAt,
            snapshot.Specs.Select(spec => new CatalogSpecSnapshot(
                spec.Key,
                spec.DisplayValue,
                spec.SourceName,
                spec.Confidence,
                spec.Status,
                spec.IsCritical)));

        var chipsetValue = snapshot.Specs
            .FirstOrDefault(spec => spec.Key.Equals("chipset", StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue;
        var valuesByKey = snapshot.Specs
            .GroupBy(spec => spec.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().DisplayValue,
                StringComparer.OrdinalIgnoreCase);

        return isStale ||
               snapshot.ReleasedAt is null ||
               !readiness.IsPublicReady ||
               LooksLikeSuspiciousChipsetValue(chipsetValue) ||
               HasMissingCriticalRefreshSpecs(valuesByKey);
    }

    private static bool HasMissingCriticalRefreshSpecs(IReadOnlyDictionary<string, string> valuesByKey)
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

    private static IReadOnlyList<CoveragePhoneResult> SearchIndexEntries(
        CoverageIndex index,
        string normalizedBrand,
        string normalizedQuery,
        IReadOnlyList<string> tokens,
        int limit)
    {
        return index.Entries
            .Where(entry => normalizedBrand.Length == 0 || entry.BrandSlug.Equals(normalizedBrand, StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var matchScore = Match(entry, normalizedQuery, tokens);
                return new
                {
                    Entry = entry,
                    MatchScore = matchScore,
                    Score = matchScore > 0 ? matchScore + entry.QualityScore : 0,
                };
            })
            .Where(candidate => candidate.MatchScore > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Entry.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(candidate => candidate.Entry.ToResult())
            .ToList();
    }

    private static bool HasExactCoverageMatch(IReadOnlyList<CoveragePhoneResult> results, string query)
    {
        var normalizedQuery = PhoneSearchText.Normalize(query);
        var comparableQuery = NormalizeComparableText(query);

        return results.Any(result =>
            PhoneSearchText.Normalize(result.Name) == normalizedQuery ||
            PhoneSearchText.Normalize($"{result.Brand} {result.Name}") == normalizedQuery ||
            NormalizeComparableText(result.Name) == comparableQuery ||
            NormalizeComparableText($"{result.Brand} {result.Name}") == comparableQuery);
    }

    private static string? ResolveMakerBrandSlug(
        string query,
        string normalizedBrand,
        IEnumerable<string> availableBrands)
    {
        var available = availableBrands.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedBrand.Length > 0 && available.Contains(normalizedBrand))
        {
            return normalizedBrand;
        }

        var tokens = PhoneSearchText.Tokenize(query);
        foreach (var token in tokens)
        {
            if (QueryBrandAliases.TryGetValue(token, out var alias) && available.Contains(alias))
            {
                return alias;
            }

            var direct = Slugger.Slugify(token);
            if (available.Contains(direct))
            {
                return direct;
            }
        }

        return null;
    }

    private static CoverageIndex MergeIndexes(CoverageIndex primary, CoverageIndex secondary)
    {
        var entries = primary.Entries
            .ToDictionary(entry => BuildKey(entry.BrandSlug, entry.Slug), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in secondary.Entries)
        {
            entries[BuildKey(entry.BrandSlug, entry.Slug)] = entry;
        }

        return new CoverageIndex(
            entries.Values.ToArray(),
            entries,
            DateTimeOffset.UtcNow,
            CoverageIndexSource.Live);
    }

    private static int Match(CoverageEntry entry, string normalizedQuery, IReadOnlyList<string> tokens)
    {
        var score = 0;

        if (entry.NormalizedName == normalizedQuery || entry.NormalizedFullName == normalizedQuery || entry.ComparableName == NormalizeComparableText(normalizedQuery))
        {
            score += 360;
        }
        else if (entry.NormalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
                 entry.NormalizedFullName.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            score += 280;
        }
        else if (entry.NormalizedName.Contains(normalizedQuery, StringComparison.Ordinal) ||
                 entry.NormalizedFullName.Contains(normalizedQuery, StringComparison.Ordinal) ||
                 entry.ComparableName.Contains(NormalizeComparableText(normalizedQuery), StringComparison.Ordinal))
        {
            score += 210;
        }

        foreach (var token in tokens)
        {
            if (PhoneSearchText.IsSearchNoiseToken(token))
            {
                continue;
            }

            var comparableToken = NormalizeComparableText(token);
            var tokenScore = EvaluateCoverageTokenMatchScore(entry.Fields, token, comparableToken);

            if (tokenScore == 0)
            {
                return 0;
            }

            score += tokenScore;
        }

        return score;
    }

    private static bool LooksLikeSpecificDeviceQuery(string query)
    {
        var tokens = PhoneSearchText.Tokenize(query);
        return tokens.Count >= 2 || query.Any(char.IsDigit);
    }

    private static int EvaluateCoverageTokenMatchScore(
        IEnumerable<string> fields,
        string token,
        string comparableToken)
    {
        var score = 0;
        var compactModelToken = PhoneSearchText.IsCompactModelToken(token) ||
                                PhoneSearchText.IsCompactModelToken(comparableToken);

        foreach (var field in fields)
        {
            if (field == token || (comparableToken.Length > 0 && field == comparableToken))
            {
                score = Math.Max(score, 95);
            }
            else if (compactModelToken)
            {
                if ((field.StartsWith(token, StringComparison.Ordinal) && field.Length - token.Length <= 2) ||
                    (comparableToken.Length > 0 &&
                     field.StartsWith(comparableToken, StringComparison.Ordinal) &&
                     field.Length - comparableToken.Length <= 2))
                {
                    score = Math.Max(score, 70);
                }
            }
            else if (field.StartsWith(token, StringComparison.Ordinal) ||
                     (comparableToken.Length > 0 && field.StartsWith(comparableToken, StringComparison.Ordinal)))
            {
                score = Math.Max(score, 70);
            }
            else if (field.Contains(token, StringComparison.Ordinal) ||
                     (comparableToken.Length > 0 && field.Contains(comparableToken, StringComparison.Ordinal)))
            {
                score = Math.Max(score, 40);
            }
        }

        return score;
    }

    private static string ResolveBrand(string slugPart)
    {
        foreach (var (prefix, brand) in BrandPrefixes)
        {
            if (slugPart.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return brand;
            }
        }

        var token = slugPart.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Desconhecida";
        return FormatToken(token);
    }

    private static string BuildModelName(string slugPart, string brand)
    {
        var rawModel = slugPart;
        var prefix = BrandPrefixes.FirstOrDefault(item => item.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase) &&
                                                          slugPart.StartsWith(item.Prefix, StringComparison.OrdinalIgnoreCase)).Prefix;
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            rawModel = slugPart[prefix.Length..];
        }

        var formatted = string.Join(' ',
            rawModel.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(FormatToken));

        return PhoneNameFormatter.ModelName(brand, formatted);
    }

    private static string CanonicalizeSubBrandModelName(string brand, string modelName, string? sourceUrl)
    {
        if (brand.Equals("Xiaomi", StringComparison.OrdinalIgnoreCase) &&
            sourceUrl?.Contains("_poco_", StringComparison.OrdinalIgnoreCase) == true &&
            !modelName.StartsWith("Poco ", StringComparison.OrdinalIgnoreCase))
        {
            return $"Poco {modelName}";
        }

        return modelName;
    }

    private static string FormatToken(string token)
    {
        var lower = token.Trim().ToLowerInvariant();
        if (lower.Length == 0)
        {
            return string.Empty;
        }

        return lower switch
        {
            "iphone" => "iPhone",
            "ipad" => "iPad",
            "ios" => "iOS",
            "ipados" => "iPadOS",
            "wifi" => "Wi-Fi",
            _ when lower.StartsWith('(') && lower.EndsWith(')') && lower.Length > 2 => $"({lower[1..^1]})",
            _ when UppercaseTokens.Contains(lower) => lower.ToUpperInvariant(),
            _ when lower.Length <= 4 && lower.Any(char.IsDigit) => lower.ToUpperInvariant(),
            _ when lower.All(char.IsDigit) => lower,
            _ => char.ToUpperInvariant(lower[0]) + lower[1..],
        };
    }

    private static IReadOnlyList<string> BuildCoverageFields(
        string brand,
        string name,
        string normalizedBrand,
        string normalizedName,
        string normalizedFullName,
        string comparableName)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal)
        {
            normalizedBrand,
            normalizedName,
            normalizedFullName,
            comparableName,
        };

        AddCoverageTokenForms(fields, name);
        AddCoverageTokenForms(fields, $"{brand} {name}");
        AddCoverageTokenForms(fields, StripLeadingBrandRaw(name, brand));
        fields.RemoveWhere(string.IsNullOrWhiteSpace);
        return fields.ToArray();
    }

    private static void AddCoverageTokenForms(ISet<string> fields, string? value)
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

            fields.Add(token);
            var comparableToken = NormalizeComparableText(token);
            if (!string.IsNullOrWhiteSpace(comparableToken))
            {
                fields.Add(comparableToken);
            }
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

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var tokens = PhoneSearchText.Tokenize(value)
            .Where(token => !VariantNoiseTokens.Contains(token) && token is not "5g" and not "4g")
            .ToList();

        return string.Concat(tokens);
    }

    private static bool LooksLikePhone(string brand, string name)
    {
        if (brand.Length < 2 || name.Length < 2)
        {
            return false;
        }

        var blob = $"{brand} {name}".ToLowerInvariant();
        if (blob.Contains("smart tv", StringComparison.Ordinal) ||
            blob.Contains("android tv", StringComparison.Ordinal) ||
            blob.Contains("google tv", StringComparison.Ordinal))
        {
            return false;
        }

        var tokens = blob
            .Split([' ', '-', '/', '(', ')', '[', ']', '.', ',', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var hasCompactModelToken = tokens.Any(PhoneSearchText.IsCompactModelToken);

        return tokens.Count > 0 &&
               (!tokens.All(token => token.Length <= 2) || hasCompactModelToken) &&
               !tokens.Overlaps(ExcludedTokens);
    }

    private static bool IsBrowseFriendly(CoverageEntry entry)
    {
        if (ContainsCjk(entry.Name) ||
            HasUnsupportedParentheticalVariant(entry.Name))
        {
            return false;
        }

        var tokens = entry.Name
            .Split([' ', '-', '/', '[', ']', '.', ',', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return !tokens.Any(token => VariantNoiseTokens.Contains(token)) &&
               !LooksLikeRawModelCode(entry.Name);
    }

    private static bool HasUnsupportedParentheticalVariant(string name)
    {
        var parentheticalMatches = Regex.Matches(name, @"\([^)]*\)");
        if (parentheticalMatches.Count == 0)
        {
            return false;
        }

        foreach (Match match in parentheticalMatches)
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

    private static string BuildKey(string brandSlug, string slug)
        => $"{Slugger.Slugify(brandSlug)}:{Slugger.Slugify(slug)}";

    private async Task<IReadOnlyList<CoveragePhoneResult>> SearchOfficialFallbackAsync(
        string? query,
        string? brandSlug,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var providers = ResolveOfficialProviders(brandSlug);
        if (providers.Count == 0)
        {
            return [];
        }

        var results = new List<CoveragePhoneResult>(limit);
        foreach (var provider in providers)
        {
            var providerResults = await provider.SearchAsync(query, limit, cancellationToken);
            results.AddRange(providerResults);
        }

        return MergeCoverageResults([], results, limit);
    }

    private async Task<IReadOnlyList<CoveragePhoneResult>> BrowseOfficialFallbackAsync(
        string brandSlug,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var providers = ResolveOfficialProviders(brandSlug);
        if (providers.Count == 0)
        {
            return [];
        }

        var results = new List<CoveragePhoneResult>(limit);
        foreach (var provider in providers)
        {
            results.AddRange(await provider.BrowseAsync(limit, cancellationToken));
        }

        return MergeCoverageResults([], results, limit);
    }

    private async Task<IReadOnlyList<CoverageBrandOption>> GetOfficialBrandOptionsAsync(CancellationToken cancellationToken)
    {
        var results = new List<CoverageBrandOption>();
        foreach (var provider in officialProviders)
        {
            results.AddRange(await provider.GetBrandOptionsAsync(cancellationToken));
        }

        return results;
    }

    private async Task<CoveragePhoneResult?> GetOfficialBySlugAsync(string brandSlug, string slug, CancellationToken cancellationToken)
    {
        foreach (var provider in ResolveOfficialProviders(brandSlug))
        {
            var match = await provider.GetBySlugAsync(slug, cancellationToken);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private async Task<IOfficialCoverageProvider?> FindOfficialProviderAsync(string brandSlug, string slug, CancellationToken cancellationToken)
    {
        foreach (var provider in ResolveOfficialProviders(brandSlug))
        {
            if (await provider.GetBySlugAsync(slug, cancellationToken) is not null)
            {
                return provider;
            }
        }

        return null;
    }

    private IReadOnlyList<IOfficialCoverageProvider> ResolveOfficialProviders(string? brandSlug)
    {
        var normalizedBrand = string.IsNullOrWhiteSpace(brandSlug) ? string.Empty : Slugger.Slugify(brandSlug);
        return normalizedBrand.Length == 0
            ? officialProviders.ToList()
            : officialProviders
                .Where(provider => provider.BrandSlug.Equals(normalizedBrand, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private static IReadOnlyList<CoveragePhoneResult> MergeCoverageResults(
        IReadOnlyList<CoveragePhoneResult> primary,
        IReadOnlyList<CoveragePhoneResult> secondary,
        int limit)
    {
        var orderedKeys = new List<string>(limit);
        var bestByKey = new Dictionary<string, CoveragePhoneResult>(StringComparer.OrdinalIgnoreCase);
        var bestPriorityByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in primary.Concat(secondary))
        {
            var key = BuildKey(result.BrandSlug, result.Slug);
            var priority = CoverageResultPriority(result);

            if (bestByKey.TryGetValue(key, out _) &&
                bestPriorityByKey[key] >= priority)
            {
                continue;
            }

            if (!bestByKey.ContainsKey(key))
            {
                orderedKeys.Add(key);
            }

            bestByKey[key] = result;
            bestPriorityByKey[key] = priority;
        }

        return orderedKeys
            .Take(limit)
            .Select(key => bestByKey[key])
            .ToList();
    }

    private static int CoverageResultPriority(CoveragePhoneResult result)
    {
        return result.SourceName.Contains("official", StringComparison.OrdinalIgnoreCase)
            ? 2
            : 1;
    }

    private static int QualityScore(string name)
    {
        var score = 0;

        if (name.Contains(' ', StringComparison.Ordinal))
        {
            score += 14;
        }

        if (MarketingKeywordRegex().IsMatch(name))
        {
            score += 18;
        }

        if (Regex.IsMatch(name, "\\b(4g|5g)\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            score += 8;
        }

        if (name.Contains('(', StringComparison.Ordinal) || name.Contains(')', StringComparison.Ordinal))
        {
            score -= 10;
        }

        if (ContainsCjk(name))
        {
            score -= 26;
        }

        var tokens = name
            .Split([' ', '-', '/', '(', ')', '[', ']', '.', ',', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Any(token => VariantNoiseTokens.Contains(token)))
        {
            score -= 18;
        }

        if (LooksLikeRawModelCode(name))
        {
            score -= 22;
        }

        return score;
    }

    private static bool LooksLikeRawModelCode(string name)
    {
        var compact = name.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.Length < 5)
        {
            return false;
        }

        var hasLetter = compact.Any(char.IsLetter);
        var hasDigit = compact.Any(char.IsDigit);
        return hasLetter &&
               hasDigit &&
               compact.Count(character => !char.IsLetterOrDigit(character)) == 0 &&
               !compact.Any(char.IsLower);
    }

    private static bool ContainsCjk(string value)
    {
        foreach (var character in value)
        {
            if (character is >= '\u2E80' and <= '\u9FFF')
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex("""^[a-z0-9_()]+-\d+\.php$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhonePageRegex();

    [GeneratedRegex("""<a\s+href\s*=\s*["']?(?<url>[^"'\s>]+-phones-\d+\.php)["']?[^>]*>(?<name>[^<]+)(?:<br\s*/?>|</a>)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MakerDirectoryLinkRegex();

    [GeneratedRegex("""href\s*=\s*["']?(?<url>[a-z0-9_+()&.%-]+-\d+\.php)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MakerPhoneLinkRegex();

    [GeneratedRegex("""href\s*=\s*["']?(?<url>[a-z0-9_+()&.%-]+-phones-\d+\.php)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MakerPageHrefRegex();

    [GeneratedRegex("""href\s*=\s*["']?(?<url>[a-z0-9_+()&.%-]+-phones-[^"'\s>]*-p(?<page>\d+)\.php)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MakerPaginationLinkRegex();

    [GeneratedRegex("""-p\d+\.php$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MakerPageNumberRegex();

    [GeneratedRegex("""\b(pro|ultra|max|plus|lite|note|edge|flip|fold|phone|galaxy|iphone|redmi|moto|pixel|nova|turbo|fe)\b""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarketingKeywordRegex();

    private sealed record CoverageIndex(
        IReadOnlyList<CoverageEntry> Entries,
        IReadOnlyDictionary<string, CoverageEntry> BySlug,
        DateTimeOffset LoadedAt,
        CoverageIndexSource Source)
    {
        public static CoverageIndex Empty { get; } = new(
            Array.Empty<CoverageEntry>(),
            new Dictionary<string, CoverageEntry>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.MinValue,
            CoverageIndexSource.Empty);
    }

    private sealed record CoverageEntry(
        string Brand,
        string BrandSlug,
        string Name,
        string Slug,
        string NormalizedBrand,
        string NormalizedName,
        string NormalizedFullName,
        string ComparableName,
        IReadOnlyList<string> Fields,
        int QualityScore,
        string SourceName,
        string? SourceUrl)
    {
        public CoveragePhoneResult ToResult()
            => new(Brand, BrandSlug, Name, Slug, SourceName, SourceUrl);
    }

    private sealed record CoverageSnapshotFile(
        int Version,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<CoverageSnapshotEntry> Entries);

    private sealed record CoverageSnapshotEntry(
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
        string? SourceUrl)
    {
        public CoverageEntry ToCoverageEntry()
        {
            var name = CanonicalizeSubBrandModelName(Brand, Name, SourceUrl);
            var normalizedBrand = PhoneSearchText.Normalize(Brand);
            var normalizedName = PhoneSearchText.Normalize(name);
            var normalizedFullName = PhoneSearchText.Normalize($"{Brand} {name}");
            var comparableName = NormalizeComparableText(name);

            return new CoverageEntry(
                Brand,
                BrandSlug,
                name,
                Slug,
                normalizedBrand,
                normalizedName,
                normalizedFullName,
                comparableName,
                BuildCoverageFields(Brand, name, normalizedBrand, normalizedName, normalizedFullName, comparableName),
                QualityScore,
                SourceName,
                SourceUrl);
        }

        public static CoverageSnapshotEntry FromCoverageEntry(CoverageEntry entry)
            => new(
                entry.Brand,
                entry.BrandSlug,
                entry.Name,
                entry.Slug,
                entry.NormalizedBrand,
                entry.NormalizedName,
                entry.NormalizedFullName,
                entry.ComparableName,
                entry.QualityScore,
                entry.SourceName,
                entry.SourceUrl);
    }

    private enum CoverageIndexSource
    {
        Empty = 0,
        Snapshot = 1,
        Live = 2,
    }
}
