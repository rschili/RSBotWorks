using System.ComponentModel;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
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

public enum RedditSort
{
    Hot,
    New,
    Top,
    Rising,
    Controversial,
    Best
}

public record RedditPost(
    string SubredditName,
    string Title,
    string Permalink,
    string? Selftext,
    string? Url
);

public class RedditPlugin
{
    // Reddit throttles requests without a descriptive User-Agent. Format: platform:appid:version (by /u/username)
    private const string UserAgent = "dotnet:RSBotWorks:1.0 (by /u/rschili)";

    // The RSS feed delivers post bodies as HTML. The old JSON API delivered markdown, and the rest
    // of the codebase (and the Markdig -> Matrix HTML pipeline) expects markdown, so we convert back.
    private static readonly ReverseMarkdown.Converter HtmlToMarkdown = new(new ReverseMarkdown.Config
    {
        UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.Bypass,
        GithubFlavored = false,
        RemoveComments = true,
        SmartHrefHandling = true
    });

    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }

    public RedditPlugin(ILogger<RedditPlugin>? logger, IHttpClientFactory httpClientFactory)
    {
        Logger = logger ?? NullLogger<RedditPlugin>.Instance;
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<List<RedditPost>> FetchPostsAsync(string subredditName, int limit = 5, RedditSort sort = RedditSort.Top, RedditTimespan timespan = RedditTimespan.Day)
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

        var sortValue = sort switch
        {
            RedditSort.Hot => "hot",
            RedditSort.New => "new",
            RedditSort.Top => "top",
            RedditSort.Rising => "rising",
            RedditSort.Controversial => "controversial",
            RedditSort.Best => "best",
            _ => "top"
        };

        var maxAge = timespan switch
        {
            RedditTimespan.Hour => TimeSpan.FromHours(1),
            RedditTimespan.Day => TimeSpan.FromDays(1),
            RedditTimespan.Week => TimeSpan.FromDays(7),
            RedditTimespan.Month => TimeSpan.FromDays(30),
            RedditTimespan.Year => TimeSpan.FromDays(365),
            RedditTimespan.All => TimeSpan.MaxValue,
            _ => TimeSpan.FromDays(1)
        };
        
        // Reddit blocks anonymous .json API access (HTTP 403). The public .rss/.atom feeds remain
        // accessible without authentication, so we read those instead. The trade-off: the feed
        // exposes fewer fields (no pinned/stickied flags).
        var url = $"https://www.reddit.com/r/{subredditName}/{sortValue}/.rss?t={timespanValue}&limit={limit}";

        using var httpClient = HttpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

        using var stream = await httpClient.GetStreamAsync(url).ConfigureAwait(false);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        var posts = new List<RedditPost>();
        if (feed == null)
            return posts;

        foreach (var item in feed.Items)
        {
            var title = item.Title?.Text;
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var link = item.Links.FirstOrDefault()?.Uri;
            if (link == null)
                continue;

            // The entry's category term carries the actual subreddit, which matters for combined
            // feeds like "worldnews+europe+germany". Fall back to the requested name.
            var localSubreddit = item.Categories.FirstOrDefault()?.Name;
            if (string.IsNullOrWhiteSpace(localSubreddit))
                localSubreddit = subredditName;

            // Atom <content type="html"> is HTML (already entity-decoded by the XML reader).
            // Convert it back to markdown so it matches the old JSON API contract and renders
            // cleanly through the Markdig -> Matrix HTML pipeline.
            var selftextHtml = (item.Content as TextSyndicationContent)?.Text;
            if (string.IsNullOrWhiteSpace(selftextHtml) && item.Summary != null)
                selftextHtml = item.Summary.Text;

            var selftext = string.IsNullOrWhiteSpace(selftextHtml)
                ? null
                : HtmlToMarkdown.Convert(selftextHtml).Trim();

            var published = item.PublishDate != default ? item.PublishDate : item.LastUpdatedTime;
            if (published != default)
            {
                var age = DateTimeOffset.UtcNow - published;
                if (age > maxAge)
                    continue; // post is older than the specified timespan
            }

            posts.Add(new RedditPost(localSubreddit, title, link.AbsolutePath, selftext, link.AbsoluteUri));
        }

        return posts;
    }

    [LocalFunction("reddit_top_posts")]
    [Description("Get the top 5 posts of the last day for a given subreddit")]
    public async Task<string> GetRedditPostsAsync(string subredditName)
    {
        var limit = 5;
        var sort = RedditSort.Top;
        var timespan = RedditTimespan.Day;
        try
        {
            var posts = await FetchPostsAsync(subredditName, limit, sort, timespan).ConfigureAwait(false);

            StringBuilder contentBuilder = new();
            if (posts.Count == 0)
            {
                contentBuilder.AppendLine("*There were no new top posts today.*");
                return contentBuilder.ToString();
            }
            
            foreach (var post in posts)
            {
                var postUrl = $"https://www.reddit.com{post.Permalink}";
                contentBuilder.AppendLine($"- {post.Title} ({postUrl}) - subreddit r/{post.SubredditName}");
                
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
            return $"Failed to fetch posts from r/{subredditName}. The subreddit may not exist or Reddit is unavailable.";
        }
        catch (XmlException ex)
        {
            Logger.LogError(ex, "Failed to parse Reddit RSS feed for r/{SubredditName}", subredditName);
            return $"Failed to parse Reddit feed for r/{subredditName}.";
        }
    }
}