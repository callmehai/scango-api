namespace ScanGo.Api.Features.Tts;

public interface ITtsService
{
    /// <summary>
    /// Synthesize <paramref name="text"/> to MP3 bytes in the conversation's
    /// target language (our 3-letter code, e.g. "vnm"/"eng"/"jpn"). Returns null
    /// when no provider/key is configured or synthesis fails.
    /// </summary>
    Task<byte[]?> SynthesizeAsync(string text, string targetLang, CancellationToken ct);
}

/// <summary>
/// Fallback used when no TTS provider is configured. The endpoint then returns
/// 503 and the client falls back to the browser's built-in speech synthesis.
/// </summary>
public class NullTtsService : ITtsService
{
    public Task<byte[]?> SynthesizeAsync(string text, string targetLang, CancellationToken ct)
        => Task.FromResult<byte[]?>(null);
}
