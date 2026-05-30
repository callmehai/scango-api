using System.Security.Cryptography;
using System.Text;

namespace ScanGo.Api.Common;

/// <summary>
/// SHA-256 hashing for opaque tokens (refresh tokens, email verification,
/// password reset). We store the hash in DB so a DB leak doesn't expose
/// usable tokens. Fast (no salt needed for high-entropy random tokens).
/// </summary>
public static class TokenHasher
{
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Generate a cryptographically secure random token (base64url, no padding).</summary>
    public static string GenerateRandom(int bytes = 32)
    {
        var buf = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buf)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
