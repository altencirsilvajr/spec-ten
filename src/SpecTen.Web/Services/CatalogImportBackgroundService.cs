using Microsoft.Extensions.Options;
using SpecTen.Web.Options;

namespace SpecTen.Web.Services;

public sealed class CatalogImportBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ScrapingOptions> options,
    ILogger<CatalogImportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Daily import background service is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(DateTimeOffset.UtcNow, options.Value.DailyUtcHour);
            logger.LogInformation("Next phone import scheduled in {Delay}.", delay);
            await Task.Delay(delay, stoppingToken);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var importer = scope.ServiceProvider.GetRequiredService<PhoneImportService>();
                await importer.RunImportAsync("daily-background", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Daily phone import failed.");
            }
        }
    }

    internal static TimeSpan GetDelayUntilNextRun(DateTimeOffset now, int utcHour)
    {
        var normalizedHour = Math.Clamp(utcHour, 0, 23);
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, normalizedHour, 0, 0, TimeSpan.Zero);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
