using Microsoft.Extensions.Logging;
using RSBotWorks.Plugins;

namespace Stoll;

partial class StollService
{
    private Queue<RedditPost> FefePostsQueue = new();

    private HashSet<string> ReturnedPostPermalinks = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset _lastFefeFetch = DateTimeOffset.MinValue;

    private SemaphoreSlim _fefeSemaphore = new(1, 1);

    private readonly RedditPlugin RedditPlugin;

    public async Task<string> GetFefePost()
    {
        await _fefeSemaphore.WaitAsync();
        try
        {
            if ((DateTimeOffset.UtcNow - _lastFefeFetch).TotalHours > 30)
                await RefreshFefePostsQueue();

            while (FefePostsQueue.TryDequeue(out var post))
            {
                if (ReturnedPostPermalinks.Add(post.Permalink))
                {
                    return FormatFefePost(post);
                }
                // Post was already returned, skip it and try next
            }

            return "No new posts available.";
        }
        finally
        {
            _fefeSemaphore.Release();
        }
    }

    private async Task RefreshFefePostsQueue()
    {
        try
        {
            var posts = await RedditPlugin.FetchTopPostsAsync("fefe_blog_interim", 12, RedditTimespan.Week);
            FefePostsQueue.Clear();
            foreach (var post in posts.Where(p => p.Selftext != null))
            {
                FefePostsQueue.Enqueue(post);
            }
            _lastFefeFetch = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing Fefe posts queue.");
        }
    }

    private static string FormatFefePost(RedditPost post)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{post.Title}]({post.Url})");
        sb.AppendLine();
        sb.AppendLine(FixMarkdownQuotes(post.Selftext));
        return sb.ToString();
    }

    private static string FixMarkdownQuotes(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(text, @"^>(?! )", "> ", System.Text.RegularExpressions.RegexOptions.Multiline);
    }
}