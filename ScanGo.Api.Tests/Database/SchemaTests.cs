using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Tests.Database;

/// <summary>
/// Schema smoke tests — these verify the EF model produces the SQL we expect
/// (constraints, defaults, indexes, FK behaviour). Catches schema drift early.
/// </summary>
public class SchemaTests(PostgresFixture pg) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => pg.ResetAsync();

    [Fact]
    public async Task User_Insert_Defaults_Apply()
    {
        await using var db = pg.CreateContext();
        var u = new User
        {
            Email = "alice@example.com",
            PasswordHash = "x",
            Name = "Alice",
            TermsAcceptedAt = DateTime.UtcNow,
            PrivacyAcceptedAt = DateTime.UtcNow,
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();

        u.Id.Should().NotBe(Guid.Empty);
        u.Role.Should().Be(UserRoles.User);
        u.Plan.Should().Be(PlanCodes.Free);
        u.Status.Should().Be(UserStatuses.Active);
        u.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task User_UniqueEmail_Enforced()
    {
        await using var db = pg.CreateContext();
        var common = new
        {
            Name = "x",
            Hash = "x",
            T = DateTime.UtcNow,
        };
        db.Users.Add(new User
        {
            Email = "dup@example.com",
            PasswordHash = common.Hash,
            Name = common.Name,
            TermsAcceptedAt = common.T,
            PrivacyAcceptedAt = common.T,
        });
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Email = "dup@example.com",
            PasswordHash = common.Hash,
            Name = common.Name,
            TermsAcceptedAt = common.T,
            PrivacyAcceptedAt = common.T,
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task User_RequiresAtLeastOneAuthMethod()
    {
        await using var db = pg.CreateContext();
        db.Users.Add(new User
        {
            Email = "noauth@example.com",
            PasswordHash = null,
            GoogleId = null,
            Name = "NoAuth",
            TermsAcceptedAt = DateTime.UtcNow,
            PrivacyAcceptedAt = DateTime.UtcNow,
        });

        var act = async () => await db.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be("23514"); // check constraint violation
    }

    [Fact]
    public async Task User_RoleCheck_Enforced()
    {
        await using var db = pg.CreateContext();
        db.Users.Add(new User
        {
            Email = "badrole@example.com",
            PasswordHash = "x",
            Name = "x",
            Role = "godmode",
            TermsAcceptedAt = DateTime.UtcNow,
            PrivacyAcceptedAt = DateTime.UtcNow,
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Conversation_CascadeDelete_OnUserDelete()
    {
        await using var db = pg.CreateContext();
        var user = new User
        {
            Email = "casc@example.com",
            PasswordHash = "x",
            Name = "x",
            TermsAcceptedAt = DateTime.UtcNow,
            PrivacyAcceptedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var conv = new Conversation
        {
            UserId = user.Id,
            Topic = ConversationTopics.General,
            RootLang = "auto",
            TargetLang = "vnm",
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        db.Messages.Add(new Message
        {
            ConversationId = conv.Id,
            Role = MessageRoles.User,
            Content = "hi",
        });
        await db.SaveChangesAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        (await db.Conversations.CountAsync()).Should().Be(0);
        (await db.Messages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UsageSummary_UniqueUserPeriod_Enforced()
    {
        await using var db = pg.CreateContext();
        var user = new User
        {
            Email = "u@example.com",
            PasswordHash = "x",
            Name = "U",
            TermsAcceptedAt = DateTime.UtcNow,
            PrivacyAcceptedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UsageSummaries.Add(new UsageSummary
        {
            UserId = user.Id,
            PeriodKey = "2026-05",
            PeriodKind = PeriodKinds.Monthly,
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync();

        db.UsageSummaries.Add(new UsageSummary
        {
            UserId = user.Id,
            PeriodKey = "2026-05",
            PeriodKind = PeriodKinds.Monthly,
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddDays(30),
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletionRequest_OnePerUserPending_Enforced()
    {
        await using var db = pg.CreateContext();
        var user = new User
        {
            Email = "delreq@example.com",
            PasswordHash = "x",
            Name = "U",
            TermsAcceptedAt = DateTime.UtcNow,
            PrivacyAcceptedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.DeletionRequests.Add(new DeletionRequest
        {
            UserId = user.Id,
            ScheduledFor = DateTime.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync();

        db.DeletionRequests.Add(new DeletionRequest
        {
            UserId = user.Id,
            ScheduledFor = DateTime.UtcNow.AddDays(30),
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task PaymentOrder_UniqueOrderCode_Enforced()
    {
        await using var db = pg.CreateContext();
        var user = new User
        {
            Email = "pay@example.com",
            PasswordHash = "x",
            Name = "U",
            TermsAcceptedAt = DateTime.UtcNow,
            PrivacyAcceptedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.PaymentOrders.Add(new PaymentOrder
        {
            UserId = user.Id,
            OrderCode = "SCANABCD",
            Plan = PlanCodes.BasicMonthly,
            AmountVnd = 49_000,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        });
        await db.SaveChangesAsync();

        db.PaymentOrders.Add(new PaymentOrder
        {
            UserId = user.Id,
            OrderCode = "SCANABCD",
            Plan = PlanCodes.BasicMonthly,
            AmountVnd = 49_000,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
