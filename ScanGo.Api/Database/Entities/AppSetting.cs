namespace ScanGo.Api.Database.Entities;

/// <summary>
/// Single-row table holding admin-editable runtime settings (AI model + mock
/// toggles). Always one row, keyed by <see cref="SingletonId"/>.
/// </summary>
public class AppSetting
{
    public static readonly Guid SingletonId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SingletonId;
    public string GeminiModel { get; set; } = "gemini-2.5-flash-lite";
    public bool AiMock { get; set; } = true;
    public bool OcrMock { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}
