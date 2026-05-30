using ScanGo.Api.Common;

namespace ScanGo.Api.Features.Ai;

/// <summary>
/// Picks the mock or real Gemini implementation per call based on the current
/// runtime <c>AiMock</c> flag — so an admin can flip mock on/off live without a
/// restart. (Replaces the old startup-time DI swap.)
/// </summary>
public class GeminiServiceDispatcher(
    RuntimeSettings settings,
    MockGeminiService mock,
    GeminiHttpService real) : IGeminiService
{
    public IAsyncEnumerable<string> StreamAsync(
        string prompt, UsageBox usage, CancellationToken ct) =>
        (settings.Current.AiMock ? (IGeminiService)mock : real)
            .StreamAsync(prompt, usage, ct);
}
