using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Billing;

namespace ScanGo.Api.Features.Auth;

public enum AuthErrorCode
{
    EmailAlreadyTaken,
    PasswordTooShort,
    PasswordNeedsLetterAndDigit,
    TermsRequired,
    PrivacyRequired,
    InvalidCredentials,
    AccountSuspended,
    AccountDeleted,
    InvalidRefreshToken,
    InvalidGoogleToken,
}

public class AuthError(AuthErrorCode code, string message)
{
    public AuthErrorCode Code { get; } = code;
    public string Message { get; } = message;
}

public class AuthResult
{
    public AuthError? Error { get; init; }
    public AuthResponse? Success { get; init; }

    public static AuthResult Fail(AuthErrorCode code, string message) =>
        new() { Error = new AuthError(code, message) };

    public static AuthResult Ok(AuthResponse resp) => new() { Success = resp };
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct);
    Task<AuthResult> RefreshAsync(RefreshRequest req, CancellationToken ct);
    Task LogoutAsync(string refreshToken, CancellationToken ct);
    Task<AuthResult> GoogleLoginAsync(GoogleLoginRequest req, CancellationToken ct);
}

public class AuthService(
    ScanGoDbContext db,
    IJwtService jwt,
    IRefreshTokenService refreshTokens,
    IGoogleTokenVerifier googleVerifier,
    IAuditLogger audit) : IAuthService
{
    public async Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        if (!req.TermsAccepted)
            return AuthResult.Fail(AuthErrorCode.TermsRequired, "Bạn cần đồng ý điều khoản sử dụng.");
        if (!req.PrivacyAccepted)
            return AuthResult.Fail(AuthErrorCode.PrivacyRequired, "Bạn cần đồng ý chính sách bảo mật.");

        var pwCheck = PasswordHasher.Validate(req.Password);
        if (pwCheck == PasswordValidationResult.TooShort)
            return AuthResult.Fail(AuthErrorCode.PasswordTooShort,
                $"Mật khẩu phải có ít nhất {PasswordHasher.MinLength} ký tự.");
        if (pwCheck == PasswordValidationResult.NeedsLetterAndDigit)
            return AuthResult.Fail(AuthErrorCode.PasswordNeedsLetterAndDigit,
                "Mật khẩu phải chứa cả chữ và số.");

        var email = req.Email.Trim().ToLowerInvariant();
        var taken = await db.Users.AnyAsync(u => u.Email == email, ct);
        if (taken)
            return AuthResult.Fail(AuthErrorCode.EmailAlreadyTaken, "Email đã được sử dụng.");

        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHasher.Hash(req.Password),
            Name = req.Name.Trim(),
            TermsAcceptedAt = now,
            PrivacyAcceptedAt = now,
            LastLoginAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var access = jwt.Issue(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, req.Device, req.Platform, ct);
        await audit.LogAsync(AuditActions.Register, actorUserId: user.Id, ct: ct);
        return AuthResult.Ok(BuildResponse(user, access, refresh, jwt.AccessTokenLifetime));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Always spend bcrypt time — even when the email is unknown or the account is
        // Google-only (no password) — so response latency can't be used to tell an
        // existing email from a non-existent one (timing user enumeration).
        bool matches;
        if (user?.PasswordHash is not null)
        {
            matches = PasswordHasher.Verify(req.Password, user.PasswordHash);
        }
        else
        {
            PasswordHasher.BurnVerify(req.Password);
            matches = false;
        }

        if (user is null || !matches)
        {
            await audit.LogAsync(AuditActions.LoginFailed,
                actorUserId: user?.Id, meta: new { email }, ct: ct);
            return AuthResult.Fail(AuthErrorCode.InvalidCredentials,
                "Email hoặc mật khẩu không đúng.");
        }

        if (user.Status == UserStatuses.Suspended)
            return AuthResult.Fail(AuthErrorCode.AccountSuspended, "Tài khoản đã bị khoá.");
        if (user.Status == UserStatuses.Deleted || user.DeletedAt is not null)
            return AuthResult.Fail(AuthErrorCode.AccountDeleted, "Tài khoản đã bị xoá.");

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var access = jwt.Issue(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, req.Device, req.Platform, ct);
        await audit.LogAsync(AuditActions.Login, actorUserId: user.Id, ct: ct);
        return AuthResult.Ok(BuildResponse(user, access, refresh, jwt.AccessTokenLifetime));
    }

    public async Task<AuthResult> RefreshAsync(RefreshRequest req, CancellationToken ct)
    {
        var rotated = await refreshTokens.RotateAsync(
            req.RefreshToken, req.Device, req.Platform, ct);

        if (rotated is null)
            return AuthResult.Fail(AuthErrorCode.InvalidRefreshToken,
                "Refresh token không hợp lệ hoặc đã hết hạn.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == rotated.Value.userId, ct);
        if (user is null
            || user.Status != UserStatuses.Active
            || user.DeletedAt is not null)
        {
            return AuthResult.Fail(AuthErrorCode.AccountDeleted, "Tài khoản không khả dụng.");
        }

        // Lazily downgrade an expired paid plan so the freshly-issued token
        // carries the correct (Free) plan claim — don't wait for the hourly sweep.
        if (Plans.EnforceExpiry(user, DateTime.UtcNow))
            await db.SaveChangesAsync(ct);

        var access = jwt.Issue(user);
        return AuthResult.Ok(BuildResponse(
            user, access, rotated.Value.newRefreshToken, jwt.AccessTokenLifetime));
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        await refreshTokens.RevokeAsync(refreshToken, ct);
        await audit.LogAsync(AuditActions.Logout, ct: ct);
    }

    public async Task<AuthResult> GoogleLoginAsync(
        GoogleLoginRequest req, CancellationToken ct)
    {
        var profile = await googleVerifier.VerifyAsync(req.IdToken, ct);
        if (profile is null)
            return AuthResult.Fail(AuthErrorCode.InvalidGoogleToken,
                "Google token không hợp lệ.");

        var email = profile.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.GoogleId == profile.Sub || u.Email == email, ct);

        var now = DateTime.UtcNow;
        if (user is null)
        {
            // First-time Google sign-in for this email -> create account.
            // Terms/Privacy auto-accept because mobile shows them on first launch
            // (we'll keep a flag the user can review later).
            user = new User
            {
                Email = email,
                Name = string.IsNullOrWhiteSpace(profile.Name) ? "Người dùng" : profile.Name,
                GoogleId = profile.Sub,
                EmailVerifiedAt = now,          // Google already verified the email
                TermsAcceptedAt = now,
                PrivacyAcceptedAt = now,
                LastLoginAt = now,
            };
            db.Users.Add(user);
        }
        else
        {
            // Existing account signing in with Google.
            if (user.Status == UserStatuses.Suspended)
                return AuthResult.Fail(AuthErrorCode.AccountSuspended, "Tài khoản đã bị khoá.");
            if (user.Status == UserStatuses.Deleted || user.DeletedAt is not null)
                return AuthResult.Fail(AuthErrorCode.AccountDeleted, "Tài khoản đã bị xoá.");

            // Pre-hijacking guard: the FIRST time we link Google to a pre-existing local
            // account whose email was never verified, the existing password may have been
            // planted by an attacker who registered with the victim's email before they
            // ever used Google. Google has now proven ownership, so drop that untrusted
            // password and kill any sessions opened under it. (A legit user who set a
            // password but never verified can just reset it.) GoogleId is set just below,
            // so the "password OR google" account-integrity check still holds.
            var firstGoogleLink = user.GoogleId is null;
            if (firstGoogleLink && user.EmailVerifiedAt is null && user.PasswordHash is not null)
            {
                user.PasswordHash = null;
                await refreshTokens.RevokeAllForUserAsync(user.Id, ct);
            }

            user.GoogleId ??= profile.Sub;
            user.EmailVerifiedAt ??= now;
            user.LastLoginAt = now;
        }

        await db.SaveChangesAsync(ct);

        var access = jwt.Issue(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, req.Device, req.Platform, ct);
        await audit.LogAsync(AuditActions.GoogleLogin, actorUserId: user.Id, ct: ct);
        return AuthResult.Ok(BuildResponse(user, access, refresh, jwt.AccessTokenLifetime));
    }

    private static AuthResponse BuildResponse(
        User user, string accessToken, string refreshToken, TimeSpan accessLifetime) =>
        new()
        {
            User = ToDto(user),
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = (int)accessLifetime.TotalSeconds,
        };

    public static UserDto ToDto(User u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        Name = u.Name,
        Role = u.Role,
        Plan = u.Plan,
        Status = u.Status,
        EmailVerified = u.EmailVerifiedAt is not null,
        HasAvatar = u.AvatarStorageKey is not null,
        CreatedAt = u.CreatedAt,
    };
}
