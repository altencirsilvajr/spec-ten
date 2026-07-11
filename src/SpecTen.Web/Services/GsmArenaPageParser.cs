using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using SpecTen.Web.Scraping;

namespace SpecTen.Web.Services;

public sealed class GsmArenaPageParser
{
    private const string PolicyStatus = "RobotsReviewed";
    private static readonly Regex TitleRegex = new(@"<h1[^>]*class=""section nobor""[^>]*>(?<value>.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ReleasedHighlightRegex = new(@"released-hl[^>]*>(?<value>.*?)</", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HistoryNameRegex = new(@"HISTORY_ITEM_NAME\s*=\s*""(?<value>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HistoryImageRegex = new(@"HISTORY_ITEM_IMAGE\s*=\s*""(?<value>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FirstImageRegex = new(@"<img[^>]+src=(?:""(?<value>[^""]+)""|(?<value>[^ >]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableRegex = new(@"<table[^>]*>(?<value>.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TableHeadingRegex = new(@"<th[^>]*colspan=""2""[^>]*>(?<value>.*?)</th>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RowRegex = new(@"<tr[^>]*>(?<value>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TtlRegex = new(@"<td[^>]*class=""ttl""[^>]*>(?<value>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex NfoRegex = new(@"<td[^>]*class=""nfo""(?<attrs>[^>]*)>(?<value>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex DataSpecRegex = new(@"data-spec=""(?<value>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BreakRegex = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ReleaseRegex = new(@"(?<year>20\d{2}),\s*(?<month>[A-Za-z]+)(?:\s+(?<day>\d{1,2}))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearOnlyRegex = new(@"(?<year>20\d{2})", RegexOptions.Compiled);
    private static readonly Regex DollarPriceRegex = new(@"\$(?<value>\d[\d,.]*)", RegexOptions.Compiled);
    private static readonly Regex StorageRegex = new(@"(?<value>\d+(?:\.\d+)?)\s*(?<unit>TB|GB)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RamRegex = new(@"(?<value>\d+(?:\.\d+)?)\s*GB\s*RAM", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InchesRegex = new(@"(?<value>\d+(?:\.\d+)?)\s*(?:""|inches|inch|in)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MilliampRegex = new(@"(?<value>\d{3,5})\s*mAh", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RefreshRateRegex = new(@"(?<value>\d{2,3})\s*Hz", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MegapixelRegex = new(@"(?<value>\d{1,3}(?:\.\d+)?)\s*MP", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IpRegex = new(@"IP\d{2}[A-Z0-9]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BenchmarkRegex = new(@"(?<name>AnTuTu|GeekBench)\s*:\s*(?<value>\d[\d,\.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
        "ul",
        "ui",
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

    public SourcePhoneRecord Parse(
        string sourceUrl,
        string html,
        DateTimeOffset collectedAt,
        string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var title = FirstMatchValue(TitleRegex, html) ?? FirstMatchValue(HistoryNameRegex, html);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("GSMArena page parser could not find the phone title.");
        }

        var brand = ResolveBrand(sourceUrl, title);
        var modelName = PhoneNameFormatter.ModelName(brand, title);
        var rows = ParseRows(html);

        var internalMemory = ValueByDataSpec(rows, "internalmemory");
        var variants = ParseVariants(internalMemory);
        var releasedAt = ParseReleaseDate(
            FirstMatchValue(ReleasedHighlightRegex, html) ??
            ValueByLabel(rows, "Launch", "Status", "Announced") ??
            ValueByDataSpec(rows, "status"));
        var summary = BuildSummary(
            modelName,
            ValueByDataSpec(rows, "displaysize"),
            ValueByDataSpec(rows, "chipset"),
            ValueByDataSpec(rows, "cam1modules"),
            ValueByDataSpec(rows, "batdescription1"),
            ValueByDataSpec(rows, "os"));

        var specs = BuildSpecs(rows, sourceName, sourceUrl, collectedAt, variants);
        var benchmarks = ParseBenchmarks(ValueByDataSpec(rows, "tbench"), sourceName, sourceUrl, collectedAt);

        return new SourcePhoneRecord(
            sourceName,
            sourceUrl,
            PolicyStatus,
            true,
            false,
            brand,
            null,
            modelName,
            summary,
            releasedAt,
            null,
            FirstMatchValue(HistoryImageRegex, html) ?? FirstMatchValue(FirstImageRegex, html),
            sourceUrl,
            specs,
            variants,
            benchmarks);
    }

    private static IReadOnlyList<SourceSpecClaim> BuildSpecs(
        IReadOnlyList<ParsedRow> rows,
        string sourceName,
        string sourceUrl,
        DateTimeOffset collectedAt,
        IReadOnlyList<SourceVariantClaim> variants)
    {
        var specs = new List<SourceSpecClaim>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string group, string key, string displayName, string? value, string? unit, bool critical)
        {
            value = NormalizeValue(value);
            if (string.IsNullOrWhiteSpace(value) || !keys.Add(key))
            {
                return;
            }

            specs.Add(new SourceSpecClaim(
                sourceName,
                sourceUrl,
                false,
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

        var displayType = ValueByDataSpec(rows, "displaytype");
        var displaySize = ValueByDataSpec(rows, "displaysize");
        var resolution = ValueByDataSpec(rows, "displayresolution");
        var mainCameraModules = ValueByDataSpec(rows, "cam1modules");
        var selfieCameraModules = ValueByDataSpec(rows, "cam2modules");
        var battery = ValueByDataSpec(rows, "batdescription1");
        var charging = ValueByLabel(rows, "Battery", "Charging");
        var bodyOther = ValueByDataSpec(rows, "bodyother");
        var internalMemory = ValueByDataSpec(rows, "internalmemory");

        Add("Mercado", "network", "Rede", ValueByDataSpec(rows, "nettech"), null, false);
        Add("Performance", "chipset", "Chipset", ValueByDataSpec(rows, "chipset"), null, true);
        Add("Performance", "cpu", "CPU", ValueByDataSpec(rows, "cpu"), null, false);
        Add("Performance", "gpu", "GPU", ValueByDataSpec(rows, "gpu"), null, false);

        Add("Memoria", "ram", "RAM", FirstVariantRam(variants) ?? ExtractRam(internalMemory), "GB", true);
        Add("Armazenamento", "storage_base", "Armazenamento base", FirstVariantStorage(variants) ?? ExtractStorage(internalMemory), "GB", false);
        Add("Armazenamento", "storage_options", "Opcoes de armazenamento", internalMemory, null, false);
        Add("Armazenamento", "card_slot", "Cartao de memoria", ValueByDataSpec(rows, "memoryslot"), null, false);

        Add("Tela", "display_size", "Tamanho da tela", ExtractDisplaySize(displaySize), "in", true);
        Add("Tela", "display_type", "Painel", displayType, null, false);
        Add("Tela", "resolution", "Resolucao", ExtractResolution(resolution), null, false);
        Add("Tela", "refresh_rate", "Taxa de atualizacao", ExtractRefreshRate(displayType), "Hz", false);
        Add("Tela", "protection", "Protecao frontal", ValueByDataSpec(rows, "displayprotection"), null, false);

        Add("Camera", "main_camera", "Camera principal", ExtractCameraByIndex(mainCameraModules, 0), "MP", true);
        Add("Camera", "ultrawide_camera", "Ultra-wide", ExtractUltrawideCamera(mainCameraModules), "MP", false);
        Add("Camera", "telephoto_camera", "Teleobjetiva", ExtractTelephotoCamera(mainCameraModules), "MP", false);
        Add("Camera", "selfie_camera", "Camera frontal", ExtractCameraByIndex(selfieCameraModules, 0), "MP", false);
        Add("Camera", "camera_features", "Recursos da camera", ValueByDataSpec(rows, "cam1features"), null, false);
        Add("Camera", "main_camera_video", "Video principal", ValueByDataSpec(rows, "cam1video"), null, false);
        Add("Camera", "selfie_camera_video", "Video frontal", ValueByDataSpec(rows, "cam2video"), null, false);

        Add("Bateria", "battery", "Bateria", ExtractBattery(battery), "mAh", true);
        Add("Bateria", "charging", "Carregamento", ExtractWiredCharging(charging), null, true);
        Add("Bateria", "wireless_charging", "Carregamento sem fio", ExtractWirelessCharging(charging), null, false);

        Add("Construcao", "dimensions", "Dimensoes", ValueByDataSpec(rows, "dimensions"), null, false);
        Add("Construcao", "weight", "Peso", ValueByDataSpec(rows, "weight"), "g", false);
        Add("Construcao", "build", "Construcao", ValueByDataSpec(rows, "build"), null, false);
        Add("Construcao", "sim", "SIM / eSIM", ValueByDataSpec(rows, "sim"), null, false);
        Add("Construcao", "ip_rating", "Resistencia", ExtractIpRating(bodyOther), null, false);

        Add("Conectividade", "wifi", "Wi-Fi", ValueByDataSpec(rows, "wlan"), null, false);
        Add("Conectividade", "bluetooth", "Bluetooth", ValueByDataSpec(rows, "bluetooth"), null, false);
        Add("Conectividade", "positioning", "Localizacao", ValueByDataSpec(rows, "gps"), null, false);
        Add("Conectividade", "nfc", "NFC", ValueByDataSpec(rows, "nfc"), null, false);
        Add("Conectividade", "radio", "Radio", ValueByDataSpec(rows, "radio"), null, false);
        Add("Conectividade", "usb", "USB", ValueByDataSpec(rows, "usb"), null, false);
        Add("Conectividade", "loudspeaker", "Alto-falante", ValueByLabel(rows, "Sound", "Loudspeaker"), null, false);
        Add("Conectividade", "headphone_jack", "Entrada 3.5 mm", ValueByLabel(rows, "Sound", "3.5mm jack"), null, false);

        Add("Software", "os", "Sistema", ValueByDataSpec(rows, "os"), null, false);
        Add("Software", "sensors", "Sensores", ValueByDataSpec(rows, "sensors"), null, false);

        Add("Mercado", "colors", "Cores", ValueByDataSpec(rows, "colors"), null, false);
        Add("Mercado", "models", "Modelos", ValueByDataSpec(rows, "models"), null, false);

        return specs;
    }

    private static IReadOnlyList<SourceBenchmarkClaim> ParseBenchmarks(
        string? performanceBlock,
        string sourceName,
        string sourceUrl,
        DateTimeOffset collectedAt)
    {
        if (string.IsNullOrWhiteSpace(performanceBlock))
        {
            return [];
        }

        var benchmarks = new List<SourceBenchmarkClaim>();
        foreach (Match match in BenchmarkRegex.Matches(performanceBlock))
        {
            if (!int.TryParse(match.Groups["value"].Value.Replace(",", string.Empty).Replace(".", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score))
            {
                continue;
            }

            var name = match.Groups["name"].Value switch
            {
                var value when value.Contains("antutu", StringComparison.OrdinalIgnoreCase) => "AnTuTu",
                var value when value.Contains("geekbench", StringComparison.OrdinalIgnoreCase) => "GeekBench",
                _ => match.Groups["name"].Value,
            };

            benchmarks.Add(new SourceBenchmarkClaim(name, score, sourceName, sourceUrl, collectedAt));
        }

        return benchmarks;
    }

    private static IReadOnlyList<SourceVariantClaim> ParseVariants(string? internalMemory)
    {
        if (string.IsNullOrWhiteSpace(internalMemory))
        {
            return [];
        }

        var variants = new List<SourceVariantClaim>();
        foreach (var part in internalMemory.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var storage = ParseStorageGb(part);
            var ram = ParseRamGb(part);
            if (storage is null && ram is null)
            {
                continue;
            }

            var name = $"{FormatStorageGb(storage)}{(ram is not null ? $" / {ram} GB RAM" : string.Empty)}".Trim();
            variants.Add(new SourceVariantClaim(name, ram, storage, null));
        }

        return variants
            .GroupBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(variant => variant.StorageGb)
            .ThenBy(variant => variant.RamGb)
            .ToList();
    }

    private static IReadOnlyList<ParsedRow> ParseRows(string html)
    {
        var rows = new List<ParsedRow>();
        foreach (Match tableMatch in TableRegex.Matches(html))
        {
            var tableHtml = tableMatch.Groups["value"].Value;
            var group = NormalizeValue(FirstMatchValue(TableHeadingRegex, tableHtml)) ?? "Outros";
            if (string.IsNullOrWhiteSpace(group))
            {
                continue;
            }

            foreach (Match rowMatch in RowRegex.Matches(tableHtml))
            {
                var rowHtml = rowMatch.Groups["value"].Value;
                var ttlMatch = TtlRegex.Match(rowHtml);
                var nfoMatch = NfoRegex.Match(rowHtml);
                if (!ttlMatch.Success || !nfoMatch.Success)
                {
                    continue;
                }

                var label = NormalizeValue(HtmlToText(ttlMatch.Groups["value"].Value));
                var value = NormalizeValue(HtmlToText(nfoMatch.Groups["value"].Value));
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var dataSpec = FirstMatchValue(DataSpecRegex, nfoMatch.Groups["attrs"].Value);
                rows.Add(new ParsedRow(group, label ?? string.Empty, NormalizeValue(dataSpec), value));
            }
        }

        return rows;
    }

    private static DateTimeOffset? ParseReleaseDate(string? value)
    {
        value = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var releaseMatch = ReleaseRegex.Match(value);
        if (releaseMatch.Success &&
            int.TryParse(releaseMatch.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            var monthName = releaseMatch.Groups["month"].Value;
            if (DateTime.TryParseExact(monthName, "MMMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthDate) ||
                DateTime.TryParseExact(monthName, "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out monthDate))
            {
                var day = releaseMatch.Groups["day"].Success &&
                          int.TryParse(releaseMatch.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDay)
                    ? parsedDay
                    : 1;

                return new DateTimeOffset(year, monthDate.Month, day, 0, 0, 0, TimeSpan.Zero);
            }
        }

        var yearMatch = YearOnlyRegex.Match(value);
        if (yearMatch.Success &&
            int.TryParse(yearMatch.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var releaseYear))
        {
            return new DateTimeOffset(releaseYear, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        return null;
    }

    private static string ResolveBrand(string sourceUrl, string title)
    {
        var slugPart = ExtractSlugPart(sourceUrl);
        foreach (var (prefix, brand) in BrandPrefixes)
        {
            if (slugPart.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return brand;
            }
        }

        var tokens = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 0 ? "Desconhecida" : FormatToken(tokens[0]);
    }

    private static string ExtractSlugPart(string sourceUrl)
    {
        var path = new Uri(sourceUrl).AbsolutePath.Trim('/');
        var fileName = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? path;
        var marker = fileName.LastIndexOf('-');
        return marker > 0 ? fileName[..marker] : fileName.Replace(".php", string.Empty, StringComparison.OrdinalIgnoreCase);
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

        var shortDisplay = ExtractDisplaySize(displaySize);
        if (!string.IsNullOrWhiteSpace(shortDisplay))
        {
            facts.Add($"{shortDisplay} de tela");
        }

        if (!string.IsNullOrWhiteSpace(chipset))
        {
            facts.Add(chipset);
        }

        var camera = ExtractCameraByIndex(mainCamera, 0);
        if (!string.IsNullOrWhiteSpace(camera))
        {
            facts.Add($"{camera} na camera principal");
        }

        var shortBattery = ExtractBattery(battery);
        if (!string.IsNullOrWhiteSpace(shortBattery))
        {
            facts.Add($"{shortBattery} de bateria");
        }

        if (!string.IsNullOrWhiteSpace(os))
        {
            facts.Add(os);
        }

        return facts.Count == 0
            ? $"{modelName} encontrado sob demanda na GSMArena para completar a ficha publica."
            : $"{modelName} com {string.Join(", ", facts)}.";
    }

    private static string? ValueByDataSpec(IReadOnlyList<ParsedRow> rows, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = rows.FirstOrDefault(row => row.DataSpec?.Equals(key, StringComparison.OrdinalIgnoreCase) == true);
            if (match is not null && !string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static string? ValueByLabel(IReadOnlyList<ParsedRow> rows, string group, params string[] labels)
    {
        foreach (var label in labels)
        {
            var match = rows.FirstOrDefault(row =>
                row.Group.Equals(group, StringComparison.OrdinalIgnoreCase) &&
                row.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

            if (match is not null && !string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static string? ExtractDisplaySize(string? value)
        => ExtractSingleMatch(value, InchesRegex, "in");

    private static string? ExtractResolution(string? value)
        => NormalizeValue(value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());

    private static string? ExtractRefreshRate(string? value)
        => ExtractSingleMatch(value, RefreshRateRegex, "Hz");

    private static string? ExtractBattery(string? value)
        => ExtractSingleMatch(value, MilliampRegex, "mAh");

    private static string? ExtractWiredCharging(string? value)
    {
        var lines = SplitLines(value);
        if (lines.Count == 0)
        {
            return null;
        }

        var wiredLine = lines.FirstOrDefault(line => line.Contains("wired", StringComparison.OrdinalIgnoreCase));
        return NormalizeValue(wiredLine ?? lines[0]);
    }

    private static string? ExtractWirelessCharging(string? value)
    {
        var lines = SplitLines(value);
        var wirelessLine = lines.FirstOrDefault(line => line.Contains("wireless", StringComparison.OrdinalIgnoreCase) ||
                                                        line.Contains("MagSafe", StringComparison.OrdinalIgnoreCase) ||
                                                        line.Contains("Qi", StringComparison.OrdinalIgnoreCase));
        return NormalizeValue(wirelessLine);
    }

    private static string? ExtractIpRating(string? value)
        => NormalizeValue(IpRegex.Match(value ?? string.Empty).Value);

    private static string? ExtractCameraByIndex(string? value, int index)
    {
        var lines = SplitLines(value);
        if (lines.Count <= index)
        {
            return null;
        }

        return ExtractSingleMatch(lines[index], MegapixelRegex, "MP");
    }

    private static string? ExtractUltrawideCamera(string? value)
    {
        foreach (var line in SplitLines(value))
        {
            if (line.Contains("ultrawide", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ultra wide", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractSingleMatch(line, MegapixelRegex, "MP");
            }
        }

        return null;
    }

    private static string? ExtractTelephotoCamera(string? value)
    {
        foreach (var line in SplitLines(value))
        {
            if (line.Contains("telephoto", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("periscope", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractSingleMatch(line, MegapixelRegex, "MP");
            }
        }

        return null;
    }

    private static string? ExtractRam(string? value)
        => ExtractSingleMatch(value, RamRegex, "GB");

    private static string? ExtractStorage(string? value)
    {
        var storage = ParseStorageGb(value);
        return FormatStorageGb(storage);
    }

    private static string? FirstVariantRam(IReadOnlyList<SourceVariantClaim> variants)
        => variants.FirstOrDefault(variant => variant.RamGb is not null)?.RamGb is int ram ? $"{ram} GB" : null;

    private static string? FirstVariantStorage(IReadOnlyList<SourceVariantClaim> variants)
        => FormatStorageGb(variants.FirstOrDefault(variant => variant.StorageGb is not null)?.StorageGb);

    private static int? ParseStorageGb(string? value)
    {
        var match = StorageRegex.Match(value ?? string.Empty);
        if (!match.Success ||
            !double.TryParse(match.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return match.Groups["unit"].Value.Equals("TB", StringComparison.OrdinalIgnoreCase)
            ? (int)Math.Round(amount * 1024, MidpointRounding.AwayFromZero)
            : (int)Math.Round(amount, MidpointRounding.AwayFromZero);
    }

    private static int? ParseRamGb(string? value)
    {
        var match = RamRegex.Match(value ?? string.Empty);
        return match.Success &&
               double.TryParse(match.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? (int)Math.Round(amount, MidpointRounding.AwayFromZero)
            : null;
    }

    private static string? FormatStorageGb(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return value >= 1024 && value % 1024 == 0
            ? $"{value / 1024} TB"
            : $"{value} GB";
    }

    private static string? ExtractSingleMatch(string? value, Regex regex, string suffix)
    {
        var match = regex.Match(value ?? string.Empty);
        return match.Success ? $"{match.Groups["value"].Value} {suffix}".Trim() : null;
    }

    private static string? FirstMatchValue(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? NormalizeValue(HtmlToText(match.Groups["value"].Value)) : null;
    }

    private static string HtmlToText(string html)
    {
        var withBreaks = BreakRegex.Replace(html, "\n");
        var withoutTags = TagRegex.Replace(withBreaks, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lines = value
            .Replace('\u00A0', ' ')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => WhitespaceRegex.Replace(line, " ").Trim())
            .Where(line => line.Length > 0)
            .ToList();

        return lines.Count == 0 ? null : string.Join(" / ", lines);
    }

    private static IReadOnlyList<string> SplitLines(string? value)
    {
        return NormalizeValue(value)?
            .Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? [];
    }

    private static double ConfidenceFor(string key, bool critical)
    {
        return key switch
        {
            "chipset" or "ram" or "storage_base" or "display_size" or "main_camera" or "battery" => 0.86,
            "charging" or "wireless_charging" => 0.82,
            _ when critical => 0.84,
            _ => 0.79,
        };
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
            _ when UppercaseTokens.Contains(lower) => lower.ToUpperInvariant(),
            _ when lower.Length <= 4 && lower.Any(char.IsDigit) => lower.ToUpperInvariant(),
            _ when lower.All(char.IsDigit) => lower,
            _ => char.ToUpperInvariant(lower[0]) + lower[1..],
        };
    }

    private sealed record ParsedRow(string Group, string Label, string? DataSpec, string Value);
}
