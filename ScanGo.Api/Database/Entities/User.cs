namespace ScanGo.Api.Database.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = UserRoles.User;
    public string Plan { get; set; } = PlanCodes.Free;
    public DateTime? PlanExpiresAt { get; set; }
    public string Status { get; set; } = UserStatuses.Active;
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime TermsAcceptedAt { get; set; }
    public DateTime PrivacyAcceptedAt { get; set; }
    public bool MarketingOptIn { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<Conversation> Conversations { get; set; } = [];
    public ICollection<UsageEvent> UsageEvents { get; set; } = [];
    public ICollection<UsageSummary> UsageSummaries { get; set; } = [];
    public ICollection<CreditLedgerEntry> CreditLedger { get; set; } = [];
    public ICollection<PaymentOrder> PaymentOrders { get; set; } = [];
}
