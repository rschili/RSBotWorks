using NSubstitute;
using DotNetEnv.Extensions;
using TUnit.Core.Logging;
using System.Globalization;
using RSBotWorks.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace RSBotWorks.Tests;

public class ToolTests
{
    [Test, Explicit]
    public async Task ObtainWeatherInDielheim()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string apiKey = env["OPENWEATHERMAP_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
        {
            Assert.Fail("OPENWEATHERMAP_API_KEY is not set in the .env file.");
            return;
        }
        var cultureInfo = new CultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        var service = new WeatherTool(httpClientFactory, NullLogger<WeatherTool>.Instance, apiKey);

        var response = await service.GetCurrentWeatherAsync("Dielheim").ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response for Dielheim: {response}");

        var response2 = await service.GetCurrentWeatherAsync("69234").ConfigureAwait(false);
        await Assert.That(response2).IsNotNullOrEmpty();
        if (logger != null)
            await logger.LogInformationAsync($"Response for ZIP 69234: {response2}");
    }

    [Test, Explicit]
    public async Task ObtainWeatherForecastInDielheim()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();

        string apiKey = env["OPENWEATHERMAP_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
        {
            Assert.Fail("OPENWEATHERMAP_API_KEY is not set in the .env file.");
            return;
        }
        var cultureInfo = new CultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        var service = new WeatherTool(httpClientFactory, NullLogger<WeatherTool>.Instance, apiKey);

        var response = await service.GetWeatherForecastAsync("Dielheim").ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response for Dielheim: {response}");

        var response2 = await service.GetWeatherForecastAsync("69234").ConfigureAwait(false);
        await Assert.That(response2).IsNotNullOrEmpty();
        if (logger != null)
            await logger.LogInformationAsync($"Response for ZIP 69234: {response2}");
    }

    [Test, Explicit]
    public async Task ObtainHeiseHeadlines()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        var service = new NewsTool(httpClientFactory, NullLogger<NewsTool>.Instance);

        var response = await service.GetHeiseHeadlinesAsync(10).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response for Heise Feed: {response}");
    }

    [Test, Explicit]
    public async Task ObtainPostillonHeadlines()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        var service = new NewsTool(httpClientFactory, NullLogger<NewsTool>.Instance);

        var response = await service.GetPostillonHeadlinesAsync(30).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response for Postillon Feed: {response}");
    }

    [Test, Explicit]
    public async Task ObtainCupraInfo()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();

        string url = env["HA_API_URL"];
        if (string.IsNullOrEmpty(url))
        {
            Assert.Fail("HA_API_URL is not set in the .env file.");
            return;
        }
        string token = env["HA_TOKEN"];
        if (string.IsNullOrEmpty(token))
        {
            Assert.Fail("HA_TOKEN is not set in the .env file.");
            return;
        }

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        var service = new HomeAssistantTool(
            httpClientFactory,
            url,
            token,
            NullLogger<HomeAssistantTool>.Instance);

        var response = await service.GetCupraInfoAsync().ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response for Cupra Info: {response}");
    }
}