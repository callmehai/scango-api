namespace ScanGo.Api.Features.Storage;

/// <summary>
/// Cloudflare R2 (S3-compatible) settings. When all four values are present the
/// app uses R2 for image storage; otherwise it falls back to LocalObjectStorage.
/// Bind from the "R2" config section (env: R2__AccountId, R2__AccessKeyId, ...).
/// </summary>
public class R2Options
{
    public const string SectionName = "R2";

    /// <summary>Cloudflare account id — forms the endpoint https://&lt;id&gt;.r2.cloudflarestorage.com.</summary>
    public string? AccountId { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? Bucket { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(Bucket);
}
