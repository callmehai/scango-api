namespace ScanGo.Api.Features.Ai;

/// <summary>
/// Topic-specific prompts for scan analysis + follow-up chat.
/// Format rule: the first non-empty line of a scan answer MUST be
/// "TITLE: ..." in targetLang (parsed out by <see cref="TitleExtractor"/>).
/// The web client renders the body as Markdown (+ KaTeX), so light Markdown is
/// encouraged for structure — but never fenced code / JSON blocks.
/// </summary>
public static class Prompts
{
    /// <summary>
    /// Map an ISO-ish language code to a human-readable name for the prompt, so
    /// we instruct the model in natural language ("Tiếng Việt") instead of a bare
    /// code ("vnm") — the latter tends to get echoed back as the first line of
    /// the answer. Unknown codes fall back to the code itself.
    /// </summary>
    /// <summary>
    /// Hướng dẫn tra cứu + trích nguồn, dùng chung cho scan & chat.
    /// Tool google_search LUÔN được bật ở <see cref="GeminiHttpService"/> (khi
    /// Ai:SearchGrounding = true) — nhưng MODEL tự quyết khi nào cần tra. Danh sách
    /// link nguồn do server tự chèn vào cuối câu trả lời từ groundingMetadata, nên
    /// ở đây dặn model KHÔNG tự dán URL (tránh link bịa).
    /// </summary>
    private const string SearchGuidance = """
        TRA CỨU & TRÍCH NGUỒN:
        - Bạn CÓ công cụ tìm kiếm Google. Hãy TỰ QUYẾT ĐỊNH khi nào cần dùng:
          • NÊN tra cứu khi câu trả lời phụ thuộc thông tin thay đổi theo thời gian
            hoặc đòi hỏi chính xác: giá cả, khuyến mãi, tin tức, sự kiện, quy định,
            thông số sản phẩm mới, tình trạng còn hàng, đánh giá thực tế, số liệu
            thống kê, thông tin về một người/địa điểm/tổ chức cụ thể.
          • KHÔNG cần tra với kiến thức phổ thông ổn định, giải thích khái niệm,
            dịch thuật, hoặc khi chỉ cần dựa vào nội dung tài liệu đã có.
        - Khi CÓ tra cứu: ưu tiên số liệu tra được thay vì phỏng đoán, và nói rõ
          thông tin lấy từ đâu ngay trong câu văn (ví dụ "theo cellphones.com.vn…").
          TUYỆT ĐỐI KHÔNG tự dán URL/link — hệ thống sẽ tự chèn danh sách nguồn ở
          cuối câu trả lời.
        - Khi KHÔNG tra cứu: đừng nói như thể vừa tra realtime; với thông tin thay
          đổi theo thời gian thì nói dạng khoảng và lưu ý có thể đã cũ.
        """;

    /// <summary>
    /// Tiêu đề mục nguồn, theo ngôn ngữ câu trả lời — do server tự chèn (model
    /// không tự dán link) nên phải tự dịch ở đây. Mã lạ -> tiếng Anh.
    /// Giữ đồng bộ với <see cref="LangName"/>.
    /// </summary>
    public static string SourcesHeading(string targetLang) => targetLang switch
    {
        "vnm" => "Nguồn tham khảo",
        "eng" => "Sources",
        "jpn" => "参考情報",
        "kor" => "출처",
        "zho" or "chi" => "参考来源",
        "fra" => "Sources",
        _ => "Sources",
    };

    private static string LangName(string code) => code switch
    {
        "vnm" => "Tiếng Việt",
        "eng" => "English",
        "jpn" => "日本語 (Tiếng Nhật)",
        "kor" => "한국어 (Tiếng Hàn)",
        "zho" or "chi" => "中文 (Tiếng Trung)",
        "fra" => "Français (Tiếng Pháp)",
        _ => code,
    };

    public static string ForScan(string ocrText, string targetLang, string topic)
    {
        var (role, instr) = TopicGuidance(topic);
        var lang = LangName(targetLang);
        return $$"""
            Bạn là {{role}}.
            Người dùng vừa chụp ảnh một tài liệu/vật thể; hệ thống OCR đã trích ra
            phần văn bản bên dưới (có thể lẫn lỗi nhận dạng, thiếu dấu, sai chính tả).
            Viết TOÀN BỘ câu trả lời bằng {{lang}}. KHÔNG in ra mã ngôn ngữ hay bất kỳ
            nhãn nào (như "vnm", "eng") ở đầu hay bất cứ đâu trong câu trả lời.

            ĐỊNH DẠNG:
            - Dòng đầu tiên BẮT BUỘC là: TITLE: <tiêu đề ngắn gọn, ≤ 80 ký tự, bằng {{lang}}>
            - Tiếp theo là một dòng trống
            - Sau đó là phần thân. Được dùng Markdown nhẹ để dễ đọc trên điện thoại:
              tiêu đề mục (##), **in đậm**, gạch đầu dòng (-). KHÔNG bọc câu trả lời
              trong khối ``` hay JSON.

            NGUYÊN TẮC:
            - Bám sát nội dung OCR. Nếu chữ mờ/thiếu/khó đọc, hãy suy luận hợp lý và
              nói rõ chỗ nào là phỏng đoán; đừng bịa ra dữ kiện RIÊNG của tài liệu này
              (thành phần, con số, tên gọi…) mà OCR không hề có.
            - Nếu văn bản OCR quá ít, trống hoặc không có nội dung có nghĩa: ĐỪNG cố
              phân tích hay bịa. Bỏ qua mọi mục hướng dẫn bên dưới và CHỈ trả về đúng
              thông điệp sau (dịch sang {{lang}}, giữ nguyên ý):
              "Có thể bạn đã gửi ảnh không phù hợp để quét/dịch thuật, hoặc quá trình
              quét chưa phát hiện được nội dung. Vui lòng thử lại hoặc dùng ảnh khác."
              Vẫn giữ dòng TITLE ở đầu (ví dụ TITLE: Cuộc trò chuyện mới).
            - ĐƯỢC dùng kiến thức nền của bạn (và công cụ tra cứu, xem mục bên dưới) để
              giải thích, cảnh báo và đưa lời khuyên hữu ích. Cũng được bổ sung thông tin
              NGOÀI tài liệu khi có ích — như giá tham khảo, mức độ phổ biến, đánh giá
              chung — nhưng đừng nhầm lẫn nó với dữ kiện in trên tài liệu.
            - Giọng văn thân thiện, chuyên nghiệp, đi thẳng vào điều người dùng quan tâm.
              Không nhắc lại đề bài, không rào đón dài dòng.

            {{SearchGuidance}}

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
        var lang = LangName(targetLang);
        return $$"""
            Bạn là trợ lý AI thân thiện và thông minh. Viết câu trả lời bằng {{lang}}.
            KHÔNG in ra mã ngôn ngữ hay nhãn nào (như "vnm", "eng") ở đầu hay bất cứ
            đâu trong câu trả lời.

            Bạn đang tiếp tục cuộc trò chuyện về một tài liệu/vật thể mà người dùng vừa
            quét — phần đầu lịch sử bên dưới thường chính là bài phân tích của bạn về nó.

            - Trả lời ĐÚNG trọng tâm câu hỏi, ngắn gọn.
            - ĐƯỢC dùng kiến thức nền của bạn để trả lời cả những điều KHÔNG có trong
              tài liệu — ví dụ sản phẩm/đối tượng này là gì, khoảng giá tham khảo trên
              thị trường, đánh giá & mức độ phổ biến nói chung, kinh nghiệm sử dụng. Cứ
              trả lời tự nhiên và hữu ích như một người am hiểu; đừng từ chối chỉ vì
              thông tin không nằm trong ảnh.
            - Phân biệt rõ HAI loại thông tin:
              • Dữ kiện RIÊNG của tài liệu này (con số, thành phần, tên, ngày tháng… in
                trên ảnh): chỉ bám theo những gì OCR có, KHÔNG bịa thêm.
              • Kiến thức chung về loại sản phẩm/đối tượng: được dùng thoải mái.
            - Với giá cả và các con số thay đổi theo thời gian: nếu tra cứu được thì
              dùng số liệu tra được và nói rõ nguồn; nếu không tra cứu thì trả lời dạng
              KHOẢNG ("khoảng…", "tầm…") và lưu ý có thể đã thay đổi tuỳ nơi bán & thời điểm.
            - Nếu thật sự không biết, hãy nói thật thay vì bịa.
            - Được dùng Markdown nhẹ (in đậm, gạch đầu dòng) cho dễ đọc; KHÔNG bọc trong
              khối ``` hay JSON.
            - Giữ giọng tự nhiên, gần gũi.

            {{SearchGuidance}}

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
            """
            Phân tích sản phẩm theo các mục dưới đây. Bỏ qua mục không áp dụng được,
            tuyệt đối không bịa — nhưng hãy tận dụng kiến thức về loại sản phẩm/thành phần
            để mục nào cũng thật sự hữu ích:

            ## Tổng quan
            Tên & loại sản phẩm, công dụng/mục đích chính, thành phần hoặc thông số đáng chú ý.

            ## Cảnh báo dị ứng / kích ứng / tác dụng phụ
            Dựa trên thành phần và loại sản phẩm, nêu nguy cơ thường gặp (chất gây dị ứng,
            cồn, đường, caffeine, chất kích ứng da, tác dụng phụ có thể có…) và dấu hiệu cần để ý.

            ## Phù hợp với ai
            Ai nên dùng, ai nên thận trọng hoặc nên tránh (vd trẻ em, phụ nữ mang thai/cho con bú,
            người dị ứng, người có bệnh nền, người đang lái xe/vận hành máy…).

            ## Giá & đánh giá tham khảo
            Nếu nhận ra sản phẩm, nêu giá phổ biến trên thị trường và đánh giá/mức độ
            ưa chuộng chung. Đây là mục NÊN tra cứu (giá đổi liên tục): tra được thì
            dùng số liệu thật kèm nguồn; không tra được thì nói KHOẢNG dựa trên kiến
            thức nền và ghi rõ là ước lượng. KHÔNG bịa con số chính xác.
            Bỏ qua mục này nếu không đủ tự tin nhận diện sản phẩm.

            ## Lời khuyên
            Cách dùng & bảo quản hợp lý, mẹo dùng an toàn/hiệu quả, hoặc gợi ý liên quan.

            Áp dụng cho MỌI loại sản phẩm — thực phẩm, bánh kẹo, đồ uống, rượu bia, mỹ phẩm,
            thuốc, thực phẩm chức năng, đồ điện tử, gia dụng… — và điều chỉnh nội dung từng mục
            cho đúng với loại sản phẩm đó.
            """),

        "history" => (
            "nhà nghiên cứu lịch sử & văn hoá",
            """
            Giúp người đọc hiểu sâu tài liệu theo các mục dưới đây. Bỏ qua mục không áp
            dụng, và đừng suy diễn quá xa khỏi tài liệu:

            ## Nội dung tài liệu
            Tài liệu/hiện vật này nói gì. Nếu là chữ Hán/Nôm, cổ ngữ hay ngoại ngữ,
            hãy dịch nghĩa rõ ràng.

            ## Bối cảnh lịch sử
            Thời kỳ/triều đại/giai đoạn, cùng nhân vật và sự kiện liên quan.

            ## Ý nghĩa & giá trị
            Vì sao đáng chú ý — giá trị văn hoá, lịch sử hoặc tư liệu.

            ## Điều thú vị
            Một chi tiết hoặc liên hệ hấp dẫn giúp người đọc dễ nhớ.
            """),

        "place" => (
            "hướng dẫn viên du lịch am hiểu địa phương",
            """
            Giới thiệu địa danh/đối tượng trong ảnh theo các mục dưới đây. Bỏ qua mục
            không suy ra được, và đừng bịa số liệu:

            ## Tổng quan
            Đó là gì, ở đâu (vùng/thành phố/quốc gia nếu nhận ra được).

            ## Điểm nổi bật & ý nghĩa
            Nét đặc sắc cùng giá trị văn hoá – lịch sử – kiến trúc.

            ## Trải nghiệm gợi ý
            Nên xem/làm gì, thời điểm đẹp, góc tham quan hoặc chụp ảnh đáng thử.

            ## Lưu ý cho du khách
            Giờ giấc, vé, trang phục, an toàn, đi lại, mẹo nhỏ nếu suy ra được; có thể
            gợi ý thêm vài điểm/hoạt động liên quan gần đó.
            """),

        _ => (
            "trợ lý phân tích tài liệu thông minh",
            """
            Tự nhận diện loại tài liệu rồi phân tích theo các mục dưới đây. Bỏ qua mục
            không áp dụng, và đừng bịa thông tin tài liệu không có:

            ## Loại tài liệu
            Nhận diện loại (hoá đơn, đơn thuốc, hợp đồng, thực đơn, biển báo, thư từ,
            ghi chú, đề thi…).

            ## Tóm tắt nội dung
            Nội dung chính, ngắn gọn và dễ hiểu.

            ## Thông tin quan trọng
            Các dữ kiện cần chú ý: con số, ngày tháng, tên, địa chỉ, điều khoản, tổng tiền…

            ## Giải thích & lời khuyên
            Làm rõ những chỗ khó hiểu; nếu người dùng nên làm gì tiếp theo thì gợi ý.
            """),
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
