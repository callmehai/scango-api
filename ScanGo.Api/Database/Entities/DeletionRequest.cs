namespace ScanGo.Api.Database.Entities;

public class DeletionRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = DeletionRequestStatuses.Pending;
    public DateTime RequestedAt { get; set; }
    public DateTime ScheduledFor { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Reason { get; set; }

    public User User { get; set; } = null!;
}
