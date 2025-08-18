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
    private volatile bool _isProcessing = false;
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
        string videoUrl,
        [Description("Optional, if provided, specifies an instruction on what information to return about the video")]
        string? instruction = null)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            throw new ArgumentException("Video URL cannot be null or empty.", nameof(videoUrl));
        }

        bool hasInstruction = !string.IsNullOrWhiteSpace(instruction);

        // Check cache first - regardless of processing state
        lock (_cacheLock)
        {
            var cachedEntry = _cache.FirstOrDefault(entry => entry.VideoUrl.Equals(videoUrl, StringComparison.OrdinalIgnoreCase));
            if (cachedEntry != null)
            {
                _logger.LogInformation("Returning cached summary for video: {VideoUrl}", videoUrl);
                return cachedEntry.Summary;
            }
        }

        // Check if already processing a request
        if (_isProcessing)
        {
            _logger.LogInformation("Tool is busy, rejecting request for video: {VideoUrl}", videoUrl);
            return "The tool is currently busy extracting information about a video, please retry later";
        }

        // Set processing flag
        _isProcessing = true;
        try
        {
            _logger.LogInformation("No cached summary found, generating new summary for video: {VideoUrl}", videoUrl);

            string summary;

            // If instruction is provided, always use GoogleAI
            if (hasInstruction)
            {
                _logger.LogInformation("Instruction provided, using GoogleAI for video: {VideoUrl}", videoUrl);
                summary = await SummarizeVideoWithGoogleAIAsync(videoUrl, instruction);
            }
            else
            {
                // Try SocialKit first (preferred but rate-limited)
                try
                {
                    summary = await SummarizeVideoWithSocialKitAsync(videoUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SocialKit API failed, falling back to GoogleAI for video: {VideoUrl}", videoUrl);
                    summary = await SummarizeVideoWithGoogleAIAsync(videoUrl, null);
                }
            }

            // Add to cache
            lock (_cacheLock)
            {
                AddToCache(videoUrl, summary);
            }

            return summary;
        }
        finally
        {
            // Always clear the processing flag
            _isProcessing = false;
        }
    }

    private async Task<string> SummarizeVideoWithGoogleAIAsync(string videoUrl, string? instruction)
    {
        _logger.LogInformation("Summarizing video with GoogleAI: {VideoUrl}", videoUrl);

        List<Part> parts = [];

        if (!string.IsNullOrWhiteSpace(instruction))
        {
            parts.Add(new Part("Please give the information requested in the following instruction about the provided YouTube video:"));
            parts.Add(new Part(instruction));
        }
        else
        {
            parts.Add(new Part("Please give a concise and compact summary of the contents of the provided YouTube video."));
        }

        parts.Add(new Part { 
            FileData = new FileData {
                FileUri = videoUrl,
            }
        });

        try
        {
            var response = await _model.GenerateContentAsync(parts);
            return response.Text() ?? throw new InvalidOperationException("No summary returned from GoogleAI model.");
        }
        catch (Exception modelEx)
        {
            if (modelEx is GenerativeAI.Exceptions.ApiException geminiException && geminiException.ErrorCode == 403)
            {
                _logger.LogWarning("GoogleAI model access denied for video: {VideoUrl}. Please check the video URL or permissions.", videoUrl);
                return "GoogleAI has no access to the video and cannot generate a summary.";
            }
            else
            {
                _logger.LogError(modelEx, "Failed to summarize video with GoogleAI model: {VideoUrl}", videoUrl);
                throw new InvalidOperationException("Failed to summarize video using GoogleAI model.", modelEx);
            }
        }
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
