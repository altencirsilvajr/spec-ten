using Microsoft.Extensions.Caching.Memory;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class ReportSubmissionGuardTests
{
    [Fact]
    public void Guard_StartsLimitingAfterFiveAttemptsWithinTheWindow()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var guard = new ReportSubmissionGuard(cache);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var limited = guard.IsRateLimited("127.0.0.1|test-agent", out var retryAfter);
            Assert.False(limited);
            Assert.Equal(TimeSpan.Zero, retryAfter);
        }

        var sixthAttemptLimited = guard.IsRateLimited("127.0.0.1|test-agent", out var retryWindow);

        Assert.True(sixthAttemptLimited);
        Assert.True(retryWindow > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("bot-field")]
    [InlineData("  still filled  ")]
    public void Guard_DetectsFilledHoneypot(string value)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var guard = new ReportSubmissionGuard(cache);

        Assert.True(guard.IsBotTrapFilled(value));
        Assert.False(guard.IsBotTrapFilled(""));
        Assert.False(guard.IsBotTrapFilled("   "));
        Assert.False(guard.IsBotTrapFilled(null));
    }
}
