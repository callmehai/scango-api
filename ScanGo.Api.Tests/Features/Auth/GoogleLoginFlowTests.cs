using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using ScanGo.Api.Features.Auth;

namespace ScanGo.Api.Tests.Features.Auth;

/// <summary>
/// Google sign-in flow tests — uses a stub IGoogleTokenVerifier so we never
/// actually call Google's servers. Tests the user-creation / linking logic.
/// </summary>
public class GoogleLoginFlowTests(AuthApiFixture fx)
    : IClassFixture<AuthApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fx.ResetDbAsync();

    /// <summary>
    /// Replace IGoogleTokenVerifier with a stub that returns the given profile.
    /// </summary>
    private WebApplicationFactory<Program> WithGoogle(GoogleProfile? profile) =>
        fx.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                var stub = Substitute.For<IGoogleTokenVerifier>();
                stub.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(profile);
                s.RemoveAll<IGoogleTokenVerifier>();
                s.AddSingleton(stub);
            }));

    [Fact]
    public async Task Google_RejectsInvalidToken()
    {
        using var f = WithGoogle(profile: null);
        var http = f.CreateClient();

        var resp = await http.PostAsJsonAsync("/api/auth/google",
            new GoogleLoginRequest { IdToken = "bad-token" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Google_FirstLogin_CreatesUser_Verified_EmailMarked()
    {
        var profile = new GoogleProfile(
            Sub: "google-sub-001",
            Email: "newgoog@example.com",
            Name: "New Goog");

        using var f = WithGoogle(profile);
        var http = f.CreateClient();

        var resp = await http.PostAsJsonAsync("/api/auth/google",
            new GoogleLoginRequest { IdToken = "anything" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        body!.User.Email.Should().Be("newgoog@example.com");
        body.User.EmailVerified.Should().BeTrue("Google already verified the email");
        body.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();

        await using var db = fx.CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Email == "newgoog@example.com");
        user.GoogleId.Should().Be("google-sub-001");
        user.EmailVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Google_ExistingEmail_LinksGoogleId()
    {
        // Register normally first (email + password)
        var regHttp = fx.CreateClient();
        (await regHttp.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = "link@example.com",
            Password = "Password1",
            Name = "Link Me",
            TermsAccepted = true,
            PrivacyAccepted = true,
        })).EnsureSuccessStatusCode();

        // Google sign-in with same email -> should link, not duplicate
        using var f = WithGoogle(new GoogleProfile(
            Sub: "google-sub-002",
            Email: "link@example.com",
            Name: "Link Me"));
        var http = f.CreateClient();
        (await http.PostAsJsonAsync("/api/auth/google",
            new GoogleLoginRequest { IdToken = "anything" }))
            .EnsureSuccessStatusCode();

        await using var db = fx.CreateDbContext();
        var users = await db.Users.Where(u => u.Email == "link@example.com").ToListAsync();
        users.Should().HaveCount(1, "no duplicate user");
        users[0].GoogleId.Should().Be("google-sub-002");
    }

    [Fact]
    public async Task Google_SecondLogin_ReusesAccount()
    {
        var profile = new GoogleProfile("google-sub-003", "repeat@example.com", "R");

        using var f = WithGoogle(profile);
        var http = f.CreateClient();

        (await http.PostAsJsonAsync("/api/auth/google",
            new GoogleLoginRequest { IdToken = "first" })).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("/api/auth/google",
            new GoogleLoginRequest { IdToken = "second" })).EnsureSuccessStatusCode();

        await using var db = fx.CreateDbContext();
        var count = await db.Users.CountAsync(u => u.Email == "repeat@example.com");
        count.Should().Be(1);
    }
}
