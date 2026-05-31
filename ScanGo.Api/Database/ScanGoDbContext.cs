using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Database;

public class ScanGoDbContext(DbContextOptions<ScanGoDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailVerification> EmailVerifications => Set<EmailVerification>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<UsageSummary> UsageSummaries => Set<UsageSummary>();
    public DbSet<CreditLedgerEntry> CreditLedger => Set<CreditLedgerEntry>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<DeletionRequest> DeletionRequests => Set<DeletionRequest>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.HasPostgresExtension("pgcrypto");

        ConfigureUsers(b);
        ConfigureRefreshTokens(b);
        ConfigureEmailVerifications(b);
        ConfigurePasswordResets(b);
        ConfigureConversations(b);
        ConfigureMessages(b);
        ConfigureUsageEvents(b);
        ConfigureUsageSummaries(b);
        ConfigureCreditLedger(b);
        ConfigurePaymentOrders(b);
        ConfigureAuditLog(b);
        ConfigureDeletionRequests(b);
        ConfigureAppSettings(b);
    }

    private static void ConfigureAppSettings(ModelBuilder b)
    {
        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.GeminiModel).HasMaxLength(64).IsRequired();
            e.Property(x => x.FreeWeeklyScans).HasDefaultValue(3);
            e.Property(x => x.FreeWeeklyAsks).HasDefaultValue(5);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
    }

    private static void ConfigureUsers(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(255);
            e.Property(x => x.GoogleId).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasMaxLength(16).HasDefaultValue(UserRoles.User).IsRequired();
            e.Property(x => x.Plan).HasMaxLength(32).HasDefaultValue(PlanCodes.Free).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16).HasDefaultValue(UserStatuses.Active).IsRequired();
            e.Property(x => x.MarketingOptIn).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("uq_users_email");
            e.HasIndex(x => x.GoogleId)
                .IsUnique()
                .HasFilter("google_id IS NOT NULL")
                .HasDatabaseName("uq_users_google_id");
            e.HasIndex(x => x.Role).HasDatabaseName("ix_users_role");
            e.HasIndex(x => x.Status).HasDatabaseName("ix_users_status");
            e.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("ix_users_created_at");

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_users_role", $"role IN ({InClause(UserRoles.All)})");
                t.HasCheckConstraint("ck_users_status", $"status IN ({InClause(UserStatuses.All)})");
                t.HasCheckConstraint("ck_users_plan", $"plan IN ({InClause(PlanCodes.All)})");
                t.HasCheckConstraint(
                    "ck_users_has_auth_method",
                    "password_hash IS NOT NULL OR google_id IS NOT NULL");
            });
        });
    }

    private static void ConfigureRefreshTokens(ModelBuilder b)
    {
        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.Device).HasMaxLength(120);
            e.Property(x => x.Platform).HasMaxLength(16);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("uq_refresh_tokens_token_hash");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_refresh_tokens_expires_at");

            e.HasOne(x => x.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureEmailVerifications(ModelBuilder b)
    {
        b.Entity<EmailVerification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("uq_email_verifications_token_hash");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_email_verifications_user_id");
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_email_verifications_expires_at");

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePasswordResets(ModelBuilder b)
    {
        b.Entity<PasswordReset>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("uq_password_resets_token_hash");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_password_resets_user_id");
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_password_resets_expires_at");

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureConversations(ModelBuilder b)
    {
        b.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Title).HasMaxLength(200).HasDefaultValue("").IsRequired();
            e.Property(x => x.Topic).HasMaxLength(16).IsRequired();
            e.Property(x => x.RootLang).HasMaxLength(8).IsRequired();
            e.Property(x => x.TargetLang).HasMaxLength(8).IsRequired();
            e.Property(x => x.ImageStorageKey).HasMaxLength(255);
            e.Property(x => x.ImageMimeType).HasMaxLength(32);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => new { x.UserId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_conversations_user_id_created_at");
            e.HasIndex(x => new { x.UserId, x.Topic })
                .HasDatabaseName("ix_conversations_user_id_topic");

            e.HasOne(x => x.User)
                .WithMany(u => u.Conversations)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.ToTable(t =>
                t.HasCheckConstraint(
                    "ck_conversations_topic",
                    $"topic IN ({InClause(ConversationTopics.All)})"));
        });
    }

    private static void ConfigureMessages(ModelBuilder b)
    {
        b.Entity<Message>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Role).HasMaxLength(16).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => new { x.ConversationId, x.CreatedAt })
                .HasDatabaseName("ix_messages_conversation_id_created_at");

            e.HasOne(x => x.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.ToTable(t =>
                t.HasCheckConstraint(
                    "ck_messages_role",
                    $"role IN ({InClause(MessageRoles.All)})"));
        });
    }

    private static void ConfigureUsageEvents(ModelBuilder b)
    {
        b.Entity<UsageEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Kind).HasMaxLength(16).IsRequired();
            e.Property(x => x.InputTokens).HasDefaultValue(0);
            e.Property(x => x.OutputTokens).HasDefaultValue(0);
            e.Property(x => x.OcrCalled).HasDefaultValue(false);
            e.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => new { x.UserId, x.PeriodKey })
                .HasDatabaseName("ix_usage_events_user_id_period_key");
            e.HasIndex(x => x.CreatedAt)
                .IsDescending()
                .HasDatabaseName("ix_usage_events_created_at");

            e.HasOne(x => x.User)
                .WithMany(u => u.UsageEvents)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Conversation)
                .WithMany()
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.SetNull);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_usage_events_kind", $"kind IN ({InClause(UsageKinds.All)})");
                t.HasCheckConstraint("ck_usage_events_credits_positive", "credits >= 1");
            });
        });
    }

    private static void ConfigureUsageSummaries(ModelBuilder b)
    {
        b.Entity<UsageSummary>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.PeriodKind).HasMaxLength(8).IsRequired();
            e.Property(x => x.CreditsUsed).HasDefaultValue(0);
            e.Property(x => x.ScanCount).HasDefaultValue(0);
            e.Property(x => x.AskCount).HasDefaultValue(0);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => new { x.UserId, x.PeriodKey })
                .IsUnique()
                .HasDatabaseName("uq_usage_summary_user_period");

            e.HasOne(x => x.User)
                .WithMany(u => u.UsageSummaries)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.ToTable(t =>
                t.HasCheckConstraint(
                    "ck_usage_summary_period_kind",
                    $"period_kind IN ({InClause(PeriodKinds.All)})"));
        });
    }

    private static void ConfigureCreditLedger(ModelBuilder b)
    {
        b.Entity<CreditLedgerEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(32).IsRequired();
            e.Property(x => x.Meta).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => new { x.UserId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_credit_ledger_user_id_created_at");
            e.HasIndex(x => x.CreatedAt)
                .IsDescending()
                .HasDatabaseName("ix_credit_ledger_created_at");

            e.HasOne(x => x.User)
                .WithMany(u => u.CreditLedger)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.ToTable(t =>
                t.HasCheckConstraint(
                    "ck_credit_ledger_reason",
                    $"reason IN ({InClause(CreditLedgerReasons.All)})"));
        });
    }

    private static void ConfigurePaymentOrders(ModelBuilder b)
    {
        b.Entity<PaymentOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OrderCode).HasMaxLength(16).IsRequired();
            e.Property(x => x.Plan).HasMaxLength(32).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16).HasDefaultValue(PaymentOrderStatuses.Pending).IsRequired();
            e.Property(x => x.PaymentMethod).HasMaxLength(16).HasDefaultValue("bank_transfer").IsRequired();
            e.Property(x => x.BankRef).HasMaxLength(120);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => x.OrderCode).IsUnique().HasDatabaseName("uq_payment_orders_order_code");
            e.HasIndex(x => new { x.UserId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_payment_orders_user_id_created_at");
            e.HasIndex(x => x.Status).HasDatabaseName("ix_payment_orders_status");

            e.HasOne(x => x.User)
                .WithMany(u => u.PaymentOrders)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ApprovedByUser)
                .WithMany()
                .HasForeignKey(x => x.ApprovedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_payment_orders_status",
                    $"status IN ({InClause(PaymentOrderStatuses.All)})");
                t.HasCheckConstraint("ck_payment_orders_amount_positive", "amount_vnd > 0");
            });
        });
    }

    private static void ConfigureAuditLog(ModelBuilder b)
    {
        b.Entity<AuditLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.Ip).HasMaxLength(45);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.Property(x => x.Meta).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => new { x.ActorUserId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_audit_log_actor_user_id_created_at");
            e.HasIndex(x => new { x.TargetUserId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_audit_log_target_user_id_created_at");
            e.HasIndex(x => new { x.Action, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_audit_log_action_created_at");

            e.HasOne(x => x.ActorUser)
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TargetUser)
                .WithMany()
                .HasForeignKey(x => x.TargetUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureDeletionRequests(ModelBuilder b)
    {
        b.Entity<DeletionRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Status).HasMaxLength(16).HasDefaultValue(DeletionRequestStatuses.Pending).IsRequired();
            e.Property(x => x.RequestedAt).HasDefaultValueSql("now()");
            e.Property(x => x.Reason).HasMaxLength(500);

            e.HasIndex(x => new { x.Status, x.ScheduledFor })
                .HasDatabaseName("ix_deletion_requests_status_scheduled_for");
            e.HasIndex(x => x.UserId)
                .IsUnique()
                .HasFilter("status = 'pending'")
                .HasDatabaseName("uq_deletion_requests_user_pending");

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.ToTable(t =>
                t.HasCheckConstraint(
                    "ck_deletion_requests_status",
                    $"status IN ({InClause(DeletionRequestStatuses.All)})"));
        });
    }

    private static string InClause(string[] values) =>
        string.Join(", ", values.Select(v => $"'{v}'"));
}
