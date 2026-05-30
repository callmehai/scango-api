using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ScanGo.Api.Common;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Auth;

namespace ScanGo.Api.Tests.Features.Auth;

public class JwtServiceTests
{
    private static JwtService MakeService(AuthOptions? opts = null) =>
        new(Options.Create(opts ?? new AuthOptions
        {
            JwtSecret = "test-secret-must-be-at-least-32-chars-long-please-ok",
            JwtIssuer = "scango-test",
            JwtAudience = "scango",
            AccessTokenMinutes = 15,
        }));

    private static User SampleUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "alice@example.com",
        Name = "Alice",
        Role = UserRoles.User,
        Plan = PlanCodes.Free,
    };

    [Fact]
    public void Issue_ProducesJwtWithExpectedClaims()
    {
        var svc = MakeService();
        var user = SampleUser();
        var jwt = svc.Issue(user);

        jwt.Should().NotBeNullOrWhiteSpace();
        jwt.Split('.').Should().HaveCount(3, "JWT is 3 dot-separated base64url segments");

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        parsed.Claims.Should().Contain(c =>
            c.Type == CurrentUserExtensions.UserIdClaim && c.Value == user.Id.ToString());
        parsed.Claims.Should().Contain(c =>
            c.Type == CurrentUserExtensions.EmailClaim && c.Value == user.Email);
        parsed.Claims.Should().Contain(c =>
            c.Type == CurrentUserExtensions.RoleClaim && c.Value == user.Role);
        parsed.Claims.Should().Contain(c => c.Type == CurrentUserExtensions.PlanClaim);
    }

    [Fact]
    public void Issue_LifetimeMatchesConfig()
    {
        var svc = MakeService(new AuthOptions
        {
            JwtSecret = "test-secret-must-be-at-least-32-chars-long-please-ok",
            JwtIssuer = "scango-test",
            JwtAudience = "scango",
            AccessTokenMinutes = 5,
        });
        var jwt = svc.Issue(SampleUser());
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(jwt);

        var lifetime = parsed.ValidTo - parsed.ValidFrom;
        lifetime.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2));
        svc.AccessTokenLifetime.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Issue_ProducesUniqueJti()
    {
        var svc = MakeService();
        var u = SampleUser();
        var a = new JwtSecurityTokenHandler().ReadJwtToken(svc.Issue(u));
        var b = new JwtSecurityTokenHandler().ReadJwtToken(svc.Issue(u));
        a.Id.Should().NotBe(b.Id);
    }
}
