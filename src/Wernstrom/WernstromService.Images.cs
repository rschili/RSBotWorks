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

    /// <summary>Max processed images kept in memory. History requests re-use the same images over and over.</summary>
    private const int MaxImageCacheEntries = 10;

    /// <summary>
    /// Processed images by attachment id. A null value marks a permanent failure (e.g. expired CDN url, undecodable
    /// image) so we do not retry it on every request.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, ImageAttachment?> ImageCache = new();

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
                continue;

            var imageAttachment = await LoadImageAttachmentAsync(attachment, mimeType).ConfigureAwait(false);
            if (imageAttachment != null)
                imageAttachments.Add(imageAttachment);
        }

        return imageAttachments.Count > 0 ? imageAttachments : null;
    }

    /// <summary>
    /// Downloads and (if needed) shrinks a single image. Never throws: a broken image must not break the
    /// whole chat request. Returns null on failure; the caller then just omits the image.
    /// </summary>
    private async Task<ImageAttachment?> LoadImageAttachmentAsync(IAttachment attachment, string mimeType)
    {
        if (ImageCache.TryGetValue(attachment.Id, out var cached))
            return cached;

        ImageAttachment? result;
        try
        {
            using var httpClient = HttpClientFactory.CreateClient();
            var imageData = await httpClient.GetByteArrayAsync(attachment.Url).ConfigureAwait(false);
            bool isResized = false;
            if (attachment.Size > MaxFileSizeBytes || attachment.Height > MaxImageHeight || attachment.Width > MaxImageWidth)
            {
                Logger.LogInformation("Attachment {Filename} exceeds the size or dimension limits (Size: {SizeKb} KB, Dimensions: {Width}x{Height}), it will be resized.",
                    attachment.Filename, attachment.Size / 1024, attachment.Width, attachment.Height);
                imageData = await ProcessImage(imageData).ConfigureAwait(false);
                mimeType = "image/jpeg"; // Assume JPEG after processing
                isResized = true;
            }
            else
            {
                // Detect actual MIME type from image bytes — Discord's ContentType can be wrong
                var detectedFormat = SixLabors.ImageSharp.Image.DetectFormat(imageData);
                if (detectedFormat != null && detectedFormat.DefaultMimeType != mimeType)
                {
                    Logger.LogWarning("Discord reported MIME type {DiscordMimeType} but image bytes indicate {DetectedMimeType} for {Filename}. Using detected type.",
                        mimeType, detectedFormat.DefaultMimeType, attachment.Filename);
                    mimeType = detectedFormat.DefaultMimeType;
                }
            }

            if (imageData == null || imageData.Length == 0)
            {
                Logger.LogWarning("Failed to download or process image: {Filename}", attachment.Filename);
                result = null;
            }
            else
            {
                result = new ImageAttachment
                {
                    Filename = attachment.Filename,
                    Size = attachment.Size,
                    Width = attachment.Width ?? 0,
                    Height = attachment.Height ?? 0,
                    IsResized = isResized,
                    MimeType = mimeType,
                    Data = imageData
                };
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } status && (int)status >= 400 && (int)status < 500)
        {
            // Typically an expired Discord CDN url on an old cached message. Will not recover, remember it.
            Logger.LogWarning("Image {Filename} could not be downloaded (HTTP {StatusCode}), skipping it.", attachment.Filename, (int)status);
            result = null;
        }
        catch (ImageFormatException ex)
        {
            Logger.LogWarning(ex, "Image {Filename} could not be decoded, skipping it.", attachment.Filename);
            result = null;
        }
        catch (Exception ex)
        {
            // Transient (network, 5xx, timeout): skip this time, but do not remember the failure.
            Logger.LogWarning(ex, "Image {Filename} could not be loaded, skipping it for this request.", attachment.Filename);
            return null;
        }

        if (ImageCache.Count >= MaxImageCacheEntries)
            ImageCache.Clear();
        ImageCache[attachment.Id] = result;
        return result;
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

    /// <summary>
    /// Cheap check (no download) for whether a message carries at least one image attachment.
    /// Used to decide whether to leave a "[Bild]" marker when images are dropped from history.
    /// </summary>
    private static bool MessageHasImage(IMessage message)
    {
        foreach (var attachment in message.Attachments)
        {
            if (attachment == null)
                continue;

            if (!string.IsNullOrWhiteSpace(attachment.ContentType) &&
                attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return true;

            var ext = Path.GetExtension(attachment.Filename);
            if (!string.IsNullOrWhiteSpace(ext) && ImageExtensionToMimeType.ContainsKey(ext))
                return true;
        }

        return false;
    }

    private bool IsImageFile(string filename, string? inputContentType, out string mimeType)
    {
        mimeType = string.Empty;

        // Check if inputContentType is a known image mime type
        if (!string.IsNullOrWhiteSpace(inputContentType) && inputContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation("Using MimeType from discord ({InputContentType})", inputContentType);
            mimeType = inputContentType;
            return true;
        }

        // Try to determine from file extension
        var ext = Path.GetExtension(filename);
        if (!string.IsNullOrWhiteSpace(ext) && ImageExtensionToMimeType.TryGetValue(ext, out var foundMimeType))
        {
            Logger.LogInformation("Using MimeType from file extension ({Extension}) set to {MimeType}", ext, foundMimeType);
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
                Logger.LogInformation("Resized image to {Width}x{Height}, size reduced from {OriginalKb} KB to {ResizedKb} KB",
                    resizedImage.Width, resizedImage.Height, imageData.Length / 1024, resizedData.Length / 1024);
                return resizedData;
            }

            var processedData = await ConvertImageToJpeg(image).ConfigureAwait(false);
            Logger.LogInformation("Size reduced from {OriginalKb} KB to {ProcessedKb} KB", imageData.Length / 1024, processedData.Length / 1024);
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