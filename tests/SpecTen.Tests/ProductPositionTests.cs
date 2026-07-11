using SpecTen.Web.Services;
using SpecTen.Web.Data;
using SpecTen.Web.Options;

namespace SpecTen.Tests;

public sealed class ProductPositionTests
{
    [Fact]
    public void For_UsesCuratedCommercialSegmentWithoutChangingPerformanceTier()
    {
        var segment = ProductPosition.For("xiaomi", "poco-x8-pro");

        Assert.Equal("Intermediario premium", segment);
        Assert.Equal("Desempenho muito alto", PhoneClassifier.LabelFor(ClassificationTier.Flagship));
    }

    [Fact]
    public void For_ReturnsNullWhenThereIsNoCuratedSegment()
    {
        Assert.Null(ProductPosition.For("samsung", "galaxy-a56"));
    }

    [Fact]
    public void Suggestion_UsesCuratedSegmentAsItsPublicBadge()
    {
        var suggestion = new PhoneSuggestionDto(
            1, "Xiaomi", "xiaomi", "Poco X8 Pro", "poco-x8-pro",
            "Desempenho muito alto", "Dimensity 8500 Ultra", null, true, true,
            ProductPosition.For("xiaomi", "poco-x8-pro"));

        Assert.Equal("Intermediario premium", suggestion.PrimaryBadgeLabel);
    }

    [Fact]
    public void CoverageOptions_DefaultToLocalOnly()
    {
        var options = new CoverageOptions();

        Assert.False(options.Enabled);
        Assert.False(options.OnDemandHydrationEnabled);
    }
}
