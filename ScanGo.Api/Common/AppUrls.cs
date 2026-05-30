namespace ScanGo.Api.Common;

/// <summary>
/// Base URLs the API references in outgoing emails (verify links, reset links).
/// In prod these point at the public web app (eg. https://scango.vn).
/// </summary>
public class AppUrlsOptions
{
    public const string SectionName = "AppUrls";

    public string Web { get; set; } = "http://localhost:5173";
    public string VerifyEmailPath { get; set; } = "/verify-email";
    public string ResetPasswordPath { get; set; } = "/reset-password";

    public string VerifyEmailLink(string token) =>
        $"{Web.TrimEnd('/')}{VerifyEmailPath}?token={Uri.EscapeDataString(token)}";

    public string ResetPasswordLink(string token) =>
        $"{Web.TrimEnd('/')}{ResetPasswordPath}?token={Uri.EscapeDataString(token)}";
}
