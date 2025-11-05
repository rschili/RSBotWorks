using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public enum RedditTimespan
{
    Hour,
    Day,
    Week,
    Month,
    Year,
    All
}

public record RedditPost(
    string Title,
    string Permalink,
    string? Selftext,
    string? Url
);

public class RedditPlugin
{
    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }

    public RedditPlugin(ILogger<RedditPlugin>? logger, IHttpClientFactory httpClientFactory)
    {
        Logger = logger ?? NullLogger<RedditPlugin>.Instance;
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<List<RedditPost>> FetchTopPostsAsync(string subredditName, int limit = 5, RedditTimespan timespan = RedditTimespan.Day)
    {
        var timespanValue = timespan switch
        {
            RedditTimespan.Hour => "hour",
            RedditTimespan.Day => "day",
            RedditTimespan.Week => "week",
            RedditTimespan.Month => "month",
            RedditTimespan.Year => "year",
            RedditTimespan.All => "all",
            _ => "day"
        };
        
        var url = $"https://www.reddit.com/r/{subredditName}/top/.json?t={timespanValue}&limit={limit}";
        
        using var httpClient = HttpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "RSBotWorks/1.0");
        
        var response = await httpClient.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        
        var root = document.RootElement;
        var children = root.GetProperty("data").GetProperty("children");
        
        var posts = new List<RedditPost>();
        foreach (var child in children.EnumerateArray())
        {
            var data = child.GetProperty("data");
            var title = data.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(title))
                continue;
            
            var permalink = data.GetProperty("permalink").GetString();
            if (permalink == null)
                continue;
            
            var selftext = data.TryGetProperty("selftext", out var selftextElement) 
                ? selftextElement.GetString() 
                : null;
            
            var url_value = data.TryGetProperty("url", out var urlElement) 
                ? urlElement.GetString() 
                : null;
            
            posts.Add(new RedditPost(title, permalink, selftext, url_value));
        }
        
        return posts;
    }

    [LocalFunction("reddit_top_posts")]
    [Description("Get a number of top posts of the last day for a given subreddit")]
    public async Task<string> GetRedditTopPostsAsync(string subredditName, int limit = 5, RedditTimespan timespan = RedditTimespan.Day)
    {
        try
        {
            var posts = await FetchTopPostsAsync(subredditName, limit, timespan).ConfigureAwait(false);
            
            var timespanLabel = timespan switch
            {
                RedditTimespan.Hour => "last hour",
                RedditTimespan.Day => "last 24 hours",
                RedditTimespan.Week => "last week",
                RedditTimespan.Month => "last month",
                RedditTimespan.Year => "last year",
                RedditTimespan.All => "all time",
                _ => "last 24 hours"
            };
            
            StringBuilder contentBuilder = new();
            contentBuilder.AppendLine($"Top {limit} posts in r/{subredditName} ({timespanLabel}):");
            
            if (posts.Count == 0)
            {
                contentBuilder.AppendLine("*There were no new top posts today.*");
                return contentBuilder.ToString();
            }
            
            foreach (var post in posts)
            {
                var postUrl = $"https://www.reddit.com{post.Permalink}";
                contentBuilder.AppendLine($"- {post.Title} ({postUrl})");
                
                if (!string.IsNullOrWhiteSpace(post.Selftext))
                {
                    var preview = post.Selftext.Length > 200 ? post.Selftext[..200] + "..." : post.Selftext;
                    var indentedPreview = preview.Replace("\n", "\n  ");
                    contentBuilder.AppendLine($"  Preview: {indentedPreview}");
                }
                
                if (!string.IsNullOrWhiteSpace(post.Url))
                {
                    contentBuilder.AppendLine($"  Link: {post.Url}");
                }
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