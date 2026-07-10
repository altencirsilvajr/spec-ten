using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Specpen.Web.Data;
using Specpen.Web.Services;

namespace Specpen.Tests;

public sealed class ImportIdempotencyTests(SpecpenWebApplicationFactory factory)
    : IClassFixture<SpecpenWebApplicationFactory>
{
    [Fact]
    public async Task Import_DoesNotDuplicatePhonesVariantsOrBenchmarks()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();

        await using var beforeDb = await dbFactory.CreateDbContextAsync();
        var phoneCountBefore = await beforeDb.PhoneModels.CountAsync();
        var variantCountBefore = await beforeDb.PhoneVariants.CountAsync();
        var benchmarkCountBefore = await beforeDb.BenchmarkScores.CountAsync();

        await importer.RunImportAsync("idempotency-test", CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Equal(phoneCountBefore, await db.PhoneModels.CountAsync());
        Assert.Equal(variantCountBefore, await db.PhoneVariants.CountAsync());
        Assert.Equal(benchmarkCountBefore, await db.BenchmarkScores.CountAsync());
    }
}
