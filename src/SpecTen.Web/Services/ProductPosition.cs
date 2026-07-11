namespace SpecTen.Web.Services;

/// <summary>
/// Posicionamento comercial curado. Ele nao e deduzido de benchmark: desempenho e
/// segmento de mercado respondem perguntas diferentes e precisam permanecer separados.
/// </summary>
public static class ProductPosition
{
    private static readonly IReadOnlyDictionary<string, string> CuratedSegments =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["xiaomi/poco-x8-pro"] = "Intermediario premium",
        };

    public static string? For(string brandSlug, string slug)
    {
        return CuratedSegments.GetValueOrDefault($"{brandSlug}/{slug}");
    }
}
