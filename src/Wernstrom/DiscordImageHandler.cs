using Discord;
using Discord.WebSocket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class DiscordImageHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private const int MaxImageWidth = 784;
    private const int MaxImageHeight = MaxImageWidth;
    private const int MaxFileSizeBytes = 2 * 1024 * 1024; // 2MB limit

    public delegate Task<string> BinaryImageToDescriptionHandler(byte[] ImageData, string mimeType);

    private readonly BinaryImageToDescriptionHandler _binaryImageToDescriptionHandler;

    public DiscordImageHandler(IHttpClientFactory httpClientFactory, BinaryImageToDescriptionHandler binaryImageToDescriptionHandler, ILogger? logger = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory), "HttpClientFactory cannot be null.");
        _binaryImageToDescriptionHandler = binaryImageToDescriptionHandler ?? throw new ArgumentNullException(nameof(binaryImageToDescriptionHandler), "BinaryImageToDescriptionHandler cannot be null.");
        _logger = logger ?? NullLogger<DiscordImageHandler>.Instance;
    }

    public async Task<string?> DescribeMessageAttachments(SocketMessage message)
    {
        if (message.Attachments.Count == 0)
            return null; // No Attachments to process

        StringBuilder descriptionBuilder = new StringBuilder();

        foreach (var attachment in message.Attachments)
        {
            if (attachment == null)
                continue;

            if(!IsImageFile(attachment.Filename, attachment.ContentType, out var mimeType))
            {
                continue;
            }

            using var httpClient = _httpClientFactory.CreateClient();
            var imageData = await httpClient.GetByteArrayAsync(attachment.Url);
            if (attachment.Size > MaxFileSizeBytes || attachment.Height > MaxImageHeight || attachment.Width > MaxImageWidth)
            {
                _logger.LogWarning($"Attachment {attachment.Filename} exceeds the size or dimension limits. (Size: {attachment.Size / 1024} KB, Dimensions: {attachment.Width}x{attachment.Height}) it will be resized.");
                imageData = await ProcessImage(attachment, imageData);
                mimeType = "image/jpeg"; // Assume JPEG after processing
            }
            if (imageData == null || imageData.Length == 0)
            {
                _logger.LogWarning($"Failed to download or process image: {attachment.Filename}");
                continue;
            }

            var imageDescription = await _binaryImageToDescriptionHandler(imageData, mimeType);
            if (!string.IsNullOrWhiteSpace(imageDescription))
            {
                descriptionBuilder.AppendLine($"[IMG:{attachment.Filename}]{imageDescription}[/IMG]");
            }
        }

        var result = descriptionBuilder.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
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

    private async Task<byte[]> ProcessImage(Attachment attachment, byte[] imageData)
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
                var resizedData = await ConvertImageToJpeg(resizedImage);
                _logger.LogWarning($"Resized image to: {resizedImage.Width}x{resizedImage.Height} Size reduced from {imageData.Length / 1024} KB to {resizedData.Length / 1024} KB");
                return resizedData;
            }

            var processedData = await ConvertImageToJpeg(image);
            _logger.LogWarning($"Size reduced from {imageData.Length / 1024} KB to {processedData.Length / 1024} KB");
            return processedData;
        }
    }

    private static async Task<byte[]> ConvertImageToJpeg(SixLabors.ImageSharp.Image image)
    {
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });
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