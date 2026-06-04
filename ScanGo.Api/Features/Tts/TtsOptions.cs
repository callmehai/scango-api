namespace ScanGo.Api.Features.Tts;

public class TtsOptions
{
    public const string SectionName = "Tts";

    /// <summary>Google Cloud Text-to-Speech API key (Cloud project with the
    /// Text-to-Speech API enabled). Distinct from the Gemini key.</summary>
    public string? GoogleApiKey { get; set; }

    /// <summary>When true (or no key), TTS is disabled and the endpoint returns
    /// 503 so the client falls back to the browser's speech synthesis.</summary>
    public bool Mock { get; set; } = true;
}
