using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SpecTen.Web.Components;
using SpecTen.Web.Data;
using SpecTen.Web.Endpoints;
using SpecTen.Web.Infrastructure;
using SpecTen.Web.Options;
using SpecTen.Web.Scraping;
using SpecTen.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(railwayPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/json",
        "application/xml",
    ]);
});
builder.Services.Configure<ScrapingOptions>(builder.Configuration.GetSection(ScrapingOptions.SectionName));
builder.Services.Configure<CoverageOptions>(builder.Configuration.GetSection(CoverageOptions.SectionName));
builder.Services.Configure<CatalogSeedOptions>(builder.Configuration.GetSection(CatalogSeedOptions.SectionName));
builder.Services.Configure<PublicApiOptions>(builder.Configuration.GetSection(PublicApiOptions.SectionName));
var scrapingOptions = builder.Configuration.GetSection(ScrapingOptions.SectionName).Get<ScrapingOptions>() ?? new();
var outboundUserAgent = string.IsNullOrWhiteSpace(scrapingOptions.UserAgent)
    ? "SpecTenBot/1.0"
    : scrapingOptions.UserAgent.Trim();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ReportSubmissionGuard>();
builder.Services.AddHttpClient("device-coverage", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(outboundUserAgent);
});
builder.Services.AddHttpClient("catalog-seed", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(outboundUserAgent);
});

var postgresConnectionString = ConfigurationHelpers.FirstConfiguredValue(
    builder.Configuration.GetConnectionString("Postgres"),
    builder.Configuration["DATABASE_URL"],
    "Host=localhost;Port=5432;Database=specten;Username=postgres;Password=postgres");

postgresConnectionString = ConfigurationHelpers.NormalizePostgresConnectionString(postgresConnectionString);

builder.Services.AddDbContextFactory<CatalogDbContext>(options =>
{
    options.UseNpgsql(postgresConnectionString);
});
builder.Services.AddDataProtection()
    .SetApplicationName("SpecTen")
    .PersistKeysToDbContext<CatalogDbContext>();

var publicApiOptions = builder.Configuration.GetSection(PublicApiOptions.SectionName).Get<PublicApiOptions>() ?? new();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = Math.Max(1, publicApiOptions.RateLimitWindowSeconds).ToString();
        return ValueTask.CompletedTask;
    };

    options.GlobalLimiter = PublicApiRateLimiterFactory.Create(publicApiOptions);
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CompareSelectionState>();
builder.Services.AddScoped<RecentlyViewedState>();
builder.Services.AddSingleton<GsmArenaPageParser>();
builder.Services.AddSingleton<IOfficialCoverageProvider, SamsungOfficialCoverageProvider>();
builder.Services.AddSingleton<IOfficialCoverageProvider, VivoOfficialCoverageProvider>();
builder.Services.AddSingleton<IDeviceCoverageService, DeviceCoverageService>();
builder.Services.AddScoped<PhoneImportService>();
builder.Services.AddSingleton<SpecFactResolver>();
builder.Services.AddSingleton<PhoneClassifier>();
builder.Services.AddScoped<IPhoneSourceAdapter, JsonCatalogSeedAdapter>();
if (scrapingOptions.UseFixtureAdapters)
{
    builder.Services.AddScoped<IPhoneSourceAdapter, OfficialFixtureAdapter>();
    builder.Services.AddScoped<IPhoneSourceAdapter, GsmArenaFixtureAdapter>();
    builder.Services.AddScoped<IPhoneSourceAdapter, KimovilFixtureAdapter>();
    builder.Services.AddScoped<IPhoneSourceAdapter, TudoCelularFixtureAdapter>();
}
builder.Services.AddHostedService<CatalogImportBackgroundService>();
builder.Services.AddHostedService<CoverageWarmupService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseRouting();
app.UseRateLimiter();
app.UseResponseCompression();
app.UseStatusCodePagesWithReExecute("/Error");
app.UseAntiforgery();
app.MapOpenApi();
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/admin", () => Results.Redirect("/celulares"));
app.MapGet("/admin/{**path}", () => Results.Redirect("/celulares"));
app.MapCatalogApi();

if (!app.Environment.IsEnvironment("Testing"))
{
    await app.Services.InitializeCatalogDatabaseAsync(app.Lifetime.ApplicationStopping);
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
