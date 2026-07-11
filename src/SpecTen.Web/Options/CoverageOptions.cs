namespace SpecTen.Web.Options;

public sealed class CoverageOptions
{
    public const string SectionName = "Coverage";

    public bool Enabled { get; set; } = true;
    public string DataUrl { get; set; } = "https://www.gsmarena.com/sitemaps/phones.xml";
    public string SourceName { get; set; } = "GSMArena";
    public string SourceUrl { get; set; } = "https://www.gsmarena.com/";
    public string SnapshotFilePath { get; set; } = "Data/coverage-index.snapshot.json";
    public int RefreshHours { get; set; } = 24;
    public int MinimumQueryLength { get; set; } = 2;
    public bool OnDemandHydrationEnabled { get; set; } = true;
    public int ExactHydrationLimit { get; set; } = 1;
    public int MakerPageLimit { get; set; } = 24;
    public int MakerPageDelayMilliseconds { get; set; } = 250;
    public int CatalogEntryRefreshHours { get; set; } = 168;
}
