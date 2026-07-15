namespace ScanGo.Api.Features.Ai;

/// <summary>Giá USD cho 1 TRIỆU token.</summary>
public record ModelPrice(double InputPerMTok, double OutputPerMTok);

/// <summary>
/// Bảng giá paid-tier chép tay từ trang giá chính thức của Google
/// (https://ai.google.dev/gemini-api/docs/pricing) — chốt ngày 2026-07-14.
///
/// CHỈ dùng để ƯỚC TÍNH con số hiển thị cho admin, KHÔNG phải hoá đơn thật:
/// - Chưa tính Google Search grounding (xem <see cref="GroundingPer1KUsd"/>).
/// - Chưa tính bậc giá theo prompt lớn: gemini-3.1-pro-preview nhảy lên
///   $4/$18 khi prompt > 200k token (ScanGo hiếm khi chạm mức này).
/// - Chưa tính giá audio (2.5-flash audio input đắt hơn text).
/// Google đổi giá lúc nào cũng được -> thấy lệch thì sửa ở đây.
/// </summary>
public static class ModelPricing
{
    private static readonly Dictionary<string, ModelPrice> Table = new()
    {
        // Không nằm trong AllowedModels (quá đắt) — giữ lại để nếu DB lỡ có giá trị
        // này thì ước tính vẫn đúng thay vì rơi về giá flash-lite.
        ["gemini-3.1-pro-preview"] = new(2.00, 12.00),   // prompt ≤200k
        ["gemini-3.5-flash"] = new(1.50, 9.00),
        ["gemini-2.5-flash"] = new(0.30, 2.50),
        ["gemini-2.5-flash-lite"] = new(0.10, 0.40),
    };

    /// <summary>Model lạ (vd Google ra bản mới) -> lấy tạm giá flash-lite.</summary>
    public static ModelPrice For(string model) =>
        Table.TryGetValue(model, out var p) ? p : Table["gemini-2.5-flash-lite"];

    /// <summary>
    /// Giá Google Search grounding, USD / 1000 truy vấn có tra cứu (sau phần free).
    /// Gemini 3.x: 5.000 prompt/tháng free rồi $14/1k. Gemini 2.5: 1.500 req/ngày
    /// free rồi $35/1k. Lấy theo tiền tố tên model.
    /// </summary>
    public static double GroundingPer1KUsd(string model) =>
        model.StartsWith("gemini-3", StringComparison.Ordinal) ? 14.0 : 35.0;

    public static double EstimateUsd(string model, long inputTokens, long outputTokens)
    {
        var p = For(model);
        return inputTokens / 1_000_000.0 * p.InputPerMTok
             + outputTokens / 1_000_000.0 * p.OutputPerMTok;
    }
}
