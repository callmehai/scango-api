using System.Security.Cryptography;

namespace ScanGo.Api.Features.Payments;

/// <summary>
/// Generates short, copy-friendly order codes used as the transfer memo. The
/// SePay webhook matches incoming transfers back to an order by this code, so it
/// must survive being read off a bank statement — hence no ambiguous characters.
/// </summary>
public static class OrderCode
{
    private const string Prefix = "SCAN";
    // No 0/O/1/I to avoid copy mistakes.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // Regex-friendly: matches Prefix + uppercase alnum. Used by the webhook parser.
    public const string Pattern = @"SCAN[0-9A-Z]{6}";

    public static string New()
    {
        Span<char> buf = stackalloc char[6];
        for (var i = 0; i < buf.Length; i++)
            buf[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return Prefix + new string(buf);
    }
}
