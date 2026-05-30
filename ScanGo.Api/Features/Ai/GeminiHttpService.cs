using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ScanGo.Api.Features.Ai;

/// <summary>
/// Real Gemini streaming via REST `streamGenerateContent?alt=sse`. Used when
/// Ai.Mock=false and an API key is configured.
/// </summary>
public class GeminiHttpService(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<GeminiHttpService> log) : IGeminiService
{
    private readonly AiOptions _opts = options.Value;

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        UsageBox usage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.GeminiApiKey))
            throw new InvalidOperationException("Ai:GeminiApiKey is not set.");

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{_opts.GeminiModel}" +
            $":streamGenerateContent?alt=sse&key={_opts.GeminiApiKey}";

        var payload = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } },
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
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
                var parts = cands[0].GetProperty("content").GetProperty("parts");
                foreach (var p in parts.EnumerateArray())
                {
                    if (p.TryGetProperty("text", out var t))
                    {
                        var s = t.GetString() ?? "";
                        if (s.Length > 0) yield return s;
                    }
                }
            }

            if (root.TryGetProperty("usageMetadata", out var um))
            {
                if (um.TryGetProperty("promptTokenCount", out var pt))
                    input = pt.GetInt32();
                if (um.TryGetProperty("candidatesTokenCount", out var ot))
                    output = ot.GetInt32();
            }
        }

        usage.Usage = new AiTokenUsage(input, output);
    }
}
