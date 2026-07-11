namespace SpecTen.Web.Services;

public static class BrandNameFormatter
{
    public static string DisplayName(string? brand)
    {
        var cleanBrand = brand?.Trim() ?? string.Empty;
        if (cleanBrand.Length == 0)
        {
            return string.Empty;
        }

        return cleanBrand.ToLowerInvariant() switch
        {
            "lg" => "LG",
            "tcl" => "TCL",
            "zte" => "ZTE",
            "hmd" => "HMD",
            "iqoo" => "iQOO",
            _ => cleanBrand,
        };
    }
}
