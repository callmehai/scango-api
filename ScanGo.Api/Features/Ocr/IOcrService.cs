namespace ScanGo.Api.Features.Ocr;

public class OcrOptions
{
    public const string SectionName = "Ocr";
    public string? OcrSpaceApiKey { get; set; }
    public bool Mock { get; set; } = true;       // default to mock; flip false in prod
}

public record OcrResult(string Text);

public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(
        Stream imageStream, string sourceLang, CancellationToken ct);
}
