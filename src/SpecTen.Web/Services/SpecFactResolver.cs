using SpecTen.Web.Data;
using SpecTen.Web.Scraping;

namespace SpecTen.Web.Services;

public sealed class SpecFactResolver
{
    private static readonly string[] TrustedReferenceSourceTokens =
    [
        "gsmarena",
        "kimovil",
        "tudocelular",
    ];

    public ResolvedSpec Resolve(IReadOnlyList<SourceSpecClaim> claims)
    {
        if (claims.Count == 0)
        {
            throw new ArgumentException("At least one source claim is required.", nameof(claims));
        }

        var official = claims
            .Where(claim => claim.IsOfficial)
            .OrderByDescending(claim => claim.Confidence)
            .ThenByDescending(claim => claim.CollectedAt)
            .FirstOrDefault();

        if (official is not null)
        {
            return FromClaim(
                official,
                SpecStatus.Published,
                Math.Max(official.Confidence, 0.94),
                "official-source");
        }

        var grouped = claims
            .GroupBy(claim => claim.NormalizedValue, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                NormalizedValue = group.Key,
                Claims = group.OrderByDescending(claim => claim.Confidence).ToList(),
                Count = group.Count(),
                AverageConfidence = group.Average(claim => claim.Confidence),
            })
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.AverageConfidence)
            .ToList();

        var winner = grouped[0];
        var hasConflict = grouped.Count > 1;
        var hasMajority = winner.Count > 1 && grouped.Count(group => group.Count == winner.Count) == 1;
        var selected = winner.Claims[0];

        if (hasMajority)
        {
            return FromClaim(
                selected,
                SpecStatus.Published,
                Math.Min(0.90, winner.AverageConfidence + 0.10),
                "source-consensus");
        }

        if (hasConflict && selected.IsCritical)
        {
            return FromClaim(
                selected,
                SpecStatus.NeedsReview,
                Math.Min(0.65, selected.Confidence),
                "conflict-review");
        }

        var singleSourceCap = ResolveSingleSourceCap(selected.SourceName, hasConflict);

        return FromClaim(
            selected,
            SpecStatus.Published,
            Math.Min(singleSourceCap, selected.Confidence),
            hasConflict
                ? IsTrustedReferenceSource(selected.SourceName)
                    ? "trusted-reference-conflict"
                    : "non-critical-conflict"
                : IsTrustedReferenceSource(selected.SourceName)
                    ? "trusted-reference"
                    : "single-source");
    }

    private static ResolvedSpec FromClaim(SourceSpecClaim claim, SpecStatus status, double confidence, string reason)
    {
        return new ResolvedSpec(
            claim.Group,
            claim.Key,
            claim.DisplayName,
            claim.NormalizedValue,
            claim.DisplayValue,
            claim.Unit,
            claim.SourceName,
            claim.SourceUrl,
            Math.Round(confidence, 2),
            status,
            claim.IsCritical,
            claim.CollectedAt,
            reason);
    }

    private static double ResolveSingleSourceCap(string? sourceName, bool hasConflict)
    {
        if (IsTrustedReferenceSource(sourceName))
        {
            return hasConflict ? 0.72 : 0.78;
        }

        return 0.62;
    }

    private static bool IsTrustedReferenceSource(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return false;
        }

        return TrustedReferenceSourceTokens.Any(token =>
            sourceName.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record ResolvedSpec(
    string Group,
    string Key,
    string DisplayName,
    string NormalizedValue,
    string DisplayValue,
    string? Unit,
    string SourceName,
    string? SourceUrl,
    double Confidence,
    SpecStatus Status,
    bool IsCritical,
    DateTimeOffset CollectedAt,
    string Reason);
