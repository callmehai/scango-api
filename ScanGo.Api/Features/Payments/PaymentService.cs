using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Billing;

namespace ScanGo.Api.Features.Payments;

public interface IPaymentService
{
    Task<(OrderView? view, CreateOrderError? error)> CreateOrderAsync(
        Guid userId, string plan, CancellationToken ct);
    Task<OrderView?> GetForUserAsync(Guid userId, Guid orderId, CancellationToken ct);
    Task<List<OrderView>> ListForUserAsync(Guid userId, CancellationToken ct);
    Task<bool> ApproveAsync(Guid orderId, Guid adminId, string? bankRef, CancellationToken ct);
    Task<bool> RejectAsync(Guid orderId, Guid adminId, CancellationToken ct);
    Task<bool> RefundAsync(Guid orderId, Guid adminId, string? note, CancellationToken ct);
    Task<WebhookResult> ProcessSePayAsync(SePayWebhook payload, CancellationToken ct);
}

public partial class PaymentService(
    ScanGoDbContext db,
    IOptions<PaymentOptions> options,
    IAuditLogger audit) : IPaymentService
{
    private readonly PaymentOptions _o = options.Value;

    public async Task<(OrderView?, CreateOrderError?)> CreateOrderAsync(
        Guid userId, string plan, CancellationToken ct)
    {
        var info = Plans.Find(plan);
        if (info is null || info.PriceVnd <= 0)
            return (null, CreateOrderError.InvalidPlan);
        if (!_o.IsConfigured)
            return (null, CreateOrderError.NotConfigured);

        var now = DateTime.UtcNow;

        // Reuse an existing still-payable pending order for the same plan so a user
        // refreshing the checkout doesn't pile up dead orders.
        var existing = await db.PaymentOrders
            .Where(p => p.UserId == userId && p.Plan == plan
                        && p.Status == PaymentOrderStatuses.Pending && p.ExpiresAt > now)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return (ToView(existing), null);

        var order = new PaymentOrder
        {
            OrderCode = await UniqueCodeAsync(ct),
            UserId = userId,
            Plan = info.Code,
            AmountVnd = info.PriceVnd,
            Status = PaymentOrderStatuses.Pending,
            ExpiresAt = now.AddMinutes(_o.OrderTtlMinutes),
        };
        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(AuditActions.PaymentCreated, userId, userId,
            new { order.OrderCode, order.Plan, order.AmountVnd }, ct);

        return (ToView(order), null);
    }

    public async Task<OrderView?> GetForUserAsync(Guid userId, Guid orderId, CancellationToken ct)
    {
        var order = await db.PaymentOrders.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == orderId && p.UserId == userId, ct);
        return order is null ? null : ToView(order);
    }

    public async Task<List<OrderView>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        var orders = await db.PaymentOrders.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        return orders.Select(ToView).ToList();
    }

    public async Task<bool> ApproveAsync(Guid orderId, Guid adminId, string? bankRef, CancellationToken ct)
    {
        var order = await db.PaymentOrders.FirstOrDefaultAsync(p => p.Id == orderId, ct);
        if (order is null || order.Status == PaymentOrderStatuses.Paid) return false;

        await MarkPaidAsync(order, bankRef, adminId, ct);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(AuditActions.PaymentApproved, adminId, order.UserId,
            new { order.OrderCode, order.Plan, manual = true }, ct);
        return true;
    }

    public async Task<bool> RejectAsync(Guid orderId, Guid adminId, CancellationToken ct)
    {
        var order = await db.PaymentOrders.FirstOrDefaultAsync(p => p.Id == orderId, ct);
        if (order is null || order.Status == PaymentOrderStatuses.Paid) return false;

        order.Status = PaymentOrderStatuses.Cancelled;
        order.ApprovedByUserId = adminId;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(AuditActions.PaymentRejected, adminId, order.UserId,
            new { order.OrderCode }, ct);
        return true;
    }

    public async Task<bool> RefundAsync(Guid orderId, Guid adminId, string? note, CancellationToken ct)
    {
        var order = await db.PaymentOrders.FirstOrDefaultAsync(p => p.Id == orderId, ct);
        if (order is null || order.Status != PaymentOrderStatuses.Paid) return false;

        // Money-only: the plan is intentionally left untouched (refunds go to
        // friends who keep their premium). We just record that a refund happened.
        order.Status = PaymentOrderStatuses.Refunded;
        order.RefundedAt = DateTime.UtcNow;
        order.Note = note;
        order.ApprovedByUserId = adminId;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(AuditActions.PaymentRefunded, adminId, order.UserId,
            new { order.OrderCode, note }, ct);
        return true;
    }

    public async Task<WebhookResult> ProcessSePayAsync(SePayWebhook payload, CancellationToken ct)
    {
        // Only money coming IN matters.
        if (!string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase))
            return WebhookResult.Ignored;

        // Idempotency: SePay retries on non-2xx, so guard against a double-grant.
        if (!string.IsNullOrWhiteSpace(payload.ReferenceCode)
            && await db.PaymentOrders.AnyAsync(
                p => p.BankRef == payload.ReferenceCode && p.Status == PaymentOrderStatuses.Paid, ct))
            return WebhookResult.Duplicate;

        var code = ExtractCode(payload.Content);
        if (code is not null)
        {
            var order = await db.PaymentOrders.FirstOrDefaultAsync(
                p => p.OrderCode == code && p.Status == PaymentOrderStatuses.Pending, ct);
            // Accept exact-or-overpay; an underpayment falls through to "unmatched".
            if (order is not null && payload.TransferAmount >= order.AmountVnd)
            {
                await MarkPaidAsync(order, payload.ReferenceCode, adminId: null, ct);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync(AuditActions.PaymentApproved, null, order.UserId,
                    new { order.OrderCode, order.Plan, viaWebhook = true, payload.ReferenceCode }, ct);
                return WebhookResult.Matched;
            }
        }

        // No/typo'd memo or wrong amount → log for admin review, don't fail the call.
        await audit.LogAsync(AuditActions.PaymentUnmatched, null, null, new
        {
            payload.ReferenceCode,
            payload.TransferAmount,
            content = payload.Content,
            parsedCode = code,
        }, ct);
        return WebhookResult.Unmatched;
    }

    // ---- helpers ------------------------------------------------------------

    private async Task MarkPaidAsync(PaymentOrder order, string? bankRef, Guid? adminId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        order.Status = PaymentOrderStatuses.Paid;
        order.PaidAt = now;
        order.BankRef = bankRef;
        order.ApprovedByUserId = adminId;
        order.UpdatedAt = now;

        // Grant the plan (same effect as the admin "change plan" control): quota
        // reads the plan from the JWT, so it takes effect on the next token refresh.
        var info = Plans.Find(order.Plan);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == order.UserId, ct);
        if (info is not null && user is not null)
        {
            user.Plan = info.Code;
            user.PlanExpiresAt = info.DurationDays is int d ? now.AddDays(d) : null;
            user.UpdatedAt = now;
        }
    }

    private async Task<string> UniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = OrderCode.New();
            if (!await db.PaymentOrders.AnyAsync(p => p.OrderCode == code, ct))
                return code;
        }
        return OrderCode.New(); // astronomically unlikely to reach here
    }

    private static string? ExtractCode(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var m = CodeRegex().Match(content.ToUpperInvariant());
        return m.Success ? m.Value : null;
    }

    private OrderView ToView(PaymentOrder o) => new(
        o.Id, o.OrderCode, o.Plan, o.AmountVnd, o.Status, o.ExpiresAt, o.CreatedAt, o.PaidAt,
        TransferContent: o.OrderCode,
        QrImageUrl: VietQr.ImageUrl(_o, o.AmountVnd, o.OrderCode),
        BankName: _o.BankName, AccountNo: _o.AccountNo, AccountHolder: _o.AccountHolder);

    [GeneratedRegex(OrderCode.Pattern)]
    private static partial Regex CodeRegex();
}
