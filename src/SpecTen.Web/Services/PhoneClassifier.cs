using System.Globalization;
using System.Text;
using SpecTen.Web.Data;

namespace SpecTen.Web.Services;

public sealed class PhoneClassifier
{
    public ClassificationResult Classify(
        string? chipset,
        IEnumerable<BenchmarkInput> benchmarks,
        DateTimeOffset? releasedAt = null,
        IReadOnlyDictionary<string, string?>? profileValues = null,
        decimal? launchPriceUsd = null)
    {
        var benchmarkList = benchmarks.ToList();
        var signals = new List<ClassificationSignal>();
        var chipsetResult = ClassifyByChipset(chipset);
        var shouldIgnoreLowerLegacyBenchmarkSignals = IsLegacyBenchmarkEra(releasedAt) &&
                                                     chipsetResult.Tier != ClassificationTier.Undefined;

        var antutu = benchmarkList
            .Where(score => score.Name.Contains("antutu", StringComparison.OrdinalIgnoreCase))
            .Select(score => score.Score)
            .DefaultIfEmpty()
            .Max();

        if (antutu > 0)
        {
            var antutuResult = antutu switch
            {
                >= 1_450_000 => new ClassificationResult(
                    ClassificationTier.Flagship,
                    antutu,
                    "benchmark",
                    $"Pontuacao AnTuTu de {antutu:N0}, faixa de topo para um smartphone premium."),
                >= 550_000 => new ClassificationResult(
                    ClassificationTier.MidRange,
                    antutu,
                    "benchmark",
                    $"Pontuacao AnTuTu de {antutu:N0}, suficiente para categoria intermediaria."),
                _ => new ClassificationResult(
                    ClassificationTier.Entry,
                    antutu,
                    "benchmark",
                    $"Pontuacao AnTuTu de {antutu:N0}, tipica de aparelho de entrada."),
            };

            if (!shouldIgnoreLowerLegacyBenchmarkSignals || antutuResult.Tier >= chipsetResult.Tier)
            {
                signals.Add(new ClassificationSignal("AnTuTu", antutuResult));
            }
        }

        var geekbench = benchmarkList
            .Where(score => score.Name.Contains("geekbench", StringComparison.OrdinalIgnoreCase))
            .Select(score => score.Score)
            .DefaultIfEmpty()
            .Max();

        if (geekbench > 0)
        {
            var geekbenchResult = geekbench switch
            {
                >= 4_500 => new ClassificationResult(
                    ClassificationTier.Flagship,
                    geekbench,
                    "benchmark",
                    $"Geekbench multi-core de {geekbench:N0}, faixa de topo para smartphone premium."),
                >= 1_800 => new ClassificationResult(
                    ClassificationTier.MidRange,
                    geekbench,
                    "benchmark",
                    $"Geekbench multi-core de {geekbench:N0}, faixa intermediaria."),
                _ => new ClassificationResult(
                    ClassificationTier.Entry,
                    geekbench,
                    "benchmark",
                    $"Geekbench multi-core de {geekbench:N0}, faixa de entrada."),
            };

            if (!shouldIgnoreLowerLegacyBenchmarkSignals || geekbenchResult.Tier >= chipsetResult.Tier)
            {
                signals.Add(new ClassificationSignal("Geekbench", geekbenchResult));
            }
        }

        if (chipsetResult.Tier != ClassificationTier.Undefined)
        {
            signals.Add(new ClassificationSignal("Chipset", chipsetResult));
        }

        if (signals.Count == 0)
        {
            var profileResult = ClassifyByProfile(profileValues, launchPriceUsd);
            return profileResult.Tier != ClassificationTier.Undefined
                ? profileResult
                : chipsetResult;
        }

        var chosenTier = signals
            .GroupBy(signal => signal.Result.Tier)
            .Select(group => new
            {
                Tier = group.Key,
                Votes = group.Count()
            })
            .OrderByDescending(group => group.Votes)
            .ThenBy(group => group.Tier)
            .First()
            .Tier;

        var supportingSignals = signals
            .Where(signal => signal.Result.Tier == chosenTier)
            .OrderBy(signal => signal.SourceName == "Chipset" ? 1 : 0)
            .ThenByDescending(signal => signal.Result.Score)
            .ToList();

        var primarySignal = supportingSignals.First();
        var conflictingSignals = signals
            .Where(signal => signal.Result.Tier != chosenTier)
            .Select(signal => $"{signal.SourceName}: {LabelFor(signal.Result.Tier)}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var explanation = primarySignal.Result.Explanation;
        if (supportingSignals.Count > 1)
        {
            var corroboratingSources = string.Join(" e ", supportingSignals.Skip(1).Select(signal => signal.SourceName));
            explanation = $"{explanation} Confirmado tambem por {corroboratingSources.ToLowerInvariant()}.";
        }

        if (conflictingSignals.Count > 0)
        {
            explanation = $"{explanation} Houve divergencia com {string.Join("; ", conflictingSignals)}, entao a faixa continua estimada.";
        }

        return primarySignal.Result with { Explanation = explanation };
    }

    private static ClassificationResult ClassifyByProfile(
        IReadOnlyDictionary<string, string?>? profileValues,
        decimal? launchPriceUsd)
    {
        if (profileValues is null || profileValues.Count == 0)
        {
            return new ClassificationResult(
                ClassificationTier.Undefined,
                0,
                "missing-data",
                "Sem benchmark ou chipset confiavel suficiente para classificar.");
        }

        var score = 0;
        var midMarkers = 0;
        var flagshipMarkers = 0;
        var reasons = new List<string>();

        if (launchPriceUsd is not null)
        {
            if (launchPriceUsd >= 900m)
            {
                score += 26;
                flagshipMarkers += 2;
                reasons.Add("faixa de preco premium");
            }
            else if (launchPriceUsd >= 650m)
            {
                score += 20;
                flagshipMarkers += 1;
                reasons.Add("faixa de preco alta");
            }
            else if (launchPriceUsd >= 350m)
            {
                score += 12;
                midMarkers += 1;
                reasons.Add("faixa de preco intermediaria");
            }
        }

        var displayType = GetProfileValue(profileValues, "display_type");
        if (ContainsAnyFragment(displayType, "ltpo"))
        {
            score += 12;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add("painel LTPO");
        }
        else if (ContainsAnyFragment(displayType, "amoled", "oled", "xdr"))
        {
            score += 8;
            midMarkers += 1;
            reasons.Add("painel AMOLED/OLED");
        }

        var refreshRate = TryParseFirstInteger(GetProfileValue(profileValues, "refresh_rate"));
        if (refreshRate >= 120)
        {
            score += 6;
            midMarkers += 1;
            reasons.Add("tela 120 Hz");
        }
        else if (refreshRate >= 90)
        {
            score += 3;
            reasons.Add("tela 90 Hz");
        }

        var ipRating = GetProfileValue(profileValues, "ip_rating");
        if (ContainsAnyFragment(ipRating, "ip68", "ip69"))
        {
            score += 8;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add(ipRating!.ToUpperInvariant());
        }
        else if (ContainsAnyFragment(ipRating, "ip67", "ip66"))
        {
            score += 6;
            midMarkers += 1;
            reasons.Add(ipRating!.ToUpperInvariant());
        }

        var wifi = GetProfileValue(profileValues, "wifi");
        if (ContainsAnyFragment(wifi, "wifi 7", "wi-fi 7", "6ghz"))
        {
            score += 6;
            midMarkers += 1;
            if (ContainsAnyFragment(wifi, "wifi 7", "wi-fi 7"))
            {
                flagshipMarkers += 1;
            }
            reasons.Add("Wi-Fi avancado");
        }
        else if (ContainsAnyFragment(wifi, "wifi 6", "wi-fi 6", "802.11ax", " ax"))
        {
            score += 4;
            midMarkers += 1;
            reasons.Add("Wi-Fi 6");
        }

        if (HasMeaningfulValue(GetProfileValue(profileValues, "wireless_charging")))
        {
            score += 10;
            midMarkers += 1;
            flagshipMarkers += 2;
            reasons.Add("carregamento sem fio");
        }

        var chargingWatts = TryParseFirstInteger(GetProfileValue(profileValues, "charging"));
        if (chargingWatts >= 80)
        {
            score += 8;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add("carga rapida alta");
        }
        else if (chargingWatts >= 45)
        {
            score += 6;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add($"{chargingWatts} W");
        }
        else if (chargingWatts >= 25)
        {
            score += 4;
            midMarkers += 1;
            reasons.Add("carga rapida");
        }

        var ramGb = TryParseFirstInteger(GetProfileValue(profileValues, "ram"));
        if (ramGb >= 12)
        {
            score += 8;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add("12 GB de RAM");
        }
        else if (ramGb >= 8)
        {
            score += 5;
            midMarkers += 1;
            reasons.Add("8 GB de RAM");
        }
        else if (ramGb >= 6)
        {
            score += 3;
        }

        var storageGb = TryParseFirstInteger(GetProfileValue(profileValues, "storage_base"));
        if (storageGb >= 512)
        {
            score += 5;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add("512 GB");
        }
        else if (storageGb >= 256)
        {
            score += 4;
            midMarkers += 1;
            reasons.Add("256 GB");
        }
        else if (storageGb >= 128)
        {
            score += 2;
        }

        var video = GetProfileValue(profileValues, "main_camera_video");
        if (ContainsAnyFragment(video, "8k"))
        {
            score += 8;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add("video 8K");
        }
        else if (ContainsAnyFragment(video, "4k", "uhd"))
        {
            score += 4;
            midMarkers += 1;
            reasons.Add("video 4K");
        }

        var usb = GetProfileValue(profileValues, "usb");
        if (ContainsAnyFragment(usb, "usb 3", "3.1", "3.2"))
        {
            score += 6;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add("USB 3.x");
        }

        var build = GetProfileValue(profileValues, "build");
        if (ContainsAnyFragment(build, "titanium"))
        {
            score += 4;
            flagshipMarkers += 1;
            reasons.Add("estrutura premium");
        }
        else if (ContainsAnyFragment(build, "victus", "gorilla glass", "ceramic"))
        {
            score += 3;
            reasons.Add("construcao reforcada");
        }

        var cpuClock = TryParseHighestGigahertz(GetProfileValue(profileValues, "cpu"));
        if (cpuClock >= 3.2m)
        {
            score += 7;
            midMarkers += 1;
            flagshipMarkers += 1;
            reasons.Add("CPU acima de 3.2 GHz");
        }
        else if (cpuClock >= 2.8m)
        {
            score += 4;
            midMarkers += 1;
            reasons.Add("CPU perto de 3 GHz");
        }

        if (flagshipMarkers >= 2 && score >= 30)
        {
            return new ClassificationResult(
                ClassificationTier.Flagship,
                score,
                "official-profile",
                $"Sem chipset publico, mas o perfil oficial do hardware ({JoinReasons(reasons)}) aponta para faixa topo de linha.");
        }

        if (midMarkers >= 2 && score >= 14)
        {
            return new ClassificationResult(
                ClassificationTier.MidRange,
                score,
                "official-profile",
                $"Sem chipset publico, mas o perfil oficial do hardware ({JoinReasons(reasons)}) aponta para faixa intermediaria.");
        }

        if (score > 0)
        {
            return new ClassificationResult(
                ClassificationTier.Entry,
                score,
                "official-profile",
                $"Sem chipset publico; os sinais oficiais disponiveis ({JoinReasons(reasons)}) sugerem aparelho de entrada.");
        }

        return new ClassificationResult(
            ClassificationTier.Undefined,
            0,
            "missing-data",
            "Sem benchmark ou chipset confiavel suficiente para classificar.");
    }

    private static ClassificationResult ClassifyByChipset(string? chipset)
    {
        if (string.IsNullOrWhiteSpace(chipset))
        {
            return new ClassificationResult(
                ClassificationTier.Undefined,
                0,
                "missing-data",
                "Sem benchmark ou chipset confiavel suficiente para classificar.");
        }

        var normalized = NormalizeChipset(chipset);
        if (IsFlagshipChipset(normalized))
        {
            return new ClassificationResult(
                ClassificationTier.Flagship,
                100,
                "chipset",
                $"Chipset {chipset} mapeado como plataforma topo de linha da sua geracao.");
        }

        if (IsMidRangeChipset(normalized))
        {
            return new ClassificationResult(
                ClassificationTier.MidRange,
                60,
                "chipset",
                $"Chipset {chipset} mapeado como plataforma intermediaria da sua geracao.");
        }

        if (IsEntryChipset(normalized))
        {
            return new ClassificationResult(
                ClassificationTier.Entry,
                25,
                "chipset",
                $"Chipset {chipset} mapeado como plataforma de entrada ou basica da sua geracao.");
        }

        return new ClassificationResult(
            ClassificationTier.Undefined,
            0,
            "missing-data",
            $"Chipset {chipset} ainda nao esta na tabela curada e precisa de benchmark para classificar.");
    }

    private static bool IsFlagshipChipset(string normalized)
    {
        return normalized.Contains("apple a", StringComparison.Ordinal) ||
               ContainsAny(
                   normalized,
                   "snapdragon 8 ",
                   "snapdragon 888",
                   "snapdragon 870",
                   "snapdragon 865",
                   "snapdragon 860",
                   "snapdragon 855",
                   "snapdragon 845",
                   "snapdragon 835",
                   "snapdragon 821",
                   "snapdragon 820",
                   "snapdragon 810",
                   "snapdragon 808",
                   "snapdragon 805",
                   "snapdragon 801",
                   "snapdragon 800") ||
               normalized.Contains("dimensity 9", StringComparison.Ordinal) ||
               ContainsAny(
                   normalized,
                   "exynos 2500",
                   "exynos 2400",
                   "exynos 2200",
                   "exynos 2100",
                   "exynos 990",
                   "exynos 9825",
                   "exynos 9820",
                   "exynos 9810",
                   "exynos 8895",
                   "exynos 8890",
                   "exynos 7420") ||
               normalized.Contains("kirin 9", StringComparison.Ordinal) ||
               normalized.Contains("helio x", StringComparison.Ordinal);
    }

    private static bool IsMidRangeChipset(string normalized)
    {
        if (ContainsAny(normalized, "snapdragon 8s", "snapdragon 7", "snapdragon 6"))
        {
            return true;
        }

        if (normalized.Contains("dimensity 8", StringComparison.Ordinal) ||
            normalized.Contains("dimensity 7", StringComparison.Ordinal) ||
            normalized.Contains("tensor", StringComparison.Ordinal))
        {
            return true;
        }

        return ContainsAny(
            normalized,
            "helio g9",
            "helio g8",
            "helio g7",
            "helio g6",
            "helio p9",
            "helio p8",
            "helio p7",
            "kirin 8",
            "kirin 810",
            "kirin 820",
            "kirin 830",
            "exynos 1680",
            "exynos 1580",
            "exynos 1480",
            "exynos 1380",
            "exynos 1330",
            "exynos 1280",
            "exynos 1080",
            "exynos 980",
            "exynos 880",
            "unisoc t820",
            "unisoc t770",
            "unisoc t760",
            "unisoc t750",
            "unisoc t740",
            "unisoc t720",
            "unisoc t710",
            "tiger t820",
            "tiger t770",
            "tiger t760",
            "tiger t750",
            "tiger t740",
            "tiger t720",
            "tiger t710");
    }

    private static bool IsEntryChipset(string normalized)
    {
        if (normalized.Contains("dimensity 6", StringComparison.Ordinal))
        {
            return true;
        }

        if (ContainsAny(normalized, "snapdragon 4", "snapdragon 2", "snapdragon 3"))
        {
            return true;
        }

        if (normalized.Contains("helio g3", StringComparison.Ordinal) ||
            normalized.Contains("helio g2", StringComparison.Ordinal) ||
            normalized.Contains("helio p3", StringComparison.Ordinal) ||
            normalized.Contains("helio p2", StringComparison.Ordinal) ||
            normalized.Contains("unisoc", StringComparison.Ordinal) ||
            normalized.Contains("tiger", StringComparison.Ordinal))
        {
            return true;
        }

        return ContainsAny(
            normalized,
            "exynos 9611",
            "exynos 9610",
            "exynos 850",
            "exynos 7904",
            "exynos 7885",
            "exynos 7870",
            "kirin 7",
            "kirin 6");
    }

    private static bool ContainsAny(string value, params string[] fragments)
    {
        return fragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));
    }

    private static bool ContainsAnyFragment(string? value, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasMeaningfulValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return normalized is not "-" and not "?" and not "N/A" and not "n/a" and not "No" and not "no";
    }

    private static string? GetProfileValue(IReadOnlyDictionary<string, string?> profileValues, string key)
    {
        return profileValues.TryGetValue(key, out var value)
            ? value
            : null;
    }

    private static int TryParseFirstInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var digits = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                digits.Append(character);
                continue;
            }

            if (digits.Length > 0)
            {
                break;
            }
        }

        return digits.Length > 0 && int.TryParse(digits.ToString(), out var parsed)
            ? parsed
            : 0;
    }

    private static decimal TryParseHighestGigahertz(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        decimal highest = 0;
        foreach (var rawToken in value.Split([' ', ',', ';', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = rawToken.Replace("GHz", "", StringComparison.OrdinalIgnoreCase)
                .Replace("ghz", "", StringComparison.OrdinalIgnoreCase)
                .Trim()
                .Replace(',', '.');

            if (decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) &&
                parsed > highest)
            {
                highest = parsed;
            }
        }

        return highest;
    }

    private static string JoinReasons(IReadOnlyList<string> reasons)
    {
        if (reasons.Count == 0)
        {
            return "sem sinais fortes adicionais";
        }

        return string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
    }

    private static bool IsLegacyBenchmarkEra(DateTimeOffset? releasedAt)
    {
        return releasedAt is not null && releasedAt.Value.Year <= 2020;
    }

    private static string NormalizeChipset(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSeparator = true;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append(' ');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static string LabelFor(ClassificationTier tier)
    {
        return tier switch
        {
            ClassificationTier.Entry => "Entrada",
            ClassificationTier.MidRange => "Intermediario",
            ClassificationTier.Flagship => "Top de linha",
            _ => "Indefinido",
        };
    }
}

public sealed record BenchmarkInput(string Name, int Score);

file sealed record ClassificationSignal(string SourceName, ClassificationResult Result);

public sealed record ClassificationResult(
    ClassificationTier Tier,
    int Score,
    string Basis,
    string Explanation);
