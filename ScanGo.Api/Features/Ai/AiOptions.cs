namespace ScanGo.Api.Features.Ai;

public class AiOptions
{
    public const string SectionName = "Ai";

    public string? GeminiApiKey { get; set; }
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public bool Mock { get; set; } = true;

    /// <summary>
    /// Giá trị KHỞI TẠO cho toggle "tra cứu &amp; trích nguồn" khi lần đầu tạo hàng
    /// app_settings. Sau đó admin bật/tắt trong Admin → Settings và giá trị sống
    /// trong DB + RuntimeSettings, KHÔNG đọc lại từ đây nữa.
    /// </summary>
    public bool SearchGrounding { get; set; } = true;

    /// <summary>Số nguồn tối đa liệt kê ở cuối câu trả lời.</summary>
    public int MaxSources { get; set; } = 6;
}
