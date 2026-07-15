namespace ScanGo.Api.Features.Ai;

public record AiTokenUsage(int InputTokens, int OutputTokens);

public interface IGeminiService
{
    /// <summary>
    /// Streams completion chunks. Caller should `await foreach`. The last
    /// "completion" tracker may surface via Usage once the stream finishes,
    /// readable via UsageObserver passed in.
    /// <paramref name="targetLang"/> = ngôn ngữ câu trả lời (mã kiểu "vnm"), dùng
    /// để dịch tiêu đề mục nguồn khi model có tra cứu web.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string prompt,
        UsageBox usage,
        string targetLang,
        CancellationToken ct);
}

/// <summary>Mutable container for token usage; updated when the stream finishes.</summary>
public class UsageBox
{
    public AiTokenUsage Usage { get; set; } = new(0, 0);
}
