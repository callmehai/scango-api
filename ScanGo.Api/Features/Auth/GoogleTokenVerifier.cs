using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace ScanGo.Api.Features.Auth;

public class GoogleTokenVerifier(
    IOptions<GoogleAuthOptions> options,
    ILogger<GoogleTokenVerifier> log) : IGoogleTokenVerifier
{
    private readonly GoogleAuthOptions _opts = options.Value;

    public async Task<GoogleProfile?> VerifyAsync(string idToken, CancellationToken ct)
    {
        if (_opts.ClientIds.Length == 0)
        {
            log.LogWarning(
                "GoogleAuth:ClientIds is empty — refusing all tokens until configured.");
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = _opts.ClientIds,
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            if (!payload.EmailVerified)
            {
                log.LogInformation("Google login rejected — email not verified.");
                return null;
            }

            return new GoogleProfile(payload.Subject, payload.Email, payload.Name ?? "");
        }
        catch (InvalidJwtException ex)
        {
            log.LogInformation("Google id token rejected: {Message}", ex.Message);
            return null;
        }
    }
}
