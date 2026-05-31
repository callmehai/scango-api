namespace ScanGo.Api.Database.Entities;

// Stored as varchar + CHECK constraint, not Postgres native enums.
// Use ToDbValue()/ParseXxx() for serialisation.

public static class UserRoles
{
    public const string Admin = "admin";
    public const string User = "user";
    public const string Tester = "tester";

    public static readonly string[] All = [Admin, User, Tester];
}

public static class UserStatuses
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Deleted = "deleted";

    public static readonly string[] All = [Active, Suspended, Deleted];
}

public static class PlanCodes
{
    public const string Free = "free";
    public const string Lite = "lite";                 // 7-day trial tier
    public const string BasicMonthly = "basic_monthly";
    public const string ProMonthly = "pro_monthly";
    public const string ProYearly = "pro_yearly";      // displayed as "Max"
    public const string Unlimited = "unlimited";

    public static readonly string[] All =
        [Free, Lite, BasicMonthly, ProMonthly, ProYearly, Unlimited];
}

public static class ConversationTopics
{
    public const string Product = "product";
    public const string History = "history";
    public const string Place = "place";
    public const string General = "general";

    public static readonly string[] All = [Product, History, Place, General];
}

public static class MessageRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string System = "system";

    public static readonly string[] All = [User, Assistant, System];
}

public static class UsageKinds
{
    public const string Scan = "scan";
    public const string Ask = "ask";

    public static readonly string[] All = [Scan, Ask];
}

public static class PeriodKinds
{
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Rolling7 = "rolling_7d";   // per-user 7-day window from signup

    public static readonly string[] All = [Weekly, Monthly, Rolling7];
}

public static class CreditLedgerReasons
{
    public const string SubscriptionGrant = "subscription_grant";
    public const string Topup = "topup";
    public const string Scan = "scan";
    public const string Ask = "ask";
    public const string AdminGrant = "admin_grant";
    public const string AdminRevoke = "admin_revoke";
    public const string Refund = "refund";
    public const string WeeklyReset = "weekly_reset";
    public const string MonthlyReset = "monthly_reset";
    public const string Expired = "expired";

    public static readonly string[] All =
    [
        SubscriptionGrant, Topup, Scan, Ask, AdminGrant, AdminRevoke,
        Refund, WeeklyReset, MonthlyReset, Expired,
    ];
}

public static class PaymentOrderStatuses
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
    public const string Refunded = "refunded";

    public static readonly string[] All = [Pending, Paid, Expired, Cancelled, Refunded];
}

public static class DeletionRequestStatuses
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = [Pending, Completed, Cancelled];
}
