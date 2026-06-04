using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScanGo.Api.Common;
using ScanGo.Api.Database.Entities;
using ScanGo.Api.Features.Metering;

namespace ScanGo.Api.Features.Conversations;

[ApiController]
[Route("api/conversations")]
[Authorize]
public class ConversationsController(
    IConversationService convos,
    IQuotaService quota) : ControllerBase
{
    [HttpPost("scan-create")]
    [RequestSizeLimit(15 * 1024 * 1024)]               // 15 MB hard cap on body
    public async Task<IActionResult> ScanCreate(
        [FromForm] IFormFile image,
        [FromForm] string topic,
        [FromForm] string rootLang,
        [FromForm] string targetLang,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { code = "NoImage", message = "Vui lòng chọn ảnh." });

        if (image.Length > ConversationService.MaxUploadBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { code = "ImageTooLarge", message = "Ảnh không được lớn hơn 10MB." });

        var plan = User.Plan() ?? PlanCodes.Free;
        var role = User.Role() ?? UserRoles.User;
        if (!await quota.TryConsumeAsync(User.RequireUserId(), plan, role, UsageKinds.Scan, ct))
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                code = "QuotaExceeded",
                message = "Bạn đã hết lượt quét miễn phí tuần này. Quota reset vào đầu tuần sau.",
            });

        await using var stream = image.OpenReadStream();
        var (dto, err) = await convos.CreateScanAsync(
            User.RequireUserId(), stream, image.ContentType ?? "image/jpeg",
            topic, rootLang, targetLang, ct);

        return err switch
        {
            null => Ok(dto),
            ConversationError.InvalidTopic =>
                BadRequest(new { code = "InvalidTopic", message = "Topic không hợp lệ." }),
            ConversationError.InvalidImage =>
                BadRequest(new { code = "InvalidImage", message = "Ảnh không hợp lệ hoặc không đọc được." }),
            _ => StatusCode(500),
        };
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 20,
        [FromQuery] string? topic = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        var page = await convos.ListAsync(User.RequireUserId(), skip, limit, topic, q, ct);
        return Ok(page);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOne(Guid id, CancellationToken ct)
    {
        var dto = await convos.GetAsync(User.RequireUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct)
    {
        var got = await convos.GetImageAsync(User.RequireUserId(), id, ct);
        if (got is null) return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(got.Value.stream, got.Value.contentType);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Rename(
        Guid id, [FromBody] RenameConversationRequest req, CancellationToken ct)
    {
        var err = await convos.RenameAsync(User.RequireUserId(), id, req.Title, ct);
        return err switch
        {
            null => Ok(new { ok = true, title = req.Title.Trim() }),
            ConversationError.NotFound => NotFound(),
            ConversationError.TitleEmpty =>
                BadRequest(new { code = "TitleEmpty", message = "Tiêu đề không được để trống." }),
            ConversationError.TitleTooLong =>
                BadRequest(new { code = "TitleTooLong", message = "Tiêu đề quá dài (tối đa 200 ký tự)." }),
            _ => StatusCode(500),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var err = await convos.DeleteAsync(User.RequireUserId(), id, ct);
        return err switch
        {
            null => Ok(new { ok = true }),
            ConversationError.NotFound => NotFound(),
            _ => StatusCode(500),
        };
    }

    [HttpPost("{id:guid}/speech")]
    public async Task<IActionResult> Speech(
        Guid id, [FromBody] SpeechRequest req, CancellationToken ct)
    {
        var (audio, found) = await convos.SpeakAsync(
            User.RequireUserId(), id, req.Text, ct);
        if (!found) return NotFound();
        if (audio is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "TtsUnavailable",
                message = "Tính năng đọc chưa được bật.",
            });

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(audio, "audio/mpeg");
    }

    [HttpPost("{id:guid}/scan-stream")]
    public async Task ScanStream(Guid id, CancellationToken ct)
    {
        await WriteSseAsync(
            convos.ScanStreamAsync(User.RequireUserId(), id, ct), ct);
    }

    [HttpPost("{id:guid}/ask-stream")]
    public async Task AskStream(
        Guid id, [FromBody] AskRequest req, CancellationToken ct)
    {
        var plan = User.Plan() ?? PlanCodes.Free;
        var role = User.Role() ?? UserRoles.User;
        if (!await quota.TryConsumeAsync(User.RequireUserId(), plan, role, UsageKinds.Ask, ct))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await Response.WriteAsJsonAsync(new
            {
                code = "QuotaExceeded",
                message = "Bạn đã hết lượt hỏi miễn phí tuần này.",
            }, ct);
            return;
        }

        await WriteSseAsync(
            convos.AskStreamAsync(User.RequireUserId(), id, req.Question, ct), ct);
    }

    /// <summary>
    /// Wire a `IAsyncEnumerable&lt;string&gt;` to the response as SSE
    /// `data: "<json-encoded chunk>"\n\n` events, finishing with `[DONE]`.
    /// Compatible with the FE's existing parser.
    /// </summary>
    private async Task WriteSseAsync(IAsyncEnumerable<string> source, CancellationToken ct)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in source.WithCancellation(ct))
            {
                if (ct.IsCancellationRequested) break;
                var payload = $"data: {JsonSerializer.Serialize(chunk)}\n\n";
                await Response.WriteAsync(payload, ct);
                await Response.Body.FlushAsync(ct);
            }
            await Response.WriteAsync("data: [DONE]\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // client disconnected — nothing to do
        }
        catch (Exception ex)
        {
            await Response.WriteAsync(
                $"data: {JsonSerializer.Serialize($"[ERROR] {ex.Message}")}\n\n",
                CancellationToken.None);
            await Response.Body.FlushAsync(CancellationToken.None);
        }
    }
}

public class AskRequest
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Question { get; set; } = "";
}

public class SpeechRequest
{
    // Google caps synthesize input at 5000 bytes; the service truncates further
    // if a multibyte answer exceeds it.
    [Required, StringLength(5000, MinimumLength = 1)]
    public string Text { get; set; } = "";
}
