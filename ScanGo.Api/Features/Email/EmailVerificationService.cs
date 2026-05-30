using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Features.Email;

public enum EmailVerifyError
{
    AlreadyVerified,
    InvalidOrExpiredToken,
    UserNotFound,
}

public interface IEmailVerificationService
{
    Task RequestAsync(Guid userId, CancellationToken ct);
    Task<EmailVerifyError?> ConfirmAsync(string token, CancellationToken ct);
}

public class EmailVerificationService(
    ScanGoDbContext db,
    IEmailService emailer,
    IOptions<AppUrlsOptions> urls) : IEmailVerificationService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);
    private readonly AppUrlsOptions _urls = urls.Value;

    public async Task RequestAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.EmailVerifiedAt is not null) return;

        // Invalidate any existing pending tokens for this user (one active token at a time)
        await db.EmailVerifications
            .Where(v => v.UserId == userId && v.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.UsedAt, DateTime.UtcNow), ct);

        var raw = TokenHasher.GenerateRandom(32);
        db.EmailVerifications.Add(new EmailVerification
        {
            UserId = userId,
            TokenHash = TokenHasher.Hash(raw),
            ExpiresAt = DateTime.UtcNow.Add(TokenLifetime),
        });
        await db.SaveChangesAsync(ct);

        var link = _urls.VerifyEmailLink(raw);
        await emailer.SendVerificationAsync(user.Email, user.Name, link, ct);
    }

    public async Task<EmailVerifyError?> ConfirmAsync(string token, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(token);
        var record = await db.EmailVerifications
            .FirstOrDefaultAsync(v => v.TokenHash == hash, ct);

        if (record is null) return EmailVerifyError.InvalidOrExpiredToken;
        if (record.UsedAt is not null) return EmailVerifyError.InvalidOrExpiredToken;
        if (record.ExpiresAt <= DateTime.UtcNow) return EmailVerifyError.InvalidOrExpiredToken;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == record.UserId, ct);
        if (user is null) return EmailVerifyError.UserNotFound;
        if (user.EmailVerifiedAt is not null) return EmailVerifyError.AlreadyVerified;

        user.EmailVerifiedAt = DateTime.UtcNow;
        record.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }
}
