using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ScanGo.Api.Features.Auth;

namespace ScanGo.Api.Tests.Features.Auth;

public class RateLimitTests(AuthApiFixture fx)
    : IClassFixture<AuthApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fx.ResetDbAsync();

    /// <summary>
    /// Builds a sibling factory that re-enables rate-limiting (the shared
    /// fixture disables it via DisableRateLimit=true). We override that key.
    /// </summary>
    private WebApplicationFactory<Program> WithRateLimitEnabled() =>
        fx.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DisableRateLimit"] = "false",
                });
            }));

    [Fact]
    public async Task Login_AuthStrictLimit_Returns429AfterBudget()
    {
        using var f = WithRateLimitEnabled();
        var http = f.CreateClient();

        // auth-strict = 5 / 15 min. The 6th request from the same IP -> 429.
        // All 6 requests use wrong creds so they don't depend on user existence.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var r = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = $"limit{i}@example.com",
                Password = "WhateverPw1",
            });
            statuses.Add(r.StatusCode);
        }

        statuses.Take(5).Should().AllSatisfy(s =>
            s.Should().Be(HttpStatusCode.Unauthorized,
                "first 5 attempts pass rate limit then fail auth"));
        statuses[5].Should().Be((HttpStatusCode)429, "6th attempt blocked by rate limiter");
    }
}
