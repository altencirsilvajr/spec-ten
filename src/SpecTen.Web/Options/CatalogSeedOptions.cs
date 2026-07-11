namespace SpecTen.Web.Options;

public sealed class CatalogSeedOptions
{
    public const string SectionName = "CatalogSeed";

    public bool Enabled { get; set; }
    public string? FilePath { get; set; }
    public string? DataUrl { get; set; }
    public string DefaultSourceName { get; set; } = "Catalog seed";
    public string? DefaultSourceUrl { get; set; }
    public string DefaultPolicyStatus { get; set; } = "ManualFeed";
    public bool RobotsAllowed { get; set; } = true;
    public bool IsOfficialSource { get; set; }
}
