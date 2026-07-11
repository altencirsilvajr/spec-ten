using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using SpecTen.Web.Data;
using SpecTen.Web.Scraping;

namespace SpecTen.Web.Services;

public sealed partial class SamsungOfficialCoverageProvider(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<SamsungOfficialCoverageProvider> logger) : IOfficialCoverageProvider
{
    private const string SourceName = "Samsung Official";
    private const string PolicyStatus = "OfficialCatalog";
    private const string OfficialDomain = "samsung.com";
    private const string BrandName = "Samsung";
    private const string BrandSlugValue = "samsung";
    private const string CatalogCacheKey = "coverage:official:samsung:catalog";
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromHours(12);
    private static readonly IReadOnlyList<CatalogEndpoint> CatalogEndpoints =
    [
        new("uk", "https://www.samsung.com/uk/smartphones/all-smartphones/", 120),
        new("ca", "https://www.samsung.com/ca/smartphones/all-smartphones/", 110),
        new("id", "https://www.samsung.com/id/smartphones/all-smartphones/", 100),
        new("br", "https://www.samsung.com/br/smartphones/all-smartphones/", 90),
        new("ph", "https://www.samsung.com/ph/smartphones/all-smartphones/", 80),
        new("us", "https://www.samsung.com/us/smartphones/all-smartphones/", 70),
    ];
    private static readonly string[] CriticalCoverageKeys =
    [
        "chipset",
        "ram",
        "storage_base",
        "display_size",
        "main_camera",
        "battery",
        "os",
    ];

    public string Brand => BrandName;
    public string BrandSlug => BrandSlugValue;

    public async Task<IReadOnlyList<CoveragePhoneResult>> SearchAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return [];
        }

        var normalizedQuery = PhoneSearchText.Normalize(NormalizeName(query));
        var tokens = PhoneSearchText.Tokenize(NormalizeName(query));
        if (normalizedQuery.Length == 0 || tokens.Count == 0)
        {
            return [];
        }

        var catalog = await GetCatalogAsync(cancellationToken);

        return catalog
            .Select(item => new
            {
                Item = item,
                Score = Match(item, normalizedQuery, tokens),
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(candidate => candidate.Item.ToCoverageResult())
            .ToList();
    }

    public async Task<IReadOnlyList<CoveragePhoneResult>> BrowseAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog
            .Take(limit)
            .Select(item => item.ToCoverageResult())
            .ToList();
    }

    public async Task<IReadOnlyList<CoverageBrandOption>> GetBrandOptionsAsync(CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.Count == 0
            ? []
            : [new CoverageBrandOption(BrandName, BrandSlugValue, catalog.Count)];
    }

    public async Task<CoveragePhoneResult?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var normalizedSlug = Slugger.Slugify(slug);
        if (normalizedSlug.Length == 0)
        {
            return null;
        }

        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog
            .FirstOrDefault(item => item.Slug.Equals(normalizedSlug, StringComparison.OrdinalIgnoreCase))
            ?.ToCoverageResult();
    }

    public async Task<SourcePhoneRecord?> FetchRecordAsync(string slug, CancellationToken cancellationToken)
    {
        var catalogItem = await GetCatalogItemAsync(slug, cancellationToken);
        if (catalogItem is null)
        {
            return null;
        }

        var client = httpClientFactory.CreateClient("device-coverage");
        var records = new List<(SourcePhoneRecord Record, int Score)>();

        foreach (var sourceUrl in catalogItem.SourceUrls)
        {
            try
            {
                var html = await client.GetStringAsync(sourceUrl, cancellationToken);
                var record = BuildRecord(catalogItem, sourceUrl, html);
                records.Add((record, ScoreRecord(record)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "Failed to load Samsung official product page {SourceUrl}.", sourceUrl);
            }
        }

        return MergeOfficialRecords(records);
    }

    private static SourcePhoneRecord? MergeOfficialRecords(
        IReadOnlyList<(SourcePhoneRecord Record, int Score)> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => candidate.Record)
            .ToList();
        var primary = ordered[0];
        var specs = ordered
            .SelectMany(record => record.Specs)
            .GroupBy(spec => spec.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var variants = ordered
            .SelectMany(record => record.Variants)
            .GroupBy(
                variant => $"{variant.RamGb}|{variant.StorageGb}|{variant.Color}|{variant.Name}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(variant => variant.StorageGb ?? int.MaxValue)
            .ThenBy(variant => variant.RamGb ?? int.MaxValue)
            .ThenBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var benchmarks = ordered
            .SelectMany(record => record.Benchmarks)
            .GroupBy(benchmark => benchmark.BenchmarkName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var imageRecord = ordered.FirstOrDefault(record => !string.IsNullOrWhiteSpace(record.ImageUrl));

        var summary = BuildSummary(
            primary.ModelName,
            specs.FirstOrDefault(spec => spec.Key == "display_size")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "chipset")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "main_camera")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "battery")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "os")?.DisplayValue);

        return primary with
        {
            Summary = summary,
            ReleasedAt = ordered.Select(record => record.ReleasedAt).FirstOrDefault(value => value is not null),
            LaunchPriceUsd = ordered.Select(record => record.LaunchPriceUsd).FirstOrDefault(value => value is not null),
            ImageUrl = imageRecord?.ImageUrl,
            ImageSourceUrl = imageRecord?.ImageSourceUrl,
            Specs = specs,
            Variants = variants,
            Benchmarks = benchmarks,
        };
    }

    private async Task<IReadOnlyList<CatalogItem>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<IReadOnlyList<CatalogItem>>(CatalogCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var client = httpClientFactory.CreateClient("device-coverage");
            var candidates = new List<CatalogCandidate>();

            foreach (var endpoint in CatalogEndpoints)
            {
                try
                {
                    var html = await client.GetStringAsync(endpoint.Url, cancellationToken);
                    candidates.AddRange(ParseCatalog(html, endpoint));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogDebug(exception, "Failed to load Samsung official catalog source {CatalogUrl}.", endpoint.Url);
                }
            }

            var items = candidates
                .GroupBy(candidate => candidate.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildCatalogItem(group.Key, group.ToList()))
                .Where(item => item is not null)
                .Select(item => item!)
                .OrderByDescending(item => QualityScore(item.Name))
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            cache.Set(CatalogCacheKey, items, CatalogCacheDuration);
            logger.LogInformation("Loaded {Count} official Samsung catalog entries for public coverage fallback.", items.Count);
            return items;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to load official Samsung catalog coverage.");
            return [];
        }
    }

    private async Task<CatalogItem?> GetCatalogItemAsync(string slug, CancellationToken cancellationToken)
    {
        var normalizedSlug = Slugger.Slugify(slug);
        if (normalizedSlug.Length == 0)
        {
            return null;
        }

        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.FirstOrDefault(item => item.Slug.Equals(normalizedSlug, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<CatalogCandidate> ParseCatalog(string html, CatalogEndpoint endpoint)
    {
        var candidates = new List<CatalogCandidate>();

        foreach (Match match in JsonLdRegex().Matches(html))
        {
            var payload = WebUtility.HtmlDecode(match.Groups["value"].Value);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            foreach (var candidate in ParseCatalogCandidates(payload, endpoint))
            {
                if (!LooksLikePhone(BrandName, candidate.Name))
                {
                    continue;
                }

                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static IEnumerable<CatalogCandidate> ParseCatalogCandidates(string payload, CatalogEndpoint endpoint)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!IsItemList(root))
            {
                yield break;
            }

            if (!root.TryGetProperty("itemListElement", out var items) || items.ValueKind is not JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var entry in items.EnumerateArray())
            {
                if (!entry.TryGetProperty("item", out var product) || product.ValueKind is not JsonValueKind.Object)
                {
                    continue;
                }

                var name = NormalizeCatalogName(GetString(product, "name"));
                var catalogUrl = NormalizeCatalogUrl(GetString(product, "url") ?? GetString(product, "@id"), endpoint.BaseUri);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(catalogUrl))
                {
                    continue;
                }

                var url = NormalizeSamsungProductPageUrl(catalogUrl);
                var imageUrl = NormalizeCatalogUrl(GetString(product, "image"), endpoint.BaseUri);
                var description = NormalizeText(GetString(product, "description"));
                var storage = ExtractStorageGb($"{name} {catalogUrl}");
                var color = ExtractCatalogColor(catalogUrl);
                var slug = BuildModelSlug(name);
                if (slug.Length == 0)
                {
                    continue;
                }

                yield return new CatalogCandidate(
                    name,
                    slug,
                    url,
                    imageUrl,
                    description,
                    storage,
                    color,
                    IsExclusiveVariant(name),
                    endpoint.Region,
                    endpoint.Priority);
            }
        }
    }

    private static CatalogItem? BuildCatalogItem(string slug, IReadOnlyList<CatalogCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var orderedCandidates = candidates
            .OrderByDescending(candidate => candidate.EndpointPriority)
            .ThenBy(candidate => candidate.IsExclusive ? 1 : 0)
            .ThenBy(candidate => candidate.StorageGb ?? int.MaxValue)
            .ThenBy(candidate => candidate.SourceUrl.Contains("?modelCode=", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var representative = orderedCandidates[0];

        var variants = candidates
            .Select(candidate =>
            {
                var variantName = candidate.StorageGb is int storage && !string.IsNullOrWhiteSpace(candidate.Color)
                    ? $"{storage} GB - {candidate.Color}"
                    : candidate.StorageGb is int storageOnly
                        ? $"{storageOnly} GB"
                        : candidate.Color;

                return new SourceVariantClaim(
                    variantName ?? representative.Name,
                    null,
                    candidate.StorageGb,
                    candidate.Color);
            })
            .GroupBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var sourceUrls = orderedCandidates
            .GroupBy(candidate => candidate.Region, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().SourceUrl)
            .Concat(orderedCandidates.Select(candidate => candidate.SourceUrl))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        var normalizedName = PhoneSearchText.Normalize(representative.Name);
        return new CatalogItem(
            representative.Name,
            slug,
            representative.SourceUrl,
            representative.ImageUrl ?? orderedCandidates.Select(candidate => candidate.ImageUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)),
            normalizedName,
            PhoneSearchText.Normalize($"{BrandName} {representative.Name}"),
            variants,
            sourceUrls);
    }

    private static SourcePhoneRecord BuildRecord(CatalogItem item, string sourceUrl, string html)
    {
        var sections = ParseSections(html);
        var collectedAt = DateTimeOffset.UtcNow;
        var releaseDate = ExtractReleaseDate(html);
        var imageUrl = ExtractPrimaryImage(html) ?? item.ImageUrl;
        var variants = BuildVariants(item, sections, html);
        var specs = BuildSpecs(item, sourceUrl, html, sections, variants, collectedAt);

        var summary = BuildSummary(
            item.Name,
            specs.FirstOrDefault(spec => spec.Key == "display_size")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "chipset")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "main_camera")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "battery")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "os")?.DisplayValue);

        return new SourcePhoneRecord(
            SourceName,
            sourceUrl,
            PolicyStatus,
            true,
            true,
            BrandName,
            OfficialDomain,
            PhoneNameFormatter.ModelName(BrandName, item.Name),
            summary,
            releaseDate,
            null,
            imageUrl,
            imageUrl,
            specs,
            variants,
            []);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ParseSections(string html)
    {
        var titles = SectionTitleRegex().Matches(html);
        var sections = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < titles.Count; index++)
        {
            var titleMatch = titles[index];
            var title = NormalizeText(HtmlToText(titleMatch.Groups["value"].Value));
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var start = titleMatch.Index + titleMatch.Length;
            var end = index + 1 < titles.Count ? titles[index + 1].Index : html.Length;
            var sectionHtml = html[start..end];
            var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match itemMatch in ContentItemRegex().Matches(sectionHtml))
            {
                var rawName = NormalizeText(HtmlToText(itemMatch.Groups["name"].Value));
                var value = NormalizeText(HtmlToText(itemMatch.Groups["value"].Value));
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var key = string.IsNullOrWhiteSpace(rawName) ? title : rawName;
                items[key] = value;
            }

            if (items.Count > 0)
            {
                sections[title] = items;
            }
        }

        return sections;
    }

    private static IReadOnlyList<SourceVariantClaim> BuildVariants(
        CatalogItem item,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections,
        string html)
    {
        var variants = new List<SourceVariantClaim>();
        var ram = ParseStorageValue(GetFirstValue(
            sections,
            ["Armazenamento/Memoria", "Storage/Memory", "Penyimpanan/Memori"],
            ["Memoria_(GB)", "Memory_(GB)", "Memori_(GB)"]));
        var storage = ParseStorageValue(GetFirstValue(
            sections,
            ["Armazenamento/Memoria", "Storage/Memory", "Penyimpanan/Memori"],
            ["Armazenamento (GB)", "Storage (GB)", "Penyimpanan (GB)"]));

        if (ram is not null || storage is not null)
        {
            variants.Add(new SourceVariantClaim(
                BuildVariantName(ram, storage),
                ram,
                storage,
                null));
        }

        variants.AddRange(ExtractTextVariants(html));
        variants.AddRange(item.CatalogVariants);

        return variants
            .Where(variant => !string.IsNullOrWhiteSpace(variant.Name))
            .GroupBy(
                variant => $"{variant.Name}|{variant.RamGb}|{variant.StorageGb}|{variant.Color}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(variant => variant.StorageGb ?? int.MaxValue)
            .ThenBy(variant => variant.RamGb ?? int.MaxValue)
            .ThenBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SourceSpecClaim> BuildSpecs(
        CatalogItem item,
        string sourceUrl,
        string html,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections,
        IReadOnlyList<SourceVariantClaim> variants,
        DateTimeOffset collectedAt)
    {
        var specs = new List<SourceSpecClaim>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string group, string key, string displayName, string? value, string? unit, bool critical)
        {
            value = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(value) || !keys.Add(key))
            {
                return;
            }

            specs.Add(new SourceSpecClaim(
                SourceName,
                sourceUrl,
                true,
                group,
                key,
                displayName,
                value,
                PhoneSearchText.Normalize(value),
                value,
                unit,
                critical,
                ConfidenceFor(key, critical),
                collectedAt));
        }

        var cameraModules = GetFirstValue(
            sections,
            ["Camera"],
            ["Cameras Traseiras (Multiplas) - Resolucao", "Rear Camera - Resolution (Multiple)"]);
        var selfieCamera = GetFirstValue(
            sections,
            ["Camera"],
            ["Camera Frontal - Resolucao", "Front Camera - Resolution"]);
        var storageBase = GetFirstValue(
            sections,
            ["Armazenamento/Memoria", "Storage/Memory", "Penyimpanan/Memori"],
            ["Armazenamento (GB)", "Storage (GB)", "Penyimpanan (GB)"]);
        var ram = GetFirstValue(
            sections,
            ["Armazenamento/Memoria", "Storage/Memory", "Penyimpanan/Memori"],
            ["Memoria_(GB)", "Memory_(GB)", "Memori_(GB)"]);
        var os = GetFirstValue(
            sections,
            ["Sistema Operacional", "OS"],
            ["Sistema Operacional", "OS"]);
        var network = GetFirstValue(
            sections,
            ["Rede / Bandas", "Network/Bearer"],
            ["Conexoes", "Infra"]);
        var simCount = GetFirstValue(
            sections,
            ["Rede / Bandas", "Network/Bearer"],
            ["Number of SIM"]);
        var simType = GetFirstValue(
            sections,
            ["Rede / Bandas", "Network/Bearer"],
            ["Tipo de Chip (SIM Card)", "SIM size"]);
        var slotType = GetFirstValue(
            sections,
            ["Rede / Bandas", "Network/Bearer"],
            ["Tipo de Slot de Chip", "SIM Slot Type"]);
        var displaySize = GetFirstValue(
            sections,
            ["Tela", "Display"],
            ["Tamanho (Tela Principal)", "Size (Main_Display)"]);
        var battery = GetFirstValue(
            sections,
            ["Bateria", "Battery"],
            ["Capacidade da Bateria (mAh, Typical)", "Battery Capacity (mAh, Typical)"]);
        var videoPlaybackHours = GetFirstValue(
            sections,
            ["Bateria", "Battery"],
            ["Tempo de Reproducao de Video (Horas)", "Video Playback Time (Hours)"]);
        var usbInterface = GetFirstValue(
            sections,
            ["Conectividade", "Connectivity"],
            ["USB Interface"]);
        var usbVersion = GetFirstValue(
            sections,
            ["Conectividade", "Connectivity"],
            ["Versao de USB", "USB Version"]);

        Add("Performance", "chipset", "Chipset", ExtractChipset(html), null, true);
        Add("Performance", "cpu", "CPU", CombineCpu(
            GetFirstValue(sections, ["Processador", "Processor"], ["Velocidade do Processador", "CPU Speed"]),
            GetFirstValue(sections, ["Processador", "Processor"], ["Tipo de Processador", "CPU Type"])), null, false);
        Add("Performance", "gpu", "GPU", ExtractGpu(html), null, false);

        Add("Memoria", "ram", "RAM", FormatStorageValue(ram) ?? FormatStorageValue(ExtractBaseVariantRam(variants)), "GB", true);
        Add("Armazenamento", "storage_base", "Armazenamento base", FormatStorageValue(storageBase) ?? FormatStorageValue(ExtractBaseVariantStorage(variants)), "GB", true);
        Add("Armazenamento", "storage_options", "Opcoes de armazenamento", FormatStorageOptions(variants), null, false);

        Add("Tela", "display_size", "Tamanho da tela", ExtractDisplaySize(displaySize) ?? ExtractDisplaySizeFromText(html), "in", true);
        Add("Tela", "resolution", "Resolucao", NormalizeResolution(GetFirstValue(sections, ["Tela", "Display"], ["Resolucao (Tela Principal)", "Resolution (Main Display)"])), null, false);
        Add("Tela", "display_type", "Painel", GetFirstValue(sections, ["Tela", "Display"], ["Tecnologia (Tela Principal)", "Technology (Main Display)"]), null, false);
        Add("Tela", "refresh_rate", "Taxa de atualizacao", ExtractRefreshRate(GetFirstValue(sections, ["Tela", "Display"], ["Taxa de Atualizacao Maxima (Tela Principal)", "Max Refresh Rate (Main Display)"])), "Hz", false);

        Add("Camera", "main_camera", "Camera principal", ExtractCameraAt(cameraModules, 0), "MP", true);
        Add("Camera", "ultrawide_camera", "Ultra-wide", ExtractCameraAt(cameraModules, 1), "MP", false);
        Add("Camera", "telephoto_camera", "Camera auxiliar", ExtractCameraAt(cameraModules, 2), "MP", false);
        Add("Camera", "selfie_camera", "Camera frontal", NormalizeMegapixels(selfieCamera), "MP", false);
        Add("Camera", "main_camera_video", "Video principal", GetFirstValue(
            sections,
            ["Camera"],
            ["Resolucao de Gravacao de Videos", "Video Recording Resolution"]), null, false);

        Add("Bateria", "battery", "Bateria", FormatBattery(battery), "mAh", true);
        Add("Bateria", "charging", "Carregamento", ExtractCharging(html), "W", true);
        Add("Bateria", "wireless_charging", "Carregamento sem fio", ExtractWirelessCharging(html), "W", false);
        Add("Bateria", "video_playback", "Video", FormatHours(videoPlaybackHours), "h", false);

        Add("Construcao", "ip_rating", "Resistencia", ExtractIpRating(html), null, true);
        Add("Construcao", "dimensions", "Dimensoes", FormatDimensions(GetFirstValue(sections, ["Especificacoes Fisicas", "Physical specification"], ["Dimensoes (AxLxP, mm)", "Dimension (HxWxD, mm)"])), null, false);
        Add("Construcao", "weight", "Peso", FormatWeight(GetFirstValue(sections, ["Especificacoes Fisicas", "Physical specification"], ["Peso (g)", "Weight (g)"])), "g", false);
        Add("Construcao", "build", "Construcao", ExtractBuild(html), null, false);

        Add("Conectividade", "sim", "SIM / eSIM", CombineSim(simCount, simType, slotType), null, false);
        Add("Conectividade", "network", "Rede", network, null, false);
        Add("Conectividade", "wifi", "Wi-Fi", GetFirstValue(sections, ["Conectividade", "Connectivity"], ["Wi-Fi"]), null, false);
        Add("Conectividade", "bluetooth", "Bluetooth", GetFirstValue(sections, ["Conectividade", "Connectivity"], ["Versao de Bluetooth", "Bluetooth Version"]), null, false);
        Add("Conectividade", "usb", "USB", CombineUsb(usbInterface, usbVersion), null, false);
        Add("Conectividade", "positioning", "Localizacao", GetFirstValue(sections, ["Conectividade", "Connectivity"], ["Localizacao", "Location Technology"]), null, false);

        Add("Software", "os", "Sistema", os, null, false);
        Add("Software", "sensors", "Sensores", GetFirstValue(sections, ["Sensores", "Sensors"], ["Sensores", "Sensors"]), null, false);

        return specs;
    }

    private static string BuildSummary(
        string modelName,
        string? displaySize,
        string? chipset,
        string? mainCamera,
        string? battery,
        string? os)
    {
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(displaySize))
        {
            facts.Add($"{displaySize} de tela");
        }

        if (!string.IsNullOrWhiteSpace(chipset))
        {
            facts.Add(chipset);
        }

        if (!string.IsNullOrWhiteSpace(mainCamera))
        {
            facts.Add($"{mainCamera} na camera principal");
        }

        if (!string.IsNullOrWhiteSpace(battery))
        {
            facts.Add($"{battery} de bateria");
        }

        if (!string.IsNullOrWhiteSpace(os))
        {
            facts.Add(os);
        }

        return facts.Count == 0
            ? $"{modelName} encontrado no catalogo oficial da Samsung para completar a ficha publica."
            : $"{modelName} com {string.Join(", ", facts)}.";
    }

    private static string? GetValue(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections,
        string section,
        string name)
    {
        var normalizedSection = PhoneSearchText.Normalize(section);
        foreach (var (sectionName, values) in sections)
        {
            if (PhoneSearchText.Normalize(sectionName) != normalizedSection)
            {
                continue;
            }

            var normalizedName = PhoneSearchText.Normalize(name);
            foreach (var (itemName, value) in values)
            {
                if (PhoneSearchText.Normalize(itemName) == normalizedName)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? GetFirstValue(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections,
        IReadOnlyList<string> sectionNames,
        IReadOnlyList<string> itemNames)
    {
        foreach (var sectionName in sectionNames)
        {
            foreach (var itemName in itemNames)
            {
                var value = GetValue(sections, sectionName, itemName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ExtractPrimaryImage(string html)
    {
        foreach (Match match in PreloadImageRegex().Matches(html))
        {
            var value = NormalizeCatalogUrl(match.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(value) &&
                value.Contains("images.samsung.com", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static DateTimeOffset? ExtractReleaseDate(string html)
    {
        var match = MetaDateRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            match.Groups["value"].Value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? ExtractChipset(string html)
    {
        var text = SearchableTextFromHtml(html);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return FirstNormalizedMatch(
            SnapdragonRegex().Match(text),
            ExynosRegex().Match(text),
            DimensityRegex().Match(text),
            TensorRegex().Match(text),
            HelioRegex().Match(text),
            KirinRegex().Match(text),
            UnisocRegex().Match(text));
    }

    private static string? ExtractGpu(string html)
    {
        var text = SearchableTextFromHtml(html);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return FirstNormalizedMatch(
            AdrenoRegex().Match(text),
            MaliRegex().Match(text),
            ImmortalisRegex().Match(text),
            XclipseRegex().Match(text));
    }

    private static string? FirstNormalizedMatch(params Match[] matches)
    {
        foreach (var match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var value = NormalizeText(match.Groups["value"].Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            value = value
                .Replace("®", string.Empty, StringComparison.Ordinal)
                .Replace(" Mobile Platform", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(" para Galaxy", " for Galaxy", StringComparison.OrdinalIgnoreCase)
                .Replace(" Para Galaxy", " for Galaxy", StringComparison.OrdinalIgnoreCase);

            return value.Trim(' ', '.', ',', ';', ':');
        }

        return null;
    }

    private static string? ExtractCharging(string html)
    {
        var text = SearchableTextFromHtml(html);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var matches = ChargingContextRegex().Matches(text)
            .Select(match => match.Groups["value"].Value)
            .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var watts) ? watts : (int?)null)
            .Where(value => value is >= 10 and <= 150)
            .Select(value => value!.Value)
            .ToList();

        return matches.Count == 0 ? null : $"{matches.Max()} W";
    }

    private static string? ExtractWirelessCharging(string html)
    {
        var text = SearchableTextFromHtml(html);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = WirelessChargingRegex().Match(text);
        return match.Success ? $"{match.Groups["value"].Value} W" : null;
    }

    private static string? ExtractIpRating(string html)
    {
        var text = SearchableTextFromHtml(html);
        var match = IpRatingRegex().Match(text ?? string.Empty);
        return match.Success ? match.Groups["value"].Value.ToUpperInvariant() : null;
    }

    private static string? ExtractBuild(string html)
    {
        var text = SearchableTextFromHtml(html);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = new List<string>();
        if (text.Contains("estrutura de metal", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("metal frame", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("Estrutura de metal");
        }

        var glassMatch = GorillaGlassRegex().Match(text);
        if (glassMatch.Success)
        {
            parts.Add(glassMatch.Groups["value"].Value.Replace("®", string.Empty, StringComparison.Ordinal));
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? ExtractDisplaySize(string? value)
    {
        var match = DisplaySizeRegex().Match(value ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Groups["value"].Value.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < 1.5m ||
            parsed > 9m)
        {
            return null;
        }

        return $"{parsed.ToString("0.#", CultureInfo.InvariantCulture)} in";
    }

    private static string? ExtractDisplaySizeFromText(string html)
    {
        var text = SearchableTextFromHtml(html);
        return string.IsNullOrWhiteSpace(text) ? null : ExtractDisplaySize(text);
    }

    private static string? NormalizeResolution(string? value)
    {
        var match = ResolutionRegex().Match(value ?? string.Empty);
        return match.Success
            ? $"{match.Groups["width"].Value} x {match.Groups["height"].Value}"
            : NormalizeText(value);
    }

    private static string? ExtractRefreshRate(string? value)
    {
        var match = RefreshRateRegex().Match(value ?? string.Empty);
        return match.Success ? $"{match.Groups["value"].Value} Hz" : null;
    }

    private static string? ExtractCameraAt(string? value, int index)
    {
        var cameras = MegapixelRegex()
            .Matches(value ?? string.Empty)
            .Select(match => FormatNumberToken(match.Groups["value"].Value))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"{item} MP")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        return index >= 0 && index < cameras.Count ? cameras[index] : null;
    }

    private static string? NormalizeMegapixels(string? value)
    {
        var match = MegapixelRegex().Match(value ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        var normalized = FormatNumberToken(match.Groups["value"].Value);
        return normalized is null ? null : $"{normalized} MP";
    }

    private static string? FormatNumberToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return NormalizeText(value);
        }

        return parsed % 1 == 0
            ? decimal.Truncate(parsed).ToString(CultureInfo.InvariantCulture)
            : parsed.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string? FormatBattery(string? value)
    {
        var match = BatteryRegex().Match(value ?? string.Empty);
        return match.Success ? $"{match.Groups["value"].Value} mAh" : null;
    }

    private static string? FormatHours(string? value)
    {
        var match = HoursRegex().Match(value ?? string.Empty);
        return match.Success ? $"{match.Groups["value"].Value} h" : null;
    }

    private static string? FormatDimensions(string? value)
    {
        var match = DimensionsRegex().Match((value ?? string.Empty).Replace('×', 'x'));
        return match.Success
            ? $"{match.Groups["height"].Value} x {match.Groups["width"].Value} x {match.Groups["depth"].Value} mm"
            : null;
    }

    private static string? FormatWeight(string? value)
    {
        var match = WeightRegex().Match(value ?? string.Empty);
        return match.Success ? $"{match.Groups["value"].Value} g" : null;
    }

    private static string? CombineUsb(string? usbInterface, string? usbVersion)
    {
        var values = new[] { NormalizeText(usbInterface), NormalizeText(usbVersion) }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 0 ? null : string.Join(" / ", values);
    }

    private static string? CombineSim(string? simCount, string? simType, string? slotType)
    {
        var values = new[] { NormalizeText(simCount), NormalizeText(simType), NormalizeText(slotType) }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 0 ? null : string.Join(" / ", values);
    }

    private static string? CombineCpu(string? speed, string? type)
    {
        var values = new[] { NormalizeText(type), NormalizeText(speed) }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 0 ? null : string.Join(" / ", values);
    }

    private static int? ParseStorageValue(string? value)
    {
        var match = StorageValueRegex().Match(value ?? string.Empty);
        return match.Success && int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? FormatStorageValue(int? value)
        => value is int parsed ? $"{parsed} GB" : null;

    private static string? FormatStorageValue(string? value)
        => ParseStorageValue(value) is int parsed ? FormatStorageValue(parsed) : null;

    private static int? ExtractBaseVariantRam(IReadOnlyList<SourceVariantClaim> variants)
    {
        return variants
            .Where(variant => variant.RamGb is not null)
            .OrderBy(variant => variant.StorageGb ?? int.MaxValue)
            .ThenBy(variant => variant.RamGb ?? int.MaxValue)
            .Select(variant => variant.RamGb)
            .FirstOrDefault();
    }

    private static int? ExtractBaseVariantStorage(IReadOnlyList<SourceVariantClaim> variants)
    {
        return variants
            .Where(variant => variant.StorageGb is not null)
            .OrderBy(variant => variant.StorageGb ?? int.MaxValue)
            .ThenBy(variant => variant.RamGb ?? int.MaxValue)
            .Select(variant => variant.StorageGb)
            .FirstOrDefault();
    }

    private static string BuildVariantName(int? ramGb, int? storageGb)
    {
        if (ramGb is int ram && storageGb is int storage)
        {
            return $"{ram} GB / {storage} GB";
        }

        if (storageGb is int storageOnly)
        {
            return $"{storageOnly} GB";
        }

        if (ramGb is int ramOnly)
        {
            return $"{ramOnly} GB RAM";
        }

        return "Variante padrao";
    }

    private static string? FormatStorageOptions(IReadOnlyList<SourceVariantClaim> variants)
    {
        var storages = variants
            .Where(variant => variant.StorageGb is >= 16)
            .Select(variant => variant.StorageGb!.Value)
            .Distinct()
            .OrderBy(value => value)
            .Select(value => $"{value} GB")
            .ToList();

        return storages.Count == 0 ? null : string.Join(" / ", storages);
    }

    private static IReadOnlyList<SourceVariantClaim> ExtractTextVariants(string html)
    {
        var text = SearchableTextFromHtml(html);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return VariantPairRegex()
            .Matches(text)
            .Select(match =>
            {
                var storage = int.TryParse(match.Groups["storage"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStorage)
                    ? parsedStorage
                    : (int?)null;
                var ram = int.TryParse(match.Groups["ram"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRam)
                    ? parsedRam
                    : (int?)null;

                return storage is null && ram is null
                    ? null
                    : new SourceVariantClaim(
                        BuildVariantName(ram, storage),
                        ram,
                        storage,
                        null);
            })
            .Where(variant => variant is not null)
            .Cast<SourceVariantClaim>()
            .ToList();
    }

    private static int ScoreRecord(SourcePhoneRecord record)
    {
        var readiness = CatalogReadiness.Evaluate(
            record.ImageUrl,
            record.ReleasedAt,
            record.Specs.Select(spec => new CatalogSpecSnapshot(
                spec.Key,
                spec.DisplayValue,
                spec.SourceName,
                spec.Confidence,
                SpecStatus.Published,
                spec.IsCritical)));

        var score = record.Specs.Count * 10;
        if (readiness.IsPublicReady)
        {
            score += 500;
        }

        foreach (var key in CriticalCoverageKeys)
        {
            if (record.Specs.Any(spec =>
                    spec.Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(spec.DisplayValue)))
            {
                score += 70;
            }
        }

        return score;
    }

    private static string? NormalizeCatalogUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = WebUtility.HtmlDecode(value.Trim());
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return $"https:{value}";
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : null;
    }

    private static string? NormalizeCatalogUrl(string? value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = WebUtility.HtmlDecode(value.Trim());
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return $"https:{value}";
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : Uri.TryCreate(baseUri, value, out var relative)
                ? relative.ToString()
                : null;
    }

    private static string NormalizeSamsungProductPageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return url;
        }

        var buyMarker = parsed.AbsolutePath.IndexOf("/buy/", StringComparison.OrdinalIgnoreCase);
        if (buyMarker < 0)
        {
            return url;
        }

        var builder = new UriBuilder(parsed)
        {
            Path = $"{parsed.AbsolutePath[..buyMarker].TrimEnd('/')}/",
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri.ToString();
    }

    private static string NormalizeCatalogName(string? value)
    {
        var normalized = NormalizeName(value);
        normalized = ExclusiveSuffixRegex().Replace(normalized, string.Empty).Trim();
        normalized = EnterpriseEditionRegex().Replace(normalized, string.Empty).Trim();
        normalized = UnlockedSuffixRegex().Replace(normalized, string.Empty).Trim();
        normalized = StorageSuffixRegex().Replace(normalized, string.Empty).Trim();
        return normalized;
    }

    private static string NormalizeName(string? value)
    {
        value = NormalizeText(value) ?? string.Empty;
        return value.Replace("+", " Plus ", StringComparison.Ordinal);
    }

    private static string BuildModelSlug(string name)
        => Slugger.Slugify(name);

    private static bool IsExclusiveVariant(string? name)
        => !string.IsNullOrWhiteSpace(name) &&
           (name.Contains("exclusiva samsung.com", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("exclusive to samsung.com", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("samsung.com exclusive", StringComparison.OrdinalIgnoreCase));

    private static int? ExtractStorageGb(string? value)
    {
        var match = StorageHintRegex().Match(value ?? string.Empty);
        return match.Success &&
               int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storage) &&
               storage >= 16
            ? storage
            : null;
    }

    private static string? ExtractCatalogColor(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segment = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        segment = segment.Replace('-', ' ');
        var storageMatch = StorageHintRegex().Match(segment);
        if (!storageMatch.Success)
        {
            return null;
        }

        var prefix = segment[..storageMatch.Index].Trim();
        var tokens = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length <= 3)
        {
            return null;
        }

        var colorTokens = tokens.Skip(3).ToArray();
        return colorTokens.Length == 0
            ? null
            : string.Join(' ', colorTokens.Select(FormatToken));
    }

    private static bool IsItemList(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => type.GetString()?.Contains("ItemList", StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Array => type.EnumerateArray().Any(item => item.GetString()?.Contains("ItemList", StringComparison.OrdinalIgnoreCase) == true),
            _ => false,
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string HtmlToText(string html)
    {
        var withBreaks = LineBreakRegex().Replace(html, "\n");
        var withoutTags = TagRegex().Replace(withBreaks, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string? SearchableTextFromHtml(string html)
    {
        var parts = new List<string>();
        var visibleText = NormalizeText(HtmlToText(html));
        if (!string.IsNullOrWhiteSpace(visibleText))
        {
            parts.Add(visibleText);
        }

        foreach (Match match in AttributeTextRegex().Matches(html))
        {
            var value = NormalizeText(WebUtility.HtmlDecode(match.Groups["value"].Value));
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = value
            .Replace('\u00A0', ' ')
            .Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("™", string.Empty, StringComparison.Ordinal)
            .Replace("℠", string.Empty, StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);

        return WhitespaceRegex().Replace(sanitized, " ").Trim();
    }

    private static int Match(CatalogItem item, string normalizedQuery, IReadOnlyList<string> tokens)
    {
        var score = 0;

        if (item.NormalizedName == normalizedQuery || item.NormalizedFullName == normalizedQuery)
        {
            score += 360;
        }
        else if (item.NormalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
                 item.NormalizedFullName.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            score += 270;
        }
        else if (item.NormalizedName.Contains(normalizedQuery, StringComparison.Ordinal) ||
                 item.NormalizedFullName.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            score += 190;
        }

        foreach (var token in tokens)
        {
            if (PhoneSearchText.IsSearchNoiseToken(token))
            {
                continue;
            }

            if (item.NormalizedFullName.Contains(token, StringComparison.Ordinal) ||
                item.NormalizedName.Contains(token, StringComparison.Ordinal))
            {
                score += 70;
                continue;
            }

            return 0;
        }

        return score;
    }

    private static int QualityScore(string name)
    {
        var score = 0;
        if (name.Contains(' ', StringComparison.Ordinal))
        {
            score += 16;
        }

        if (MarketingKeywordRegex().IsMatch(name))
        {
            score += 14;
        }

        if (name.Contains("5G", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        return score;
    }

    private static bool LooksLikePhone(string brand, string name)
    {
        if (brand.Length == 0 || name.Length == 0)
        {
            return false;
        }

        var blob = $"{brand} {name}".ToLowerInvariant();
        return blob.Contains("galaxy", StringComparison.Ordinal) &&
               !blob.Contains("bundle", StringComparison.Ordinal) &&
               !blob.Contains("tablet", StringComparison.Ordinal) &&
               !blob.Contains("watch", StringComparison.Ordinal) &&
               !blob.Contains("buds", StringComparison.Ordinal);
    }

    private static string FormatToken(string token)
    {
        var lower = token.ToLowerInvariant();
        return lower.Length == 0
            ? string.Empty
            : char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static double ConfidenceFor(string key, bool critical)
    {
        return key switch
        {
            "chipset" or "ram" or "storage_base" or "display_size" or "main_camera" or "battery" => 0.98,
            "charging" or "ip_rating" => 0.97,
            _ when critical => 0.96,
            _ => 0.95,
        };
    }

    [GeneratedRegex("""<script type="application/ld\+json"[^>]*>(?<value>.*?)</script>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex JsonLdRegex();

    [GeneratedRegex("""<button class="pdd32-product-spec__toggle-cta"[^>]*>\s*(?<value>[^<]+?)\s*<svg""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SectionTitleRegex();

    [GeneratedRegex("""<li class="pdd32-product-spec__content-item"[^>]*>\s*(?:<p class="pdd32-product-spec__content-item-title">(?<name>.*?)</p>)?.*?<p class="pdd32-product-spec__content-item-desc">(?<value>.*?)</p>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ContentItemRegex();

    [GeneratedRegex("<meta name=\"date\" content=\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaDateRegex();

    [GeneratedRegex("<link rel=\"preload\" as=\"image\"[^>]+href=\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreloadImageRegex();

    [GeneratedRegex("""(?<value>Snapdragon(?:\s*®)?\s+\d+\s+(?:Elite|Gen\s*\d+)(?:\s+(?:Mobile Platform\s+)?(?:for|para)\s+Galaxy)?)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SnapdragonRegex();

    [GeneratedRegex("""(?<value>Exynos\s+\d{3,5})""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExynosRegex();

    [GeneratedRegex("""(?<value>Dimensity\s+\d{3,4}(?:\s+(?:Ultra|Pro|Max|Plus))?)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimensityRegex();

    [GeneratedRegex("""(?<value>Tensor\s+G\d+[A-Za-z]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TensorRegex();

    [GeneratedRegex("""(?<value>Helio\s+[GPX]\d{2,3}[A-Za-z]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HelioRegex();

    [GeneratedRegex("""(?<value>Kirin\s+\d{3,4}[A-Za-z]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KirinRegex();

    [GeneratedRegex("""(?<value>Unisoc\s+[A-Za-z]{1,2}\d{3,4}[A-Za-z]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnisocRegex();

    [GeneratedRegex("""(?<value>Adreno\s+\d{3,4})""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdrenoRegex();

    [GeneratedRegex("""(?<value>Mali-[A-Za-z0-9]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaliRegex();

    [GeneratedRegex("""(?<value>Immortalis-[A-Za-z0-9]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImmortalisRegex();

    [GeneratedRegex("""(?<value>Xclipse\s+\d+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XclipseRegex();

    [GeneratedRegex("""(?:(?:super\s*fast|fast|wired)\s+charging|charging|carregamento)[^.]{0,32}?(?<value>\d{2,3})\s*W|(?<value>\d{2,3})\s*W[^.]{0,32}?(?:charging|carregamento)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChargingContextRegex();

    [GeneratedRegex("""(?:wireless|sem fio)[^0-9]{0,20}(?<value>\d{1,3})\s*W|(?<value>\d{1,3})\s*W[^.]{0,30}(?:wireless|sem fio)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WirelessChargingRegex();

    [GeneratedRegex("""(?<value>IP\d{2})""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IpRatingRegex();

    [GeneratedRegex("""(?<value>Gorilla Glass(?:\s+(?:Victus(?:\s*2|\+)?|Armor(?:\s+[A-Za-z0-9+]+)?|DX(?:\+)?|Ceramic(?:\s+[A-Za-z0-9+]+)?|\d{1,2}[A-Za-z+]*)))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GorillaGlassRegex();

    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*(?:[""″]|pol|in|inch(?:es)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisplaySizeRegex();

    [GeneratedRegex("""(?<width>\d{3,4})\s*x\s*(?<height>\d{3,4})""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex("""(?<value>\d{2,3})\s*Hz""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RefreshRateRegex();

    [GeneratedRegex("""(?<value>\d+(?:\.\d+)?)\s*MP""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MegapixelRegex();

    [GeneratedRegex("""(?<value>\d{3,5})""", RegexOptions.CultureInvariant)]
    private static partial Regex BatteryRegex();

    [GeneratedRegex("""(?<value>\d{1,3})""", RegexOptions.CultureInvariant)]
    private static partial Regex HoursRegex();

    [GeneratedRegex("""(?<height>\d+(?:\.\d+)?)\s*x\s*(?<width>\d+(?:\.\d+)?)\s*x\s*(?<depth>\d+(?:\.\d+)?)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimensionsRegex();

    [GeneratedRegex("""(?<value>\d+(?:\.\d+)?)""", RegexOptions.CultureInvariant)]
    private static partial Regex WeightRegex();

    [GeneratedRegex("""(?<value>\d{1,4})\s*gb""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StorageHintRegex();

    [GeneratedRegex("""(?<value>\d{1,4})""", RegexOptions.CultureInvariant)]
    private static partial Regex StorageValueRegex();

    [GeneratedRegex("""\s*(?:\((?:exclusiva samsung\.com|exclusive to samsung\.com|samsung\.com exclusive)\)|(?:exclusiva samsung\.com|exclusive to samsung\.com|samsung\.com exclusive))\s*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExclusiveSuffixRegex();

    [GeneratedRegex("""\s*enterprise edition\s*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnterpriseEditionRegex();

    [GeneratedRegex("""\s*\(?unlocked\)?\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnlockedSuffixRegex();

    [GeneratedRegex("""\s*(?:\(|-|/)?\s*\d{2,4}\s*gb\)?\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StorageSuffixRegex();

    [GeneratedRegex("""(?<storage>\d{2,4})\s*GB(?:\s+of)?(?:\s+storage)?\s+with\s+(?<ram>\d{1,2})\s*GB(?:\s+of)?\s+(?:memory|RAM)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VariantPairRegex();

    [GeneratedRegex("""\b(pro|ultra|max|plus|edge|flip|fold|fe|note|galaxy)\b""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarketingKeywordRegex();

    [GeneratedRegex("""<br\s*/?>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex("(?:\\b(?:alt|data-desktop-alt|data-mobile-alt|content))=\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AttributeTextRegex();

    [GeneratedRegex("""<[^>]+>""", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex("""\s+""", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record CatalogEndpoint(
        string Region,
        string Url,
        int Priority)
    {
        public Uri BaseUri { get; } = new(Url, UriKind.Absolute);
    }

    private sealed record CatalogCandidate(
        string Name,
        string Slug,
        string SourceUrl,
        string? ImageUrl,
        string? Description,
        int? StorageGb,
        string? Color,
        bool IsExclusive,
        string Region,
        int EndpointPriority);

    private sealed record CatalogItem(
        string Name,
        string Slug,
        string SourceUrl,
        string? ImageUrl,
        string NormalizedName,
        string NormalizedFullName,
        IReadOnlyList<SourceVariantClaim> CatalogVariants,
        IReadOnlyList<string> SourceUrls)
    {
        public CoveragePhoneResult ToCoverageResult()
            => new(BrandName, BrandSlugValue, Name, Slug, SourceName, SourceUrl);
    }
}
