using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using SpecTen.Web.Options;

namespace SpecTen.Web.Infrastructure;

public static class PublicApiRateLimiterFactory
{
    public static PartitionedRateLimiter<HttpContext> Create(PublicApiOptions options)
    {
        var configuredOptions = options ?? new PublicApiOptions();

        return PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            if (!httpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                return RateLimitPartition.GetNoLimiter("non-api");
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                $"public-api:{ResolveClientKey(httpContext)}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, configuredOptions.RateLimitPermitLimit),
                    Window = TimeSpan.FromSeconds(Math.Max(1, configuredOptions.RateLimitWindowSeconds)),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    }

    public static string ResolveClientKey(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstForwarded = forwardedFor
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(firstForwarded))
            {
                return firstForwarded;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
