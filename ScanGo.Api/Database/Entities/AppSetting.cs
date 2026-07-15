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

    // Read-aloud (TTS). false = use the real Google Cloud TTS; true = "mock"
    // (no server voice → client falls back to the browser's built-in voice).
    // Defaults false because GoogleTtsService degrades gracefully without a key.
    public bool TtsMock { get; set; } = false;

    // Đưa tool Google Search cho Gemini để nó tự tra cứu & trích nguồn khi cần.
    // Mặc định BẬT. Tắt nếu muốn cắt phí grounding (tính theo lượt tra cứu).
    public bool SearchGrounding { get; set; } = true;

    // Free-tier quota per ISO week (admin-editable).
    public int FreeWeeklyScans { get; set; } = 3;
    public int FreeWeeklyAsks { get; set; } = 5;

    public DateTime UpdatedAt { get; set; }
}
