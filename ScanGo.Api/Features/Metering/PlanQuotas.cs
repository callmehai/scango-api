using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Features.Metering;

/// <summary>Weekly scan/ask allowance for a plan.</summary>
public readonly record struct PlanQuota(int WeeklyScans, int WeeklyAsks);

/// <summary>
/// Per-plan weekly quota catalogue. Mirrors the pricing shown in the web
/// Settings page. EVERY plan is metered (there is no truly-unlimited plan;
/// the "unlimited" plan is just a very high ceiling). Only the Free plan's
/// numbers are admin-tunable at runtime (FreeWeeklyScans/FreeWeeklyAsks);
/// paid tiers are fixed here.
///
/// NOTE: admin and tester ROLES still bypass metering entirely — see
/// <see cref="QuotaService.IsLimited"/>.
/// </summary>
public static class PlanQuotas
{
    // Paid + internal tiers (Free is resolved from runtime settings).
    private static readonly Dictionary<string, PlanQuota> Fixed = new()
    {
        // Lite = same weekly allowance as Basic, but only valid for 7 days.
        [PlanCodes.Lite] = new(WeeklyScans: 20, WeeklyAsks: 50),
        [PlanCodes.BasicMonthly] = new(WeeklyScans: 20, WeeklyAsks: 50),
        [PlanCodes.ProMonthly] = new(WeeklyScans: 100, WeeklyAsks: 200),
        [PlanCodes.ProYearly] = new(WeeklyScans: 100, WeeklyAsks: 200),
        [PlanCodes.Unlimited] = new(WeeklyScans: 999, WeeklyAsks: 999),
    };

    /// <summary>
    /// Resolve the weekly quota for <paramref name="plan"/>. Free (or any
    /// unrecognised plan) uses the admin-tunable free allowance passed in.
    /// </summary>
    public static PlanQuota Resolve(string plan, int freeWeeklyScans, int freeWeeklyAsks)
    {
        if (Fixed.TryGetValue(plan, out var q)) return q;
        return new PlanQuota(freeWeeklyScans, freeWeeklyAsks);
    }
}
