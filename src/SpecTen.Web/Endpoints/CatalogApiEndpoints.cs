using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SpecTen.Web.Options;
using SpecTen.Web.Data;
using SpecTen.Web.Services;

namespace SpecTen.Web.Endpoints;

public static class CatalogApiEndpoints
{
    public static void MapCatalogApi(this WebApplication app)
    {
        app.MapGet("/health", async Task<Results<Ok<HealthResponse>, ProblemHttpResult>> (
            CatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            var databaseReachable = await catalog.CanReachDatabaseAsync(cancellationToken);
            if (!databaseReachable)
            {
                return TypedResults.Problem(statusCode: 503, detail: "Nao foi possivel acessar o PostgreSQL.");
            }

            return TypedResults.Ok(new HealthResponse("ok", "reachable", DateTimeOffset.UtcNow));
        });

        app.MapGet("/sitemap.xml", async (
            HttpContext context,
            CatalogService catalog,
            IOptions<PublicApiOptions> publicApi,
            CancellationToken cancellationToken) =>
        {
            ApplyPublicCache(context, publicApi.Value.SitemapCacheSeconds);
            var phones = await catalog.GetSitemapPhonesAsync(cancellationToken);
            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            var xml = BuildSitemap(origin, phones);
            return Results.Text(xml, MediaTypeNames.Application.Xml, Encoding.UTF8);
        });

        app.MapGet("/robots.txt", (
            HttpContext context,
            IOptions<PublicApiOptions> publicApi) =>
        {
            ApplyPublicCache(context, publicApi.Value.RobotsCacheSeconds);
            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            var robots = BuildRobots(origin);
            return Results.Text(robots, MediaTypeNames.Text.Plain, Encoding.UTF8);
        });

        var api = app.MapGroup("/api");

        api.MapGet("/search", async (
            HttpContext context,
            string? query,
            string? q,
            string? tier,
            string? brand,
            string? sort,
            CatalogService catalog,
            IOptions<PublicApiOptions> publicApi,
            CancellationToken cancellationToken) =>
        {
            ApplyPublicCache(context, publicApi.Value.SearchCacheSeconds);
            var effectiveQuery = string.IsNullOrWhiteSpace(query) ? q : query;
            var results = await catalog.SearchAsync(effectiveQuery, ParseTier(tier), brand, ParseSort(sort), 25, cancellationToken);
            return TypedResults.Ok(results);
        });

        api.MapGet("/search/suggestions", async (
            HttpContext context,
            string? query,
            string? q,
            CatalogService catalog,
            IOptions<PublicApiOptions> publicApi,
            CancellationToken cancellationToken) =>
        {
            ApplyPublicCache(context, publicApi.Value.SuggestionCacheSeconds);
            var effectiveQuery = string.IsNullOrWhiteSpace(query) ? q : query;
            var results = await catalog.SuggestAsync(effectiveQuery, 8, cancellationToken);
            return TypedResults.Ok(results);
        });

        api.MapGet("/phones/{id:int}", async Task<Results<Ok<PhoneDetailsDto>, NotFound>> (
            HttpContext context,
            int id,
            CatalogService catalog,
            IOptions<PublicApiOptions> publicApi,
            CancellationToken cancellationToken) =>
        {
            ApplyPublicCache(context, publicApi.Value.PhoneCacheSeconds);
            var phone = await catalog.GetPhoneByIdAsync(id, cancellationToken);
            return phone is null ? TypedResults.NotFound() : TypedResults.Ok(phone);
        });

        api.MapGet("/compare", async (
            HttpContext context,
            string? ids,
            CatalogService catalog,
            IOptions<PublicApiOptions> publicApi,
            CancellationToken cancellationToken) =>
        {
            ApplyPublicCache(context, publicApi.Value.CompareCacheSeconds);
            var parsedIds = ParseIds(ids);
            var comparison = await catalog.CompareAsync(parsedIds, cancellationToken);
            return TypedResults.Ok(comparison);
        });

        api.MapPost("/reports", async Task<Results<Created, ValidationProblem>> (
            HttpContext context,
            CorrectionReportRequest request,
            ReportSubmissionGuard submissionGuard,
            CatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            if (submissionGuard.IsRateLimited(ReportSubmissionGuard.BuildClientKey(context), out _))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["message"] = ["Voce enviou correcoes demais em pouco tempo. Aguarde alguns minutos e tente novamente."],
                });
            }

            try
            {
                await catalog.SubmitCorrectionReportAsync(request, cancellationToken);
                return TypedResults.Created();
            }
            catch (InvalidOperationException exception)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["message"] = [exception.Message],
                });
            }
        });

        api.MapPost("/reports/form", async (
            HttpContext context,
            ReportSubmissionGuard submissionGuard,
            CatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var phoneId = int.TryParse(form["phoneModelId"], out var parsedPhoneId) ? parsedPhoneId : 0;
            var returnUrl = form["returnUrl"].FirstOrDefault();
            var honeypot = form["company"].FirstOrDefault();

            if (submissionGuard.IsBotTrapFilled(honeypot))
            {
                return Results.Redirect(SafeLocalReturnUrl(returnUrl, ReportRedirectState.Success));
            }

            if (submissionGuard.IsRateLimited(ReportSubmissionGuard.BuildClientKey(context), out _))
            {
                return Results.Redirect(SafeLocalReturnUrl(returnUrl, ReportRedirectState.RateLimited));
            }

            try
            {
                await catalog.SubmitCorrectionReportAsync(
                    new CorrectionReportRequest(
                        phoneId,
                        form["fieldKey"].FirstOrDefault(),
                        form["reporterEmail"].FirstOrDefault(),
                        form["message"].FirstOrDefault() ?? ""),
                    cancellationToken);

                return Results.Redirect(SafeLocalReturnUrl(returnUrl, ReportRedirectState.Success));
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect(SafeLocalReturnUrl(returnUrl, ReportRedirectState.Invalid));
            }
        });
    }

    private static ClassificationTier? ParseTier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return null;
        }

        return tier.Trim().ToLowerInvariant() switch
        {
            "entrada" or "entry" => ClassificationTier.Entry,
            "intermediario" or "midrange" or "mid-range" => ClassificationTier.MidRange,
            "top" or "flagship" or "top-de-linha" => ClassificationTier.Flagship,
            _ => null,
        };
    }

    private static int[] ParseIds(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            return [];
        }

        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(4)
            .ToArray();
    }

    private static CatalogSortOption ParseSort(string? sort)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "newest" or "recentes" => CatalogSortOption.Newest,
            "name" or "nome" => CatalogSortOption.Name,
            "confidence" or "confianca" => CatalogSortOption.Confidence,
            _ => CatalogSortOption.Relevance,
        };
    }

    private static string SafeLocalReturnUrl(string? returnUrl, ReportRedirectState state)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal) || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/celulares";
        }

        if (state == ReportRedirectState.Success)
        {
            return AppendFlag(returnUrl, "reported=1");
        }

        if (state == ReportRedirectState.RateLimited)
        {
            return AppendFlag(returnUrl, "reportLimited=1");
        }

        return AppendFlag(returnUrl, "reportError=1");
    }

    private static string AppendFlag(string returnUrl, string flag)
    {
        var separator = returnUrl.Contains("?", StringComparison.Ordinal) ? "&" : "?";
        return $"{returnUrl}{separator}{flag}";
    }

    private static string BuildSitemap(string origin, IReadOnlyList<SitemapPhoneDto> phones)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");
        AddUrl(builder, $"{origin}/", DateTimeOffset.UtcNow);
        AddUrl(builder, $"{origin}/celulares", DateTimeOffset.UtcNow);

        foreach (var phone in phones)
        {
            AddUrl(builder, $"{origin}/celulares/{phone.BrandSlug}/{phone.Slug}", phone.UpdatedAt);
        }

        builder.AppendLine("</urlset>");
        return builder.ToString();
    }

    private static void AddUrl(StringBuilder builder, string loc, DateTimeOffset lastModified)
    {
        builder.AppendLine("  <url>");
        builder.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(loc)}</loc>");
        builder.AppendLine($"    <lastmod>{lastModified:yyyy-MM-dd}</lastmod>");
        builder.AppendLine("  </url>");
    }

    private static string BuildRobots(string origin)
    {
        var builder = new StringBuilder();
        builder.AppendLine("User-agent: *");
        builder.AppendLine("Allow: /");
        builder.AppendLine($"Sitemap: {origin}/sitemap.xml");
        return builder.ToString();
    }

    private static void ApplyPublicCache(HttpContext context, int maxAgeSeconds)
    {
        var cacheSeconds = Math.Max(0, maxAgeSeconds);
        context.Response.Headers["Cache-Control"] = cacheSeconds == 0
            ? "no-store"
            : $"public,max-age={cacheSeconds}";
    }
}

public sealed record HealthResponse(string Status, string Database, DateTimeOffset Timestamp);

internal enum ReportRedirectState
{
    Success,
    Invalid,
    RateLimited,
}
