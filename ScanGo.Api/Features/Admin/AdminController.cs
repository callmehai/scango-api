using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Common;
using ScanGo.Api.Database;
using ScanGo.Api.Database.Entities;

namespace ScanGo.Api.Features.Admin;

/// <summary>
/// Admin-only runtime configuration. Changes persist to the DB and refresh the
/// in-memory <see cref="RuntimeSettings"/> cache so they apply without a restart.
/// </summary>
[ApiController]
[Route("api/admin")]
// Admin + tester can read settings; only admin can change them (PATCH below).
[Authorize(Roles = UserRoles.Admin + "," + UserRoles.Tester)]
public class AdminController(ScanGoDbContext db, RuntimeSettings settings) : ControllerBase
{
    public static readonly string[] AllowedModels =
        ["gemini-2.5-flash", "gemini-2.5-flash-lite"];

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var s = settings.Current;
        return Ok(new
        {
            geminiModel = s.GeminiModel,
            aiMock = s.AiMock,
            ocrMock = s.OcrMock,
            ttsMock = s.TtsMock,
            freeWeeklyScans = s.FreeWeeklyScans,
            freeWeeklyAsks = s.FreeWeeklyAsks,
            availableModels = AllowedModels,
        });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateSettingsRequest req, CancellationToken ct)
    {
        if (req.GeminiModel is not null && !AllowedModels.Contains(req.GeminiModel))
            return BadRequest(new { code = "InvalidModel", message = "Model không hợp lệ." });

        var row = await db.AppSettings.FindAsync([AppSetting.SingletonId], ct);
        if (row is null)
        {
            row = new AppSetting { Id = AppSetting.SingletonId };
            db.AppSettings.Add(row);
        }

        if (req.GeminiModel is not null) row.GeminiModel = req.GeminiModel;
        if (req.AiMock is not null) row.AiMock = req.AiMock.Value;
        if (req.OcrMock is not null) row.OcrMock = req.OcrMock.Value;
        if (req.TtsMock is not null) row.TtsMock = req.TtsMock.Value;
        if (req.FreeWeeklyScans is not null) row.FreeWeeklyScans = Math.Max(0, req.FreeWeeklyScans.Value);
        if (req.FreeWeeklyAsks is not null) row.FreeWeeklyAsks = Math.Max(0, req.FreeWeeklyAsks.Value);
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        settings.Set(new SettingsSnapshot(
            row.GeminiModel, row.AiMock, row.OcrMock, row.TtsMock,
            row.FreeWeeklyScans, row.FreeWeeklyAsks));

        return Ok(new
        {
            geminiModel = row.GeminiModel,
            aiMock = row.AiMock,
            ocrMock = row.OcrMock,
            ttsMock = row.TtsMock,
            freeWeeklyScans = row.FreeWeeklyScans,
            freeWeeklyAsks = row.FreeWeeklyAsks,
            availableModels = AllowedModels,
        });
    }
}

public class UpdateSettingsRequest
{
    public string? GeminiModel { get; set; }
    public bool? AiMock { get; set; }
    public bool? OcrMock { get; set; }
    public bool? TtsMock { get; set; }
    public int? FreeWeeklyScans { get; set; }
    public int? FreeWeeklyAsks { get; set; }
}
