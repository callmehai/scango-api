using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace ScanGo.Api.Features.Storage;

/// <summary>
/// Cloudflare R2 image storage via the S3-compatible API (AWSSDK.S3). Same
/// <see cref="IObjectStorage"/> contract as LocalObjectStorage, so swapping is a
/// config/DI change only — ConversationService is unaware which backend is used.
/// AmazonS3Client is thread-safe and intended to be reused → registered Singleton.
/// </summary>
public class R2ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public R2ObjectStorage(IOptions<R2Options> options)
    {
        var o = options.Value;
        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{o.AccountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,          // R2 prefers path-style addressing
            AuthenticationRegion = "auto",  // R2 ignores region but SigV4 needs one
        };
        _s3 = new AmazonS3Client(o.AccessKeyId, o.SecretAccessKey, config);
        _bucket = o.Bucket!;
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true,   // R2 doesn't support streaming SigV4 chunked payloads
        }, ct);
    }

    public async Task<(Stream stream, string contentType)?> GetAsync(string key, CancellationToken ct)
    {
        try
        {
            using var resp = await _s3.GetObjectAsync(_bucket, key, ct);
            // Buffer into memory so we can dispose the network response immediately.
            // Images are small (<1MB after optimisation) so this is fine.
            var ms = new MemoryStream();
            await resp.ResponseStream.CopyToAsync(ms, ct);
            ms.Position = 0;
            var contentType = string.IsNullOrWhiteSpace(resp.Headers.ContentType)
                ? "application/octet-stream"
                : resp.Headers.ContentType;
            return (ms, contentType);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await _s3.DeleteObjectAsync(_bucket, key, ct);
    }

    public bool Exists(string key)
    {
        try
        {
            _ = _s3.GetObjectMetadataAsync(_bucket, key).GetAwaiter().GetResult();
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
