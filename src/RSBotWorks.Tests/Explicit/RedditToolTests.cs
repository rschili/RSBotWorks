using NSubstitute;
using DotNetEnv.Extensions;
using TUnit.Core.Logging;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.UniversalAI;
using RSBotWorks.Plugins;
using System.Text.Json;

namespace RSBotWorks.Tests;

public class RedditToolTests
{

    [Test, Explicit]
    public async Task GetTopPosts()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var redditPlugin = new RedditPlugin(NullLogger<RedditPlugin>.Instance, httpClientFactory);

        var result = await redditPlugin.GetRedditPostsAsync("worldnews");
        Console.WriteLine(result);
        await Assert.That(result).IsNotEmpty();
    }

    [Test, Explicit]
    public async Task FetchTopPostsFromFefe()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var redditPlugin = new RedditPlugin(NullLogger<RedditPlugin>.Instance, httpClientFactory);

        var result = await redditPlugin.FetchPostsAsync("fefe_blog_interim", 3, RedditSort.Top, RedditTimespan.Week);
        var jsonResult = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Console.WriteLine(jsonResult);
        await Assert.That(result).IsNotEmpty();
    }

    
}