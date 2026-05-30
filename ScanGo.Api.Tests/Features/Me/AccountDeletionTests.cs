using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Me;
using ScanGo.Api.Tests.Features.Auth;

namespace ScanGo.Api.Tests.Features.Me;

public class AccountDeletionTests(AuthApiFixture fx)
    : IClassFixture<AuthApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fx.ResetDbAsync();

    private async Task<(HttpClient http, AuthResponse body)> RegisterAsync(string email)
    {
        var http = fx.CreateClient();
        var reg = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Password = "Password1",
            Name = "T",
            TermsAccepted = true,
            PrivacyAccepted = true,
        });
        reg.EnsureSuccessStatusCode();
        var body = await reg.Content.ReadFromJsonAsync<AuthResponse>();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return (http, body);
    }

    [Fact]
    public async Task Delete_RequiresAuth()
    {
        var http = fx.CreateClient();
        var resp = await http.PostAsJsonAsync("/api/me/delete",
            new DeleteAccountRequest { Reason = "Test" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_SoftDeletesUserImmediately()
    {
        var (http, body) = await RegisterAsync("del1@example.com");
        (await http.PostAsJsonAsync("/api/me/delete",
            new DeleteAccountRequest { Reason = "Don't want it" }))
            .EnsureSuccessStatusCode();

        await using var db = fx.CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Id == body.User.Id);
        user.Status.Should().Be(UserStatuses.Deleted);
        user.DeletedAt.Should().NotBeNull();

        var req = await db.DeletionRequests
            .FirstAsync(d => d.UserId == body.User.Id);
        req.Status.Should().Be(DeletionRequestStatuses.Pending);
        req.ScheduledFor.Should()
            .BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Delete_BlocksFurtherLogin()
    {
        var (http, _) = await RegisterAsync("del2@example.com");
        (await http.PostAsJsonAsync("/api/me/delete",
            new DeleteAccountRequest())).EnsureSuccessStatusCode();

        var fresh = fx.CreateClient();
        var login = await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "del2@example.com",
            Password = "Password1",
        });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_RevokesRefreshTokens()
    {
        var (http, body) = await RegisterAsync("del3@example.com");
        (await http.PostAsJsonAsync("/api/me/delete",
            new DeleteAccountRequest())).EnsureSuccessStatusCode();

        var fresh = fx.CreateClient();
        var refresh = await fresh.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest
        {
            RefreshToken = body.RefreshToken,
        });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "refresh token was revoked at delete time → rotation fails before user lookup");

        await using var db = fx.CreateDbContext();
        var active = await db.RefreshTokens.CountAsync(
            r => r.UserId == body.User.Id && r.RevokedAt == null);
        active.Should().Be(0);
    }

    [Fact]
    public async Task Delete_DoubleRequestReturnsConflict()
    {
        var (http, _) = await RegisterAsync("del4@example.com");
        (await http.PostAsJsonAsync("/api/me/delete",
            new DeleteAccountRequest())).EnsureSuccessStatusCode();

        // Token still valid (15 min). Second call -> conflict.
        var second = await http.PostAsJsonAsync("/api/me/delete",
            new DeleteAccountRequest());
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ProcessScheduledDeletions_AnonymisesExpiredAccounts()
    {
        // Register two users; mark one's deletion as expired
        var (http1, b1) = await RegisterAsync("expired@example.com");
        (await http1.PostAsJsonAsync("/api/me/delete",
            new DeleteAccountRequest())).EnsureSuccessStatusCode();

        var (_, b2) = await RegisterAsync("active@example.com");

        await using (var db = fx.CreateDbContext())
        {
            var req = await db.DeletionRequests.FirstAsync(d => d.UserId == b1.User.Id);
            req.ScheduledFor = DateTime.UtcNow.AddDays(-1);    // make it due
            await db.SaveChangesAsync();
        }

        // Trigger the job directly via service
        using var scope = fx.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAccountDeletionService>();
        var count = await svc.ProcessScheduledDeletionsAsync(CancellationToken.None);
        count.Should().Be(1);

        await using var verify = fx.CreateDbContext();

        // Expired user anonymised
        var expired = await verify.Users.FirstAsync(u => u.Id == b1.User.Id);
        expired.Email.Should().StartWith("deleted-").And.EndWith("@scango.deleted");
        expired.Name.Should().Be("[deleted]");
        expired.PasswordHash.Should().BeNull();

        // Active user unchanged
        var active = await verify.Users.FirstAsync(u => u.Id == b2.User.Id);
        active.Email.Should().Be("active@example.com");
    }
}
