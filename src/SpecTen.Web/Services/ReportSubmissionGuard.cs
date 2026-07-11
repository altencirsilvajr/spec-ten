using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace SpecTen.Web.Services;

public sealed class ReportSubmissionGuard(IMemoryCache cache)
{
    private const int PermitLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public bool IsBotTrapFilled(string? trapValue)
        => !string.IsNullOrWhiteSpace(trapValue);

    public bool IsRateLimited(string? clientKey, out TimeSpan retryAfter)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(clientKey) ? "unknown" : clientKey.Trim();
        var cacheKey = $"report-window:{normalizedKey}";
        var state = cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            return new SubmissionWindow();
        })!;

        lock (state.Sync)
        {
            var now = DateTimeOffset.UtcNow;
            while (state.Attempts.Count > 0 && now - state.Attempts.Peek() >= Window)
            {
                state.Attempts.Dequeue();
            }

            if (state.Attempts.Count >= PermitLimit)
            {
                retryAfter = Window - (now - state.Attempts.Peek());
                return true;
            }

            state.Attempts.Enqueue(now);
            cache.Set(cacheKey, state, Window);
        }

        retryAfter = TimeSpan.Zero;
        return false;
    }

    public static string BuildClientKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(address))
        {
            address = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        }

        var agent = context.Request.Headers.UserAgent.ToString().Trim();
        if (agent.Length > 64)
        {
            agent = agent[..64];
        }

        return string.IsNullOrWhiteSpace(agent)
            ? address ?? "unknown"
            : $"{address ?? "unknown"}|{agent}";
    }

    private sealed class SubmissionWindow
    {
        public Queue<DateTimeOffset> Attempts { get; } = new();

        public object Sync { get; } = new();
    }
}
