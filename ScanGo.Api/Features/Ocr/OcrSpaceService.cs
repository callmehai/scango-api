using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ScanGo.Api.Features.Ocr;

/// <summary>
/// Real OCR.Space HTTP client. Mirrors the Node implementation:
/// engine 2, JPEG, orientation detection. Used when OcrOptions.Mock is false.
/// </summary>
public class OcrSpaceService(
    HttpClient http,
    IOptions<OcrOptions> options,
    ILogger<OcrSpaceService> log) : IOcrService
{
    private const string Endpoint = "https://api.ocr.space/parse/image";
    private readonly OcrOptions _opts = options.Value;

    public async Task<OcrResult> ExtractTextAsync(
        Stream imageStream, string sourceLang, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.OcrSpaceApiKey))
            throw new InvalidOperationException("Ocr:OcrSpaceApiKey is not set.");

        // Buffer to a temp memory stream so multipart can re-read.
        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        using var form = new MultipartFormDataContent();
        var img = new StreamContent(buffer);
        img.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "file", "image.jpg");
        form.Add(new StringContent(_opts.OcrSpaceApiKey), "apikey");
        form.Add(new StringContent(sourceLang), "language");
        form.Add(new StringContent("true"), "detectOrientation");
        form.Add(new StringContent("true"), "scale");
        form.Add(new StringContent("2"), "OCREngine");
        form.Add(new StringContent("false"), "isOverlayRequired");

        using var resp = await http.PostAsync(Endpoint, form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("OCR.Space HTTP {Status}: {Body}", resp.StatusCode, body);
            throw new InvalidOperationException(
                $"OCR.Space returned {resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("IsErroredOnProcessing", out var err) && err.GetBoolean())
        {
            var msg = root.TryGetProperty("ErrorMessage", out var em)
                ? em.ToString() : "Unknown OCR error";
            throw new InvalidOperationException($"OCR error: {msg}");
        }

        var text = root.GetProperty("ParsedResults")[0]
            .GetProperty("ParsedText").GetString() ?? "";
        return new OcrResult(text.Trim());
    }
}
