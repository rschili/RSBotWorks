using Discord;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using Microsoft.Extensions.Logging;

namespace Wernstrom;

public record ImageAttachment
{
    public required string Filename { get; set; }
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsResized { get; set; }
    public required string MimeType { get; set; }
    public required byte[] Data { get; set; }
}

public partial class WernstromService
{
    private const int MaxImageWidth = 784;
    private const int MaxImageHeight = MaxImageWidth;
    private const int MaxFileSizeBytes = 2 * 1024 * 1024; // 2MB limit

    internal const string IMAGE_INSTRUCTION = $"""
        Beschreibe das Bild, das du übergeben bekommst prägnant und kurz in 1-3 Sätzen (je nach Menge der Details im Bild).
        Ich werde den generierten Text anstelle des Originalbildes als Kontext für weitere Aufrufe übergeben.
        """;

    public async Task<IList<ImageAttachment>?> ExtractImageAttachments(IMessage message)
    {
        if (message.Attachments.Count == 0)
            return null; // No Attachments to process

        var imageAttachments = new List<ImageAttachment>();

        foreach (var attachment in message.Attachments)
        {
            if (attachment == null)
                continue;

            if (!IsImageFile(attachment.Filename, attachment.ContentType, out var mimeType))
            {
                continue;
            }

            using var httpClient = HttpClientFactory.CreateClient();
            var imageData = await httpClient.GetByteArrayAsync(attachment.Url).ConfigureAwait(false);
            bool isResized = false;
            if (attachment.Size > MaxFileSizeBytes || attachment.Height > MaxImageHeight || attachment.Width > MaxImageWidth)
            {
                Logger.LogWarning($"Attachment {attachment.Filename} exceeds the size or dimension limits. (Size: {attachment.Size / 1024} KB, Dimensions: {attachment.Width}x{attachment.Height}) it will be resized.");
                imageData = await ProcessImage(imageData).ConfigureAwait(false);
                mimeType = "image/jpeg"; // Assume JPEG after processing
                isResized = true;
            }
            if (imageData == null || imageData.Length == 0)
            {
                Logger.LogWarning($"Failed to download or process image: {attachment.Filename}");
                continue;
            }

            var imageAttachment = new ImageAttachment
            {
                Filename = attachment.Filename,
                Size = attachment.Size,
                Width = attachment.Width ?? 0,
                Height = attachment.Height ?? 0,
                IsResized = isResized,
                MimeType = mimeType,
                Data = imageData
            };
            imageAttachments.Add(imageAttachment);
        }

        return imageAttachments.Count > 0 ? imageAttachments : null;
    }

    private static readonly Dictionary<string, string> ImageExtensionToMimeType = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".bmp", "image/bmp" },
        { ".webp", "image/webp" }
    };

    private bool IsImageFile(string filename, string? inputContentType, out string mimeType)
    {
        mimeType = string.Empty;

        // Check if inputContentType is a known image mime type
        if (!string.IsNullOrWhiteSpace(inputContentType) && inputContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            mimeType = inputContentType;
            return true;
        }

        // Try to determine from file extension
        var ext = Path.GetExtension(filename);
        if (!string.IsNullOrWhiteSpace(ext) && ImageExtensionToMimeType.TryGetValue(ext, out var foundMimeType))
        {
            mimeType = foundMimeType;
            return true;
        }

        return false;
    }

    private async Task<byte[]> ProcessImage(byte[] imageData)
    {
        using (var image = SixLabors.ImageSharp.Image.Load(imageData))
        {
            var originalWidth = image.Width;
            var originalHeight = image.Height;

            // Check if resizing is needed
            if (originalWidth > MaxImageWidth || originalHeight > MaxImageHeight)
            {
                using var resizedImage = ResizeImage(image, MaxImageWidth, MaxImageHeight);

                // Save or process the resized image
                var resizedData = await ConvertImageToJpeg(resizedImage).ConfigureAwait(false);
                Logger.LogWarning($"Resized image to: {resizedImage.Width}x{resizedImage.Height} Size reduced from {imageData.Length / 1024} KB to {resizedData.Length / 1024} KB");
                return resizedData;
            }

            var processedData = await ConvertImageToJpeg(image).ConfigureAwait(false);
            Logger.LogWarning($"Size reduced from {imageData.Length / 1024} KB to {processedData.Length / 1024} KB");
            return processedData;
        }
    }

    private static async Task<byte[]> ConvertImageToJpeg(SixLabors.ImageSharp.Image image)
    {
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 }).ConfigureAwait(false);
        return ms.ToArray();
    }

    private SixLabors.ImageSharp.Image ResizeImage(SixLabors.ImageSharp.Image image, int maxWidth, int maxHeight)
    {
        var ratioX = (double)maxWidth / image.Width;
        var ratioY = (double)maxHeight / image.Height;
        var ratio = Math.Min(ratioX, ratioY);

        var newWidth = (int)(image.Width * ratio);
        var newHeight = (int)(image.Height * ratio);

        var resized = image.Clone(ctx => ctx.Resize(newWidth, newHeight));
        return resized;
    }
}