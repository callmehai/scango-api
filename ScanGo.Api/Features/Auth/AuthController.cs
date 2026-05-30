using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ScanGo.Api.Common;
using ScanGo.Api.Features.Email;

namespace ScanGo.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAuthService auth,
    IEmailVerificationService emailVerification,
    IPasswordResetService passwordReset) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest req, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(req, ct);
        if (result.Success is not null)
        {
            // fire-and-trigger verification email; failure not fatal to register
            await emailVerification.RequestAsync(result.Success.User.Id, ct);
        }
        return ToResponse(result);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await auth.LoginAsync(req, ct);
        return ToResponse(result);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth-loose")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest req, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(req, ct);
        return ToResponse(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest req, CancellationToken ct)
    {
        await auth.LogoutAsync(req.RefreshToken, ct);
        return Ok(new { ok = true });
    }

    [HttpPost("google")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Google(
        [FromBody] GoogleLoginRequest req, CancellationToken ct)
    {
        var result = await auth.GoogleLoginAsync(req, ct);
        return ToResponse(result);
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting("auth-loose")]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest req, CancellationToken ct)
    {
        var err = await emailVerification.ConfirmAsync(req.Token, ct);
        return err switch
        {
            null => Ok(new { ok = true }),
            EmailVerifyError.AlreadyVerified => Ok(new { ok = true, alreadyVerified = true }),
            EmailVerifyError.InvalidOrExpiredToken =>
                BadRequest(new { code = "InvalidOrExpiredToken", message = "Token không hợp lệ hoặc đã hết hạn." }),
            EmailVerifyError.UserNotFound =>
                NotFound(new { code = "UserNotFound", message = "Người dùng không tồn tại." }),
            _ => StatusCode(500),
        };
    }

    [HttpPost("resend-verification")]
    [Authorize]
    [EnableRateLimiting("auth-loose")]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        await emailVerification.RequestAsync(userId, ct);
        return Ok(new { ok = true });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        await passwordReset.RequestAsync(req.Email, ct);
        // Always 200 to avoid email enumeration
        return Ok(new { ok = true });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var err = await passwordReset.ResetAsync(req.Token, req.NewPassword, ct);
        return err switch
        {
            null => Ok(new { ok = true }),
            PasswordResetError.InvalidOrExpiredToken =>
                BadRequest(new { code = "InvalidOrExpiredToken", message = "Token không hợp lệ hoặc đã hết hạn." }),
            PasswordResetError.PasswordTooShort =>
                BadRequest(new { code = "PasswordTooShort", message = $"Mật khẩu phải có ít nhất {PasswordHasher.MinLength} ký tự." }),
            PasswordResetError.PasswordNeedsLetterAndDigit =>
                BadRequest(new { code = "PasswordNeedsLetterAndDigit", message = "Mật khẩu phải chứa cả chữ và số." }),
            PasswordResetError.UserNotFound =>
                NotFound(new { code = "UserNotFound", message = "Người dùng không tồn tại." }),
            _ => StatusCode(500),
        };
    }

    private IActionResult ToResponse(AuthResult result)
    {
        if (result.Success is not null) return Ok(result.Success);

        var err = result.Error!;
        var status = err.Code switch
        {
            AuthErrorCode.EmailAlreadyTaken => StatusCodes.Status409Conflict,
            AuthErrorCode.AccountSuspended => StatusCodes.Status403Forbidden,
            AuthErrorCode.AccountDeleted => StatusCodes.Status403Forbidden,
            AuthErrorCode.InvalidCredentials => StatusCodes.Status401Unauthorized,
            AuthErrorCode.InvalidRefreshToken => StatusCodes.Status401Unauthorized,
            AuthErrorCode.InvalidGoogleToken => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, new { code = err.Code.ToString(), message = err.Message });
    }
}
