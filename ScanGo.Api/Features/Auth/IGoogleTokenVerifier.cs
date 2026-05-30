namespace ScanGo.Api.Features.Auth;

public record GoogleProfile(string Sub, string Email, string Name);

public interface IGoogleTokenVerifier
{
    /// <summary>
    /// Verify a Google ID token. Returns null if invalid (bad signature, wrong
    /// audience, expired, unverified email).
    /// </summary>
    Task<GoogleProfile?> VerifyAsync(string idToken, CancellationToken ct);
}
