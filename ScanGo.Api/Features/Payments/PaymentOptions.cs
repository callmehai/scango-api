namespace ScanGo.Api.Features.Payments;

/// <summary>
/// Bank + payment-gateway config for the QR checkout. Bound from the "Payment"
/// section (appsettings / env). The account here is where the money actually
/// lands — VietQR just encodes a transfer to it; nothing intermediates the funds.
/// </summary>
public class PaymentOptions
{
    public const string SectionName = "Payment";

    /// Napas bank BIN, e.g. "970418" (BIDV), "970436" (Vietcombank). Drives VietQR.
    public string BankBin { get; set; } = "";

    /// Human-readable bank name shown next to the QR, e.g. "BIDV".
    public string BankName { get; set; } = "";

    public string AccountNo { get; set; } = "";
    public string AccountHolder { get; set; } = "";

    /// Shared secret SePay sends in the webhook Authorization header
    /// ("Apikey &lt;key&gt;"). Webhook is rejected if this is set and doesn't match.
    public string? SePayApiKey { get; set; }

    /// How long a pending order stays payable before it's considered expired.
    public int OrderTtlMinutes { get; set; } = 60;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BankBin) && !string.IsNullOrWhiteSpace(AccountNo);
}
