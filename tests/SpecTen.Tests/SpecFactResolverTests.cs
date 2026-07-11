using SpecTen.Web.Data;
using SpecTen.Web.Scraping;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class SpecFactResolverTests
{
    private readonly SpecFactResolver _resolver = new();

    [Fact]
    public void Resolve_PrefersOfficialClaim()
    {
        var result = _resolver.Resolve(
        [
            Claim("GSMArena", false, "5000 mAh", 0.80),
            Claim("Official", true, "5100 mAh", 0.95),
            Claim("Kimovil", false, "5000 mAh", 0.82),
        ]);

        Assert.Equal("5100 mAh", result.DisplayValue);
        Assert.Equal(SpecStatus.Published, result.Status);
        Assert.True(result.Confidence >= 0.94);
    }

    [Fact]
    public void Resolve_MarksCriticalConflictsForReviewWhenNoOfficialSourceExists()
    {
        var result = _resolver.Resolve(
        [
            Claim("GSMArena", false, "5000 mAh", 0.80),
            Claim("Kimovil", false, "5100 mAh", 0.82),
        ]);

        Assert.Equal(SpecStatus.NeedsReview, result.Status);
    }

    [Fact]
    public void Resolve_PublishesMajorityConsensus()
    {
        var result = _resolver.Resolve(
        [
            Claim("GSMArena", false, "5000 mAh", 0.80),
            Claim("Kimovil", false, "5000 mAh", 0.82),
            Claim("TudoCelular", false, "5100 mAh", 0.78),
        ]);

        Assert.Equal("5000 mAh", result.DisplayValue);
        Assert.Equal(SpecStatus.Published, result.Status);
    }

    [Fact]
    public void Resolve_KeepsTrustedReferenceSingleSourceAboveModernFallbackFloor()
    {
        var result = _resolver.Resolve(
        [
            Claim("GSMArena", false, "Exynos 1580 (4 nm)", 0.84),
        ]);

        Assert.Equal("Exynos 1580 (4 nm)", result.DisplayValue);
        Assert.Equal(SpecStatus.Published, result.Status);
        Assert.Equal(0.78, result.Confidence);
        Assert.Equal("trusted-reference", result.Reason);
    }

    private static SourceSpecClaim Claim(string source, bool official, string value, double confidence)
    {
        return new SourceSpecClaim(
            source,
            $"https://example.test/{source}",
            official,
            "Bateria",
            "battery",
            "Bateria",
            value,
            value.ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal),
            value,
            "mAh",
            true,
            confidence,
            DateTimeOffset.UtcNow);
    }
}
