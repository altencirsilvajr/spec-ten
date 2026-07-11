using SpecTen.Web.Data;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class ClassifierTests
{
    private readonly PhoneClassifier _classifier = new();

    [Fact]
    public void Classify_UsesBenchmarkWhenItIsTheOnlySignal()
    {
        var result = _classifier.Classify(
            "Unknown chip",
            [new BenchmarkInput("AnTuTu", 2_250_000)]);

        Assert.Equal(ClassificationTier.Flagship, result.Tier);
        Assert.Equal("benchmark", result.Basis);
    }

    [Fact]
    public void Classify_TreatsUpperMidrangeBenchmarkAsMidRange()
    {
        var result = _classifier.Classify(
            "Google Tensor G4",
            [new BenchmarkInput("AnTuTu 11", 1_321_069)]);

        Assert.Equal(ClassificationTier.MidRange, result.Tier);
        Assert.Equal("benchmark", result.Basis);
    }

    [Fact]
    public void Classify_UsesCorroboratedHigherTierWhenSignalsDisagree()
    {
        var result = _classifier.Classify(
            "Apple A16 Bionic",
            [
                new BenchmarkInput("AnTuTu", 955_884),
                new BenchmarkInput("GeekBench", 5_423)
            ]);

        Assert.Equal(ClassificationTier.Flagship, result.Tier);
        Assert.Equal("benchmark", result.Basis);
        Assert.Contains("divergencia", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_PrefersSaferTierWhenSignalsTie()
    {
        var result = _classifier.Classify(
            "Snapdragon 7s Gen 3",
            [new BenchmarkInput("GeekBench", 4_700)]);

        Assert.Equal(ClassificationTier.MidRange, result.Tier);
    }

    [Fact]
    public void Classify_FallsBackToCuratedChipsetTable()
    {
        var result = _classifier.Classify("Dimensity 7300 Ultra", []);

        Assert.Equal(ClassificationTier.MidRange, result.Tier);
        Assert.Equal("chipset", result.Basis);
    }

    [Fact]
    public void Classify_TreatsExynos1680AsMidRange()
    {
        var result = _classifier.Classify("Exynos 1680", []);

        Assert.Equal(ClassificationTier.MidRange, result.Tier);
        Assert.Equal("chipset", result.Basis);
    }

    [Fact]
    public void Classify_Normalizes_OfficialChipsetSymbols_BeforeUsingCuratedTable()
    {
        var result = _classifier.Classify("Snapdragon® 8 Elite Gen 5", []);

        Assert.Equal(ClassificationTier.Flagship, result.Tier);
        Assert.Equal("chipset", result.Basis);
    }

    [Fact]
    public void Classify_TreatsLegacySnapdragon800SeriesFlagshipAsTopTier()
    {
        var result = _classifier.Classify("Qualcomm MSM8996 Snapdragon 821 (14 nm)", []);

        Assert.Equal(ClassificationTier.Flagship, result.Tier);
        Assert.Equal("chipset", result.Basis);
        Assert.Contains("geracao", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_TreatsBudgetExynosAsEntry()
    {
        var result = _classifier.Classify("Exynos 7870 Octa (14 nm)", []);

        Assert.Equal(ClassificationTier.Entry, result.Tier);
        Assert.Equal("chipset", result.Basis);
    }

    [Fact]
    public void Classify_TreatsSnapdragon8sFamilyAsMidRange()
    {
        var result = _classifier.Classify("Qualcomm SM8735 Snapdragon 8s Gen 4 (4 nm)", []);

        Assert.Equal(ClassificationTier.MidRange, result.Tier);
        Assert.Equal("chipset", result.Basis);
    }

    [Fact]
    public void Classify_TreatsLegacyAppleAChipAsTopTier()
    {
        var result = _classifier.Classify("Apple A4 (45 nm)", []);

        Assert.Equal(ClassificationTier.Flagship, result.Tier);
        Assert.Equal("chipset", result.Basis);
    }

    [Fact]
    public void Classify_FallsBackToOfficialProfile_WhenChipsetIsMissing()
    {
        var result = _classifier.Classify(
            null,
            [],
            new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["cpu"] = "Octa Core / 2.9GHz, 2.6GHz, 1.9GHz",
                ["display_type"] = "Super AMOLED Plus",
                ["refresh_rate"] = "120 Hz",
                ["ip_rating"] = "IP68",
                ["wifi"] = "802.11a/b/g/n/ac/ax 2.4GHz+5GHz+6GHz",
                ["storage_base"] = "128 GB",
                ["main_camera_video"] = "UHD 4K (3840 x 2160) @30fps",
                ["build"] = "Gorilla Glass Victus+",
            });

        Assert.Equal(ClassificationTier.MidRange, result.Tier);
        Assert.Equal("official-profile", result.Basis);
        Assert.Contains("perfil oficial", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LabelFor_ReturnsPortuguesePublicLabels()
    {
        Assert.Equal("Entrada", PhoneClassifier.LabelFor(ClassificationTier.Entry));
        Assert.Equal("Intermediario", PhoneClassifier.LabelFor(ClassificationTier.MidRange));
        Assert.Equal("Top de linha", PhoneClassifier.LabelFor(ClassificationTier.Flagship));
    }

    [Fact]
    public void ChipsetText_AcceptsLongKnownPlatformNames_AndRejectsMarketingSentences()
    {
        Assert.False(ChipsetText.IsSuspicious("Qualcomm SM8850-AC Snapdragon 8 Elite Gen 5 (3 nm)"));
        Assert.True(ChipsetText.IsSuspicious("Processador rapido enquanto melhora camera bateria e display"));
    }
}
