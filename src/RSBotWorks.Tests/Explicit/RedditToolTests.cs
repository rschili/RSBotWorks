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

        var result = await redditPlugin.GetRedditTopPostsAsync("worldnews", 3);
        Console.WriteLine(result);
        await Assert.That(result).IsNotEmpty();
    }

    [Test, Explicit]
    public async Task FetchTopPostsFromFefe()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var redditPlugin = new RedditPlugin(NullLogger<RedditPlugin>.Instance, httpClientFactory);

        var result = await redditPlugin.FetchTopPostsAsync("fefe_blog_interim", 3, RedditTimespan.Week);
        var jsonResult = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(jsonResult);
        await Assert.That(result).IsNotEmpty();
    }

    
}