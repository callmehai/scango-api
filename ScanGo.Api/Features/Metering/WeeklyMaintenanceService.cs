using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Me;

namespace ScanGo.Api.Features.Metering;

/// <summary>
/// Periodic background maintenance. Quota itself resets automatically via
/// ISO-week period keys (Mon 00:00 VN), so this job hard-deletes accounts whose
/// 30-day deletion grace has expired AND downgrades users whose paid plan has
/// expired back to Free. Runs every 6h; first run is delayed so it never fires
/// during the test run's lifetime.
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

                // Downgrade users whose paid plan has expired back to Free.
                // (Free/Unlimited have null expiry → never matched.) The lazy
                // enforcement on refresh/usage usually handles this first; this
                // sweep catches dormant accounts that don't make requests.
                var db = scope.ServiceProvider.GetRequiredService<ScanGoDbContext>();
                var now = DateTime.UtcNow;
                var expired = await db.Users
                    .Where(u => u.PlanExpiresAt != null
                                && u.PlanExpiresAt <= now
                                && u.Plan != PlanCodes.Free)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.Plan, PlanCodes.Free)
                        .SetProperty(u => u.PlanExpiresAt, (DateTime?)null)
                        .SetProperty(u => u.UpdatedAt, now), stoppingToken);
                if (expired > 0)
                    log.LogInformation("Maintenance: downgraded {Count} expired plan(s) to Free", expired);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Maintenance cycle failed");
            }
        }
    }
}
