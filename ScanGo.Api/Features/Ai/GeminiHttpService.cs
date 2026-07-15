using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ScanGo.Api.Common;

namespace ScanGo.Api.Features.Ai;

/// <summary>
/// Real Gemini streaming via REST `streamGenerateContent?alt=sse`. Used when
/// Ai.Mock=false and an API key is configured.
/// </summary>
public class GeminiHttpService(
    HttpClient http,
    IOptions<AiOptions> options,
    RuntimeSettings settings,
    ILogger<GeminiHttpService> log) : IGeminiService
{
    private readonly AiOptions _opts = options.Value;

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        UsageBox usage,
        string targetLang,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.GeminiApiKey))
            throw new InvalidOperationException("Ai:GeminiApiKey is not set.");

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{settings.Current.GeminiModel}" +
            $":streamGenerateContent?alt=sse&key={_opts.GeminiApiKey}";

        var contents = new[]
        {
            new { role = "user", parts = new[] { new { text = prompt } } },
        };

        // Google Search grounding: ta LUÔN đưa tool cho model (khi admin bật), còn
        // việc có tra cứu hay không là do MODEL tự quyết theo từng câu hỏi. Không
        // tra -> không có groundingMetadata -> không chèn nguồn.
        // Đọc từ RuntimeSettings để admin bật/tắt là ăn ngay, khỏi restart.
        // Hai nhánh serialize kiểu cụ thể (không dùng biến `object`) vì
        // JsonSerializer.Serialize trên biến khai báo `object` sẽ ra "{}".
        var requestBody = settings.Current.SearchGrounding
            ? JsonSerializer.Serialize(new
            {
                contents,
                tools = new[] { new { google_search = new { } } },
            })
            : JsonSerializer.Serialize(new { contents });

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };

        using var resp = await http.SendAsync(
            req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("Gemini HTTP {Status}: {Body}", resp.StatusCode, body);
            throw new InvalidOperationException(
                $"Gemini returned {resp.StatusCode}");
        }

        var input = 0;
        var output = 0;
        // Nguồn model đã tra cứu (giữ thứ tự, khử trùng theo uri).
        var sources = new List<(string Title, string Uri)>();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data: ")) continue;
            var json = line[6..];
            if (string.IsNullOrWhiteSpace(json)) continue;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
            {
                var cand = cands[0];
                if (cand.TryGetProperty("content", out var content)
                    && content.TryGetProperty("parts", out var parts))
                {
                    foreach (var p in parts.EnumerateArray())
                    {
                        if (p.TryGetProperty("text", out var t))
                        {
                            var s = t.GetString() ?? "";
                            if (s.Length > 0) yield return s;
                        }
                    }
                }

                CollectSources(cand, sources);
            }

            if (root.TryGetProperty("usageMetadata", out var um))
            {
                if (um.TryGetProperty("promptTokenCount", out var pt))
                    input = pt.GetInt32();
                if (um.TryGetProperty("candidatesTokenCount", out var ot))
                    output = ot.GetInt32();
            }
        }

        // Model có tra cứu -> nối danh sách nguồn vào cuối câu trả lời dưới dạng
        // Markdown (client vốn đã render Markdown nên link tự bấm được, và nguồn
        // được lưu luôn vào nội dung tin nhắn). Không tra cứu -> không có gì.
        if (sources.Count > 0)
            yield return BuildSourcesBlock(
                sources, _opts.MaxSources, Prompts.SourcesHeading(targetLang));

        usage.Usage = new AiTokenUsage(input, output);
    }

    /// <summary>
    /// Nhặt nguồn từ candidate.groundingMetadata.groundingChunks[].web
    /// (title = tên miền, uri = link redirect của Google). Khử trùng theo uri.
    /// </summary>
    private static void CollectSources(
        JsonElement candidate, List<(string Title, string Uri)> sources)
    {
        if (!candidate.TryGetProperty("groundingMetadata", out var gm)
            || !gm.TryGetProperty("groundingChunks", out var chunks)
            || chunks.ValueKind != JsonValueKind.Array)
            return;

        foreach (var chunk in chunks.EnumerateArray())
        {
            if (!chunk.TryGetProperty("web", out var web)) continue;

            var uri = web.TryGetProperty("uri", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(uri)) continue;
            if (sources.Any(s => s.Uri == uri)) continue;

            var title = web.TryGetProperty("title", out var t) ? t.GetString() : null;
            sources.Add((string.IsNullOrWhiteSpace(title) ? uri : title, uri));
        }
    }

    private static string BuildSourcesBlock(
        List<(string Title, string Uri)> sources, int max, string heading)
    {
        var sb = new StringBuilder($"\n\n---\n**{heading}:**\n");
        foreach (var (title, uri) in sources.Take(Math.Max(1, max)))
            sb.Append(CultureInfo.InvariantCulture, $"- [{title}]({uri})\n");
        return sb.ToString();
    }
}
