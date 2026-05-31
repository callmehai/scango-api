using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Metering;

namespace ScanGo.Api.Features.Me;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(
    ScanGoDbContext db,
    IRefreshTokenService refreshTokens,
    IAccountDeletionService deletion,
    IQuotaService quota) : ControllerBase
{
    [HttpGet("usage")]
    public async Task<IActionResult> Usage(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var plan = User.Plan() ?? PlanCodes.Free;
        var role = User.Role() ?? UserRoles.User;
        var s = await quota.GetStatusAsync(userId, plan, role, ct);
        return Ok(new
        {
            limited = s.Limited,
            scansUsed = s.ScansUsed,
            scansLimit = s.ScansLimit,
            asksUsed = s.AsksUsed,
            asksLimit = s.AsksLimit,
            resetAtUtc = s.ResetAtUtc,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null) return NotFound();
        return Ok(AuthService.ToDto(user));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        if (user.PasswordHash is null)
        {
            return BadRequest(new
            {
                code = "NoPasswordSet",
                message = "Tài khoản này chưa đặt mật khẩu (đăng nhập bằng Google).",
            });
        }
        if (!PasswordHasher.Verify(req.OldPassword, user.PasswordHash))
        {
            return Unauthorized(new
            {
                code = "InvalidCredentials",
                message = "Mật khẩu hiện tại không đúng.",
            });
        }

        var pwCheck = PasswordHasher.Validate(req.NewPassword);
        if (pwCheck == PasswordValidationResult.TooShort)
            return BadRequest(new
            {
                code = "PasswordTooShort",
                message = $"Mật khẩu mới phải có ít nhất {PasswordHasher.MinLength} ký tự.",
            });
        if (pwCheck == PasswordValidationResult.NeedsLetterAndDigit)
            return BadRequest(new
            {
                code = "PasswordNeedsLetterAndDigit",
                message = "Mật khẩu mới phải chứa cả chữ và số.",
            });

        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);

        // Security: revoke all refresh tokens so other devices need to re-login
        await db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.RevokedAt, DateTime.UtcNow), ct);

        await db.SaveChangesAsync(ct);
        _ = refreshTokens;        // dep kept; future audit log hook here
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Request account deletion. Soft-deletes immediately + schedules hard
    /// delete after 30d grace. Apple G5.1.1(v) + Google P1 compliance.
    /// </summary>
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(
        [FromBody] DeleteAccountRequest req, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var err = await deletion.RequestAsync(userId, req.Reason, ct);
        return err switch
        {
            null => Ok(new
            {
                ok = true,
                message = "Tài khoản sẽ bị xoá vĩnh viễn sau 30 ngày. Bạn có thể huỷ yêu cầu trong khoảng thời gian này.",
                scheduledFor = DateTime.UtcNow.Add(IAccountDeletionService.GracePeriod),
            }),
            DeletionError.AlreadyPending =>
                Conflict(new { code = "AlreadyPending", message = "Đã có yêu cầu xoá tài khoản đang chờ." }),
            DeletionError.UserNotFound => NotFound(),
            _ => StatusCode(500),
        };
    }

    [HttpPost("cancel-deletion")]
    [AllowAnonymous]    // user can't login after soft-delete; need to allow via token in URL eventually.
                        // For now require a special flow — see PR2e or admin endpoint.
    public IActionResult CancelDeletion() =>
        BadRequest(new
        {
            code = "NotImplementedHere",
            message = "Liên hệ admin để huỷ yêu cầu xoá tài khoản đang trong thời hạn 30 ngày.",
        });
}
