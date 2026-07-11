using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SpecTen.Web.Data;
using SpecTen.Web.Infrastructure;
using SpecTen.Web.Options;
using SpecTen.Web.Services;

namespace SpecTen.Tests;

public sealed class ApiSmokeTests(SpecTenWebApplicationFactory factory)
    : IClassFixture<SpecTenWebApplicationFactory>
{
    [Fact]
    public async Task PublicResponses_IncludeBaselineSecurityHeaders()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "camera=(), microphone=(), geolocation=()",
            Assert.Single(response.Headers.GetValues("Permissions-Policy")));
    }

    [Fact]
    public async Task SearchApi_MatchesTokenizedQueries()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=xiaomi%2015t%20pro");

        Assert.NotNull(results);
        var phone = results!.First();
        Assert.Equal("Xiaomi 15T Pro", phone.Name);
        Assert.Equal("Top de linha", phone.Tier);
    }

    [Fact]
    public async Task SearchApi_ReturnsLegacyFullCatalogMatch_ForIphone14()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=iphone%2014");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal("iPhone 14", results[0].Name);
        Assert.True(results[0].HasFullCatalogEntry);
        Assert.False(string.IsNullOrWhiteSpace(results[0].ImageUrl));
        Assert.True(results[0].SpecCount >= 12);
    }

    [Theory]
    [InlineData("galaxy s22", "Galaxy S22")]
    [InlineData("galaxy s22 5g", "Galaxy S22")]
    [InlineData("galaxy a54", "Galaxy A54 5G")]
    [InlineData("redmi note 12", "Redmi Note 12")]
    public async Task SearchApi_ReturnsLegacyAndroidFullCatalogMatch(string query, string expectedName)
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>($"/api/search?query={Uri.EscapeDataString(query)}");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal(expectedName, results[0].Name);
        Assert.True(results[0].HasFullCatalogEntry);
        Assert.False(string.IsNullOrWhiteSpace(results[0].ImageUrl));
    }

    [Fact]
    public async Task SearchApi_PrefersExactCompactModelMatch_OverNumericSuperset()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=lg%20g6");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal("G6", results[0].Name);
        Assert.Equal("LG", results[0].Brand);
        Assert.True(results[0].HasFullCatalogEntry);
        Assert.True(results[0].IsPublicReady);
    }

    [Fact]
    public async Task SearchApi_ClassifiesLegacyFlagshipAsTopDeLinha()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=lg%20g6");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal("G6", results[0].Name);
        Assert.Equal("Top de linha", results[0].Tier);
    }

    [Fact]
    public async Task SearchApi_DoesNotDuplicateCoverageResult_WhenFullCatalogAlreadyExists()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=redmi%20note%2012");

        Assert.NotNull(results);
        Assert.Contains(results!, item => item.Name == "Redmi Note 12" && item.HasFullCatalogEntry);
        Assert.DoesNotContain(results, item => item.Name == "Redmi Note 12" && !item.HasFullCatalogEntry);
    }

    [Fact]
    public async Task SuggestionsApi_DoesNotDuplicateCoverageOption_WhenFullCatalogAlreadyExists()
    {
        var client = factory.CreateClient();

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=redmi%20note%2012");

        Assert.NotNull(suggestions);
        Assert.Contains(suggestions!, item => item.Name == "Redmi Note 12" && item.HasFullCatalogEntry);
        Assert.DoesNotContain(suggestions, item => item.Name == "Redmi Note 12" && !item.HasFullCatalogEntry);
    }

    [Fact]
    public async Task SuggestionsApi_ReturnsAutocompleteOptions()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/search/suggestions?query=galaxy%20a");
        var suggestions = await response.Content.ReadFromJsonAsync<List<PhoneSuggestionDto>>();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("max-age=10", response.Headers.CacheControl?.ToString());
        Assert.NotNull(suggestions);
        Assert.Contains(suggestions!, item => item.Name == "Galaxy A56 5G");
        Assert.Contains(suggestions!, item => item.Name == "Galaxy A36 5G");
        Assert.Contains(suggestions!, item => item.Name == "Galaxy A16 5G");
    }

    [Fact]
    public async Task SuggestionsApi_ReturnsLegacyAutocompleteOption_ForIphone14()
    {
        var client = factory.CreateClient();

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=iphone%2014");

        Assert.NotNull(suggestions);
        Assert.NotEmpty(suggestions!);
        Assert.Equal("iPhone 14", suggestions[0].Name);
        Assert.True(suggestions[0].HasFullCatalogEntry);
    }

    [Fact]
    public async Task SearchApi_RefreshesStaleExactCatalogMatch_OnDemand()
    {
        await using var staleFactory = new SpecTenWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Coverage:CatalogEntryRefreshHours"] = "1",
        });
        await staleFactory.InitializeAsync();
        var client = staleFactory.CreateClient();
        var baselineResults = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=iphone%2014");

        Assert.NotNull(baselineResults);
        Assert.NotEmpty(baselineResults!);
        var baseline = baselineResults[0];
        var expectedKey = $"{baseline.BrandSlug}:{baseline.Slug}";

        await using (var scope = staleFactory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var phone = await db.PhoneModels.FirstAsync(model => model.Id == baseline.Id);
            phone.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30);
            await db.SaveChangesAsync();

            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            if (cache is MemoryCache concreteCache)
            {
                concreteCache.Compact(1.0);
            }
        }

        TestDeviceCoverageService.ResetObservedRequests();
        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=iphone%2014");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Contains(expectedKey, TestDeviceCoverageService.ObservedRequests());
    }

    [Fact]
    public async Task SearchApi_HydratesExactCoverageResult_WhenFullCatalogDoesNotHaveTheModel()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=vivo%20x300%20ultra");

        Assert.NotNull(results);
        var phone = Assert.Single(results!);
        Assert.Equal("Vivo", phone.Brand);
        Assert.Equal("X300 Ultra", phone.Name);
        Assert.True(phone.HasFullCatalogEntry);
        Assert.False(string.IsNullOrWhiteSpace(phone.ImageUrl));
        Assert.True(phone.SpecCount >= 6);
    }

    [Fact]
    public async Task LiveDiscovery_ReturnsExactCoverageWithoutHydratingDuringTyping()
    {
        await using var discoveryFactory = new SpecTenWebApplicationFactory();
        await discoveryFactory.InitializeAsync();
        var catalog = discoveryFactory.Services.GetRequiredService<CatalogService>();
        TestDeviceCoverageService.ResetObservedRequests();

        var results = await catalog.SearchForLiveDiscoveryAsync(
            "vivo x300 ultra",
            null,
            null,
            CatalogSortOption.Relevance,
            8,
            CancellationToken.None);
        var suggestions = await catalog.SuggestForLiveDiscoveryAsync("vivo x300 ultra", 8, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Vivo", result.Brand);
        Assert.Equal("X300 Ultra", result.Name);
        Assert.False(result.HasFullCatalogEntry);
        Assert.Contains(suggestions, item => item.Name == "X300 Ultra" && !item.HasFullCatalogEntry);
        Assert.Empty(TestDeviceCoverageService.ObservedRequests());
    }

    [Fact]
    public async Task SearchApi_DoesNotLeakIrrelevantCoverageFallbacks_WhenQueryIsSpecific()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=xiaomi%2015t%20pro");

        Assert.NotNull(results);
        Assert.Equal("Xiaomi 15T Pro", results!.First().Name);
        Assert.DoesNotContain(results, item => !item.HasFullCatalogEntry && !string.Equals(item.Brand, "Xiaomi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchApi_PromotesExactCoverageMatch_AheadOfRelatedFullModels()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=xiaomi%2015%20ultra");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal("Xiaomi", results![0].Brand);
        Assert.Equal("15 Ultra", results![0].Name);
        Assert.True(results[0].HasFullCatalogEntry);
        Assert.False(string.IsNullOrWhiteSpace(results[0].ImageUrl));
    }

    [Fact]
    public async Task SearchApi_PromotesStrongCoverageMatch_ForShortLegacyModelQueries()
    {
        var client = factory.CreateClient();

        await client.GetAsync("/api/search?query=vivo%20x300%20ultra");
        await client.GetAsync("/api/search?query=xiaomi%2015%20ultra");
        await client.GetAsync("/api/search?query=apple%20iphone%2014%20pro%20max");

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=mi%20a2");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal("Xiaomi", results[0].Brand);
        Assert.Equal("Mi A2 (Mi 6X)", results[0].Name);
        Assert.True(results[0].HasFullCatalogEntry);
        Assert.False(string.IsNullOrWhiteSpace(results[0].ImageUrl));
    }

    [Fact]
    public async Task SearchApi_KeepsSpecificModelResultsFocused_WhenSingleDigitTokenWouldOtherwiseLeakSpecNoise()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=pixel%206");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal("Pixel 6", results[0].Name);
        Assert.DoesNotContain(results, item => item.Name == "Pixel 9a");
        Assert.DoesNotContain(results, item => item.Brand == "Samsung" && item.Name == "Galaxy S7");
    }

    [Fact]
    public async Task SuggestionsApi_HydratesExactCoverageOption_ForMissingRecentModels()
    {
        var client = factory.CreateClient();

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=vivo%20x300%20ultra");

        Assert.NotNull(suggestions);
        Assert.Contains(suggestions!, item => item.Brand == "Vivo" && item.Name == "X300 Ultra" && item.HasFullCatalogEntry);
    }

    [Fact]
    public async Task SearchApi_AcceptsShortQAlias()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?q=xiaomi%2015t%20pro");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal("Xiaomi 15T Pro", results[0].Name);
    }

    [Fact]
    public async Task SuggestionsApi_PromotesExactCoverageMatch_AheadOfRelatedFullModels()
    {
        var client = factory.CreateClient();

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=xiaomi%2015%20ultra");

        Assert.NotNull(suggestions);
        Assert.NotEmpty(suggestions!);
        Assert.Equal("Xiaomi", suggestions![0].Brand);
        Assert.Equal("15 Ultra", suggestions![0].Name);
        Assert.True(suggestions[0].HasFullCatalogEntry);
    }

    [Fact]
    public async Task SuggestionsApi_PromotesStrongCoverageMatch_ForShortLegacyModelQueries()
    {
        var client = factory.CreateClient();

        await client.GetAsync("/api/search?query=vivo%20x300%20ultra");
        await client.GetAsync("/api/search?query=xiaomi%2015%20ultra");
        await client.GetAsync("/api/search?query=apple%20iphone%2014%20pro%20max");

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=mi%20a2");

        Assert.NotNull(suggestions);
        Assert.NotEmpty(suggestions!);
        Assert.Equal("Xiaomi", suggestions[0].Brand);
        Assert.Equal("Mi A2 (Mi 6X)", suggestions[0].Name);
        Assert.True(suggestions[0].HasFullCatalogEntry);
    }

    [Fact]
    public async Task SuggestionsApi_KeepsSpecificModelAutocompleteFocused_WhenSingleDigitTokenWouldOtherwiseLeakSpecNoise()
    {
        var client = factory.CreateClient();

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=pixel%206");

        Assert.NotNull(suggestions);
        Assert.NotEmpty(suggestions!);
        Assert.Equal("Pixel 6", suggestions[0].Name);
        Assert.DoesNotContain(suggestions, item => item.Name == "Pixel 9a");
    }

    [Fact]
    public async Task SearchApi_PrefersFullCatalogResults_ForBroadSeriesQuery()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=galaxy");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.True(results[0].HasFullCatalogEntry);
        Assert.Equal("Samsung", results[0].Brand);
        Assert.DoesNotContain(results.Take(5), item => !item.HasFullCatalogEntry && item.Name == "Galaxy");
    }

    [Fact]
    public async Task SuggestionsApi_PrefersFullCatalogResults_ForBroadSeriesQuery()
    {
        var client = factory.CreateClient();

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=galaxy");

        Assert.NotNull(suggestions);
        Assert.NotEmpty(suggestions!);
        Assert.True(suggestions[0].HasFullCatalogEntry);
        Assert.Equal("Samsung", suggestions[0].Brand);
        Assert.DoesNotContain(suggestions.Take(5), item => !item.HasFullCatalogEntry && item.Name == "Galaxy");
    }

    [Fact]
    public async Task SuggestionsApi_HidesNoisyCoverageVariants_FromAutocomplete()
    {
        var client = factory.CreateClient();

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=vivo%20x300");

        Assert.NotNull(suggestions);
        Assert.DoesNotContain(suggestions!, item => item.Name.Contains("卫星", StringComparison.Ordinal));
        Assert.Contains(suggestions!, item => item.Name == "X300 Ultra");
    }

    [Fact]
    public async Task SuggestionsApi_KeepsParentheticalModelNames_WhenTheyAreLegitimateProducts()
    {
        var client = factory.CreateClient();

        var suggestions = await client.GetFromJsonAsync<List<PhoneSuggestionDto>>("/api/search/suggestions?query=nothing%20phone%202a%20plus");

        Assert.NotNull(suggestions);
        Assert.Contains(suggestions!, item => item.Name == "Phone (2a) Plus" && item.HasFullCatalogEntry);
    }

    [Fact]
    public async Task SearchApi_KeepsLegacyFlagshipAsTopTier_WhenSignalsDisagree()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=iphone%2014%20pro%20max");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.Equal("iPhone 14 Pro Max", results[0].Name);
        Assert.Equal("Top de linha", results[0].Tier);
    }

    [Fact]
    public async Task SearchApi_FiltersByBrandAndSortsAlphabetically()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?brand=samsung&sort=name");

        Assert.NotNull(results);
        Assert.NotEmpty(results!);
        Assert.All(results!, phone => Assert.Equal("Samsung", phone.Brand));

        var names = results.Select(phone => phone.Name).ToList();
        var ordered = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(ordered, names);
    }

    [Fact]
    public async Task SearchApi_ReturnsCoverageOnlyBrandBrowseResults()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?brand=vivo");

        Assert.NotNull(results);
        Assert.Equal(2, results!.Count);
        Assert.All(results, result => Assert.Equal("vivo", result.BrandSlug));
        Assert.Contains(results, result => result.Name == "X300 Ultra");
        Assert.Contains(results, result => result.Name == "X300");
        Assert.DoesNotContain(results, result => result.Name.Contains("卫星", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchApi_ProvidesStrongerImageCoverage_ForFullCatalog()
    {
        var client = factory.CreateClient();

        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");

        Assert.NotNull(results);
        Assert.All(results!.Where(phone => phone.HasFullCatalogEntry), phone => Assert.False(string.IsNullOrWhiteSpace(phone.ImageUrl)));
    }

    [Fact]
    public async Task SearchApi_RefreshesIncompleteExistingCoverageMatch_WhenFixtureHasRicherPublicData()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var brand = await db.Brands.FirstOrDefaultAsync(item => item.Slug == "xiaomi");
            if (brand is null)
            {
                brand = new Brand
                {
                    Name = "Xiaomi",
                    Slug = "xiaomi",
                };
                db.Brands.Add(brand);
                await db.SaveChangesAsync();
            }

            var existingPhone = await db.PhoneModels.FirstOrDefaultAsync(item =>
                item.BrandId == brand.Id &&
                item.Slug == "mi-a2-mi-6x");

            if (existingPhone is null)
            {
                existingPhone = new PhoneModel
                {
                    BrandId = brand.Id,
                    Name = "Mi A2 (Mi 6X)",
                    Slug = "mi-a2-mi-6x",
                    Summary = "Stub incompleto para validar refresh da cobertura.",
                };
                db.PhoneModels.Add(existingPhone);
            }

            existingPhone.ImageUrl = null;
            existingPhone.ImageSourceUrl = null;
            existingPhone.ReleasedAt = null;
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var results = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=mi%20a2");

        Assert.NotNull(results);
        var refreshedPhone = Assert.Single(results!, item => item.Name == "Mi A2 (Mi 6X)");
        Assert.NotNull(refreshedPhone.ReleasedAt);
        Assert.False(string.IsNullOrWhiteSpace(refreshedPhone.ImageUrl));
        Assert.True(refreshedPhone.SpecCount >= 7);
    }

    [Fact]
    public async Task CatalogPage_RendersPublicFilters()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("catalog-brand", html, StringComparison.Ordinal);
        Assert.Contains("catalog-sort", html, StringComparison.Ordinal);
        Assert.Contains("Ir para resultados", html, StringComparison.Ordinal);
        Assert.Contains("catalog-results", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Atualizar URL", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminRoute_RedirectsToCatalog()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/celulares", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task UnknownRoute_ShowsFriendlyNotFoundPage()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/rota-inexistente");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Pagina nao encontrada", html, StringComparison.Ordinal);
        Assert.Contains("Esse caminho nao existe no catalogo.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Algo saiu errado", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_ShowsActiveFilterBar_WhenFiltering()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?busca=iphone&marca=apple&ordem=name");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Filtros ativos", html, StringComparison.Ordinal);
        Assert.Contains("Busca: iphone", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Marca: Apple", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_UsesCompactHero_WhenFiltering()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?busca=galaxy");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("catalog-hero tool-hero compact", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_PromotesCoverageExactMatch_WhenOnlyRelatedFullResultsExist()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?busca=xiaomi%2015%20ultra");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Xiaomi 15 Ultra", html, StringComparison.Ordinal);
        Assert.True(
            html.Contains("Ver ficha", StringComparison.Ordinal) ||
            html.Contains("Abrir ficha parcial", StringComparison.Ordinal));
        Assert.DoesNotContain("ficha publica com specs verificadas ainda esta em preparacao", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Correspondencia exata", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_UsesProgressiveDisclosure_WhenCoverageBrandHasMixedStates()
    {
        var client = factory.CreateClient();
        _ = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=vivo%20x300%20ultra");

        var response = await client.GetAsync("/celulares?marca=vivo");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Vivo X300 Ultra", html, StringComparison.Ordinal);
        Assert.True(
            html.Contains("Cobertura inicial", StringComparison.Ordinal) ||
            html.Contains("Fichas parciais", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CatalogPage_RendersMobileFilterToggle()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Refinar filtros", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_BrandFilter_StaysFocusedOnBrandsWithFullCatalog()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Apple (", html, StringComparison.Ordinal);
        Assert.Contains("Samsung (", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Vivo (382)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ZTE (549)", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_DoesNotRenderSuggestionPanel_ByDefaultWhenSearchComesFromQuery()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?busca=xiaomi%2015");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.DoesNotContain("<div id=\"catalog-suggestions\"", html, StringComparison.Ordinal);
        Assert.Contains("Busca: xiaomi 15", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CatalogPage_HydratesExactCoverageMatch_WhenFullCatalogIsMissing()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?busca=vivo%20x300%20ultra");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Vivo X300 Ultra", html, StringComparison.Ordinal);
        Assert.True(
            html.Contains("Ver ficha", StringComparison.Ordinal) ||
            html.Contains("Abrir ficha parcial", StringComparison.Ordinal));
        Assert.DoesNotContain("Abrir ficha completa", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_ShowsQuickStartNavigation()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Comecar rapido", html, StringComparison.Ordinal);
        Assert.Contains("Top de linha", html, StringComparison.Ordinal);
        Assert.Contains("Comparador", html, StringComparison.Ordinal);
        Assert.Contains("Explorar por marca", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_ShowsPrimaryHeroActions()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Abrir catalogo", html, StringComparison.Ordinal);
        Assert.Contains("Comparar modelos", html, StringComparison.Ordinal);
        Assert.Contains("Ver destaque", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_RendersGlobalHeaderSearch()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("topbar-search-input", html, StringComparison.Ordinal);
        Assert.Contains("Buscar em qualquer pagina", html, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Buscar celular...\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_ShowsPopularComparisonShortcuts()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Comparacoes prontas", html, StringComparison.Ordinal);
        Assert.Contains("jump-chip", html, StringComparison.Ordinal);
        Assert.Matches("/comparar\\?ids=\\d+,\\d+", html);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsRelatedModelsSection()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/xiaomi-15t-pro");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Modelos parecidos para comparar", html, StringComparison.Ordinal);
        Assert.Contains("Comparar com este", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsImmediateCompareTargetsNearHero()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/xiaomi-15t-pro");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Comparar agora com", html, StringComparison.Ordinal);
        Assert.Contains("/comparar?ids=", html, StringComparison.Ordinal);
        Assert.Contains("Xiaomi 15T Pro", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsLegacyIphone14WithImageAndSpecs()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/apple/iphone-14");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Apple iPhone 14", html, StringComparison.Ordinal);
        Assert.Contains("Bateria", html, StringComparison.Ordinal);
        Assert.Contains("Camera principal", html, StringComparison.Ordinal);
        Assert.Contains("https://fdn2.gsmarena.com/vv/bigpic/apple-iphone-14.jpg", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsLegacyRedmiNote12WithImageAndSpecs()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/redmi-note-12");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Redmi Note 12", html, StringComparison.Ordinal);
        Assert.Contains("Bateria", html, StringComparison.Ordinal);
        Assert.Contains("Camera principal", html, StringComparison.Ordinal);
        Assert.Contains("https://i02.appmifile.com/861_operatorx_operatorx_xm/16/03/2023/0ac834a7279d58346efb2fa8196442cc.jpg", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsFreshnessSignals()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/xiaomi-15t-pro");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Ultima consolidacao", html, StringComparison.Ordinal);
        Assert.Contains("Atualizado", html, StringComparison.Ordinal);
        Assert.Contains("Coletado", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsShareAction()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/xiaomi-15t-pro");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Compartilhar", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsCompareSelectionActions()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/xiaomi-15t-pro");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Adicionar ao comparador", html, StringComparison.Ordinal);
        Assert.Contains("Escolher rival no catalogo", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsSectionJumpBar()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/xiaomi-15t-pro");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Navegar pela ficha", html, StringComparison.Ordinal);
        Assert.Contains("#spec-performance", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#variantes", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsHeroShortcutToSpecs()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/xiaomi-15t-pro");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Ver ficha tecnica", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#ficha-tecnica\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ficha-tecnica\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhoneDetailsPage_ShowsSpecFilterTools()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/google/pixel-9a");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Filtrar specs desta ficha", html, StringComparison.Ordinal);
        Assert.Contains("Mostrar so itens em revisao", html, StringComparison.Ordinal);
        Assert.Matches(@"\d+ specs visiveis", html);
    }

    [Fact]
    public async Task PhoneDetailsPage_RendersCoveragePlaceholder_WhenOnlyCoverageExists()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/vivo/x300");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Ficha completa em preparacao", html, StringComparison.Ordinal);
        Assert.Contains("Cobertura inicial", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Reportar erro", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_AddsNoIndexMeta_WhenPhoneIsNotPublicReady()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/vivo/x300");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("name=\"robots\" content=\"noindex,follow\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhoneDetailsPage_DoesNotAddNoIndexMeta_WhenPhoneIsPublicReady()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/apple/iphone-14");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.DoesNotContain("name=\"robots\" content=\"noindex,follow\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhoneDetailsPage_ReturnsNotFoundStatus_ForMissingPhoneSlug()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares/xiaomi/modelo-que-nao-existe");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Aparelho nao encontrado", html, StringComparison.Ordinal);
        Assert.Contains("Buscar nome parecido", html, StringComparison.Ordinal);
        Assert.Contains("/celulares?busca=xiaomi%20modelo%20que%20nao%20existe", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComparePage_ShowsShareActionAndCoverageMetrics()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=galaxy");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "Galaxy S25 Ultra" or "Galaxy A56 5G")
            .Take(2)
            .Select(phone => phone.Id));

        var response = await client.GetAsync($"/comparar?ids={ids}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Compartilhar", html, StringComparison.Ordinal);
        Assert.Contains("Cobertura na tabela:", html, StringComparison.Ordinal);
        Assert.Contains("Ir para grupo", html, StringComparison.Ordinal);
        Assert.Contains("Filtrar specs visiveis", html, StringComparison.Ordinal);
        Assert.Contains("Lancamento", html, StringComparison.Ordinal);
        Assert.Contains("Atualizado", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComparePage_OffersQuickStartSuggestions_WhenEmpty()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/comparar");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Sugestoes rapidas para comecar", html, StringComparison.Ordinal);
        Assert.Contains("sem obrigar o usuario a adivinhar o nome exato", html, StringComparison.Ordinal);
        Assert.Contains("Nenhum aparelho selecionado", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComparePage_HidesQuickStartSuggestions_WhenComparisonAlreadyHasTwoModels()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "Galaxy S25 Ultra" or "Galaxy A56 5G")
            .Take(2)
            .Select(phone => phone.Id));

        var response = await client.GetAsync($"/comparar?ids={ids}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.DoesNotContain("Sugestoes rapidas para comecar", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_RendersCoverageOnlySearchResult_WhenHydrationIsUnavailable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?busca=vivo%20x300");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("X300", html, StringComparison.Ordinal);
        Assert.Contains("Abrir ficha completa", html, StringComparison.Ordinal);
        Assert.Contains("Cobertura inicial", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_OffersRecoveryShortcuts_WhenNoResults()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?busca=modelo-que-nao-existe-123");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Nenhum aparelho encontrado", html, StringComparison.Ordinal);
        Assert.Contains("Atalhos para destravar a busca", html, StringComparison.Ordinal);
        Assert.Contains("Remover so a busca", html, StringComparison.Ordinal);
        Assert.Contains("Samsung", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_SplitsFullAndCoverageSections_WhenSearchMixesStates()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?busca=pro");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Fichas completas", html, StringComparison.Ordinal);
        Assert.Contains("Cobertura inicial", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_RendersSectionJumpBar_ForPublicBrowsing()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Ir para categoria", html, StringComparison.Ordinal);
        Assert.Contains("#catalog-top-de-linha", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#catalog-intermediarios", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CatalogPage_RendersQuickBrandStrip()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Marcas com ficha completa", html, StringComparison.Ordinal);
        Assert.Contains("Samsung", html, StringComparison.Ordinal);
        Assert.Contains("Xiaomi", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_RendersPopularComparisonShortcuts()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Comparacoes prontas", html, StringComparison.Ordinal);
        Assert.Contains("Abrir comparacao", html, StringComparison.Ordinal);
        Assert.Matches("/comparar\\?ids=\\d+,\\d+", html);
    }

    [Fact]
    public async Task CatalogPage_RendersDiscoveryExtras_AfterPrimaryResults()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);

        var firstCatalogSectionIndex = html.IndexOf("<section id=\"catalog-top-de-linha\"", StringComparison.OrdinalIgnoreCase);
        var metricsIndex = html.IndexOf("Resumo do catalogo", StringComparison.Ordinal);
        var brandsIndex = html.IndexOf("Marcas com ficha completa", StringComparison.Ordinal);
        var comparisonsIndex = html.IndexOf("Comparacoes prontas", StringComparison.Ordinal);

        Assert.True(firstCatalogSectionIndex >= 0);
        Assert.True(metricsIndex > firstCatalogSectionIndex);
        Assert.True(brandsIndex > firstCatalogSectionIndex);
        Assert.True(comparisonsIndex > firstCatalogSectionIndex);
    }

    [Fact]
    public async Task CatalogPage_PrefersFeaturedBrandsWithPublishedSpecs()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Marcas com ficha completa", html, StringComparison.Ordinal);
        Assert.Contains(">Samsung<", html, StringComparison.Ordinal);
        Assert.Contains(">Xiaomi<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">ZTE<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_UsesProgressiveDisclosure_ForLargeSections()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Matches(@"\d+ de \d+ aparelho\(s\) visiveis", html);
        Assert.Contains("Mostrar mais", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_ShowsCoverageOnlySection_WhenBrowsingCoverageOnlyBrand()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares?marca=vivo");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Cobertura inicial", html, StringComparison.Ordinal);
        Assert.Contains("Vivo X300 Ultra", html, StringComparison.Ordinal);
        Assert.Contains("Vivo X300", html, StringComparison.Ordinal);
        Assert.DoesNotContain("卫星", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_ShowsLiveSearchGuidance()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Busca instantanea", html, StringComparison.Ordinal);
        Assert.Contains("sugestoes enquanto a lista filtra ao vivo", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogPage_RendersReusableMediaFallbackMarkup()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/celulares");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("media-frame", html, StringComparison.Ordinal);
        Assert.Contains("fallback-hidden", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompareApi_ReturnsRicherRowsForSelectedPhones()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=galaxy");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "Galaxy S25 Ultra" or "Galaxy A56 5G")
            .Take(2)
            .Select(phone => phone.Id));
        var response = await client.GetAsync($"/api/compare?ids={ids}");
        var comparison = await response.Content.ReadFromJsonAsync<CompareResultDto>();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("max-age=30", response.Headers.CacheControl?.ToString());
        Assert.NotNull(comparison);
        Assert.Equal(2, comparison!.Phones.Count);
        Assert.Contains(comparison.Rows, row => row.Key == "chipset");
        Assert.Contains(comparison.Rows, row => row.Key == "released_at");
        Assert.Contains(comparison.Rows, row => row.Key == "launch_price");
        Assert.Contains(comparison.Rows, row => row.Key == "storage_options");
        Assert.Contains(comparison.Rows, row => row.Key == "benchmark_score");
        Assert.Contains(comparison.Rows, row => row.Key == "dimensions");
        Assert.Contains(comparison.Rows, row => row.Key == "sim");
        Assert.Contains(comparison.Rows, row => row.Key == "wifi");
        Assert.Contains(comparison.Rows, row => row.Key == "bluetooth");
        Assert.Contains(comparison.Rows, row => row.Key == "usb");
        Assert.Single(comparison.Rows, row => row.Key == "storage_options");
        Assert.True(comparison.Rows.Count >= 20);
    }

    [Fact]
    public async Task CompareApi_NormalizesBenchmarkFamiliesAcrossRecentModels()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "Pixel 9a" or "Galaxy A56 5G")
            .Take(2)
            .Select(phone => phone.Id));
        var comparison = await client.GetFromJsonAsync<CompareResultDto>($"/api/compare?ids={ids}");

        Assert.NotNull(comparison);
        var benchmark = Assert.Single(comparison!.Rows, row => row.Key == "benchmark_score");

        Assert.Equal("AnTuTu", benchmark.DisplayName);
        Assert.NotEqual("-", benchmark.Values.Values.First());
        Assert.NotEqual("-", benchmark.Values.Values.Last());
    }

    [Fact]
    public async Task CompareApi_HighlightsMorePublicFacingWinnerRows()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "Galaxy S25 Ultra" or "Galaxy A56 5G")
            .Take(2)
            .Select(phone => phone.Id));
        var comparison = await client.GetFromJsonAsync<CompareResultDto>($"/api/compare?ids={ids}");

        Assert.NotNull(comparison);
        var display = Assert.Single(comparison!.Rows, row => row.Key == "display_size");
        var mainCamera = Assert.Single(comparison.Rows, row => row.Key == "main_camera");

        Assert.Equal(
            comparison.Phones.First(phone => phone.Name == "Galaxy S25 Ultra").Id,
            display.WinnerPhoneId);
        Assert.Equal(
            comparison.Phones.First(phone => phone.Name == "Galaxy S25 Ultra").Id,
            mainCamera.WinnerPhoneId);
    }

    [Fact]
    public async Task CompareApi_AvoidsFalseWinner_WhenChargingUnitsDoNotMatch()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "iPhone 16 Pro" or "Galaxy S25 Ultra")
            .Take(2)
            .Select(phone => phone.Id));
        var comparison = await client.GetFromJsonAsync<CompareResultDto>($"/api/compare?ids={ids}");

        Assert.NotNull(comparison);
        var charging = Assert.Single(comparison!.Rows, row => row.Key == "charging");
        var weight = Assert.Single(comparison.Rows, row => row.Key == "weight");

        Assert.Null(charging.WinnerPhoneId);
        Assert.Equal(
            comparison.Phones.First(phone => phone.Name == "iPhone 16 Pro").Id,
            weight.WinnerPhoneId);
    }

    [Fact]
    public async Task ComparePage_ShowsComparisonHighlights()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "iPhone 16 Pro" or "Galaxy S25 Ultra")
            .Take(2)
            .Select(phone => phone.Id));

        var response = await client.GetAsync($"/comparar?ids={ids}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Destaques da comparacao", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Galaxy S25 Ultra", html, StringComparison.Ordinal);
        Assert.Contains("Camera principal em destaque", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComparePage_UsesCompactHeading_WhenComparisonIsReady()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "iPhone 16 Pro" or "Galaxy S25 Ultra")
            .Take(2)
            .Select(phone => phone.Id));

        var response = await client.GetAsync($"/comparar?ids={ids}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("compare-heading tool-heading compact", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComparePage_CollapsesBuilder_WhenComparisonIsReady()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "iPhone 16 Pro" or "Galaxy S25 Ultra")
            .Take(2)
            .Select(phone => phone.Id));

        var response = await client.GetAsync($"/comparar?ids={ids}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("compare-ready-bar", html, StringComparison.Ordinal);
        Assert.Contains("Trocar modelos", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Buscar e adicionar modelos", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComparePage_ShowsJumpToTableAction_WhenComparisonIsReady()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "iPhone 16 Pro" or "Galaxy S25 Ultra")
            .Take(2)
            .Select(phone => phone.Id));

        var response = await client.GetAsync($"/comparar?ids={ids}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Ir para tabela", html, StringComparison.Ordinal);
        Assert.Contains("compare-results-shell", html, StringComparison.Ordinal);
        Assert.Contains("compare-table-shell", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComparePage_RendersMobileComparisonStackMarkup()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "iPhone 16 Pro" or "Galaxy S25 Ultra")
            .Take(2)
            .Select(phone => phone.Id));

        var response = await client.GetAsync($"/comparar?ids={ids}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("compare-mobile-stack", html, StringComparison.Ordinal);
        Assert.Contains("mobile-spec-card", html, StringComparison.Ordinal);
        Assert.Contains("mobile-spec-phone", html, StringComparison.Ordinal);
        Assert.Contains("Galaxy S25 Ultra", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComparePage_RendersCollapsibleMobileGroupControls()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search");
        Assert.NotNull(search);

        var ids = string.Join(",", search!
            .Where(phone => phone.Name is "iPhone 16 Pro" or "Galaxy S25 Ultra")
            .Take(2)
            .Select(phone => phone.Id));

        var response = await client.GetAsync($"/comparar?ids={ids}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("mobile-section-toolbar", html, StringComparison.Ordinal);
        Assert.Contains("mobile-spec-group-toggle", html, StringComparison.Ordinal);
        Assert.Contains("Ver essenciais", html, StringComparison.Ordinal);
        Assert.Contains("Abrir grupo", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportApi_CreatesCorrectionReport()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=iphone%2016%20pro");
        Assert.NotNull(search);
        var phone = search!.First(item => item.Name == "iPhone 16 Pro");

        var response = await client.PostAsJsonAsync("/api/reports", new CorrectionReportRequest(
            phone.Id,
            "battery",
            "reader@example.test",
            "Bateria parece divergente."));

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
    }

    [Fact]
    public async Task ReportApi_RejectsInvalidEmail()
    {
        var client = factory.CreateClient();
        var search = await client.GetFromJsonAsync<List<PhoneSearchResult>>("/api/search?query=iphone%2016%20pro");
        Assert.NotNull(search);
        var phone = search!.First(item => item.Name == "iPhone 16 Pro");

        var response = await client.PostAsJsonAsync("/api/reports", new CorrectionReportRequest(
            phone.Id,
            "battery",
            "nao-e-email",
            "Bateria parece divergente."));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("email valido", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RobotsTxt_ListsSitemap()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/robots.txt");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("User-agent: *", body, StringComparison.Ordinal);
        Assert.Contains("Allow: /", body, StringComparison.Ordinal);
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhoneDetailsPage_EmbedsProtectedPublicReportForm()
    {
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/celulares/apple/iphone-16-pro");

        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("name=\"company\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SitemapAndRobots_ExposePublicCacheHeaders()
    {
        var client = factory.CreateClient();

        var sitemap = await client.GetAsync("/sitemap.xml");
        var robots = await client.GetAsync("/robots.txt");

        Assert.True(sitemap.IsSuccessStatusCode);
        Assert.True(robots.IsSuccessStatusCode);
        Assert.Contains("max-age=900", sitemap.Headers.CacheControl?.ToString());
        Assert.Contains("max-age=3600", robots.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ApiRateLimiter_ReturnsTooManyRequests_WhenConfiguredThresholdIsExceeded()
    {
        using var limiter = PublicApiRateLimiterFactory.Create(new PublicApiOptions
        {
            RateLimitPermitLimit = 3,
            RateLimitWindowSeconds = 60,
        });
        RateLimitLease? rejected = null;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/search";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";

            var lease = await limiter.AcquireAsync(context, permitCount: 1);
            if (!lease.IsAcquired)
            {
                rejected = lease;
                break;
            }

            lease.Dispose();
        }

        Assert.NotNull(rejected);
        Assert.False(rejected!.IsAcquired);
        Assert.True(rejected.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(60), retryAfter);
        rejected.Dispose();
    }

    [Fact]
    public async Task ReportForm_RedirectsBackWithErrorFlag_WhenPayloadIsInvalid()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var html = await client.GetStringAsync("/celulares/apple/iphone-16-pro");
        var antiforgeryToken = ExtractAntiforgeryToken(html);

        var payload = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["__RequestVerificationToken"] = antiforgeryToken,
            ["phoneModelId"] = "0",
            ["fieldKey"] = "battery",
            ["company"] = "",
            ["reporterEmail"] = "reader@example.test",
            ["message"] = "",
            ["returnUrl"] = "/celulares/apple/iphone-16-pro",
        }!);

        var response = await client.PostAsync("/api/reports/form", payload);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        var locationValue = location.OriginalString;
        Assert.StartsWith("/celulares/apple/iphone-16-pro", locationValue, StringComparison.Ordinal);
        Assert.Contains("reportError=1", locationValue, StringComparison.Ordinal);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"[^>]*>",
            RegexOptions.IgnoreCase);

        Assert.True(match.Success, "Nao foi possivel localizar o token antiforgery no HTML.");
        return match.Groups[1].Value;
    }
}
