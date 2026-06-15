namespace ScanGo.Api.Features.Payments;

/// <summary>
/// Builds a VietQR (Napas standard) quick-link image URL for a transfer into the
/// configured account, with the amount and memo embedded so the payer's banking
/// app pre-fills them. The money goes straight to the account — VietQR is just an
/// encoding standard, not a payment intermediary.
/// </summary>
public static class VietQr
{
    public static string ImageUrl(PaymentOptions o, long amountVnd, string memo) =>
        $"https://img.vietqr.io/image/{o.BankBin}-{o.AccountNo}-compact2.png"
        + $"?amount={amountVnd}"
        + $"&addInfo={Uri.EscapeDataString(memo)}"
        + $"&accountName={Uri.EscapeDataString(o.AccountHolder)}";
}
