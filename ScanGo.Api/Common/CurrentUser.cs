using System.Security.Claims;

namespace ScanGo.Api.Common;

/// <summary>
/// Extension helpers to read the authenticated user from ClaimsPrincipal.
/// </summary>
public static class CurrentUserExtensions
{
    public const string UserIdClaim = "sub";          // JWT standard "subject"
    public const string EmailClaim = "email";
    public const string RoleClaim = ClaimTypes.Role;
    public const string PlanClaim = "plan";

    public static Guid? UserId(this ClaimsPrincipal user)
    {
        // Try both raw "sub" (MapInboundClaims=false) and NameIdentifier
        // (default mapping). JwtBearer middleware may map "sub" -> NameIdentifier.
        var raw =
            user.FindFirstValue(UserIdClaim)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static Guid RequireUserId(this ClaimsPrincipal user) =>
        user.UserId() ?? throw new UnauthorizedAccessException("Not authenticated");

    public static string? Email(this ClaimsPrincipal user) =>
        user.FindFirstValue(EmailClaim);

    public static string? Role(this ClaimsPrincipal user) =>
        user.FindFirstValue(RoleClaim);

    public static string? Plan(this ClaimsPrincipal user) =>
        user.FindFirstValue(PlanClaim);
}
