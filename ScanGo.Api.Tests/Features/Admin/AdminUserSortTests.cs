using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Tests.Database;

namespace ScanGo.Api.Tests.Features.Admin;

/// <summary>
/// Guards the admin user-list sorting. The risky bit is sorting by
/// <c>convos</c> / <c>tokens</c>, which order by a correlated subquery over the
/// related tables — these tests prove EF can translate those ORDER BY shapes to
/// real Postgres SQL (a compile pass alone wouldn't catch a translation failure)
/// and that every sort direction comes back in the expected order.
/// </summary>
public class AdminUserSortTests(PostgresFixture pg) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => pg.ResetAsync();

    private static User NewUser(string email) => new()
    {
        Email = email,
        PasswordHash = "x",
        Name = email,
        TermsAcceptedAt = DateTime.UtcNow,
        PrivacyAcceptedAt = DateTime.UtcNow,
    };

    /// Seeds three users with distinct sign-up dates, conversation counts and
    /// token totals so every sort key produces an unambiguous order.
    private async Task<(Guid a, Guid b, Guid c)> SeedAsync()
    {
        await using var db = pg.CreateContext();
        var now = DateTime.UtcNow;

        var a = NewUser("a@example.com"); a.CreatedAt = now.AddDays(-3); // oldest
        var b = NewUser("b@example.com"); b.CreatedAt = now.AddDays(-2);
        var c = NewUser("c@example.com"); c.CreatedAt = now.AddDays(-1); // newest
        db.Users.AddRange(a, b, c);
        await db.SaveChangesAsync();

        // Conversations: A=2, B=0, C=1
        db.Conversations.AddRange(
            Conv(a.Id), Conv(a.Id), Conv(c.Id));
        // Tokens: A=100, B=500, C=50
        db.UsageEvents.AddRange(
            Usage(a.Id, 60, 40), Usage(b.Id, 200, 300), Usage(c.Id, 20, 30));
        await db.SaveChangesAsync();

        return (a.Id, b.Id, c.Id);

        static Conversation Conv(Guid uid) => new()
        {
            UserId = uid,
            Topic = ConversationTopics.General,
            RootLang = "auto",
            TargetLang = "vnm",
        };
        static UsageEvent Usage(Guid uid, int input, int output) => new()
        {
            UserId = uid,
            Kind = UsageKinds.Scan,
            InputTokens = input,
            OutputTokens = output,
            Credits = 1, // ck_usage_events_credits_positive
            PeriodKey = "2026-W24",
            CreatedAt = DateTime.UtcNow,
        };
    }

    // Mirrors AdminUsersController's sort switch so the test exercises the exact
    // ORDER BY expressions the endpoint builds.
    private async Task<List<Guid>> SortedIdsAsync(string sort, bool desc)
    {
        await using var db = pg.CreateContext();
        var query = db.Users.AsNoTracking().AsQueryable();
        IOrderedQueryable<User> ordered = sort switch
        {
            "convos" => Order(query, u => db.Conversations.Count(c => c.UserId == u.Id), desc),
            "tokens" => Order(query, u => db.UsageEvents
                .Where(e => e.UserId == u.Id)
                .Sum(e => (long)e.InputTokens + e.OutputTokens), desc),
            _ => Order(query, u => u.CreatedAt, desc),
        };
        var rows = await ordered.ToListAsync();
        return rows.Select(u => u.Id).ToList();

        static IOrderedQueryable<User> Order<TKey>(
            IQueryable<User> q, System.Linq.Expressions.Expression<Func<User, TKey>> key, bool d) =>
            d ? q.OrderByDescending(key).ThenByDescending(u => u.Id)
              : q.OrderBy(key).ThenBy(u => u.Id);
    }

    [Fact]
    public async Task SortByCreated_BothDirections()
    {
        var (a, _, c) = await SeedAsync();

        (await SortedIdsAsync("created", desc: true)).First().Should().Be(c);  // newest first
        (await SortedIdsAsync("created", desc: false)).First().Should().Be(a); // oldest first
    }

    [Fact]
    public async Task SortByConvos_TranslatesAndOrders()
    {
        var (a, b, _) = await SeedAsync();

        (await SortedIdsAsync("convos", desc: true)).First().Should().Be(a);  // A has 2
        (await SortedIdsAsync("convos", desc: false)).First().Should().Be(b); // B has 0
    }

    [Fact]
    public async Task SortByTokens_TranslatesAndOrders()
    {
        var (_, b, c) = await SeedAsync();

        (await SortedIdsAsync("tokens", desc: true)).First().Should().Be(b);  // B has 500
        (await SortedIdsAsync("tokens", desc: false)).First().Should().Be(c); // C has 50
    }
}
