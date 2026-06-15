using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScanGo.Api.Common;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Payments;
using ScanGo.Api.Tests.Database;

namespace ScanGo.Api.Tests.Features.Payments;

/// <summary>
/// End-to-end (real Postgres) tests for the payment flow: order creation, the
/// SePay webhook auto-grant + its guards (wrong amount, idempotency), and that a
/// refund records the money-back WITHOUT revoking the plan.
/// </summary>
public class PaymentFlowTests(PostgresFixture pg) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => pg.ResetAsync();

    private static readonly PaymentOptions Opts = new()
    {
        BankBin = "970418",
        BankName = "BIDV",
        AccountNo = "12345678",
        AccountHolder = "NGUYEN VAN A",
        OrderTtlMinutes = 60,
        SePayApiKey = "testkey",
    };

    private PaymentService NewSvc() =>
        new(pg.CreateContext(), Options.Create(Opts), new NoopAudit());

    private async Task<Guid> SeedUserAsync(string email = "buyer@example.com", string role = UserRoles.User)
    {
        await using var db = pg.CreateContext();
        var u = new User
        {
            Email = email,
            PasswordHash = "x",
            Name = email,
            Role = role,
            TermsAcceptedAt = DateTime.UtcNow,
            PrivacyAcceptedAt = DateTime.UtcNow,
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    [Fact]
    public async Task CreateOrder_BuildsPayableOrderWithVietQr()
    {
        var userId = await SeedUserAsync();

        var (view, error) = await NewSvc().CreateOrderAsync(userId, PlanCodes.Lite, default);

        error.Should().BeNull();
        view.Should().NotBeNull();
        view!.AmountVnd.Should().Be(29_000);            // Lite price
        view.Status.Should().Be(PaymentOrderStatuses.Pending);
        view.TransferContent.Should().Be(view.OrderCode);
        view.OrderCode.Should().MatchRegex("^SCAN[0-9A-Z]{6}$");
        view.QrImageUrl.Should().Contain("970418-12345678")
            .And.Contain("amount=29000")
            .And.Contain(view.OrderCode);
    }

    [Fact]
    public async Task CreateOrder_RejectsFreePlan()
    {
        var userId = await SeedUserAsync();
        var (_, error) = await NewSvc().CreateOrderAsync(userId, PlanCodes.Free, default);
        error.Should().Be(CreateOrderError.InvalidPlan);
    }

    [Fact]
    public async Task Webhook_MatchingTransfer_GrantsPlan()
    {
        var userId = await SeedUserAsync();
        var (view, _) = await NewSvc().CreateOrderAsync(userId, PlanCodes.BasicMonthly, default);

        var result = await NewSvc().ProcessSePayAsync(new SePayWebhook
        {
            TransferType = "in",
            TransferAmount = 49_000,
            Content = $"thanh toan {view!.OrderCode}",
            ReferenceCode = "FT0001",
        }, default);

        result.Should().Be(WebhookResult.Matched);

        await using var db = pg.CreateContext();
        var order = await db.PaymentOrders.SingleAsync(p => p.Id == view.Id);
        order.Status.Should().Be(PaymentOrderStatuses.Paid);
        order.BankRef.Should().Be("FT0001");
        order.PaidAt.Should().NotBeNull();

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.Plan.Should().Be(PlanCodes.BasicMonthly);
        user.PlanExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Webhook_Duplicate_IsIgnored()
    {
        var userId = await SeedUserAsync();
        var (view, _) = await NewSvc().CreateOrderAsync(userId, PlanCodes.BasicMonthly, default);
        SePayWebhook Hook() => new()
        {
            TransferType = "in",
            TransferAmount = 49_000,
            Content = view!.OrderCode,
            ReferenceCode = "FT0002",
        };

        (await NewSvc().ProcessSePayAsync(Hook(), default)).Should().Be(WebhookResult.Matched);
        (await NewSvc().ProcessSePayAsync(Hook(), default)).Should().Be(WebhookResult.Duplicate);
    }

    [Fact]
    public async Task Webhook_Underpaid_IsUnmatched_NoGrant()
    {
        var userId = await SeedUserAsync();
        var (view, _) = await NewSvc().CreateOrderAsync(userId, PlanCodes.Lite, default);

        var result = await NewSvc().ProcessSePayAsync(new SePayWebhook
        {
            TransferType = "in",
            TransferAmount = 10_000,                       // less than 29.000
            Content = view!.OrderCode,
            ReferenceCode = "FT0003",
        }, default);

        result.Should().Be(WebhookResult.Unmatched);

        await using var db = pg.CreateContext();
        (await db.PaymentOrders.SingleAsync(p => p.Id == view.Id)).Status
            .Should().Be(PaymentOrderStatuses.Pending);
        (await db.Users.SingleAsync(u => u.Id == userId)).Plan.Should().Be(PlanCodes.Free);
    }

    [Fact]
    public async Task Refund_RecordsRefund_ButKeepsPlan()
    {
        var userId = await SeedUserAsync();
        var (view, _) = await NewSvc().CreateOrderAsync(userId, PlanCodes.BasicMonthly, default);
        await NewSvc().ProcessSePayAsync(new SePayWebhook
        {
            TransferType = "in",
            TransferAmount = 49_000,
            Content = view!.OrderCode,
            ReferenceCode = "FT0004",
        }, default);

        var adminId = await SeedUserAsync("admin@example.com", UserRoles.Admin);
        var ok = await NewSvc().RefundAsync(view.Id, adminId, "bạn — đã hoàn tay", default);
        ok.Should().BeTrue();

        await using var db = pg.CreateContext();
        var order = await db.PaymentOrders.SingleAsync(p => p.Id == view.Id);
        order.Status.Should().Be(PaymentOrderStatuses.Refunded);
        order.RefundedAt.Should().NotBeNull();
        order.Note.Should().Be("bạn — đã hoàn tay");

        // Plan stays — friends keep premium after the manual money refund.
        (await db.Users.SingleAsync(u => u.Id == userId)).Plan.Should().Be(PlanCodes.BasicMonthly);
    }

    private async Task SetPlanAsync(Guid userId, string plan, int daysLeft)
    {
        await using var db = pg.CreateContext();
        var u = await db.Users.SingleAsync(x => x.Id == userId);
        u.Plan = plan;
        u.PlanExpiresAt = DateTime.UtcNow.AddDays(daysLeft);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateOrder_BlocksDowngradeWhilePlanActive()
    {
        var userId = await SeedUserAsync();
        await SetPlanAsync(userId, PlanCodes.ProYearly, daysLeft: 300); // priciest plan

        // Cheaper plan while Pro (year) is active → blocked.
        var (_, err) = await NewSvc().CreateOrderAsync(userId, PlanCodes.Lite, default);
        err.Should().Be(CreateOrderError.Downgrade);

        // Re-buying the same plan (renewal) is allowed.
        var (renew, renewErr) =
            await NewSvc().CreateOrderAsync(userId, PlanCodes.ProYearly, default);
        renewErr.Should().BeNull();
        renew.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOrder_AllowsUpgradeWhilePlanActive()
    {
        var userId = await SeedUserAsync();
        await SetPlanAsync(userId, PlanCodes.BasicMonthly, daysLeft: 20);

        // Pro is pricier than Basic → upgrade allowed.
        var (view, err) =
            await NewSvc().CreateOrderAsync(userId, PlanCodes.ProMonthly, default);
        err.Should().BeNull();
        view.Should().NotBeNull();
    }

    private sealed class NoopAudit : IAuditLogger
    {
        public Task LogAsync(string action, Guid? actorUserId = null, Guid? targetUserId = null,
            object? meta = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
