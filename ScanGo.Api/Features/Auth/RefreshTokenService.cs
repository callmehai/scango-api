using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Features.Auth;

public interface IRefreshTokenService
{
    Task<string> IssueAsync(Guid userId, string? device, string? platform, CancellationToken ct);

    /// <summary>Validate + rotate. Returns the userId for the new access token, OR null if invalid.</summary>
    Task<(Guid userId, string newRefreshToken)?> RotateAsync(
        string presentedToken, string? device, string? platform, CancellationToken ct);

    Task RevokeAsync(string presentedToken, CancellationToken ct);

    /// <summary>Revoke every active refresh token for a user (e.g. on account takeover defence).</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}

public class RefreshTokenService(
    ScanGoDbContext db,
    IOptions<AuthOptions> options) : IRefreshTokenService
{
    private readonly AuthOptions _opts = options.Value;

    public async Task<string> IssueAsync(
        Guid userId, string? device, string? platform, CancellationToken ct)
    {
        var raw = TokenHasher.GenerateRandom(32);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = TokenHasher.Hash(raw),
            Device = device,
            Platform = platform,
            ExpiresAt = DateTime.UtcNow.AddDays(_opts.RefreshTokenDays),
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<(Guid userId, string newRefreshToken)?> RotateAsync(
        string presentedToken, string? device, string? platform, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(presentedToken);
        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null) return null;
        if (token.RevokedAt is not null) return null;
        if (token.ExpiresAt <= DateTime.UtcNow) return null;

        // Single-use: mark current as revoked, issue a new one in the same tx
        token.RevokedAt = DateTime.UtcNow;

        var newRaw = TokenHasher.GenerateRandom(32);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = token.UserId,
            TokenHash = TokenHasher.Hash(newRaw),
            Device = device,
            Platform = platform,
            ExpiresAt = DateTime.UtcNow.AddDays(_opts.RefreshTokenDays),
        });
        await db.SaveChangesAsync(ct);

        return (token.UserId, newRaw);
    }

    public async Task RevokeAsync(string presentedToken, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(presentedToken);
        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null) return;
        token.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }
}
