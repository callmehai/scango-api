using System.Runtime.CompilerServices;

namespace ScanGo.Api.Features.Ai;

/// <summary>
/// Deterministic streaming response for dev/test. Yields a predictable
/// TITLE-prefixed answer so tests can assert title extraction + token billing.
/// </summary>
public class MockGeminiService : IGeminiService
{
    // A long, multi-paragraph markdown answer so the streaming UI can be tested
    // for smoothness, auto-scroll and markdown rendering. NOT a real result.
    private const string Body =
        "🔧 **Đây là phản hồi GIẢ LẬP** (Mock AI đang bật) — không phải kết quả " +
        "thật từ Gemini, nên nội dung bên dưới chỉ để **kiểm thử giao diện** chứ " +
        "không liên quan tới ảnh bạn đã quét.\n\n" +
        "Để dùng AI thật, vào trang **Quản trị → tắt \"Mock AI\"**.\n\n" +
        "## Một số điều mình (giả vờ) phân tích được\n\n" +
        "1. **Bố cục văn bản** — đoạn văn, tiêu đề và danh sách được tách rõ ràng " +
        "để bạn xem markdown render có đẹp không.\n" +
        "2. **Khả năng đọc** — cỡ chữ, khoảng cách dòng và màu nền bong bóng cần " +
        "dễ nhìn trong cả chế độ sáng lẫn tối.\n" +
        "3. **Hiệu ứng stream** — chữ phải hiện ra mượt mà, từng chút một, và khung " +
        "chat tự cuộn xuống cuối khi có nội dung mới.\n\n" +
        "### Ví dụ một đoạn dài hơn\n\n" +
        "Khi bạn dùng AI thật, trợ lý sẽ đọc nội dung trong ảnh (hoá đơn, thực đơn, " +
        "biển báo, tài liệu...) rồi giải thích, dịch hoặc trả lời câu hỏi của bạn " +
        "bằng ngôn ngữ bạn chọn. Bạn có thể hỏi tiếp nhiều lần trong cùng một hội " +
        "thoại, và trợ lý sẽ nhớ ngữ cảnh các câu trước để trả lời mạch lạc hơn.\n\n" +
        "> 💡 Mẹo: chụp ảnh rõ nét, đủ sáng và không bị nghiêng để AI đọc chính xác nhất.\n\n" +
        "Cảm ơn bạn đã thử ScanGo! Khi tắt Mock AI, đây sẽ là câu trả lời thật từ Gemini.";

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        UsageBox usage,
        string targetLang,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _ = targetLang;   // mock không tra cứu nên không có mục nguồn để dịch

        // Title line first (extracted by TitleExtractor, stripped from the bubble).
        yield return "TITLE: [DEMO] Phản hồi giả lập\n\n";

        // Stream word-by-word so the FE receives many small chunks at a natural
        // pace — this is what a real token stream looks like.
        var words = Body.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return i == 0 ? words[i] : " " + words[i];
            await Task.Delay(18, ct);
        }

        // Roughly mimic token usage so quota tests have something to assert on
        usage.Usage = new AiTokenUsage(
            InputTokens: Math.Max(50, prompt.Length / 4),
            OutputTokens: Body.Length / 4);
    }
}
