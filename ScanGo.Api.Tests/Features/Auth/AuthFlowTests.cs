using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ScanGo.Api.Features.Auth;

namespace ScanGo.Api.Tests.Features.Auth;

public class AuthFlowTests(AuthApiFixture fx)
    : IClassFixture<AuthApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fx.ResetDbAsync();

    private HttpClient Client() => fx.CreateClient();

    private static RegisterRequest GoodRegister(string email = "alice@example.com") => new()
    {
        Email = email,
        Password = "Password1",
        Name = "Alice",
        TermsAccepted = true,
        PrivacyAccepted = true,
        Device = "test",
        Platform = "web",
    };

    [Fact]
    public async Task Register_Succeeds_AndReturnsTokens()
    {
        var http = Client();
        var resp = await http.PostAsJsonAsync("/api/auth/register", GoodRegister());

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.User.Email.Should().Be("alice@example.com");
        body.User.Role.Should().Be("user");
        body.User.Plan.Should().Be("free");
        body.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Register_RejectsDuplicateEmail()
    {
        var http = Client();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("dup@example.com")))
            .EnsureSuccessStatusCode();

        var resp = await http.PostAsJsonAsync(
            "/api/auth/register", GoodRegister("dup@example.com"));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_RejectsWeakPassword()
    {
        var http = Client();
        var req = GoodRegister("weakpw@example.com");
        req.Password = "abcdefgh";       // no digit
        var resp = await http.PostAsJsonAsync("/api/auth/register", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_RejectsMissingTermsOrPrivacy()
    {
        var http = Client();
        var req = GoodRegister("noterms@example.com");
        req.TermsAccepted = false;
        var resp = await http.PostAsJsonAsync("/api/auth/register", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_Succeeds_AfterRegister()
    {
        var http = Client();
        await http.PostAsJsonAsync("/api/auth/register", GoodRegister("login@example.com"));

        var resp = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "login@example.com",
            Password = "Password1",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_RejectsWrongPassword()
    {
        var http = Client();
        await http.PostAsJsonAsync("/api/auth/register", GoodRegister("wrong@example.com"));

        var resp = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "wrong@example.com",
            Password = "notmypassword",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_RejectsUnknownEmail()
    {
        var http = Client();
        var resp = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "ghost@example.com",
            Password = "doesntmatter1",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldOneRejected()
    {
        var http = Client();
        var regResp = await http.PostAsJsonAsync(
            "/api/auth/register", GoodRegister("rotate@example.com"));
        var regBody = await regResp.Content.ReadFromJsonAsync<AuthResponse>();
        var oldRefresh = regBody!.RefreshToken;

        // First refresh -> succeeds
        var first = await http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest
        {
            RefreshToken = oldRefresh,
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<AuthResponse>();
        firstBody!.RefreshToken.Should().NotBe(oldRefresh, "single-use rotation");

        // Replay old token -> rejected
        var replay = await http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest
        {
            RefreshToken = oldRefresh,
        });
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var http = Client();
        var regResp = await http.PostAsJsonAsync(
            "/api/auth/register", GoodRegister("logout@example.com"));
        var regBody = await regResp.Content.ReadFromJsonAsync<AuthResponse>();

        (await http.PostAsJsonAsync("/api/auth/logout", new LogoutRequest
        {
            RefreshToken = regBody!.RefreshToken,
        })).EnsureSuccessStatusCode();

        // Same refresh after logout -> 401
        var refreshAfter = await http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest
        {
            RefreshToken = regBody.RefreshToken,
        });
        refreshAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_RequiresAuth()
    {
        var http = Client();
        var resp = await http.GetAsync("/api/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ReturnsAuthenticatedUser()
    {
        var http = Client();
        var regResp = await http.PostAsJsonAsync(
            "/api/auth/register", GoodRegister("me@example.com"));
        regResp.EnsureSuccessStatusCode();
        var regBody = await regResp.Content.ReadFromJsonAsync<AuthResponse>();

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", regBody!.AccessToken);

        var resp = await http.GetAsync("/api/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<UserDto>();
        dto!.Email.Should().Be("me@example.com");
        dto.Id.Should().Be(regBody.User.Id);
    }
}
