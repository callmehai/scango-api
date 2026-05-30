namespace ScanGo.Api.Features.Auth;

public class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    /// <summary>OAuth client ID(s) — accept multiple to support web + iOS + Android client IDs.</summary>
    public string[] ClientIds { get; set; } = [];
}
