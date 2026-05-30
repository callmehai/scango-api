using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ScanGo.Api.Features.Conversations;

/// <summary>
/// Image optimisation for storage + OCR. Mirrors what the Node backend did:
/// resize to max width 1600px, JPEG 80 quality. Returns a fresh MemoryStream
/// caller is responsible for disposing.
/// </summary>
public static class ImageProcessor
{
    public const int MaxWidthForStorage = 1600;
    public const int JpegQuality = 80;

    public static async Task<MemoryStream> OptimiseForStorageAsync(
        Stream input, CancellationToken ct)
    {
        using var img = await Image.LoadAsync(input, ct);
        if (img.Width > MaxWidthForStorage)
        {
            var ratio = (double)MaxWidthForStorage / img.Width;
            img.Mutate(x => x.Resize(MaxWidthForStorage, (int)(img.Height * ratio)));
        }

        var output = new MemoryStream();
        await img.SaveAsync(output, new JpegEncoder { Quality = JpegQuality }, ct);
        output.Position = 0;
        return output;
    }
}
