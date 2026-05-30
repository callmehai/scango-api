using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Email;

namespace ScanGo.Api.Features.Auth;

public enum PasswordResetError
{
    InvalidOrExpiredToken,
    PasswordTooShort,
    PasswordNeedsLetterAndDigit,
    UserNotFound,
}

public interface IPasswordResetService
{
    /// <summary>
    /// Always returns without leaking whether the email exists (prevents enum).
    /// </summary>
    Task RequestAsync(string email, CancellationToken ct);

    Task<PasswordResetError?> ResetAsync(
        string token, string newPassword, CancellationToken ct);
}

public class PasswordResetService(
    ScanGoDbContext db,
    IEmailService emailer,
    IRefreshTokenService refreshTokens,
    IOptions<AppUrlsOptions> urls) : IPasswordResetService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);
    private readonly AppUrlsOptions _urls = urls.Value;

    public async Task RequestAsync(string email, CancellationToken ct)
    {
        var normalised = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalised, ct);
        if (user is null) return;                             // silent (no enum leak)
        if (user.Status != UserStatuses.Active) return;       // suspended/deleted -> ignore
        if (user.PasswordHash is null) return;                // Google-only account -> nothing to reset

        // Invalidate previous pending reset tokens
        await db.PasswordResets
            .Where(p => p.UserId == user.Id && p.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedAt, DateTime.UtcNow), ct);

        var raw = TokenHasher.GenerateRandom(32);
        db.PasswordResets.Add(new PasswordReset
        {
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(raw),
            ExpiresAt = DateTime.UtcNow.Add(TokenLifetime),
        });
        await db.SaveChangesAsync(ct);

        var link = _urls.ResetPasswordLink(raw);
        await emailer.SendPasswordResetAsync(user.Email, user.Name, link, ct);
    }

    public async Task<PasswordResetError?> ResetAsync(
        string token, string newPassword, CancellationToken ct)
    {
        var pwCheck = PasswordHasher.Validate(newPassword);
        if (pwCheck == PasswordValidationResult.TooShort)
            return PasswordResetError.PasswordTooShort;
        if (pwCheck == PasswordValidationResult.NeedsLetterAndDigit)
            return PasswordResetError.PasswordNeedsLetterAndDigit;

        var hash = TokenHasher.Hash(token);
        var record = await db.PasswordResets.FirstOrDefaultAsync(p => p.TokenHash == hash, ct);
        if (record is null || record.UsedAt is not null || record.ExpiresAt <= DateTime.UtcNow)
            return PasswordResetError.InvalidOrExpiredToken;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == record.UserId, ct);
        if (user is null) return PasswordResetError.UserNotFound;

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        record.UsedAt = DateTime.UtcNow;

        // Security: nuke all refresh tokens for this user so other devices are logged out
        await db.RefreshTokens
            .Where(r => r.UserId == user.Id && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, DateTime.UtcNow), ct);

        await db.SaveChangesAsync(ct);
        _ = refreshTokens; // dep kept for parity with login flows
        return null;
    }
}
