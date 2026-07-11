using Microsoft.EntityFrameworkCore;
using SpecTen.Web.Data;

namespace SpecTen.Web.Services;

public static class CatalogDatabaseInitializer
{
    public static async Task InitializeCatalogDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
        await importer.RunImportAsync("startup-sync", cancellationToken);
        await importer.NormalizeCatalogAsync(cancellationToken);
        await importer.RefreshAllClassificationsAsync(cancellationToken);
    }
}
