using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Tests.Features.Auth;

namespace ScanGo.Api.Tests.Features.Me;

public class ChangePasswordTests(AuthApiFixture fx)
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
    public async Task ChangePassword_RequiresAuth()
    {
        var http = fx.CreateClient();
        var resp = await http.PostAsJsonAsync("/api/me/change-password",
            new ChangePasswordRequest { OldPassword = "x", NewPassword = "Password2" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_RejectsWrongOldPassword()
    {
        var (http, _) = await RegisterAsync("cp1@example.com");
        var resp = await http.PostAsJsonAsync("/api/me/change-password",
            new ChangePasswordRequest { OldPassword = "WrongPw1", NewPassword = "NewPw1234" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_RejectsWeakNewPassword()
    {
        var (http, _) = await RegisterAsync("cp2@example.com");
        var resp = await http.PostAsJsonAsync("/api/me/change-password",
            new ChangePasswordRequest { OldPassword = "Password1", NewPassword = "abcdefgh" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_HappyPath_LoginsWithNewPassword()
    {
        var (http, _) = await RegisterAsync("cp3@example.com");
        (await http.PostAsJsonAsync("/api/me/change-password",
            new ChangePasswordRequest { OldPassword = "Password1", NewPassword = "NewPw1234" }))
            .EnsureSuccessStatusCode();

        var fresh = fx.CreateClient();
        var old = await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = "cp3@example.com", Password = "Password1" });
        old.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var nu = await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = "cp3@example.com", Password = "NewPw1234" });
        nu.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_RevokesOtherRefreshTokens()
    {
        var (http, body) = await RegisterAsync("cp4@example.com");
        var oldRefresh = body.RefreshToken;

        (await http.PostAsJsonAsync("/api/me/change-password",
            new ChangePasswordRequest { OldPassword = "Password1", NewPassword = "NewPw1234" }))
            .EnsureSuccessStatusCode();

        var fresh = fx.CreateClient();
        var refresh = await fresh.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest { RefreshToken = oldRefresh });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var db = fx.CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Email == "cp4@example.com");
        var active = await db.RefreshTokens.CountAsync(
            r => r.UserId == user.Id && r.RevokedAt == null);
        active.Should().Be(0);
    }
}
