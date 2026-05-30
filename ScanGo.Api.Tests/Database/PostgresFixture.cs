using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Database;
using Testcontainers.PostgreSql;

namespace ScanGo.Api.Tests.Database;

/// <summary>
/// Shared xUnit fixture: spins a real Postgres container once per test class
/// (or per collection if used with ICollectionFixture) and applies EF
/// migrations. Each test should clean its own data — see ResetAsync helper.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("scango_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public ScanGoDbContext CreateContext()
    {
        var opts = new DbContextOptionsBuilder<ScanGoDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .EnableSensitiveDataLogging()
            .Options;
        return new ScanGoDbContext(opts);
    }

    /// <summary>
    /// Truncate every user-table so the next test starts clean. Cheaper than
    /// re-running migrations between tests.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                deletion_requests, audit_log, payment_orders, credit_ledger,
                usage_summaries, usage_events, messages, conversations,
                password_resets, email_verifications, refresh_tokens, users
            RESTART IDENTITY CASCADE;
            """);
    }
}
