using ScanGo.Api.Common;

namespace ScanGo.Api.Features.Ocr;

/// <summary>
/// Picks the mock or real OCR implementation per call based on the current
/// runtime <c>OcrMock</c> flag — admin can flip it live without a restart.
/// </summary>
public class OcrServiceDispatcher(
    RuntimeSettings settings,
    MockOcrService mock,
    OcrSpaceService real) : IOcrService
{
    public Task<OcrResult> ExtractTextAsync(
        Stream imageStream, string sourceLang, CancellationToken ct) =>
        (settings.Current.OcrMock ? (IOcrService)mock : real)
            .ExtractTextAsync(imageStream, sourceLang, ct);
}
