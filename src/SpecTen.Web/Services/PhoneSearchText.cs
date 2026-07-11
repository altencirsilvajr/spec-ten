using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpecTen.Web.Services;

public static partial class PhoneSearchText
{
    private static readonly HashSet<string> SearchNoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "5g",
        "4g",
        "lte",
        "wifi",
        "global",
        "row",
        "cn",
        "emea",
        "latam",
        "uw",
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> Tokenize(string query)
    {
        return SearchTokenRegex()
            .Split(query)
            .Select(Normalize)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool IsSearchNoiseToken(string? token)
    {
        return !string.IsNullOrWhiteSpace(token) &&
               SearchNoiseTokens.Contains(token);
    }

    public static bool IsCompactModelToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var value = token.Trim();
        return value.Length <= 5 &&
               value.Any(char.IsLetter) &&
               value.Any(char.IsDigit);
    }

    [GeneratedRegex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled)]
    private static partial Regex SearchTokenRegex();
}
