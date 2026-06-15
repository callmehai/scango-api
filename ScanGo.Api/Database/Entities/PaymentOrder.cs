namespace ScanGo.Api.Database.Entities;

public class PaymentOrder
{
    public Guid Id { get; set; }
    public string OrderCode { get; set; } = "";        // "SCANXY12"
    public Guid UserId { get; set; }
    public string Plan { get; set; } = "";
    public long AmountVnd { get; set; }
    public string Status { get; set; } = PaymentOrderStatuses.Pending;
    public string PaymentMethod { get; set; } = "bank_transfer";
    public string? BankRef { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Note { get; set; }               // admin note, e.g. refund reason
    public DateTime? RefundedAt { get; set; }        // set when status -> refunded
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public User? ApprovedByUser { get; set; }
}
