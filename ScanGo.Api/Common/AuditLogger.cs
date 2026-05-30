using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Common;

public static class AuditActions
{
    public const string Register = "register";
    public const string Login = "login";
    public const string LoginFailed = "login_failed";
    public const string Logout = "logout";
    public const string GoogleLogin = "google_login";
    public const string PasswordChanged = "password_changed";
    public const string PasswordResetRequested = "password_reset_requested";
    public const string PasswordResetCompleted = "password_reset_completed";
    public const string EmailVerified = "email_verified";
    public const string AccountDeletionRequested = "account_deletion_requested";
    public const string AccountHardDeleted = "account_hard_deleted";
}

public interface IAuditLogger
{
    Task LogAsync(
        string action,
        Guid? actorUserId = null,
        Guid? targetUserId = null,
        object? meta = null,
        CancellationToken ct = default);
}

public class AuditLogger(
    ScanGoDbContext db,
    IHttpContextAccessor httpAccessor) : IAuditLogger
{
    public async Task LogAsync(
        string action,
        Guid? actorUserId = null,
        Guid? targetUserId = null,
        object? meta = null,
        CancellationToken ct = default)
    {
        var ctx = httpAccessor.HttpContext;
        var entry = new AuditLogEntry
        {
            Action = action,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId ?? actorUserId,
            Ip = ctx?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = ctx?.Request.Headers["User-Agent"].ToString(),
            Meta = meta is null
                ? JsonDocument.Parse("{}")
                : JsonSerializer.SerializeToDocument(meta),
        };
        db.AuditLog.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
