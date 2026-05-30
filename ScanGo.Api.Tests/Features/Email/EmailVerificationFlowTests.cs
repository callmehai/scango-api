using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Email;
using ScanGo.Api.Tests.Features.Auth;

namespace ScanGo.Api.Tests.Features.Email;

public class EmailVerificationFlowTests(AuthApiFixture fx)
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
    public async Task Register_SendsVerificationEmail()
    {
        var http = Client();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("v1@example.com")))
            .EnsureSuccessStatusCode();

        var sent = DevEmailService.GetLast("v1@example.com");
        sent.Should().NotBeNull();
        sent!.Kind.Should().Be("verification");
        sent.Link.Should().Contain("/verify-email?token=");
    }

    [Fact]
    public async Task VerifyEmail_HappyPath_MarksUserVerified()
    {
        var http = Client();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("v2@example.com")))
            .EnsureSuccessStatusCode();

        var token = ExtractTokenFromLink(DevEmailService.GetLast("v2@example.com")!.Link);

        var resp = await http.PostAsJsonAsync(
            "/api/auth/verify-email", new VerifyEmailRequest { Token = token });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = fx.CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Email == "v2@example.com");
        user.EmailVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyEmail_RejectsBadToken()
    {
        var http = Client();
        var resp = await http.PostAsJsonAsync(
            "/api/auth/verify-email", new VerifyEmailRequest { Token = "totally-bogus" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VerifyEmail_TokenSingleUse()
    {
        var http = Client();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("v3@example.com")))
            .EnsureSuccessStatusCode();
        var token = ExtractTokenFromLink(DevEmailService.GetLast("v3@example.com")!.Link);

        // First use succeeds
        (await http.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest { Token = token }))
            .EnsureSuccessStatusCode();

        // Replay rejected
        var second = await http.PostAsJsonAsync(
            "/api/auth/verify-email", new VerifyEmailRequest { Token = token });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResendVerification_RequiresAuth()
    {
        var http = Client();
        var resp = await http.PostAsync("/api/auth/resend-verification", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResendVerification_GeneratesNewTokenAndInvalidatesOld()
    {
        var http = Client();
        var reg = await http.PostAsJsonAsync("/api/auth/register", GoodRegister("v4@example.com"));
        var body = await reg.Content.ReadFromJsonAsync<AuthResponse>();
        var oldToken = ExtractTokenFromLink(DevEmailService.GetLast("v4@example.com")!.Link);

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        (await http.PostAsync("/api/auth/resend-verification", null))
            .EnsureSuccessStatusCode();
        var newToken = ExtractTokenFromLink(DevEmailService.GetLast("v4@example.com")!.Link);
        newToken.Should().NotBe(oldToken);

        // Old token rejected
        var oldUse = await http.PostAsJsonAsync(
            "/api/auth/verify-email", new VerifyEmailRequest { Token = oldToken });
        oldUse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // New token works
        (await http.PostAsJsonAsync(
            "/api/auth/verify-email", new VerifyEmailRequest { Token = newToken }))
            .EnsureSuccessStatusCode();
    }
}
