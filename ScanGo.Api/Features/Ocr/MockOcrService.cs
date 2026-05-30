namespace ScanGo.Api.Features.Ocr;

public class MockOcrService : IOcrService
{
    public Task<OcrResult> ExtractTextAsync(
        Stream imageStream, string sourceLang, CancellationToken ct)
    {
        // Deterministic placeholder so tests can assert; mimics what the Node
        // backend's MOCK_OCR returned for parity.
        return Task.FromResult(new OcrResult(
            $"OCR MOCK RESULT (lang={sourceLang}): " +
            "INGREDIENTS: aqua, sodium laureth sulfate, glycerin, parfum."));
    }
}
