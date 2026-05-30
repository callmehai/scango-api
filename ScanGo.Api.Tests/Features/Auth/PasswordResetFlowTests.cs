using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Email;

namespace ScanGo.Api.Tests.Features.Auth;

public class PasswordResetFlowTests(AuthApiFixture fx)
    : IClassFixture<AuthApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        DevEmailService.Clear();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => fx.ResetDbAsync();

    private HttpClient Client() => fx.CreateClient();

    private static RegisterRequest GoodRegister(string email) => new()
    {
        Email = email,
        Password = "Password1",
        Name = "T",
        TermsAccepted = true,
        PrivacyAccepted = true,
    };

    private static string ExtractTokenFromLink(string link)
    {
        var qs = new Uri(link).Query;
        var pairs = qs.TrimStart('?').Split('&');
        var tokenPair = pairs.First(p => p.StartsWith("token="));
        return Uri.UnescapeDataString(tokenPair["token=".Length..]);
    }

    [Fact]
    public async Task Forgot_AlwaysReturns200_EvenForUnknownEmail()
    {
        var http = Client();
        var resp = await http.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest { Email = "ghost@example.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        DevEmailService.GetLast("ghost@example.com").Should().BeNull();
    }

    [Fact]
    public async Task Forgot_SendsResetEmailForKnownUser()
    {
        var http = Client();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("p1@example.com")))
            .EnsureSuccessStatusCode();
        DevEmailService.Clear();    // ignore the verification email

        var resp = await http.PostAsJsonAsync(
            "/api/auth/forgot-password", new ForgotPasswordRequest { Email = "p1@example.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var sent = DevEmailService.GetLast("p1@example.com");
        sent.Should().NotBeNull();
        sent!.Kind.Should().Be("password_reset");
    }

    [Fact]
    public async Task Reset_HappyPath_LetsUserLoginWithNewPassword()
    {
        var http = Client();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("p2@example.com")))
            .EnsureSuccessStatusCode();
        DevEmailService.Clear();

        (await http.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest { Email = "p2@example.com" }))
            .EnsureSuccessStatusCode();

        var token = ExtractTokenFromLink(DevEmailService.GetLast("p2@example.com")!.Link);

        (await http.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "NewPassword2",
        })).EnsureSuccessStatusCode();

        // Old password rejected
        var oldLogin = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "p2@example.com",
            Password = "Password1",
        });
        oldLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // New password works
        var newLogin = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "p2@example.com",
            Password = "NewPassword2",
        });
        newLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_TokenSingleUse()
    {
        var http = Client();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("p3@example.com")))
            .EnsureSuccessStatusCode();
        DevEmailService.Clear();
        (await http.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest { Email = "p3@example.com" }))
            .EnsureSuccessStatusCode();

        var token = ExtractTokenFromLink(DevEmailService.GetLast("p3@example.com")!.Link);

        (await http.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "NewPassword2",
        })).EnsureSuccessStatusCode();

        var second = await http.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "AnotherPw1",
        });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reset_RejectsWeakNewPassword()
    {
        var http = Client();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("p4@example.com")))
            .EnsureSuccessStatusCode();
        DevEmailService.Clear();
        (await http.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest { Email = "p4@example.com" }))
            .EnsureSuccessStatusCode();
        var token = ExtractTokenFromLink(DevEmailService.GetLast("p4@example.com")!.Link);

        var resp = await http.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "abcdefgh",   // no digit
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reset_RevokesAllRefreshTokens()
    {
        var http = Client();
        var reg = await http.PostAsJsonAsync("/api/auth/register", GoodRegister("p5@example.com"));
        var regBody = await reg.Content.ReadFromJsonAsync<AuthResponse>();
        var refreshTokenBefore = regBody!.RefreshToken;

        DevEmailService.Clear();
        (await http.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest { Email = "p5@example.com" }))
            .EnsureSuccessStatusCode();
        var token = ExtractTokenFromLink(DevEmailService.GetLast("p5@example.com")!.Link);

        (await http.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "NewPassword2",
        })).EnsureSuccessStatusCode();

        // Old refresh token revoked
        var refresh = await http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest
        {
            RefreshToken = refreshTokenBefore,
        });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify in DB
        await using var db = fx.CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Email == "p5@example.com");
        var activeTokens = await db.RefreshTokens
            .CountAsync(r => r.UserId == user.Id && r.RevokedAt == null);
        activeTokens.Should().Be(0);
    }
}
