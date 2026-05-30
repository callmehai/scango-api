namespace ScanGo.Api.Features.Auth;

public enum PasswordValidationResult
{
    Ok,
    TooShort,
    NeedsLetterAndDigit,
}

public static class PasswordHasher
{
    public const int MinLength = 8;
    public const int BcryptWorkFactor = 12;       // ~250ms hash time on commodity HW

    public static PasswordValidationResult Validate(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return PasswordValidationResult.TooShort;

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        if (!hasLetter || !hasDigit)
            return PasswordValidationResult.NeedsLetterAndDigit;

        return PasswordValidationResult.Ok;
    }

    public static string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);

    public static bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;                          // malformed hash -> treat as mismatch
        }
    }

    // A real bcrypt hash (same work factor) to verify against when the account is
    // absent or has no password (Google-only). Computed once at first use. Running a
    // full verify in that path keeps login timing constant, so an attacker can't tell
    // an existing email from a non-existent one by response latency (user enumeration).
    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword("not-a-real-password", BcryptWorkFactor);

    public static void BurnVerify(string password)
    {
        try { BCrypt.Net.BCrypt.Verify(password, DummyHash); }
        catch { /* timing side-effect only; result discarded */ }
    }
}
