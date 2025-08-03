using System.ComponentModel;
using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public class NewsPlugin
{
    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }

    private TimedCache<string> _heiseCache = new(TimeSpan.FromHours(3));

    private TimedCache<string> _postillonCache = new(TimeSpan.FromHours(3));

    public NewsPlugin(IHttpClientFactory httpClientFactory, ILogger<NewsPlugin>? logger)
    {
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        Logger = logger ?? NullLogger<NewsPlugin>.Instance;
    }

    [LocalFunction("heise_headlines")]
    [Description("Get the latest headlines from Heise Online (Technology related news)")]
    public async Task<string> GetHeiseHeadlinesAsync(int count = 5)
    {
        if (_heiseCache.TryGet(out var cachedValue) && !string.IsNullOrEmpty(cachedValue))
        {
            Logger.LogInformation("Returning cached Heise headlines.");
            return cachedValue;
        }
        Logger.LogInformation("Fetching Heise headlines, count: {Count}", count);
        const string feedUrl = "https://www.heise.de/rss/heise-atom.xml";

        using var httpClient = HttpClientFactory.CreateClient();
        using var stream = await httpClient.GetStreamAsync(feedUrl);

        using XmlReader reader = XmlReader.Create(stream);
        SyndicationFeed feed = SyndicationFeed.Load(reader);

        var summaries = feed.Items.Take(count).Select(item => item.Summary.Text);
        var result = string.Join(Environment.NewLine, summaries);
        _heiseCache.Set(result);
        return result;
    }

    [LocalFunction("postillon_headlines")]
    [Description("Get the latest headlines from Der Postillon (german satire magazine)")]
    public async Task<string> GetPostillonHeadlinesAsync(int count = 5)
    {
        if (_postillonCache.TryGet(out var cachedValue) && !string.IsNullOrEmpty(cachedValue))
        {
            Logger.LogInformation("Returning cached Postillon headlines.");
            return cachedValue;
        }
        Logger.LogInformation("Fetching Postillon headlines, count: {Count}", count);
        const string feedUrl = "https://follow.it/der-postillon-abo/rss";

        using var httpClient = HttpClientFactory.CreateClient();
        using var stream = await httpClient.GetStreamAsync(feedUrl);
        using XmlReader reader = XmlReader.Create(stream);
        List<string> titles = new();
        while (reader.Read()) // rss feed uses version 0.91 which is not supported by SyndicationFeed.Load, so we just fetch all item/title elements manually
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "item")
            {
                // Innerhalb des <item>-Elements nach dem <title>-Element suchen
                while (reader.Read())
                {
                    // Wenn das Ende des <item>-Elements erreicht ist, Schleife verlassen
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "item")
                        break;

                    // Wenn ein <title>-Element gefunden wird, dessen Inhalt lesen
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "title")
                    {
                        titles.Add(System.Net.WebUtility.HtmlDecode(reader.ReadElementContentAsString()));
                    }
                }
            }
        }

        var summaries = titles.Where(PostillonFilter).Take(count);
        var result = string.Join(Environment.NewLine, summaries);
        _postillonCache.Set(result);
        return result;
    }

    private static readonly string[] PostillonBlacklist = ["Newsticker", "des Tages", "der Woche", "Sonntagsfrage"];
    public bool PostillonFilter(string title)
    {
        foreach (var word in PostillonBlacklist)
        {
            if (title.Contains(word, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}