using ScanGo.Api.Features.Me;

namespace ScanGo.Api.Features.Metering;

/// <summary>
/// Periodic background maintenance. Quota itself resets automatically via
/// ISO-week period keys (Mon 00:00 VN), so this job only hard-deletes accounts
/// whose 30-day deletion grace has expired. Runs every 6h; first run is delayed
/// so it never fires during the test run's lifetime.
/// </summary>
public class WeeklyMaintenanceService(
    IServiceScopeFactory scopeFactory,
    ILogger<WeeklyMaintenanceService> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var deletion = scope.ServiceProvider
                    .GetRequiredService<IAccountDeletionService>();
                var n = await deletion.ProcessScheduledDeletionsAsync(stoppingToken);
                if (n > 0)
                    log.LogInformation("Maintenance: hard-deleted {Count} expired accounts", n);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Maintenance cycle failed");
            }
        }
    }
}
