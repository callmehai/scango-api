namespace ScanGo.Api.Features.Ai;

/// <summary>
/// Topic-specific prompts. Port from the Node backend so behaviour stays the
/// same. Format rule: first non-empty line MUST be "TITLE: ..." in targetLang.
/// </summary>
public static class Prompts
{
    public static string ForScan(string ocrText, string targetLang, string topic)
    {
        var (role, instr) = TopicGuidance(topic);
        return $$"""
            Bạn là {{role}}.
            Người dùng đã chụp ảnh tài liệu và OCR trích ra văn bản dưới đây.
            Hãy trả lời bằng ngôn ngữ ISO {{targetLang}} (vnm=tiếng Việt, eng=English, ...).

            Yêu cầu định dạng:
            - Dòng đầu tiên PHẢI là: TITLE: <tiêu đề ngắn gọn, tối đa 80 ký tự, bằng ngôn ngữ {{targetLang}}>
            - Một dòng trống
            - Sau đó là phần trả lời tự nhiên cho người dùng

            Đừng dùng markdown JSON template, đừng thêm rào trước/sau, đừng bịa thông tin không có trong văn bản OCR.

            {{instr}}

            ===== VĂN BẢN OCR =====
            {{ocrText}}
            =======================
            """;
    }

    public static string ForChat(
        IEnumerable<(string Role, string Content)> history,
        string question,
        string targetLang)
    {
        var convo = string.Join("\n",
            history.Select(h => $"{h.Role}: {h.Content}"));
        return $$"""
            Bạn là trợ lý AI thân thiện. Trả lời bằng ngôn ngữ {{targetLang}}.
            Trả lời tự nhiên, không dùng markdown/JSON, không bịa thông tin.

            ===== LỊCH SỬ CUỘC TRÒ CHUYỆN =====
            {{convo}}
            user: {{question}}
            ===================================
            """;
    }

    private static (string Role, string Instr) TopicGuidance(string topic) => topic switch
    {
        "product" => (
            "chuyên gia phân tích sản phẩm tiêu dùng",
            "Hãy mô tả sản phẩm, công dụng, thành phần đáng chú ý, lưu ý an toàn nếu có."),
        "history" => (
            "nhà sử học",
            "Hãy giải thích bối cảnh lịch sử, ý nghĩa của tài liệu."),
        "place" => (
            "hướng dẫn viên du lịch",
            "Hãy giới thiệu về địa danh: vị trí, ý nghĩa, lưu ý cho du khách."),
        _ => (
            "trợ lý tổng quát",
            "Hãy tóm tắt và giải thích nội dung tài liệu một cách dễ hiểu."),
    };
}

public static class TitleExtractor
{
    /// <summary>
    /// Parse the first "TITLE: ..." line out of the AI response. Returns
    /// (title, remainingBody). If no title line, returns ("", originalText).
    /// </summary>
    public static (string Title, string Body) Extract(string fullText)
    {
        var newline = fullText.IndexOf('\n');
        if (newline < 0) return ("", fullText);

        var firstLine = fullText[..newline].Trim();
        if (!firstLine.StartsWith("TITLE:", StringComparison.OrdinalIgnoreCase))
            return ("", fullText);

        var title = firstLine[6..].Trim();
        var rest = fullText[(newline + 1)..].TrimStart('\r', '\n');
        if (title.Length > 200) title = title[..200];
        return (title, rest);
    }
}
