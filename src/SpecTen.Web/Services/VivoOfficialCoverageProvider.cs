using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using SpecTen.Web.Scraping;

namespace SpecTen.Web.Services;

public sealed partial class VivoOfficialCoverageProvider(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<VivoOfficialCoverageProvider> logger) : IOfficialCoverageProvider
{
    private const string SourceName = "vivo Official";
    private const string PolicyStatus = "OfficialCatalog";
    private const string OfficialDomain = "vivo.com";
    private const string BrandName = "Vivo";
    private const string BrandSlugValue = "vivo";
    private const string CatalogCacheKey = "coverage:official:vivo:catalog";
    private const string CatalogUrl = "https://www.vivo.com/in/products";
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromHours(12);

    public string Brand => BrandName;
    public string BrandSlug => BrandSlugValue;

    public async Task<IReadOnlyList<CoveragePhoneResult>> SearchAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return [];
        }

        var normalizedQuery = PhoneSearchText.Normalize(query);
        var tokens = PhoneSearchText.Tokenize(query);
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
        var overviewUrl = catalogItem.SourceUrl;
        var parameterUrl = BuildParameterUrl(catalogItem.Slug);

        var overviewHtml = await client.GetStringAsync(overviewUrl, cancellationToken);
        var parameterHtml = await client.GetStringAsync(parameterUrl, cancellationToken);

        return BuildRecord(catalogItem, overviewUrl, parameterUrl, overviewHtml, parameterHtml);
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
            var html = await client.GetStringAsync(CatalogUrl, cancellationToken);
            var items = ParseCatalog(html);
            cache.Set(CatalogCacheKey, items, CatalogCacheDuration);
            logger.LogInformation("Loaded {Count} official vivo catalog entries for public coverage fallback.", items.Count);
            return items;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to load official vivo catalog coverage.");
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

    private static IReadOnlyList<CatalogItem> ParseCatalog(string html)
    {
        var items = new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ProductLinkRegex().Matches(html))
        {
            var slug = Slugger.Slugify(match.Groups["slug"].Value);
            if (slug.Length == 0)
            {
                continue;
            }

            var body = match.Groups["body"].Value;
            var title = NormalizeText(FirstMatchValue(ProductTitleRegex(), body))
                        ?? NormalizeText(FirstMatchValue(ProductAltRegex(), body));
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var imageUrl = ResolveUrl(CatalogUrl, NormalizeText(FirstMatchValue(ProductImageRegex(), body)));
            var sourceUrl = $"https://www.vivo.com/in/products/{slug}";

            items[slug] = new CatalogItem(
                title,
                slug,
                sourceUrl,
                imageUrl,
                PhoneSearchText.Normalize(title),
                PhoneSearchText.Normalize($"{BrandName} {title}"));
        }

        return items.Values
            .OrderByDescending(item => QualityScore(item.Name))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SourcePhoneRecord BuildRecord(
        CatalogItem item,
        string overviewUrl,
        string parameterUrl,
        string overviewHtml,
        string parameterHtml)
    {
        var sectionMap = ParseParameterSections(parameterHtml);
        var imageUrl = ResolveUrl(parameterUrl, ExtractPrimaryImage(parameterHtml))
                       ?? item.ImageUrl
                       ?? ResolveUrl(overviewUrl, ExtractHeroImage(overviewHtml));

        var variants = ParseVariants(GetValue(sectionMap, "Storage", "RAM & ROM"));
        var specs = BuildSpecs(sectionMap, parameterUrl, variants);

        var summary = BuildSummary(
            item.Name,
            specs.FirstOrDefault(spec => spec.Key == "display_size")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "chipset")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "main_camera")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "battery")?.DisplayValue,
            specs.FirstOrDefault(spec => spec.Key == "os")?.DisplayValue);

        return new SourcePhoneRecord(
            SourceName,
            parameterUrl,
            PolicyStatus,
            true,
            true,
            BrandName,
            OfficialDomain,
            PhoneNameFormatter.ModelName(BrandName, item.Name),
            summary,
            null,
            null,
            imageUrl,
            imageUrl is null ? null : overviewUrl,
            specs,
            variants,
            []);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ParseParameterSections(string html)
    {
        var titles = ParameterSectionTitleRegex().Matches(html);
        var sections = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < titles.Count; index++)
        {
            var titleMatch = titles[index];
            var title = NormalizeSectionTitle(titleMatch.Groups["value"].Value);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var start = titleMatch.Index + titleMatch.Length;
            var end = index + 1 < titles.Count ? titles[index + 1].Index : html.Length;
            var sectionHtml = html[start..end];
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match attributeMatch in AttributeRegex().Matches(sectionHtml))
            {
                var attributeName = NormalizeText(HtmlToText(attributeMatch.Groups["name"].Value));
                var attributeValue = NormalizeValue(attributeMatch.Groups["value"].Value);
                if (string.IsNullOrWhiteSpace(attributeName) || string.IsNullOrWhiteSpace(attributeValue))
                {
                    continue;
                }

                attributes[attributeName] = attributeValue;
            }

            if (title.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                var locationValue = NormalizeValue(FirstMatchValue(SectionLineRegex(), sectionHtml));
                if (!string.IsNullOrWhiteSpace(locationValue))
                {
                    attributes["Location"] = locationValue;
                }
            }

            sections[title] = attributes;
        }

        return sections;
    }

    private static IReadOnlyList<SourceSpecClaim> BuildSpecs(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections,
        string sourceUrl,
        IReadOnlyList<SourceVariantClaim> variants)
    {
        var collectedAt = DateTimeOffset.UtcNow;
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

        var operatingSystem = GetValue(sections, "Basic", "Operating System");
        var androidVersion = GetValue(sections, "Basic", "Android Version");
        var ramAndRom = GetValue(sections, "Storage", "RAM & ROM");
        var cameraBlock = GetValue(sections, "Camera", "Camera");
        var location = GetValue(sections, "Location", "Location");

        Add("Mercado", "colors", "Cores", GetValue(sections, "Basic", "Color"), null, false);
        Add("Performance", "chipset", "Chipset", GetValue(sections, "Platform", "Processor"), null, true);
        Add("Performance", "cpu", "CPU", GetValue(sections, "Platform", "CPU Clock Speed"), null, false);

        Add("Memoria", "ram", "RAM", FirstVariantRam(variants) ?? ExtractRam(ramAndRom), "GB", true);
        Add("Armazenamento", "storage_base", "Armazenamento base", FirstVariantStorage(variants) ?? ExtractStorage(ramAndRom), "GB", true);
        Add("Armazenamento", "storage_options", "Opcoes de armazenamento", FormatStorageOptions(variants) ?? NormalizeStorageOptions(ramAndRom), null, false);
        Add("Armazenamento", "storage_type", "Tipo de armazenamento", GetValue(sections, "Storage", "ROM Type"), null, false);

        Add("Tela", "display_size", "Tamanho da tela", ExtractDisplaySize(GetValue(sections, "Display", "Size")), "in", true);
        Add("Tela", "resolution", "Resolucao", ExtractResolution(GetValue(sections, "Display", "Resolution")), null, false);
        Add("Tela", "display_type", "Painel", GetValue(sections, "Display", "Type"), null, false);
        Add("Tela", "refresh_rate", "Taxa de atualizacao", ExtractRefreshRate(GetValue(sections, "Display", "Refresh Rate")), "Hz", false);
        Add("Tela", "brightness", "Brilho de pico", GetValue(sections, "Display", "Local Peak Brightness"), null, false);

        Add("Camera", "main_camera", "Camera principal", ExtractPrimaryRearCamera(cameraBlock), "MP", true);
        Add("Camera", "ultrawide_camera", "Ultra-wide", ExtractUltrawideCamera(cameraBlock), "MP", false);
        Add("Camera", "telephoto_camera", "Teleobjetiva", ExtractTelephotoCamera(cameraBlock), "MP", false);
        Add("Camera", "selfie_camera", "Camera frontal", ExtractFrontCamera(cameraBlock), "MP", false);
        Add("Camera", "camera_features", "Recursos da camera", GetValue(sections, "Camera", "Scene Mode"), null, false);
        Add("Camera", "main_camera_video", "Video principal", GetValue(sections, "Media", "Video Recording Resolution"), null, false);

        Add("Bateria", "battery", "Bateria", ExtractBattery(GetValue(sections, "Battery", "Battery")), "mAh", true);
        Add("Bateria", "charging", "Carregamento", ExtractCharging(GetValue(sections, "Battery", "Charging Power")), "W", true);

        Add("Construcao", "dimensions", "Dimensoes", ExtractDimensions(GetValue(sections, "Design", "Dimensions")), null, false);
        Add("Construcao", "weight", "Peso", ExtractWeight(GetValue(sections, "Design", "Weight")), "g", false);
        Add("Construcao", "build", "Construcao", GetValue(sections, "Design", "Back Cover Material"), null, false);
        Add("Construcao", "ip_rating", "Resistencia", GetValue(sections, "Basic", "Ingress Protection Rating"), null, true);

        Add("Conectividade", "sim", "SIM / eSIM", GetValue(sections, "Network", "Card Slot"), null, false);
        Add("Conectividade", "network", "Rede", GetValue(sections, "Network", "Network type"), null, false);
        Add("Conectividade", "wifi", "Wi-Fi", NormalizeWirelessBands(GetValue(sections, "Connectivity", "Wi-Fi")), null, false);
        Add("Conectividade", "bluetooth", "Bluetooth", GetValue(sections, "Connectivity", "Bluetooth"), null, false);
        Add("Conectividade", "nfc", "NFC", GetValue(sections, "Connectivity", "NFC"), null, false);
        Add("Conectividade", "usb", "USB", GetValue(sections, "Connectivity", "USB"), null, false);
        Add("Conectividade", "positioning", "Localizacao", location, null, false);

        Add("Software", "os", "Sistema", CombineOperatingSystem(operatingSystem, androidVersion), null, false);
        Add("Software", "sensors", "Sensores", BuildSupportedSensors(sections), null, false);

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
            ? $"{modelName} encontrado no catalogo oficial da vivo para completar a ficha publica."
            : $"{modelName} com {string.Join(", ", facts)}.";
    }

    private static IReadOnlyList<SourceVariantClaim> ParseVariants(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var variants = new List<SourceVariantClaim>();
        foreach (Match match in VariantRegex().Matches(value))
        {
            if (!int.TryParse(match.Groups["ram"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ram))
            {
                continue;
            }

            if (!int.TryParse(match.Groups["storage"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storage))
            {
                continue;
            }

            variants.Add(new SourceVariantClaim($"{ram} GB / {storage} GB", ram, storage, null));
        }

        return variants
            .GroupBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(variant => variant.StorageGb)
            .ThenBy(variant => variant.RamGb)
            .ToList();
    }

    private static string? FirstVariantRam(IReadOnlyList<SourceVariantClaim> variants)
        => variants.FirstOrDefault(variant => variant.RamGb is not null)?.RamGb is int ram ? $"{ram} GB" : null;

    private static string? FirstVariantStorage(IReadOnlyList<SourceVariantClaim> variants)
        => variants.FirstOrDefault(variant => variant.StorageGb is not null)?.StorageGb is int storage ? $"{storage} GB" : null;

    private static string? FormatStorageOptions(IReadOnlyList<SourceVariantClaim> variants)
    {
        var storages = variants
            .Select(variant => variant.StorageGb)
            .Where(storage => storage is not null)
            .Select(storage => $"{storage!.Value} GB")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return storages.Count == 0 ? null : string.Join(" / ", storages);
    }

    private static string? NormalizeStorageOptions(string? value)
    {
        var variants = ParseVariants(value);
        return FormatStorageOptions(variants);
    }

    private static string? ExtractRam(string? value)
        => MatchWithSuffix(value, RamRegex(), "GB");

    private static string? ExtractStorage(string? value)
    {
        var match = StorageRegex().Match(value ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["storage"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storage)
            ? $"{storage} GB"
            : null;
    }

    private static string? ExtractBattery(string? value)
        => MatchWithSuffix(value, BatteryRegex(), "mAh");

    private static string? ExtractCharging(string? value)
        => MatchWithSuffix(value, ChargingRegex(), "W");

    private static string? ExtractDisplaySize(string? value)
        => MatchWithSuffix(value, DisplaySizeRegex(), "in");

    private static string? ExtractResolution(string? value)
    {
        var match = ResolutionRegex().Match(value ?? string.Empty);
        return match.Success
            ? $"{match.Groups["width"].Value} x {match.Groups["height"].Value}"
            : null;
    }

    private static string? ExtractRefreshRate(string? value)
        => MatchWithSuffix(value, RefreshRateRegex(), "Hz");

    private static string? ExtractDimensions(string? value)
    {
        foreach (var line in SplitLines(value))
        {
            if (!line.Contains("mm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = DimensionsRegex().Match(line.Replace('×', 'x'));
            if (match.Success)
            {
                return $"{match.Groups["height"].Value} x {match.Groups["width"].Value} x {match.Groups["depth"].Value} mm";
            }
        }

        return null;
    }

    private static string? ExtractWeight(string? value)
        => MatchWithSuffix(value, WeightRegex(), "g");

    private static string? ExtractFrontCamera(string? value)
    {
        var lines = SplitLines(value);
        var rearIndex = lines.FindIndex(line => line.StartsWith("Rear", StringComparison.OrdinalIgnoreCase));
        var frontLines = rearIndex > 0 ? lines.Take(rearIndex).ToList() : lines;
        return frontLines.Select(ExtractMegapixels).FirstOrDefault(megapixels => megapixels is not null);
    }

    private static string? ExtractPrimaryRearCamera(string? value)
    {
        var rearLines = RearCameraLines(value);
        return rearLines.Select(ExtractMegapixels).FirstOrDefault(megapixels => megapixels is not null);
    }

    private static string? ExtractUltrawideCamera(string? value)
    {
        var line = RearCameraLines(value)
            .FirstOrDefault(item => item.Contains("wide", StringComparison.OrdinalIgnoreCase));
        return ExtractMegapixels(line);
    }

    private static string? ExtractTelephotoCamera(string? value)
    {
        var line = RearCameraLines(value)
            .FirstOrDefault(item => item.Contains("telephoto", StringComparison.OrdinalIgnoreCase));
        return ExtractMegapixels(line);
    }

    private static IReadOnlyList<string> RearCameraLines(string? value)
    {
        var lines = SplitLines(value);
        var rearIndex = lines.FindIndex(line => line.StartsWith("Rear", StringComparison.OrdinalIgnoreCase));
        if (rearIndex < 0)
        {
            return [];
        }

        return lines
            .Skip(rearIndex + 1)
            .Where(line => !line.StartsWith("Front", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? ExtractMegapixels(string? value)
        => MatchWithSuffix(value, MegapixelRegex(), "MP");

    private static string? CombineOperatingSystem(string? operatingSystem, string? androidVersion)
    {
        var values = new[]
            {
                NormalizeText(operatingSystem),
                NormalizeText(androidVersion),
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 0 ? null : string.Join(" / ", values);
    }

    private static string? BuildSupportedSensors(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections)
    {
        if (!sections.TryGetValue("Sensors", out var sensorSection))
        {
            return null;
        }

        var supportedSensors = sensorSection
            .Where(item => item.Value.Contains("supported", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return supportedSensors.Count == 0 ? null : string.Join(", ", supportedSensors);
    }

    private static string? NormalizeWirelessBands(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Replace(';', '/');

    private static string? ExtractPrimaryImage(string html)
        => NormalizeText(
            FirstMatchValue(ColorImageRegex(), html) ??
            FirstMatchValue(ParameterImageRegex(), html));

    private static string? ExtractHeroImage(string html)
        => NormalizeText(FirstMatchValue(HeroImageRegex(), html));

    private static string BuildParameterUrl(string slug)
        => $"https://www.vivo.com/in/products/param/{slug}";

    private static string? GetValue(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections,
        string section,
        string name)
    {
        return sections.TryGetValue(section, out var sectionValues) &&
               sectionValues.TryGetValue(name, out var value)
            ? value
            : null;
    }

    private static string? ResolveUrl(string baseUrl, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return Uri.TryCreate(new Uri(baseUrl), value, out var relative)
            ? relative.ToString()
            : null;
    }

    private static string? FirstMatchValue(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string HtmlToText(string html)
    {
        var withBreaks = LineBreakRegex().Replace(html, "\n");
        var withoutTags = TagRegex().Replace(withBreaks, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lines = HtmlToText(value)
            .Replace('\u00A0', ' ')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => WhitespaceRegex().Replace(line, " ").Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('*'))
            .ToList();

        return lines.Count == 0 ? null : string.Join(" / ", lines);
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = value
            .Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("™", string.Empty, StringComparison.Ordinal)
            .Replace("℠", string.Empty, StringComparison.Ordinal);

        return WhitespaceRegex().Replace(sanitized, " ").Trim();
    }

    private static string? NormalizeSectionTitle(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var normalized = NormalizeText(HtmlToText(html));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = SectionTitleFootnoteRegex().Replace(normalized, string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static List<string> SplitLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static string? MatchWithSuffix(string? value, Regex regex, string suffix)
    {
        var match = regex.Match(value ?? string.Empty);
        return match.Success ? $"{match.Groups["value"].Value} {suffix}" : null;
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
            score += 18;
        }

        if (MarketingKeywordRegex().IsMatch(name))
        {
            score += 14;
        }

        return score;
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

    [GeneratedRegex("""<a\s+href="https://www\.vivo\.com/in/products/(?<slug>[^"#?/]+)"[^>]*>(?<body>.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ProductLinkRegex();

    [GeneratedRegex("""<p\s+class="vep-(?:pc|wap)-product-title[^"]*"[^>]*>\s*(?<value>[^<]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductTitleRegex();

    [GeneratedRegex(@"alt=""(?<value>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductAltRegex();

    [GeneratedRegex(@"data-src=""(?<value>https://[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductImageRegex();

    [GeneratedRegex("""<h2 class="parameter-title[^"]*"[^>]*>(?<value>.*?)</h2>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ParameterSectionTitleRegex();

    [GeneratedRegex("""<li class="attr-item[^"]*"[^>]*>.*?<p class="attr-item-name"[^>]*>(?<name>.*?)</p>.*?<div class="more-text-content"[^>]*>.*?<p[^>]*>(?<value>.*?)</p>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex("""<div class="more-text-content"[^>]*>.*?<p[^>]*>(?<value>.*?)</p>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SectionLineRegex();

    [GeneratedRegex(@"<li class=""(?:opacity\s+)?color-image-item""[^>]*>\s*<img[^>]+src=""(?<value>https://[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ColorImageRegex();

    [GeneratedRegex(@"<img class=""parameter-image[^""]*""[^>]+src=""(?<value>https://[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ParameterImageRegex();

    [GeneratedRegex(@"<img class=""cover""[^>]+data-one-src=""(?<value>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HeroImageRegex();

    [GeneratedRegex("""(?<ram>\d+)\s*GB\s*\+\s*(?<storage>\d+)\s*GB""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VariantRegex();

    [GeneratedRegex("""(?<value>\d+)\s*GB""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RamRegex();

    [GeneratedRegex("""\+\s*(?<storage>\d+)\s*GB""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StorageRegex();

    [GeneratedRegex("""(?<value>\d{3,5})\s*mAh""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BatteryRegex();

    [GeneratedRegex("""(?<value>\d{2,3})\s*W""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChargingRegex();

    [GeneratedRegex("""(?<value>\d+(?:\.\d+)?)\s*(?:[""″]|in)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisplaySizeRegex();

    [GeneratedRegex("""(?<width>\d+(?:\.\d+)?)\s*[x×]\s*(?<height>\d+(?:\.\d+)?)(?:\s*[x×]\s*(?<depth>\d+(?:\.\d+)?))?""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex("""(?<height>\d+(?:\.\d+)?)\s*[x×]\s*(?<width>\d+(?:\.\d+)?)\s*[x×]\s*(?<depth>\d+(?:\.\d+)?)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimensionsRegex();

    [GeneratedRegex("""(?<value>\d{2,3})\s*Hz""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RefreshRateRegex();

    [GeneratedRegex("""(?<value>\d{1,3}(?:\.\d+)?)\s*MP""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MegapixelRegex();

    [GeneratedRegex("""(?<value>\d{2,3}(?:\.\d+)?)""", RegexOptions.CultureInvariant)]
    private static partial Regex WeightRegex();

    [GeneratedRegex("""\b(pro|ultra|max|plus|fold|flip|phone|note|elite)\b""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarketingKeywordRegex();

    [GeneratedRegex("""<br\s*/?>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex("""<[^>]+>""", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex("""\s+""", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("""\s+\d+$""", RegexOptions.CultureInvariant)]
    private static partial Regex SectionTitleFootnoteRegex();

    private sealed record CatalogItem(
        string Name,
        string Slug,
        string SourceUrl,
        string? ImageUrl,
        string NormalizedName,
        string NormalizedFullName)
    {
        public CoveragePhoneResult ToCoverageResult()
            => new(BrandName, BrandSlugValue, Name, Slug, SourceName, SourceUrl);
    }
}
