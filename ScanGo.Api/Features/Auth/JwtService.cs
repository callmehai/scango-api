using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ScanGo.Api.Common;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Features.Auth;

public interface IJwtService
{
    string Issue(User user);
    TimeSpan AccessTokenLifetime { get; }
}

public class JwtService(IOptions<AuthOptions> options) : IJwtService
{
    private readonly AuthOptions _opts = options.Value;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_opts.AccessTokenMinutes);

    public string Issue(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(CurrentUserExtensions.UserIdClaim, user.Id.ToString()),
            new(CurrentUserExtensions.EmailClaim, user.Email),
            new(CurrentUserExtensions.RoleClaim, user.Role),
            new(CurrentUserExtensions.PlanClaim, user.Plan),
            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var token = new JwtSecurityToken(
            issuer: _opts.JwtIssuer,
            audience: _opts.JwtAudience,
            claims: claims,
            notBefore: now,
            expires: now.Add(AccessTokenLifetime),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
