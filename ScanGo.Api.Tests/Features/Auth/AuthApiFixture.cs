using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScanGo.Api.Database;
using ScanGo.Api.Tests.Database;

namespace ScanGo.Api.Tests.Features.Auth;

/// <summary>
/// Hosts the real ASP.NET pipeline in-process, pointed at a real Postgres
/// (Testcontainers). Used by Auth integration tests.
/// </summary>
public class AuthApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    public string ConnectionString => _pg.ConnectionString;

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _pg.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _pg.ConnectionString,
                ["Auth:JwtSecret"] = "integration-test-secret-32-chars-or-more-please-ok",
                ["Auth:JwtIssuer"] = "scango-test",
                ["Auth:JwtAudience"] = "scango",
                ["Auth:AccessTokenMinutes"] = "15",
                ["Auth:RefreshTokenDays"] = "30",
                ["DbAutoMigrate"] = "true",
                ["DisableRateLimit"] = "true",
                // Force mock AI/OCR so the suite is hermetic — never calls Gemini /
                // OCR.Space (no quota burn, no 429 flakiness, runs offline/CI). The SSE
                // tests assert against MockGeminiService's canned output, so this is also
                // required for them to be deterministic. Overrides appsettings.Development.json.
                ["Ai:Mock"] = "true",
                ["Ocr:Mock"] = "true",
                // Effectively unlimited free quota in tests so flows that create
                // many conversations aren't blocked by the 429 quota gate.
                ["Quota:FreeWeeklyScans"] = "100000",
                ["Quota:FreeWeeklyAsks"] = "100000",
            });
        });
    }

    public ScanGoDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ScanGoDbContext>();
    }

    public async Task ResetDbAsync() => await _pg.ResetAsync();
}
