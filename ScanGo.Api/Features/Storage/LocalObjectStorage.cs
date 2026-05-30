namespace ScanGo.Api.Features.Storage;

public class LocalStorageOptions
{
    public const string SectionName = "Storage:Local";
    public string Root { get; set; } = Path.Combine(Path.GetTempPath(), "scango-images");
}

/// <summary>
/// Filesystem-backed storage for dev/test. Keys are joined under Root with
/// path traversal prevention.
/// </summary>
public class LocalObjectStorage : IObjectStorage
{
    private readonly string _root;
    private readonly ILogger<LocalObjectStorage> _log;
    // Map of key -> contentType, kept beside file as a .meta sidecar (one-line text).
    // Crude but enough for dev — production R2 carries Content-Type natively.

    public LocalObjectStorage(
        Microsoft.Extensions.Options.IOptions<LocalStorageOptions> options,
        ILogger<LocalObjectStorage> log)
    {
        _root = options.Value.Root;
        Directory.CreateDirectory(_root);
        _log = log;
    }

    private string Resolve(string key)
    {
        var safe = key.Replace("..", "_").Replace('\\', '/');
        var path = Path.Combine(_root, safe);
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage key (path traversal).");
        return full;
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var fs = File.Create(path))
        {
            await content.CopyToAsync(fs, ct);
        }
        await File.WriteAllTextAsync(path + ".meta", contentType, ct);
        _log.LogDebug("Stored {Key} → {Path}", key, path);
    }

    public Task<(Stream stream, string contentType)?> GetAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) return Task.FromResult<(Stream, string)?>(null);

        var contentType = File.Exists(path + ".meta")
            ? File.ReadAllText(path + ".meta").Trim()
            : "application/octet-stream";

        Stream s = File.OpenRead(path);
        return Task.FromResult<(Stream, string)?>((s, contentType));
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
        return Task.CompletedTask;
    }

    public bool Exists(string key) => File.Exists(Resolve(key));
}
