namespace SpecTen.Web.Services;

internal static class ChipsetText
{
    private static readonly string[] KnownFamilies =
    [
        "snapdragon",
        "exynos",
        "dimensity",
        "tensor",
        "helio",
        "kirin",
        "unisoc",
        "mediatek",
        "qualcomm",
        "apple a",
        "tiger",
        "spreadtrum",
    ];

    private static readonly string[] MarketingFragments =
    [
        "enquanto",
        "processador",
        "camera",
        "bateria",
        "display",
        "tela",
    ];

    public static bool IsSuspicious(string? chipset)
    {
        if (string.IsNullOrWhiteSpace(chipset))
        {
            return false;
        }

        var normalized = PhoneSearchText.Normalize(chipset);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (MarketingFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
        {
            return true;
        }

        if (KnownFamilies.Any(family => normalized.Contains(family, StringComparison.Ordinal)))
        {
            return false;
        }

        return PhoneSearchText.Tokenize(chipset).Count > 5;
    }
}
