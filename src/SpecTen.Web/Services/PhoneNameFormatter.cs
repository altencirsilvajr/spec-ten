namespace SpecTen.Web.Services;

public static class PhoneNameFormatter
{
    private static readonly string[] ChipsetPrefixes =
    [
        "Snapdragon",
        "Dimensity",
        "Exynos",
        "Tensor",
        "Helio",
        "Kirin",
    ];

    public static string ModelName(string? brand, string? name)
    {
        var cleanName = name?.Trim() ?? string.Empty;
        var cleanBrand = BrandNameFormatter.DisplayName(brand);

        if (cleanName.Length == 0)
        {
            return cleanBrand;
        }

        if (cleanBrand.Length == 0)
        {
            return cleanName;
        }

        if (cleanName.Equals(cleanBrand, StringComparison.OrdinalIgnoreCase))
        {
            return cleanName;
        }

        if (TryResolveSubBrand(cleanName, out _, out var subBrandModelName))
        {
            return subBrandModelName;
        }

        if (StartsWithBrand(cleanName, cleanBrand))
        {
            return cleanName[cleanBrand.Length..].TrimStart(' ', '-', '_');
        }

        return cleanName;
    }

    public static string DisplayBrand(string? brand, string? name)
    {
        var cleanBrand = BrandNameFormatter.DisplayName(brand);
        var cleanName = name?.Trim() ?? string.Empty;

        if (TryResolveSubBrand(cleanName, out var subBrand, out _))
        {
            return subBrand;
        }

        return cleanBrand;
    }

    public static string FullName(string? brand, string? name)
    {
        var cleanBrand = DisplayBrand(brand, name);
        var modelName = ModelName(brand, name);

        if (cleanBrand.Length == 0)
        {
            return modelName;
        }

        if (modelName.Length == 0 || modelName.Equals(cleanBrand, StringComparison.OrdinalIgnoreCase))
        {
            return cleanBrand;
        }

        return $"{cleanBrand} {modelName}";
    }

    public static string CompactChipset(string? chipset)
    {
        var value = chipset?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }

        foreach (var prefix in ChipsetPrefixes)
        {
            var index = value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                value = value[index..];
                break;
            }
        }

        value = value
            .Replace(" Mobile Platform", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(for Galaxy)", "for Galaxy", StringComparison.OrdinalIgnoreCase);

        var nodeIndex = value.LastIndexOf('(');
        if (nodeIndex > 0 && value.EndsWith(')'))
        {
            var suffix = value[nodeIndex..];
            if (suffix.Contains("nm", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..nodeIndex].TrimEnd();
            }
        }

        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool StartsWithBrand(string cleanName, string cleanBrand)
    {
        if (!cleanName.StartsWith(cleanBrand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (cleanName.Length == cleanBrand.Length)
        {
            return true;
        }

        return cleanName[cleanBrand.Length] is ' ' or '-' or '_';
    }

    private static bool TryResolveSubBrand(string cleanName, out string displayBrand, out string modelName)
    {
        if (TryMatchSubBrand(cleanName, "nubia redmagic", "RedMagic", out displayBrand, out modelName) ||
            TryMatchSubBrand(cleanName, "redmagic", "RedMagic", out displayBrand, out modelName) ||
            TryMatchSubBrand(cleanName, "iqoo", "iQOO", out displayBrand, out modelName) ||
            TryMatchSubBrand(cleanName, "poco", "POCO", out displayBrand, out modelName) ||
            TryMatchSubBrand(cleanName, "redmi", "Redmi", out displayBrand, out modelName) ||
            TryMatchSubBrand(cleanName, "nubia", "nubia", out displayBrand, out modelName))
        {
            return true;
        }

        displayBrand = string.Empty;
        modelName = string.Empty;
        return false;
    }

    private static bool TryMatchSubBrand(
        string cleanName,
        string prefix,
        string matchedDisplayBrand,
        out string displayBrand,
        out string modelName)
    {
        displayBrand = string.Empty;
        modelName = string.Empty;

        if (!cleanName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (cleanName.Length > prefix.Length &&
            cleanName[prefix.Length] is not (' ' or '-' or '_'))
        {
            return false;
        }

        modelName = cleanName[prefix.Length..].TrimStart(' ', '-', '_');
        if (modelName.Length == 0)
        {
            modelName = matchedDisplayBrand;
        }

        displayBrand = matchedDisplayBrand;
        return true;
    }
}
