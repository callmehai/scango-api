namespace ScanGo.Api.Features.Ai;

public class AiOptions
{
    public const string SectionName = "Ai";

    public string? GeminiApiKey { get; set; }
    public string GeminiModel { get; set; } = "gemini-2.0-flash";
    public bool Mock { get; set; } = true;
}
