using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScanGo.Api.Common;

namespace ScanGo.Api.Features.Payments;

/// <summary>User-facing checkout: create an order, then poll its status.</summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController(IPaymentService payments) : ControllerBase
{
    [HttpPost("orders")]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest req, CancellationToken ct)
    {
        var (view, error) = await payments.CreateOrderAsync(User.RequireUserId(), req.Plan, ct);
        return error switch
        {
            CreateOrderError.InvalidPlan =>
                BadRequest(new { code = "InvalidPlan", message = "Gói không hợp lệ." }),
            CreateOrderError.NotConfigured =>
                StatusCode(503, new { code = "PaymentNotConfigured", message = "Thanh toán chưa được cấu hình." }),
            _ => Ok(view),
        };
    }

    [HttpGet("orders/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var view = await payments.GetForUserAsync(User.RequireUserId(), id, ct);
        return view is null ? NotFound() : Ok(view);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        Ok(await payments.ListForUserAsync(User.RequireUserId(), ct));
}
