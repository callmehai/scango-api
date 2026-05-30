using System.Text.Json;

namespace ScanGo.Api.Database.Entities;

public class CreditLedgerEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PeriodKey { get; set; } = "";
    public int Delta { get; set; }
    public int BalanceAfter { get; set; }
    public string Reason { get; set; } = "";
    public JsonDocument Meta { get; set; } = JsonDocument.Parse("{}");
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
