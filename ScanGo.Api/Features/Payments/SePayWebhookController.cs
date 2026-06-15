using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ScanGo.Api.Features.Payments;

/// <summary>
/// Receives SePay transaction webhooks. Anonymous (SePay can't carry a JWT) but
/// gated by the shared API key SePay sends in the Authorization header.
/// </summary>
[ApiController]
[Route("api/payments/webhook")]
[AllowAnonymous]
public class SePayWebhookController(
    IPaymentService payments, IOptions<PaymentOptions> options) : ControllerBase
{
    [HttpPost("sepay")]
    public async Task<IActionResult> SePay([FromBody] SePayWebhook payload, CancellationToken ct)
    {
        var key = options.Value.SePayApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            // SePay sends "Authorization: Apikey <key>"; accept a bare key too.
            var auth = Request.Headers.Authorization.ToString();
            if (auth != $"Apikey {key}" && auth != key)
                return Unauthorized();
        }

        var result = await payments.ProcessSePayAsync(payload, ct);
        // Always 200 so SePay doesn't retry endlessly on business-level no-ops.
        return Ok(new { success = true, result = result.ToString() });
    }
}
