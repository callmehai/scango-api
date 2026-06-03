using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Billing;
using ScanGo.Api.Features.Conversations;
using ScanGo.Api.Features.Metering;
using ScanGo.Api.Features.Storage;

namespace ScanGo.Api.Features.Me;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(
    ScanGoDbContext db,
    IRefreshTokenService refreshTokens,
    IAccountDeletionService deletion,
    IQuotaService quota,
    IObjectStorage storage) : ControllerBase
{
    // Avatars are small; cap the raw upload well below the scan limit.
    private const long MaxAvatarBytes = 5 * 1024 * 1024;

    private static string AvatarKey(Guid userId) => $"avatars/{userId:N}.jpg";

    [HttpGet("usage")]
    public async Task<IActionResult> Usage(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var role = User.Role() ?? UserRoles.User;

        // Resolve the EFFECTIVE plan from the DB (not the possibly-stale JWT
        // claim) and lazily downgrade if it has expired, so the quota shown is
        // always accurate even before the next token refresh.
        var dbUser = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        var plan = dbUser?.Plan ?? User.Plan() ?? PlanCodes.Free;
        if (dbUser is not null && Plans.EnforceExpiry(dbUser, DateTime.UtcNow))
        {
            await db.SaveChangesAsync(ct);
            plan = dbUser.Plan;
        }

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

    /// <summary>Update editable profile fields (currently just the display name).</summary>
    [HttpPatch]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        var name = req.Name.Trim();
        if (name.Length == 0)
            return BadRequest(new { code = "NameRequired", message = "Tên không được để trống." });

        user.Name = name;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(AuthService.ToDto(user));
    }

    /// <summary>Upload / replace the user's avatar. Center-cropped to a square JPEG.</summary>
    [HttpPost("avatar")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { code = "NoImage", message = "Vui lòng chọn ảnh." });
        if (file.Length > MaxAvatarBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { code = "ImageTooLarge", message = "Ảnh không được lớn hơn 5MB." });

        var userId = User.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        MemoryStream optimised;
        try
        {
            await using var raw = file.OpenReadStream();
            optimised = await ImageProcessor.OptimiseAvatarAsync(raw, ct);
        }
        catch
        {
            return BadRequest(new { code = "InvalidImage", message = "Ảnh không hợp lệ hoặc không đọc được." });
        }

        await using (optimised)
        {
            var key = AvatarKey(userId);
            await storage.PutAsync(key, optimised, "image/jpeg", ct);
            user.AvatarStorageKey = key;
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Ok(AuthService.ToDto(user));
    }

    /// <summary>Remove the current avatar (falls back to the initial-letter avatar).</summary>
    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        if (user.AvatarStorageKey is not null)
        {
            await storage.DeleteAsync(user.AvatarStorageKey, ct);
            user.AvatarStorageKey = null;
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Ok(AuthService.ToDto(user));
    }

    /// <summary>Stream the user's avatar bytes (authorized; fetched as a blob by the SPA).</summary>
    [HttpGet("avatar")]
    public async Task<IActionResult> GetAvatar(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.AvatarStorageKey is null) return NotFound();

        var got = await storage.GetAsync(user.AvatarStorageKey, ct);
        if (got is null) return NotFound();

        // Key is stable per user, so avoid stale caches: revalidate each load.
        Response.Headers.CacheControl = "private, no-cache";
        return File(got.Value.stream, got.Value.contentType);
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
