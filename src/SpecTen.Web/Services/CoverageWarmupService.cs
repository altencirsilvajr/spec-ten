namespace SpecTen.Web.Services;

public sealed class CoverageWarmupService(
    IDeviceCoverageService coverageService,
    ILogger<CoverageWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            await coverageService.WarmupAsync(stoppingToken);
            logger.LogInformation("Public coverage index warmup completed.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Public coverage warmup failed. Search fallback will keep lazy-loading.");
        }
    }
}
