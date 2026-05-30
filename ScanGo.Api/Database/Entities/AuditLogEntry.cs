using System.Text.Json;

namespace ScanGo.Api.Database.Entities;

public class AuditLogEntry
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public Guid? TargetUserId { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public JsonDocument Meta { get; set; } = JsonDocument.Parse("{}");
    public DateTime CreatedAt { get; set; }

    public User? ActorUser { get; set; }
    public User? TargetUser { get; set; }
}
