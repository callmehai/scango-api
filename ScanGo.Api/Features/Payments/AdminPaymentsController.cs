using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Features.Payments;

/// <summary>Admin payment log + manual actions (review unmatched, refund).</summary>
[ApiController]
[Route("api/admin/payments")]
[Authorize(Roles = UserRoles.Admin)]
public class AdminPaymentsController(ScanGoDbContext db, IPaymentService payments) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        skip = Math.Max(0, skip);
        limit = Math.Clamp(limit, 1, 100);

        // Lazily flip overdue pending orders to "expired" so the list shows the
        // real state (and stops offering approve/reject on dead orders). A late
        // bank transfer can still revive one — the SePay webhook matches expired
        // orders too.
        await db.PaymentOrders
            .Where(p => p.Status == PaymentOrderStatuses.Pending && p.ExpiresAt < DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentOrderStatuses.Expired)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);

        var query = db.PaymentOrders.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && PaymentOrderStatuses.All.Contains(status))
            query = query.Where(p => p.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip(skip).Take(limit)
            .Select(p => new
            {
                id = p.Id,
                orderCode = p.OrderCode,
                userId = p.UserId,
                userEmail = p.User.Email,
                userName = p.User.Name,
                plan = p.Plan,
                amountVnd = p.AmountVnd,
                status = p.Status,
                bankRef = p.BankRef,
                note = p.Note,
                createdAt = p.CreatedAt,
                paidAt = p.PaidAt,
                refundedAt = p.RefundedAt,
                expiresAt = p.ExpiresAt,
            })
            .ToListAsync(ct);

        return Ok(new { items, total, skip, limit });
    }

    [HttpGet("revenue")]
    public async Task<IActionResult> Revenue(
        [FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 180);
        var tz = TimeSpan.FromHours(7); // VN = UTC+7, no DST
        var now = DateTime.UtcNow;
        var sinceUtc = now.AddDays(-days);

        // Net revenue = paid orders only (refunds drop to status 'refunded' and are
        // excluded). Volume is small, so bucket by VN-local day in memory.
        var paid = await db.PaymentOrders.AsNoTracking()
            .Where(p => p.Status == PaymentOrderStatuses.Paid
                        && p.PaidAt != null && p.PaidAt >= sinceUtc)
            .Select(p => new { p.PaidAt, p.AmountVnd })
            .ToListAsync(ct);

        var byDay = paid
            .GroupBy(p => DateOnly.FromDateTime(p.PaidAt!.Value.Add(tz)))
            .ToDictionary(
                g => g.Key,
                g => new { Revenue = g.Sum(x => x.AmountVnd), Count = g.Count() });

        var todayVn = DateOnly.FromDateTime(now.Add(tz));
        var series = Enumerable.Range(0, days)
            .Select(i => todayVn.AddDays(-(days - 1) + i))
            .Select(d =>
            {
                byDay.TryGetValue(d, out var v);
                return new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    revenueVnd = v?.Revenue ?? 0L,
                    orderCount = v?.Count ?? 0,
                };
            })
            .ToList();

        var refundedTotal = await db.PaymentOrders
            .Where(p => p.Status == PaymentOrderStatuses.Refunded)
            .SumAsync(p => p.AmountVnd, ct);

        return Ok(new
        {
            days,
            totalRevenue = series.Sum(s => s.revenueVnd),
            totalOrders = series.Sum(s => s.orderCount),
            refundedTotal,
            series,
        });
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        await payments.ApproveAsync(id, User.RequireUserId(), bankRef: null, ct)
            ? Ok(new { ok = true })
            : BadRequest(new { code = "CannotApprove", message = "Đơn không tồn tại hoặc đã thanh toán." });

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct) =>
        await payments.RejectAsync(id, User.RequireUserId(), ct)
            ? Ok(new { ok = true })
            : BadRequest(new { code = "CannotReject", message = "Đơn không tồn tại hoặc đã thanh toán." });

    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id, [FromBody] RefundRequest req, CancellationToken ct) =>
        await payments.RefundAsync(id, User.RequireUserId(), req.Note, ct)
            ? Ok(new { ok = true })
            : BadRequest(new { code = "CannotRefund", message = "Chỉ hoàn được đơn đã thanh toán." });
}
