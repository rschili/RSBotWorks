using System.ComponentModel;
using System.Text.Json;
using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.Extensions.Logging;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public record VideoCacheEntry(string VideoUrl, string Summary, DateTimeOffset CachedAt);

public class YoutubePlugin
{
    private readonly ILogger<YoutubePlugin> _logger;
    private readonly GoogleAi _googleAi;
    private readonly GenerativeModel _model;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string socialKitApiKey;
    private readonly List<VideoCacheEntry> _cache = new();
    private readonly object _cacheLock = new();
    private const int MaxCacheEntries = 14;
    private const int EntriesToRemoveWhenFull = 5;

    public YoutubePlugin(ILogger<YoutubePlugin> logger, string geminiApiKey, string socialKitApiKey, IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _googleAi = new GoogleAi(geminiApiKey ?? throw new ArgumentNullException(nameof(geminiApiKey)));
        this.socialKitApiKey = socialKitApiKey ?? throw new ArgumentNullException(nameof(socialKitApiKey));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _model = _googleAi.CreateGenerativeModel("gemini-2.5-flash");
    }
    
    private async Task<string> SummarizeVideoWithSocialKitAsync(string videoUrl)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            throw new ArgumentException("Video URL cannot be null or empty.", nameof(videoUrl));
        }

        _logger.LogInformation("Summarizing video with SocialKit API: {VideoUrl}", videoUrl);

        var encodedUrl = Uri.EscapeDataString(videoUrl);
        var apiUrl = $"https://api.socialkit.dev/youtube/summarize?access_key={socialKitApiKey}&url={encodedUrl}";

        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(apiUrl);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();

        // Check if the response contains a SocialKit error
        try
        {
            using var jsonDoc = JsonDocument.Parse(content);
            if (jsonDoc.RootElement.TryGetProperty("success", out var successElement))
            {
                if (successElement.ValueKind == JsonValueKind.False)
                {
                    var errorMessage = jsonDoc.RootElement.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : "Unknown SocialKit API error";

                    throw new InvalidOperationException($"SocialKit API error: {errorMessage}");
                }
                else if (successElement.ValueKind == JsonValueKind.True && jsonDoc.RootElement.TryGetProperty("data", out var dataElement))
                {
                    return dataElement.GetRawText();
                }
            }
        }
        catch (JsonException)
        {
            // If JSON parsing fails, assume it's a valid response and continue
            _logger.LogDebug("Response is not JSON, treating as valid summary");
        }
            
        return content;
    }

    [LocalFunction("summarize_youtube_video")]
    [Description("Get a summary of the contents for a provided youtube video")]
    public async Task<string> SummarizeVideoAsync(
        [Description("Video URI supporting various formats like youtube.com/watch or youtu.be/")]
        string videoUrl)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            throw new ArgumentException("Video URL cannot be null or empty.", nameof(videoUrl));
        }

        // Check cache first
        lock (_cacheLock)
        {
            var cachedEntry = _cache.FirstOrDefault(entry => entry.VideoUrl.Equals(videoUrl, StringComparison.OrdinalIgnoreCase));
            if (cachedEntry != null)
            {
                _logger.LogInformation("Returning cached summary for video: {VideoUrl}", videoUrl);
                return cachedEntry.Summary;
            }
        }

        _logger.LogInformation("No cached summary found, generating new summary for video: {VideoUrl}", videoUrl);

        string summary;
        try
        {
            summary = await SummarizeVideoWithSocialKitAsync(videoUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SocialKit API failed, falling back to Gemini model for video: {VideoUrl}", videoUrl);

            List<Part> parts = [
                new Part("Please give a concise and compact summary of the contents of the provided YouTube video."),
                new Part {
                    FileData = new FileData
                    {
                        FileUri = videoUrl,
                    }}
            ];
            var response = await _model.GenerateContentAsync(parts);
            summary = response.Text() ?? throw new InvalidOperationException("No summary returned from the model.");
        }

        // Add to cache
        lock (_cacheLock)
        {
            AddToCache(videoUrl, summary);
        }

        return summary;
    }

    private void AddToCache(string videoUrl, string summary)
    {
        var entry = new VideoCacheEntry(videoUrl, summary, DateTimeOffset.UtcNow);
        _cache.Add(entry);

        // If we've reached the limit, remove the oldest entries
        if (_cache.Count > MaxCacheEntries)
        {
            var entriesToRemove = _cache.OrderBy(e => e.CachedAt).Take(EntriesToRemoveWhenFull).ToList();
            foreach (var entryToRemove in entriesToRemove)
            {
                _cache.Remove(entryToRemove);
            }
            _logger.LogDebug("Removed {Count} old cache entries, cache size is now {Size}", 
                entriesToRemove.Count, _cache.Count);
        }

        _logger.LogDebug("Added video summary to cache. Cache size: {Size}", _cache.Count);
    }



}
