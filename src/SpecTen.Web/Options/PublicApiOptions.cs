namespace SpecTen.Web.Options;

public sealed class PublicApiOptions
{
    public const string SectionName = "PublicApi";

    public int RateLimitPermitLimit { get; set; } = 120;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public int SearchCacheSeconds { get; set; } = 20;
    public int SuggestionCacheSeconds { get; set; } = 10;
    public int PhoneCacheSeconds { get; set; } = 120;
    public int CompareCacheSeconds { get; set; } = 30;
    public int SitemapCacheSeconds { get; set; } = 900;
    public int RobotsCacheSeconds { get; set; } = 3600;
}
