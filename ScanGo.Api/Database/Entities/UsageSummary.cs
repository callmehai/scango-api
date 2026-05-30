namespace ScanGo.Api.Database.Entities;

public class UsageSummary
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PeriodKey { get; set; } = "";
    public string PeriodKind { get; set; } = PeriodKinds.Monthly;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int CreditsUsed { get; set; }
    public int ScanCount { get; set; }
    public int AskCount { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
