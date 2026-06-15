using System.Text.Json.Serialization;

namespace ScanGo.Api.Features.Payments;

public class CreateOrderRequest
{
    public string Plan { get; set; } = "";
}

public class RefundRequest
{
    public string? Note { get; set; }
}

/// <summary>
/// SePay transaction webhook payload. Field names match SePay's JSON; we only
/// read the few we need. See https://docs.sepay.vn for the full shape.
/// </summary>
public class SePayWebhook
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("transferType")] public string? TransferType { get; set; } // "in" | "out"
    [JsonPropertyName("transferAmount")] public long TransferAmount { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }            // memo
    [JsonPropertyName("referenceCode")] public string? ReferenceCode { get; set; }
    [JsonPropertyName("gateway")] public string? Gateway { get; set; }
    [JsonPropertyName("accountNumber")] public string? AccountNumber { get; set; }
}

public enum WebhookResult { Matched, Unmatched, Duplicate, Ignored }

/// What the checkout screen + order history need. QR fields are only meaningful
/// while the order is still pending/payable.
public record OrderView(
    Guid Id,
    string OrderCode,
    string Plan,
    long AmountVnd,
    string Status,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    DateTime? PaidAt,
    string TransferContent,
    string QrImageUrl,
    string BankName,
    string AccountNo,
    string AccountHolder);

public enum CreateOrderError { InvalidPlan, NotConfigured }
