using System.Globalization;
using SpecTen.Web.Data;

namespace SpecTen.Web.Services;

internal static class CatalogReadiness
{
    public const int MinimumPublishedSmartphoneSpecCount = 16;
    public const int MinimumPublishedFeaturePhoneSpecCount = 10;
    public const int MinimumPublishedLegacyPhoneSpecCount = 8;
    public const double MinimumModernSmartphoneConfidence = 0.72;
    public const double MinimumLegacySmartphoneConfidence = 0.62;
    public const double MinimumLegacyPhoneConfidence = 0.60;
    public const int MinimumRichSingleSourceSpecCount = 24;
    public const double MinimumRichSingleSourceConfidence = 0.78;

    public static CatalogReadinessEvaluation Evaluate(
        string? imageUrl,
        DateTimeOffset? releasedAt,
        IReadOnlyList<SpecGroupDto> specGroups)
    {
        return Evaluate(
            imageUrl,
            releasedAt,
            specGroups.SelectMany(group => group.Specs)
                .Select(spec => new CatalogSpecSnapshot(
                    spec.Key,
                    spec.DisplayValue,
                    spec.SourceName,
                    spec.Confidence,
                    spec.Status,
                    spec.IsCritical)));
    }

    public static CatalogReadinessEvaluation Evaluate(
        string? imageUrl,
        DateTimeOffset? releasedAt,
        IEnumerable<CatalogSpecSnapshot> specs)
    {
        var publishedSpecs = specs
            .Where(spec => !string.IsNullOrWhiteSpace(spec.Key) && !string.IsNullOrWhiteSpace(spec.DisplayValue))
            .ToList();

        if (publishedSpecs.Count == 0)
        {
            return CatalogReadinessEvaluation.NotReady with
            {
                ReadinessNote = "Ainda faltam specs suficientes para publicar a ficha como pronta."
            };
        }

        var hasTrustedImage = !IsPlaceholderImage(imageUrl);
        var valuesByKey = publishedSpecs
            .GroupBy(spec => spec.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().DisplayValue,
                StringComparer.OrdinalIgnoreCase);
        var reviewFlagCount = publishedSpecs.Count(spec => spec.Status == SpecStatus.NeedsReview);
        var hasReviewFlags = reviewFlagCount > 0;
        var sourceNames = publishedSpecs
            .Select(spec => spec.SourceName?.Trim())
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sourceCount = sourceNames.Count;
        var hasOfficialSource = sourceNames.Any(IsOfficialSourceName);
        var minConfidence = publishedSpecs.Min(spec => spec.Confidence);

        var hasBattery = HasMeaningfulValue(valuesByKey, "battery");
        var hasDisplaySize = HasMeaningfulValue(valuesByKey, "display_size");
        var hasDisplaySignal = hasDisplaySize ||
                               HasMeaningfulValue(valuesByKey, "display_type") ||
                               HasMeaningfulValue(valuesByKey, "resolution");
        var hasMainCamera = HasMeaningfulValue(valuesByKey, "main_camera");
        var hasNetwork = HasMeaningfulValue(valuesByKey, "network");
        var hasBodySignal = HasMeaningfulValue(valuesByKey, "dimensions") ||
                            HasMeaningfulValue(valuesByKey, "weight") ||
                            HasMeaningfulValue(valuesByKey, "sim");
        var hasChipset = HasMeaningfulValue(valuesByKey, "chipset");
        var hasRam = HasMeaningfulValue(valuesByKey, "ram");
        var hasCpu = HasMeaningfulValue(valuesByKey, "cpu");
        var hasGpu = HasMeaningfulValue(valuesByKey, "gpu");
        var osValue = GetMeaningfulValue(valuesByKey, "os");
        var displaySizeInches = TryParseDisplaySize(GetMeaningfulValue(valuesByKey, "display_size"));
        var hasSmartphoneOsSignal = LooksLikeSmartphoneOs(osValue);
        var isLikelyFeaturePhone = !hasChipset &&
                                   !hasRam &&
                                   !hasSmartphoneOsSignal &&
                                   (displaySizeInches is null || displaySizeInches <= 3.4m);
        var isLegacyRelease = releasedAt is not null && releasedAt.Value.Year <= 2012;
        var hasSmartphonePerformanceSignal = hasChipset || hasRam || hasCpu || hasGpu || hasSmartphoneOsSignal;
        var requiresRecentSourceHardening = !isLikelyFeaturePhone &&
                                            releasedAt is not null &&
                                            releasedAt.Value.Year >= 2025;
        var hasConsensusSources = sourceCount >= 2;
        var hasRichSingleSourceCoverage = sourceCount == 1 &&
                                          minConfidence >= MinimumRichSingleSourceConfidence &&
                                          publishedSpecs.Count >= MinimumRichSingleSourceSpecCount;
        var supportsModernPublicPromise = hasOfficialSource ||
                                          (hasConsensusSources && minConfidence >= 0.75) ||
                                          hasRichSingleSourceCoverage;
        var supportsLegacyPublicPromise = sourceCount >= 1 && minConfidence >= MinimumLegacySmartphoneConfidence;
        var hasDeepLegacyFeatureCoverage = !hasSmartphonePerformanceSignal &&
                                           publishedSpecs.Count >= MinimumPublishedFeaturePhoneSpecCount + 2;
        var hasFeaturePhonePowerSignal = hasBattery || hasDeepLegacyFeatureCoverage;
        var featurePhoneProfile = isLikelyFeaturePhone || isLegacyRelease || hasDeepLegacyFeatureCoverage;

        var smartphoneShapeReady = hasBattery &&
                                   hasDisplaySize &&
                                   hasMainCamera &&
                                   publishedSpecs.Count >= MinimumPublishedSmartphoneSpecCount &&
                                   hasSmartphonePerformanceSignal;

        var smartphoneReady = smartphoneShapeReady &&
                              hasTrustedImage &&
                              !hasReviewFlags &&
                              (requiresRecentSourceHardening
                                  ? minConfidence >= MinimumModernSmartphoneConfidence && supportsModernPublicPromise
                                  : minConfidence >= MinimumLegacySmartphoneConfidence && supportsLegacyPublicPromise);

        var featurePhoneReady = hasFeaturePhonePowerSignal &&
                                hasNetwork &&
                                hasDisplaySignal &&
                                hasBodySignal &&
                                publishedSpecs.Count >= (hasMainCamera ? MinimumPublishedFeaturePhoneSpecCount : MinimumPublishedLegacyPhoneSpecCount) &&
                                featurePhoneProfile &&
                                hasTrustedImage &&
                                !hasReviewFlags &&
                                sourceCount >= 1 &&
                                minConfidence >= MinimumLegacyPhoneConfidence;

        var trustTier = ResolveTrustTier(hasOfficialSource, hasConsensusSources, hasReviewFlags, minConfidence);
        var readinessNote = BuildReadinessNote(
            hasTrustedImage,
            hasReviewFlags,
            smartphoneShapeReady,
            featurePhoneReady,
            featurePhoneProfile,
            requiresRecentSourceHardening,
            supportsModernPublicPromise,
            hasRichSingleSourceCoverage,
            hasOfficialSource,
            hasConsensusSources,
            minConfidence);

        return new CatalogReadinessEvaluation(
            smartphoneReady || featurePhoneReady,
            featurePhoneReady,
            trustTier,
            sourceCount,
            hasOfficialSource,
            hasReviewFlags,
            minConfidence,
            readinessNote);
    }

    public static int PublishedSpecCount(IReadOnlyList<SpecGroupDto> specGroups)
    {
        return specGroups.Sum(group => group.Specs.Count);
    }

    public static bool IsPlaceholderImage(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return true;
        }

        return imageUrl.Contains("smartphone.jpg", StringComparison.OrdinalIgnoreCase) ||
               imageUrl.Contains("coming-soon", StringComparison.OrdinalIgnoreCase) ||
               imageUrl.Contains("noimage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMeaningfulValue(IReadOnlyDictionary<string, string?> valuesByKey, string key)
    {
        return !string.IsNullOrWhiteSpace(GetMeaningfulValue(valuesByKey, key));
    }

    private static string? GetMeaningfulValue(IReadOnlyDictionary<string, string?> valuesByKey, string key)
    {
        if (!valuesByKey.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized is "-" or "?" or "N/A" or "n/a" ? null : normalized;
    }

    private static bool LooksLikeSmartphoneOs(string? osValue)
    {
        if (string.IsNullOrWhiteSpace(osValue))
        {
            return false;
        }

        return osValue.Contains("android", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("ios", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("ipados", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("harmonyos", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("symbian", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("windows phone", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("touchwiz", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("one ui", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("miui", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("hyperos", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("emui", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("coloros", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("oxygenos", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("realme ui", StringComparison.OrdinalIgnoreCase) ||
               osValue.Contains("funtouch", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? TryParseDisplaySize(string? displaySize)
    {
        if (string.IsNullOrWhiteSpace(displaySize))
        {
            return null;
        }

        var token = displaySize
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (token is null)
        {
            return null;
        }

        token = token.Replace(',', '.');
        return decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static CatalogTrustTier ResolveTrustTier(
        bool hasOfficialSource,
        bool hasConsensusSources,
        bool hasReviewFlags,
        double minConfidence)
    {
        if (hasReviewFlags || minConfidence <= 0)
        {
            return CatalogTrustTier.Review;
        }

        if (hasOfficialSource)
        {
            return CatalogTrustTier.Official;
        }

        if (hasConsensusSources && minConfidence >= 0.75)
        {
            return CatalogTrustTier.Consensus;
        }

        return CatalogTrustTier.SingleSource;
    }

    private static bool IsOfficialSourceName(string? sourceName)
    {
        return !string.IsNullOrWhiteSpace(sourceName) &&
               sourceName.Contains("official", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildReadinessNote(
        bool hasTrustedImage,
        bool hasReviewFlags,
        bool smartphoneShapeReady,
        bool featurePhoneReady,
        bool legacyProfile,
        bool isModernSmartphone,
        bool supportsModernPublicPromise,
        bool hasRichSingleSourceCoverage,
        bool hasOfficialSource,
        bool hasConsensusSources,
        double minConfidence)
    {
        if (!hasTrustedImage)
        {
            return "Ainda falta uma imagem publica confiavel para liberar esta ficha como pronta.";
        }

        if (hasReviewFlags)
        {
            return "Campos criticos ainda estao em revisao; a ficha continua visivel, mas sem promessa publica completa.";
        }

        if (isModernSmartphone && !supportsModernPublicPromise)
        {
            return "Modelo recente ainda depende de fonte unica ou confianca baixa. Estamos buscando confirmacao adicional antes de tratar a ficha como publica completa.";
        }

        if (isModernSmartphone &&
            hasRichSingleSourceCoverage &&
            !hasOfficialSource &&
            !hasConsensusSources)
        {
            return "Ficha pronta para navegacao publica com fonte unica dominante. Consulte a origem por campo nas specs mais sensiveis.";
        }

        if (!smartphoneShapeReady && !featurePhoneReady)
        {
            return legacyProfile
                ? "Ainda faltam campos basicos para fechar esta ficha historica com seguranca."
                : "Ainda faltam campos essenciais para fechar esta ficha como pronta para comparacao publica.";
        }

        if (minConfidence < MinimumLegacySmartphoneConfidence)
        {
            return "A ficha ja aparece publicada, mas a confianca minima ainda esta baixa para uma promessa publica sem ressalvas.";
        }

        return "Ficha pronta para navegacao publica, mas continue checando a fonte de cada campo quando o modelo estiver em consolidacao recente.";
    }
}

internal sealed record CatalogSpecSnapshot(
    string Key,
    string? DisplayValue,
    string? SourceName = null,
    double Confidence = 0,
    SpecStatus Status = SpecStatus.Published,
    bool IsCritical = false);

internal enum CatalogTrustTier
{
    Review,
    SingleSource,
    Consensus,
    Official,
}

internal sealed record CatalogReadinessEvaluation(
    bool IsPublicReady,
    bool UsesEntryFallbackTier,
    CatalogTrustTier TrustTier,
    int SourceCount,
    bool HasOfficialSource,
    bool HasReviewFlags,
    double MinimumConfidence,
    string ReadinessNote)
{
    public static CatalogReadinessEvaluation NotReady { get; } = new(
        false,
        false,
        CatalogTrustTier.Review,
        0,
        false,
        false,
        0,
        "Esta ficha ainda nao tem evidencias suficientes para prometer uso publico completo.");

    public string TrustLabel => TrustTier switch
    {
        CatalogTrustTier.Official => "Fonte oficial",
        CatalogTrustTier.Consensus => "Consenso de fontes",
        CatalogTrustTier.SingleSource => "Fonte unica",
        _ => "Em revisao",
    };

    public string TrustSummary => TrustTier switch
    {
        CatalogTrustTier.Official => "Campos principais sustentados por fonte oficial verificada.",
        CatalogTrustTier.Consensus => "Campos principais batem entre fontes independentes.",
        CatalogTrustTier.SingleSource => "Ficha navegavel, mas ainda apoiada por uma fonte dominante.",
        _ => "Ainda existem conflitos, lacunas ou confianca baixa em campos essenciais.",
    };
}
