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
}
