namespace SpecTen.Web.Options;

public sealed class ScrapingOptions
{
    public const string SectionName = "Scraping";

    public bool Enabled { get; set; }
    public bool UseFixtureAdapters { get; set; }
    public int DailyUtcHour { get; set; } = 6;
    public int PerDomainDelayMilliseconds { get; set; } = 750;
    public string UserAgent { get; set; } = "SpecTenBot/1.0";
}
