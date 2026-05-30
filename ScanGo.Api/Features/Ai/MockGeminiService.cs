using System.Runtime.CompilerServices;

namespace ScanGo.Api.Features.Ai;

/// <summary>
/// Deterministic streaming response for dev/test. Yields a predictable
/// TITLE-prefixed answer so tests can assert title extraction + token billing.
/// </summary>
public class MockGeminiService : IGeminiService
{
    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        UsageBox usage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var canned = new[]
        {
            "TITLE: Dầu gội Head & Shoulders trị gàu\n",
            "\n",
            "Đây là sản phẩm dầu gội ", "trị gàu của Head & Shoulders. ",
            "Thành phần chính là Pyrithione zinc (1%) ",
            "có tác dụng kháng nấm và giảm gàu.",
        };

        foreach (var chunk in canned)
        {
            ct.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Delay(5, ct);
        }

        // Roughly mimic token usage so quota tests have something to assert on
        usage.Usage = new AiTokenUsage(
            InputTokens: Math.Max(50, prompt.Length / 4),
            OutputTokens: canned.Sum(c => c.Length / 4));
    }
}
