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
