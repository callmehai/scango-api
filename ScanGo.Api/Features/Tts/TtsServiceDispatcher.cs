using ScanGo.Api.Common;

namespace ScanGo.Api.Features.Tts;

/// <summary>
/// Picks the real (Google) or null TTS implementation per call based on the live
/// <c>TtsMock</c> runtime flag — so an admin can turn server-side read-aloud
/// on/off without a restart. NullTtsService returns null → endpoint 503 → the
/// client falls back to the browser's built-in voice.
/// </summary>
public class TtsServiceDispatcher(
    RuntimeSettings settings,
    NullTtsService mock,
    GoogleTtsService real) : ITtsService
{
    public Task<byte[]?> SynthesizeAsync(string text, string targetLang, CancellationToken ct) =>
        (settings.Current.TtsMock ? (ITtsService)mock : real)
            .SynthesizeAsync(text, targetLang, ct);
}
