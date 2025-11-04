using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public class RedditPlugin
{
    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }

    public RedditPlugin(ILogger<RedditPlugin>? logger, IHttpClientFactory httpClientFactory)
    {
        Logger = logger ?? NullLogger<RedditPlugin>.Instance;
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    [LocalFunction("reddit_top_posts")]
    [Description("Get a number of top posts of the last day for a given subreddit")]
    public async Task<string> GetRedditTopPostsAsync(string subredditName, int limit = 5)
    {
        var url = $"https://www.reddit.com/r/{subredditName}/top/.json?t=day&limit={limit}";
        
        using var httpClient = HttpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "RSBotWorks/1.0");
        
        try
        {
            var response = await httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            
            var root = document.RootElement;
            var children = root.GetProperty("data").GetProperty("children");
            
            StringBuilder contentBuilder = new();
            contentBuilder.AppendLine($"Top {limit} posts in r/{subredditName} (last 24 hours):");
            
            int postCount = 0;
            foreach (var child in children.EnumerateArray())
            {
                var data = child.GetProperty("data");
                var title = data.GetProperty("title").GetString();
                if(string.IsNullOrWhiteSpace(title))
                    continue;
                
                var permalink = data.GetProperty("permalink").GetString();
                var postUrl = permalink != null ? $" (https://www.reddit.com{permalink})" : null;

                contentBuilder.AppendLine($"- {title}{postUrl}");

                if (data.TryGetProperty("selftext", out var selftextElement))
                {
                    var selftext = selftextElement.GetString();
                    if (!string.IsNullOrWhiteSpace(selftext))
                    {
                        var preview = selftext.Length > 200 ? selftext[..200] + "..." : selftext;
                        var indentedPreview = preview.Replace("\n", "\n  ");
                        contentBuilder.AppendLine($"  Preview: {indentedPreview}");
                    }
                }
                // Check if it's a link post
                if (data.TryGetProperty("url", out var urlElement))
                    {
                        var linkUrl = urlElement.GetString();
                        if (!string.IsNullOrWhiteSpace(linkUrl))
                        {
                            contentBuilder.AppendLine($"  Link: {linkUrl}");
                        }
                    }
                
                postCount++;
            }
            
            if (postCount == 0)
            {
                contentBuilder.AppendLine("*There were no new top posts today.*");
            }
            
            return contentBuilder.ToString();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Failed to fetch Reddit posts from r/{SubredditName}", subredditName);
            return $"Failed to fetch posts from r/{subredditName}. The subreddit may not exist or Reddit API is unavailable.";
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse Reddit JSON response for r/{SubredditName}", subredditName);
            return $"Failed to parse Reddit response for r/{subredditName}.";
        }
    }
}