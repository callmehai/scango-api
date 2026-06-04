using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Ai;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Features.Conversations;
using ScanGo.Api.Features.Email;
using ScanGo.Api.Features.Metering;
using ScanGo.Api.Features.Me;
using ScanGo.Api.Features.Ocr;
using ScanGo.Api.Features.Storage;
using ScanGo.Api.Features.Tts;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// Services
// ============================================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// ============================================================================
// CORS — allow web dev server + production web origin.
// Tightened in prod via Cors:Origins config (comma-separated whitelist).
// ============================================================================
const string CorsPolicy = "scango-web";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
{
    var configured = builder.Configuration["Cors:Origins"];
    var origins = string.IsNullOrWhiteSpace(configured)
        ? new[] { "http://localhost:5173", "http://localhost:5174" }
        : configured.Split(',', StringSplitOptions.RemoveEmptyEntries
                              | StringSplitOptions.TrimEntries);

    p.WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Content-Disposition");
}));

// Options pattern — bind from "Auth" + "AppUrls" sections. Read lazily so test
// fixtures can override via ConfigureAppConfiguration after default sources.
builder.Services.Configure<AuthOptions>(
    builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<AppUrlsOptions>(
    builder.Configuration.GetSection(AppUrlsOptions.SectionName));
builder.Services.Configure<GoogleAuthOptions>(
    builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<LocalStorageOptions>(
    builder.Configuration.GetSection(LocalStorageOptions.SectionName));
builder.Services.Configure<R2Options>(
    builder.Configuration.GetSection(R2Options.SectionName));
builder.Services.Configure<OcrOptions>(
    builder.Configuration.GetSection(OcrOptions.SectionName));
builder.Services.Configure<AiOptions>(
    builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<TtsOptions>(
    builder.Configuration.GetSection(TtsOptions.SectionName));

// DbContext — connection string read LAZILY from IConfiguration so test
// fixtures' InMemoryCollection overrides apply correctly.
builder.Services.AddDbContext<ScanGoDbContext>((sp, opts) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    // NB: check IsNullOrWhiteSpace, not ?? — appsettings.json ships an empty
    // "ConnectionStrings:Postgres" placeholder, which is non-null, so a plain
    // ?? would swallow it and never fall through to the DATABASE_URL env var.
    var connStr = cfg.GetConnectionString("Postgres");
    if (string.IsNullOrWhiteSpace(connStr))
        connStr = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrWhiteSpace(connStr))
        throw new InvalidOperationException(
            "Connection string 'Postgres' (or DATABASE_URL env) is required.");
    opts.UseNpgsql(connStr).UseSnakeCaseNamingConvention();
    // Our First/FirstOrDefault calls all filter on unique keys (PK id, or
    // user_id+period_key), so the result is deterministic — silence EF's
    // "row-limiting operator without OrderBy" warning (10103) it can't infer.
    opts.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId
            .RowLimitingOperationWithoutOrderByWarning));
});

// Auth services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();
builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
// Image storage — use Cloudflare R2 when the R2__* env vars are set, else fall
// back to the local filesystem (dev/test). Both implement IObjectStorage so the
// rest of the app is unaware which backend is active.
builder.Services.AddSingleton<IObjectStorage>(sp =>
{
    var r2 = sp.GetRequiredService<IOptions<R2Options>>().Value;
    return r2.IsConfigured
        ? ActivatorUtilities.CreateInstance<R2ObjectStorage>(sp)
        : ActivatorUtilities.CreateInstance<LocalObjectStorage>(sp);
});
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IQuotaService, QuotaService>();

// Background maintenance: grace-period account hard-deletes (quota itself
// auto-resets via ISO-week period keys, no job needed).
builder.Services.AddHostedService<WeeklyMaintenanceService>();

// OCR + Gemini — both mock and real impls are registered; a per-request
// dispatcher picks which to use based on the live RuntimeSettings flags, so an
// admin can toggle mock on/off (and switch model) at runtime without a restart.
builder.Services.AddSingleton<RuntimeSettings>();
builder.Services.AddHttpClient<OcrSpaceService>();
builder.Services.AddHttpClient<GeminiHttpService>();
builder.Services.AddScoped<MockOcrService>();
builder.Services.AddScoped<MockGeminiService>();
builder.Services.AddScoped<IOcrService, OcrServiceDispatcher>();
builder.Services.AddScoped<IGeminiService, GeminiServiceDispatcher>();

// Text-to-speech (read AI answers aloud). A per-request dispatcher picks the
// real Google Cloud TTS or NullTtsService based on the live TtsMock flag, so an
// admin can toggle it without a restart. Null → endpoint 503 → client falls back
// to the browser's built-in voice (also the path when no key is configured).
builder.Services.AddHttpClient<GoogleTtsService>();
builder.Services.AddScoped<NullTtsService>();
builder.Services.AddScoped<ITtsService, TtsServiceDispatcher>();

// Email — DevEmailService logs + remembers in memory. Swap for ResendEmailService later.
builder.Services.AddScoped<IEmailService, DevEmailService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// JwtBearerOptions configured LAZILY (PostConfigure) so test fixtures can
// override Auth:* values before the options are materialised.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((jwt, authOptsAccessor) =>
    {
        var auth = authOptsAccessor.Value;
        if (string.IsNullOrWhiteSpace(auth.JwtSecret) || auth.JwtSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:JwtSecret must be set (min 32 chars). Configure via appsettings or env Auth__JwtSecret.");
        }

        jwt.MapInboundClaims = false;             // keep "sub" / "email" as-is
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = auth.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = auth.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(auth.JwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = CurrentUserExtensions.EmailClaim,
            RoleClaimType = CurrentUserExtensions.RoleClaim,
        };
    });

builder.Services.AddAuthorization();

// ============================================================================
// Rate limiting — keyed per remote IP. Different buckets for auth-sensitive
// endpoints. Read live config inside the partition factory so test fixtures
// (DisableRateLimit=true) take effect even though they bind config late.
// ============================================================================

static string ClientIp(HttpContext ctx) =>
    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static bool IsRateLimitDisabled(HttpContext ctx) =>
    Environment.GetEnvironmentVariable("DISABLE_RATE_LIMIT") == "true"
    || ctx.RequestServices.GetRequiredService<IConfiguration>()
        .GetValue("DisableRateLimit", false);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 5 attempts / 15 min / IP — login, register, forgot-password, google
    o.AddPolicy("auth-strict", ctx =>
    {
        if (IsRateLimitDisabled(ctx))
            return RateLimitPartition.GetNoLimiter("noop");
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
            });
    });

    // 20 / min / IP — refresh, verify-email, resend
    o.AddPolicy("auth-loose", ctx =>
    {
        if (IsRateLimitDisabled(ctx))
            return RateLimitPartition.GetNoLimiter("noop");
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });

    // Global default — 100 req/min/IP
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        if (IsRateLimitDisabled(ctx))
            return RateLimitPartition.GetNoLimiter("noop");
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});

var app = builder.Build();

// ============================================================================
// Auto-migrate on startup (single-instance friendly).
// Toggle off via DB_AUTO_MIGRATE=false for multi-instance + dedicated migrator.
// ============================================================================

var autoMigrate = app.Configuration.GetValue("DbAutoMigrate", true);
var envFlag = Environment.GetEnvironmentVariable("DB_AUTO_MIGRATE");
if (bool.TryParse(envFlag, out var envParsed))
{
    autoMigrate = envParsed;
}

if (autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ScanGoDbContext>();
    await db.Database.MigrateAsync();
}

// Load admin-editable runtime settings into the in-memory cache. The row is
// seeded from env config on first run; thereafter the DB row (admin-controlled)
// is the source of truth, so changing env later won't override admin choices.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ScanGoDbContext>();
    var runtime = scope.ServiceProvider.GetRequiredService<RuntimeSettings>();
    var row = await db.AppSettings.FindAsync(AppSetting.SingletonId);
    if (row is null)
    {
        var ai = scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
        var ocr = scope.ServiceProvider.GetRequiredService<IOptions<OcrOptions>>().Value;
        var tts = scope.ServiceProvider.GetRequiredService<IOptions<TtsOptions>>().Value;
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        row = new AppSetting
        {
            Id = AppSetting.SingletonId,
            GeminiModel = string.IsNullOrWhiteSpace(ai.GeminiModel)
                ? "gemini-2.5-flash-lite"
                : ai.GeminiModel,
            AiMock = ai.Mock || string.IsNullOrWhiteSpace(ai.GeminiApiKey),
            OcrMock = ocr.Mock || string.IsNullOrWhiteSpace(ocr.OcrSpaceApiKey),
            TtsMock = tts.Mock || string.IsNullOrWhiteSpace(tts.GoogleApiKey),
            FreeWeeklyScans = cfg.GetValue("Quota:FreeWeeklyScans", 3),
            FreeWeeklyAsks = cfg.GetValue("Quota:FreeWeeklyAsks", 5),
            UpdatedAt = DateTime.UtcNow,
        };
        db.AppSettings.Add(row);
        await db.SaveChangesAsync();
    }
    runtime.Set(new SettingsSnapshot(
        row.GeminiModel, row.AiMock, row.OcrMock, row.TtsMock,
        row.FreeWeeklyScans, row.FreeWeeklyAsks));
}

// ============================================================================
// Pipeline
// ============================================================================

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ScanGo API" }));

app.Run();

// Expose Program class for WebApplicationFactory in tests
public partial class Program;
