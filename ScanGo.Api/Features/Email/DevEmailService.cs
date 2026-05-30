using System.Collections.Concurrent;

namespace ScanGo.Api.Features.Email;

/// <summary>
/// Dev/test implementation — logs to console + remembers last messages in
/// memory so integration tests can read them back without a real SMTP/Resend.
/// </summary>
public class DevEmailService(ILogger<DevEmailService> log) : IEmailService
{
    private static readonly ConcurrentDictionary<string, SentEmail> _lastByRecipient = new();

    public Task SendVerificationAsync(
        string toEmail, string toName, string verifyLink, CancellationToken ct)
    {
        var msg = new SentEmail("verification", toEmail, toName, verifyLink, DateTime.UtcNow);
        _lastByRecipient[toEmail.ToLowerInvariant()] = msg;
        log.LogInformation(
            "[DEV EMAIL] verify → {Email} ({Name}) link={Link}", toEmail, toName, verifyLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string toEmail, string toName, string resetLink, CancellationToken ct)
    {
        var msg = new SentEmail("password_reset", toEmail, toName, resetLink, DateTime.UtcNow);
        _lastByRecipient[toEmail.ToLowerInvariant()] = msg;
        log.LogInformation(
            "[DEV EMAIL] reset → {Email} ({Name}) link={Link}", toEmail, toName, resetLink);
        return Task.CompletedTask;
    }

    /// <summary>Test hook — read the last email sent to a recipient.</summary>
    public static SentEmail? GetLast(string toEmail) =>
        _lastByRecipient.TryGetValue(toEmail.ToLowerInvariant(), out var e) ? e : null;

    /// <summary>Test hook — clear the in-memory inbox between tests.</summary>
    public static void Clear() => _lastByRecipient.Clear();

    public record SentEmail(
        string Kind, string Email, string Name, string Link, DateTime SentAt);
}
