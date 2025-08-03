using NSubstitute;
using DotNetEnv.Extensions;
using TUnit.Core.Logging;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.UniversalAI;
using RSBotWorks.Plugins;
using System.Text.Json;

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
        
        var weatherPlugin = new WeatherPlugin(httpClientFactory, NullLogger<WeatherPlugin>.Instance, new WeahterPluginConfig() { ApiKey = apiKey });
        var functions = LocalFunction.FromObject(weatherPlugin);
        var currentWeatherFunction = functions.FirstOrDefault(f => f.Name == "get_current_weather");
        
        if (currentWeatherFunction == null)
        {
            Assert.Fail("get_current_weather function not found");
            return;
        }

        using var args = JsonDocument.Parse("""{"location": "Dielheim"}""");
        var response = await currentWeatherFunction.ExecuteAsync(args).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response for Dielheim: {response}");

        using var args2 = JsonDocument.Parse("""{"location": "69234"}""");
        var response2 = await currentWeatherFunction.ExecuteAsync(args2).ConfigureAwait(false);
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
        
        var weatherPlugin = new WeatherPlugin(httpClientFactory, NullLogger<WeatherPlugin>.Instance, new WeahterPluginConfig() { ApiKey = apiKey });
        var functions = LocalFunction.FromObject(weatherPlugin);
        var forecastFunction = functions.FirstOrDefault(f => f.Name == "weather_forecast");
        
        if (forecastFunction == null)
        {
            Assert.Fail("get_weather_forecast function not found");
            return;
        }

        using var args = JsonDocument.Parse("""{"location": "Dielheim"}""");
        var response = await forecastFunction.ExecuteAsync(args).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response for Dielheim: {response}");

        using var args2 = JsonDocument.Parse("""{"location": "69234"}""");
        var response2 = await forecastFunction.ExecuteAsync(args2).ConfigureAwait(false);
        await Assert.That(response2).IsNotNullOrEmpty();
        if (logger != null)
            await logger.LogInformationAsync($"Response for ZIP 69234: {response2}");
    }

    [Test, Explicit]
    public async Task ObtainHeiseHeadlines()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        
        var newsPlugin = new NewsPlugin(httpClientFactory, NullLogger<NewsPlugin>.Instance);
        var functions = LocalFunction.FromObject(newsPlugin);
        var heiseFunction = functions.FirstOrDefault(f => f.Name == "heise_headlines");
        
        if (heiseFunction == null)
        {
            Assert.Fail("heise_headlines function not found");
            return;
        }

        using var args = JsonDocument.Parse("""{"count": 10}""");
        var response = await heiseFunction.ExecuteAsync(args).ConfigureAwait(false);
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
        
        var newsPlugin = new NewsPlugin(httpClientFactory, NullLogger<NewsPlugin>.Instance);
        var functions = LocalFunction.FromObject(newsPlugin);
        var postillonFunction = functions.FirstOrDefault(f => f.Name == "postillon_headlines");
        
        if (postillonFunction == null)
        {
            Assert.Fail("postillon_headlines function not found");
            return;
        }

        using var args = JsonDocument.Parse("""{"count": 30}""");
        var response = await postillonFunction.ExecuteAsync(args).ConfigureAwait(false);
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
        
        var homeAssistantPlugin = new HomeAssistantPlugin(httpClientFactory, new HomeAssistantPluginConfig() { HomeAssistantUrl = url, HomeAssistantToken = token }, NullLogger<HomeAssistantPlugin>.Instance);
        var functions = LocalFunction.FromObject(homeAssistantPlugin);
        var cupraFunction = functions.FirstOrDefault(f => f.Name == "get_cupra_info");
        
        if (cupraFunction == null)
        {
            Assert.Fail("get_cupra_info function not found");
            return;
        }

        using var args = JsonDocument.Parse("""{}""");
        var response = await cupraFunction.ExecuteAsync(args).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response for Cupra Info: {response}");
    }

    [Test, Explicit]
    public async Task SupplementCityNameInGermany()
    {
        var lat = 49.2824981;
        var lon = 8.7351709;
        var cityName = "Dielheim";
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        var tool = new PlaceUtility(httpClientFactory);
        var result = await tool.TrySupplement(cityName, lat, lon).ConfigureAwait(false);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.SupplementedCityName).IsEqualTo("69234 Dielheim (Baden-Württemberg)");
    }
}