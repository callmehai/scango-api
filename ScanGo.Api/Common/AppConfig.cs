namespace ScanGo.Api.Common;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Symmetric key for HS256 signing. Min 32 chars. Must be set via env in prod.</summary>
    public string JwtSecret { get; set; } = "";

    public string JwtIssuer { get; set; } = "scango-api";
    public string JwtAudience { get; set; } = "scango";

    /// <summary>Access token lifetime. Short — refresh token handles long-lived sessions.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh token lifetime (days). Single-use rotation on refresh.</summary>
    public int RefreshTokenDays { get; set; } = 30;
}
