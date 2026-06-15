using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Billing;
using ScanGo.Api.Features.Metering;

namespace ScanGo.Api.Features.Admin;

[ApiController]
[Route("api/admin")]
// Admin-only. Testers previously had a read-only view of users/metrics/plans,
// but that leaked too much user data as the tester pool grew, so the entire
// admin surface is now restricted to admins. (Testers keep their quota bypass —
// that lives in QuotaService and is unrelated to admin-panel access.)
[Authorize(Roles = UserRoles.Admin)]
public class AdminUsersController(
    ScanGoDbContext db,
    RuntimeSettings settings,
    IQuotaService quota,
    IRefreshTokenService refreshTokens) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? q = null,
        [FromQuery] string sort = "created",
        [FromQuery] string order = "desc",
        CancellationToken ct = default)
    {
        skip = Math.Max(0, skip);
        limit = Math.Clamp(limit, 1, 100);
        var desc = !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase);

        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(needle) || u.Name.ToLower().Contains(needle));
        }

        var total = await query.CountAsync(ct);

        // Sort server-side (with a stable Id tiebreaker) so paging stays correct
        // past the first page and "newest registered" is a first-class option.
        // `convos`/`tokens` order by a correlated subquery over the related tables.
        var ordered = sort switch
        {
            "email" => SortBy(query, u => u.Email, desc),
            "name" => SortBy(query, u => u.Name, desc),
            "plan" => SortBy(query, u => u.Plan, desc),
            "status" => SortBy(query, u => u.Status, desc),
            "role" => SortBy(query, u => u.Role, desc),
            "lastLogin" => SortBy(query, u => u.LastLoginAt, desc),
            "convos" => SortBy(query, u => db.Conversations.Count(c => c.UserId == u.Id), desc),
            "tokens" => SortBy(query, u => db.UsageEvents
                .Where(e => e.UserId == u.Id)
                .Sum(e => (long)e.InputTokens + e.OutputTokens), desc),
            _ => SortBy(query, u => u.CreatedAt, desc), // "created" (default)
        };

        var users = await ordered
            .Skip(skip).Take(limit)
            .ToListAsync(ct);

        var ids = users.Select(u => u.Id).ToList();
        var now = DateTime.UtcNow;

        var convCounts = await db.Conversations.AsNoTracking()
            .Where(c => ids.Contains(c.UserId))
            .GroupBy(c => c.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Quota is a per-user rolling 7-day window (anchored at signup), so each
        // user has their own period key. The currently-open period is the one
        // whose window still contains `now` — grab those in one query.
        var summaries = await db.UsageSummaries.AsNoTracking()
            .Where(s => ids.Contains(s.UserId)
                        && s.PeriodStart <= now && s.PeriodEnd > now)
            .ToDictionaryAsync(s => s.UserId, ct);

        var tokenSums = await db.UsageEvents.AsNoTracking()
            .Where(e => ids.Contains(e.UserId))
            .GroupBy(e => e.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Total = g.Sum(e => (long)e.InputTokens + e.OutputTokens),
            })
            .ToDictionaryAsync(x => x.UserId, x => x.Total, ct);

        var cfg = settings.Current;
        var items = users.Select(u =>
        {
            summaries.TryGetValue(u.Id, out var s);
            return new
            {
                id = u.Id,
                email = u.Email,
                name = u.Name,
                role = u.Role,
                plan = u.Plan,
                status = u.Status,
                emailVerified = u.EmailVerifiedAt != null,
                isPaid = u.Plan != PlanCodes.Free,
                createdAt = u.CreatedAt,
                lastLoginAt = u.LastLoginAt,
                conversationCount = convCounts.GetValueOrDefault(u.Id, 0),
                limited = quota.IsLimited(u.Plan, u.Role),
                scansUsed = s?.ScanCount ?? 0,
                scansLimit = PlanQuotas.Resolve(u.Plan, cfg.FreeWeeklyScans, cfg.FreeWeeklyAsks).WeeklyScans,
                asksUsed = s?.AskCount ?? 0,
                asksLimit = PlanQuotas.Resolve(u.Plan, cfg.FreeWeeklyScans, cfg.FreeWeeklyAsks).WeeklyAsks,
                totalTokens = tokenSums.GetValueOrDefault(u.Id, 0L),
            };
        });

        return Ok(new { items, total, skip, limit });
    }

    // Applies the chosen sort direction plus a stable Id tiebreaker so that rows
    // with equal sort keys keep a deterministic order across pages.
    private static IOrderedQueryable<User> SortBy<TKey>(
        IQueryable<User> query, Expression<Func<User, TKey>> key, bool desc) =>
        desc
            ? query.OrderByDescending(key).ThenByDescending(u => u.Id)
            : query.OrderBy(key).ThenBy(u => u.Id);

    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(CancellationToken ct)
    {
        var totalUsers = await db.Users.CountAsync(ct);
        var paidUsers = await db.Users.CountAsync(u => u.Plan != PlanCodes.Free, ct);
        var totalConversations = await db.Conversations.CountAsync(ct);

        var agg = await db.UsageEvents
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Input = g.Sum(e => (long)e.InputTokens),
                Output = g.Sum(e => (long)e.OutputTokens),
                Events = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        var input = agg?.Input ?? 0;
        var output = agg?.Output ?? 0;
        // Rough estimate using gemini-2.5-flash-lite rates (~$0.10/1M in, $0.40/1M out).
        var estCostUsd = Math.Round(input / 1_000_000.0 * 0.10 + output / 1_000_000.0 * 0.40, 4);

        return Ok(new
        {
            totalUsers,
            paidUsers,
            freeUsers = totalUsers - paidUsers,
            totalConversations,
            aiCalls = agg?.Events ?? 0,
            totalInputTokens = input,
            totalOutputTokens = output,
            estimatedGeminiCostUsd = estCostUsd,
            costNote = "Ước tính theo giá gemini-2.5-flash-lite (~$0.10/1M in, $0.40/1M out) — chỉ tham khảo.",
        });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("users/{id:guid}/reset-quota")]
    public async Task<IActionResult> ResetQuota(Guid id, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, ct)) return NotFound();
        await quota.ResetAsync(id, ct);
        return Ok(new { ok = true });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("users/{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken ct)
    {
        if (id == User.RequireUserId())
            return BadRequest(new { code = "CannotSuspendSelf", message = "Không thể tự khoá chính mình." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.Status = UserStatuses.Suspended;
        user.UpdatedAt = DateTime.UtcNow;
        await refreshTokens.RevokeAllForUserAsync(id, ct);   // force logout everywhere
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true, status = user.Status });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("users/{id:guid}/unsuspend")]
    public async Task<IActionResult> Unsuspend(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (user.DeletedAt is not null)
            return BadRequest(new { code = "AccountDeleted", message = "Tài khoản đã bị xoá." });

        user.Status = UserStatuses.Active;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true, status = user.Status });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (id == User.RequireUserId())
            return BadRequest(new { code = "CannotDeleteSelf", message = "Không thể tự xoá chính mình." });

        // Hard delete; DB FK cascade drops conversations/messages/usage/tokens.
        // (R2 image objects for that user are not pruned here — minor storage leak.)
        var rows = await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0 ? Ok(new { ok = true }) : NotFound();
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPatch("users/{id:guid}/role")]
    public async Task<IActionResult> ChangeRole(
        Guid id, [FromBody] ChangeRoleRequest req, CancellationToken ct)
    {
        if (!UserRoles.All.Contains(req.Role))
            return BadRequest(new { code = "InvalidRole", message = "Vai trò không hợp lệ." });
        if (id == User.RequireUserId())
            return BadRequest(new { code = "CannotChangeSelfRole", message = "Không thể tự đổi vai trò của chính mình." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.Role = req.Role;
        user.UpdatedAt = DateTime.UtcNow;
        // Role is baked into the access JWT — revoke refresh tokens so the new role
        // takes effect (forces re-login; the current short-lived access token keeps
        // the old role only until it expires).
        await refreshTokens.RevokeAllForUserAsync(id, ct);
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true, role = user.Role });
    }

    [HttpGet("plans")]
    public IActionResult GetPlans() =>
        Ok(Plans.All.Select(p => new
        {
            code = p.Code,
            name = p.Name,
            priceVnd = p.PriceVnd,
            durationDays = p.DurationDays,
        }));

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPatch("users/{id:guid}/plan")]
    public async Task<IActionResult> ChangePlan(
        Guid id, [FromBody] ChangePlanRequest req, CancellationToken ct)
    {
        var plan = Plans.Find(req.Plan);
        if (plan is null)
            return BadRequest(new { code = "InvalidPlan", message = "Gói không hợp lệ." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.Plan = plan.Code;
        user.PlanExpiresAt = plan.DurationDays is int d ? DateTime.UtcNow.AddDays(d) : null;
        user.UpdatedAt = DateTime.UtcNow;
        // Plan is read from the JWT for quota; it takes effect on the user's next
        // token refresh (≤ access-token lifetime) — no forced logout, smoother for
        // an upgrade. The future payment webhook can call this same logic.
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true, plan = user.Plan, planExpiresAt = user.PlanExpiresAt });
    }
}

public class ChangeRoleRequest
{
    public string Role { get; set; } = "";
}

public class ChangePlanRequest
{
    public string Plan { get; set; } = "";
}
