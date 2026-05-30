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
            "TITLE: [DEMO] Phản hồi giả lập\n",
            "\n",
            "🔧 Đây là phản hồi ", "GIẢ LẬP (Mock AI đang bật) — ",
            "không phải kết quả thật từ Gemini nên nội dung không liên quan tới ảnh. ",
            "Tắt \"Mock AI\" trong trang Quản trị để dùng AI thật.",
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
