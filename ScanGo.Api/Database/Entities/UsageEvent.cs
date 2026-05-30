namespace ScanGo.Api.Database.Entities;

public class UsageEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ConversationId { get; set; }
    public string Kind { get; set; } = UsageKinds.Scan;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int Credits { get; set; }
    public bool OcrCalled { get; set; }
    public string PeriodKey { get; set; } = "";       // "2026-05" or "2026-W21"
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public Conversation? Conversation { get; set; }
}
