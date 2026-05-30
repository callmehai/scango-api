namespace ScanGo.Api.Features.Email;

public interface IEmailService
{
    Task SendVerificationAsync(
        string toEmail, string toName, string verifyLink, CancellationToken ct);

    Task SendPasswordResetAsync(
        string toEmail, string toName, string resetLink, CancellationToken ct);
}
