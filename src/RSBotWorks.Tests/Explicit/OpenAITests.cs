using NSubstitute;
using DotNetEnv.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core.Logging;
using System.Globalization;
using RSBotWorks;
using RSBotWorks.Tools;

namespace RSBotWorks.Tests;

public class OpenAITests
{
    [Test, Explicit]
    public async Task SendGenericRequest()
    {
        var cultureInfo = new CultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string openAiKey = env["OPENAI_API_KEY"];
        if (string.IsNullOrEmpty(openAiKey))
        {
            Assert.Fail("OPENAI_API_KEY is not set in the .env file.");
            return;
        }

        var aiService = AIServiceFactory.CreateService(AIModel.GPT41, openAiKey, new ToolHub());

        List<AIMessage> messages = new()
        {
            new AIMessage(false, "Hey, wir geht's?", "sikk"),
            new AIMessage(true, "Hi [sikk], mir geht's gut, danke!", "Wernstrom"),
            new AIMessage(false, "Was ist dein Lieblingsessen?", "sikk"),
        };
        var response = await aiService.GenerateResponseAsync(Wernstrom.Runner.DEFAULT_INSTRUCTION, messages).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }

    [Test, Explicit]
    public async Task SendWeatherToolRequest()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string openAiKey = env["OPENAI_API_KEY"];
        if (string.IsNullOrEmpty(openAiKey))
        {
            Assert.Fail("OPENAI_API_KEY is not set in the .env file.");
            return;
        }
        string openWeatherMapKey = env["OPENWEATHERMAP_API_KEY"];
        if (string.IsNullOrEmpty(openWeatherMapKey))
        {
            Assert.Fail("OPENWEATHERMAP_API_KEY is not set in the .env file.");
            return;
        }

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var toolService = new ToolHub();
        toolService.RegisterToolProvider(new WeatherToolProvider(
            httpClientFactory,
            null,
            openWeatherMapKey));
        var aiService = AIServiceFactory.CreateService(AIModel.GPT41, openAiKey, toolService);

        List<AIMessage> messages = new()
        {
            new AIMessage(false, "Hey, wie ist das Wetter in Dielheim?", "sikk"),
            new AIMessage(true, "Das Wetter in Dielheim ist sonnig und warm.", "Wernstrom"),
            new AIMessage(false, "Und in Heidelberg?", "sikk"),
        };
        var response = await aiService.GenerateResponseAsync(Wernstrom.Runner.DEFAULT_INSTRUCTION, messages).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }

    [Test, Explicit]
    public async Task RequestWebSearch()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string openAiKey = env["OPENAI_API_KEY"];
        if (string.IsNullOrEmpty(openAiKey))
        {
            Assert.Fail("OPENAI_API_KEY is not set in the .env file.");
            return;
        }

        var toolHub = new ToolHub();
        toolHub.EnableWebSearch = true;
        var aiService = AIServiceFactory.CreateService(AIModel.GPT41, openAiKey, toolHub);

        List<AIMessage> messages = new()
        {
            new AIMessage(false, "Suche bitte im Netz nach Kinofilmen, die nächste Woche erscheinen.", "sikk"),
        };
        var response = await aiService.GenerateResponseAsync(Wernstrom.Runner.DEFAULT_INSTRUCTION, messages).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }

    [Test, Explicit]
    public async Task SendCarToolRequest()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string openAiKey = env["OPENAI_API_KEY"];
        if (string.IsNullOrEmpty(openAiKey))
        {
            Assert.Fail("OPENAI_API_KEY is not set in the .env file.");
            return;
        }
        string homeAssistantUrl = env["HA_API_URL"];
        if (string.IsNullOrEmpty(homeAssistantUrl))
        {
            Assert.Fail("HA_API_URL is not set in the .env file.");
            return;
        }
        string homeAssistantToken = env["HA_TOKEN"];
        if (string.IsNullOrEmpty(homeAssistantToken))
        {
            Assert.Fail("HA_TOKEN is not set in the .env file.");
            return;
        }

        var toolHub = new ToolHub();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        toolHub.RegisterToolProvider(new HomeAssistantToolProvider(httpClientFactory, homeAssistantUrl, homeAssistantToken));
        var aiService = AIServiceFactory.CreateService(AIModel.GPT41, openAiKey, toolHub);

        List<AIMessage> messages = new()
        {
            new AIMessage(false, "Hey, wie ist das Wetter in Dielheim?", "sikk"),
            new AIMessage(true, "Das Wetter in Dielheim ist sonnig und warm.", "Wernstrom"),
            new AIMessage(false, "Wieviel Ladung hat mein Auto gerade?", "krael"),
        };
        var response = await aiService.GenerateResponseAsync(Wernstrom.Runner.DEFAULT_INSTRUCTION, messages).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }
}

