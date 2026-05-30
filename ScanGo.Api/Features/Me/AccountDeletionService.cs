using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Features.Me;

public enum DeletionError
{
    UserNotFound,
    AlreadyPending,
    NoPendingRequest,
}

public interface IAccountDeletionService
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(30);

    Task<DeletionError?> RequestAsync(Guid userId, string? reason, CancellationToken ct);
    Task<DeletionError?> CancelAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Hard-delete (anonymise + cascade) any users whose grace period has expired.
    /// Intended to be invoked by a background job, but also callable manually.
    /// Returns the count of users hard-deleted.
    /// </summary>
    Task<int> ProcessScheduledDeletionsAsync(CancellationToken ct);
}

public class AccountDeletionService(ScanGoDbContext db) : IAccountDeletionService
{
    public async Task<DeletionError?> RequestAsync(
        Guid userId, string? reason, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return DeletionError.UserNotFound;

        var pending = await db.DeletionRequests.AnyAsync(
            d => d.UserId == userId && d.Status == DeletionRequestStatuses.Pending, ct);
        if (pending) return DeletionError.AlreadyPending;

        var now = DateTime.UtcNow;
        db.DeletionRequests.Add(new DeletionRequest
        {
            UserId = userId,
            Status = DeletionRequestStatuses.Pending,
            RequestedAt = now,
            ScheduledFor = now.Add(IAccountDeletionService.GracePeriod),
            Reason = reason,
        });

        // Mark user as soft-deleted immediately — login blocked, but data kept
        // for the 30d grace so user can cancel.
        user.Status = UserStatuses.Deleted;
        user.DeletedAt = now;

        // Revoke all refresh tokens — force logout everywhere
        await db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, now), ct);

        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<DeletionError?> CancelAsync(Guid userId, CancellationToken ct)
    {
        var request = await db.DeletionRequests
            .Where(d => d.UserId == userId && d.Status == DeletionRequestStatuses.Pending)
            .FirstOrDefaultAsync(ct);
        if (request is null) return DeletionError.NoPendingRequest;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return DeletionError.UserNotFound;

        request.Status = DeletionRequestStatuses.Cancelled;
        user.Status = UserStatuses.Active;
        user.DeletedAt = null;

        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<int> ProcessScheduledDeletionsAsync(CancellationToken ct)
    {
        var due = await db.DeletionRequests
            .Where(d => d.Status == DeletionRequestStatuses.Pending
                && d.ScheduledFor <= DateTime.UtcNow)
            .ToListAsync(ct);

        var count = 0;
        foreach (var req in due)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId, ct);
            if (user is null)
            {
                req.Status = DeletionRequestStatuses.Completed;
                req.CompletedAt = DateTime.UtcNow;
                continue;
            }

            // Anonymise PII; let cascade FKs drop conversations/messages/etc.
            // Email replaced with deterministic tombstone so the row is kept
            // (audit_log references stay valid via SET NULL).
            var tombstone = $"deleted-{user.Id:N}@scango.deleted";
            user.Email = tombstone;
            user.Name = "[deleted]";
            // Keep a tombstone in google_id so the CHECK (password OR google)
            // constraint stays satisfied. Won't enable login (no matching Google sub).
            user.PasswordHash = null;
            user.GoogleId = $"deleted-{user.Id:N}";
            user.Status = UserStatuses.Deleted;
            user.DeletedAt ??= DateTime.UtcNow;

            // Cascade-drop conversations (and messages via FK)
            await db.Conversations.Where(c => c.UserId == req.UserId).ExecuteDeleteAsync(ct);

            req.Status = DeletionRequestStatuses.Completed;
            req.CompletedAt = DateTime.UtcNow;
            count++;
        }

        if (count > 0) await db.SaveChangesAsync(ct);
        return count;
    }
}
