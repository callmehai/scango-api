namespace ScanGo.Api.Features.Storage;

/// <summary>
/// Pluggable blob storage. Local impl writes under /tmp for dev; R2 impl will
/// land in a later PR (S3-compatible, AWSSDK.S3).
/// </summary>
public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<(Stream stream, string contentType)?> GetAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    bool Exists(string key);
}
