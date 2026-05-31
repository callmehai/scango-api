using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Ai;

namespace ScanGo.Api.Features.Metering;

public record QuotaStatus(
    bool Limited,
    int ScansUsed, int ScansLimit,
    int AsksUsed, int AsksLimit,
    string PeriodKey, DateTime ResetAtUtc);

public interface IQuotaService
{
    /// <summary>Free plan = limited; paid plans, admin and tester roles are not.</summary>
    bool IsLimited(string plan, string role);

    Task<QuotaStatus> GetStatusAsync(Guid userId, string plan, string role, CancellationToken ct);

    /// <summary>Increment the counter for <paramref name="kind"/> (scan/ask) for
    /// the current week. Returns false (no increment) if a limited user is at the cap.</summary>
    Task<bool> TryConsumeAsync(Guid userId, string plan, string role, string kind, CancellationToken ct);

    /// <summary>Queue a usage_events row (token log) on the context — caller saves.</summary>
    void AddUsageEvent(Guid userId, Guid? convId, string kind, AiTokenUsage usage, bool ocrCalled);

    /// <summary>Admin action: zero out the current week's counters for a user.</summary>
    Task ResetAsync(Guid userId, CancellationToken ct);
}

public class QuotaService(ScanGoDbContext db, RuntimeSettings settings) : IQuotaService
{
    // Every PLAN is metered now (each has its own weekly allowance — see
    // PlanQuotas). Only the admin/tester ROLES bypass metering entirely.
    public bool IsLimited(string plan, string role)
    {
        _ = plan;
        return role != UserRoles.Admin && role != UserRoles.Tester;
    }

    // Quota resets on a rolling 7-day cycle anchored at the user's signup date,
    // so every user gets a full 7 days from when they joined / activated — not a
    // shared calendar week. Falls back to "now" if the user can't be found.
    private async Task<DateTime> AnchorAsync(Guid userId, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (DateTime?)u.CreatedAt)
            .FirstOrDefaultAsync(ct) ?? DateTime.UtcNow;

    public async Task<QuotaStatus> GetStatusAsync(
        Guid userId, string plan, string role, CancellationToken ct)
    {
        var anchor = await AnchorAsync(userId, ct);
        var (key, _, endUtc) = QuotaPeriod.CurrentRolling(DateTime.UtcNow, anchor);
        var s = await db.UsageSummaries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PeriodKey == key, ct);
        var cfg = settings.Current;
        var quota = PlanQuotas.Resolve(plan, cfg.FreeWeeklyScans, cfg.FreeWeeklyAsks);
        return new QuotaStatus(
            Limited: IsLimited(plan, role),
            ScansUsed: s?.ScanCount ?? 0, ScansLimit: quota.WeeklyScans,
            AsksUsed: s?.AskCount ?? 0, AsksLimit: quota.WeeklyAsks,
            PeriodKey: key, ResetAtUtc: endUtc);
    }

    public async Task<bool> TryConsumeAsync(
        Guid userId, string plan, string role, string kind, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var anchor = await AnchorAsync(userId, ct);
        var (key, start, end) = QuotaPeriod.CurrentRolling(now, anchor);
        var s = await db.UsageSummaries
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PeriodKey == key, ct);
        if (s is null)
        {
            s = new UsageSummary
            {
                UserId = userId,
                PeriodKey = key,
                PeriodKind = PeriodKinds.Rolling7,
                PeriodStart = start,
                PeriodEnd = end,
                UpdatedAt = now,
            };
            db.UsageSummaries.Add(s);
        }

        if (IsLimited(plan, role))
        {
            var cfg = settings.Current;
            var quota = PlanQuotas.Resolve(plan, cfg.FreeWeeklyScans, cfg.FreeWeeklyAsks);
            if (kind == UsageKinds.Scan && s.ScanCount >= quota.WeeklyScans) return false;
            if (kind == UsageKinds.Ask && s.AskCount >= quota.WeeklyAsks) return false;
        }

        if (kind == UsageKinds.Scan) s.ScanCount++;
        else s.AskCount++;
        s.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public void AddUsageEvent(
        Guid userId, Guid? convId, string kind, AiTokenUsage usage, bool ocrCalled)
    {
        var (key, _, _) = QuotaPeriod.CurrentWeek(DateTime.UtcNow);
        // Mock AI / OCR have no real cost — log the call but with 0 tokens so they
        // don't inflate the Gemini cost estimate (the mock reports fake token counts).
        var cfg = settings.Current;
        var input = cfg.AiMock ? 0 : usage.InputTokens;
        var output = cfg.AiMock ? 0 : usage.OutputTokens;
        var credits = Math.Max(1, (int)Math.Ceiling((input + output) / 1000.0));
        db.UsageEvents.Add(new UsageEvent
        {
            UserId = userId,
            ConversationId = convId,
            Kind = kind,
            InputTokens = input,
            OutputTokens = output,
            Credits = credits,
            OcrCalled = ocrCalled && !cfg.OcrMock,
            PeriodKey = key,
        });
    }

    public async Task ResetAsync(Guid userId, CancellationToken ct)
    {
        var (key, _, _) = QuotaPeriod.CurrentWeek(DateTime.UtcNow);
        await db.UsageSummaries
            .Where(x => x.UserId == userId && x.PeriodKey == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ScanCount, 0)
                .SetProperty(x => x.AskCount, 0)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
    }
}
