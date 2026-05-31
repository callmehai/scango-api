using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Features.Billing;

/// <summary>
/// Plan catalogue — single source of truth for plan codes, display name, VND
/// price and validity. Used by admin manual upgrades now and the (future)
/// automatic payment flow. Codes must match <see cref="PlanCodes"/>.
/// </summary>
public record PlanInfo(string Code, string Name, long PriceVnd, int? DurationDays);

public static class Plans
{
    public static readonly IReadOnlyList<PlanInfo> All =
    [
        new(PlanCodes.Free, "Free", 0, null),
        new(PlanCodes.Lite, "Lite (tuần)", 19_000, 7),
        new(PlanCodes.BasicMonthly, "Basic (tháng)", 49_000, 30),
        new(PlanCodes.ProMonthly, "Pro (tháng)", 149_000, 30),
        new(PlanCodes.ProYearly, "Max (năm)", 1_290_000, 365),
        new(PlanCodes.Unlimited, "Unlimited (nội bộ)", 0, null),
    ];

    public static PlanInfo? Find(string code) =>
        All.FirstOrDefault(p => p.Code == code);

    /// <summary>
    /// When does a plan activated at <paramref name="from"/> expire?
    /// Returns null for plans without a duration (Free, Unlimited) — they never
    /// expire. Paid tiers expire <c>DurationDays</c> after activation.
    /// </summary>
    public static DateTime? ExpiryFrom(string code, DateTime from)
    {
        var info = Find(code);
        return info?.DurationDays is int days ? from.AddDays(days) : null;
    }

    /// <summary>
    /// If <paramref name="user"/>'s paid plan has expired (PlanExpiresAt in the
    /// past), downgrade it to Free in-place and return true. Caller is
    /// responsible for persisting (SaveChanges). Safe to call on any user —
    /// no-op for plans without an expiry. This is the lazy counterpart to the
    /// hourly background sweep, so an expired plan is corrected on the very next
    /// token refresh / usage read instead of waiting up to an hour.
    /// </summary>
    public static bool EnforceExpiry(Database.Entities.User user, DateTime now)
    {
        if (user.PlanExpiresAt is { } exp
            && exp <= now
            && user.Plan != PlanCodes.Free)
        {
            user.Plan = PlanCodes.Free;
            user.PlanExpiresAt = null;
            return true;
        }
        return false;
    }
}
