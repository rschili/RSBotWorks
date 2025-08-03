using NSubstitute;
using DotNetEnv.Extensions;
using TUnit.Core.Logging;
using System.Globalization;
using RSBotWorks.UniversalAI;
using RSBotWorks.Plugins;
using Anthropic.SDK.Constants;

namespace RSBotWorks.Tests;

public class AnthropicTests
{
    [Test, Explicit]
    public async Task SendGenericRequest()
    {
        var cultureInfo = new CultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string apiKey = env["CLAUDE_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
        {
            Assert.Fail("CLAUDE_API_KEY is not set in the .env file.");
            return;
        }

        var chatClient = ChatClient.CreateAnthropicClient(AnthropicModels.Claude4Sonnet, apiKey, null);

        List<Message> messages = [
            Message.FromText(Role.User, "[[sikk]]: Hey, wir geht's?"),
            Message.FromText(Role.Assistant, "Hallo [[sikk]], mir geht's gut"),
            Message.FromText(Role.User, "[[sikk]]: Was ist dein Lieblingsessen?"),
        ];
        var parameters = new ChatParameters()
        {
            Temperature = 0.7m,
            MaxTokens = 1000,
        };
        var preparedParameters = chatClient.PrepareParameters(parameters);
        var response = await chatClient.CallAsync(Wernstrom.WernstromService.CHAT_INSTRUCTION, messages, preparedParameters).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }

    [Test, Explicit]
    public async Task SendWeatherToolRequest()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string apiKey = env["CLAUDE_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
        {
            Assert.Fail("CLAUDE_API_KEY is not set in the .env file.");
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

        List<LocalFunction> tools = [];
        var weatherPlugin = new WeatherPlugin(httpClientFactory, null, new WeahterPluginConfig() { ApiKey = openWeatherMapKey });
        tools.AddRange(LocalFunction.FromObject(weatherPlugin));
        var chatClient = ChatClient.CreateAnthropicClient(AnthropicModels.Claude4Sonnet, apiKey, null);

        var parameters = new ChatParameters()
        {
            Temperature = 0.7m,
            MaxTokens = 1000,
            ToolChoiceType = ToolChoiceType.Auto,
            AvailableLocalFunctions = tools,
        };

        List<Message> messages = [
            Message.FromText(Role.User, "[[sikk]]: Hey, wie ist das Wetter in Dielheim?"),
            Message.FromText(Role.Assistant, "Das Wetter in Dielheim ist sonnig und warm."),
            Message.FromText(Role.User, "[[sikk]]: Und in Heidelberg?"),
        ];
        var preparedParameters = chatClient.PrepareParameters(parameters);
        var response = await chatClient.CallAsync(Wernstrom.WernstromService.CHAT_INSTRUCTION, messages, preparedParameters).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }

    [Test, Explicit]
    public async Task RequestWebSearch()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string apiKey = env["CLAUDE_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
        {
            Assert.Fail("CLAUDE_API_KEY is not set in the .env file.");
            return;
        }

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        List<LocalFunction> tools = [];
        // Add web search tools here if needed
        var chatClient = ChatClient.CreateAnthropicClient(AnthropicModels.Claude4Sonnet, apiKey, null);

        var parameters = new ChatParameters()
        {
            Temperature = 0.7m,
            MaxTokens = 1000,
            ToolChoiceType = ToolChoiceType.Auto,
            AvailableLocalFunctions = tools,
            EnableWebSearch = true,
        };

        List<Message> messages = [
            Message.FromText(Role.User, "[[sikk]]: Suche bitte im Netz nach Kinofilmen, die nächste Woche erscheinen."),
        ];
        var preparedParameters = chatClient.PrepareParameters(parameters);
        var response = await chatClient.CallAsync(Wernstrom.WernstromService.CHAT_INSTRUCTION, messages, preparedParameters).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }

    [Test, Explicit]
    public async Task SendCarToolRequest()
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        string apiKey = env["CLAUDE_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
        {
            Assert.Fail("CLAUDE_API_KEY is not set in the .env file.");
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

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        List<LocalFunction> tools = [];
        var homeAssistantPlugin = new HomeAssistantPlugin(httpClientFactory, new HomeAssistantPluginConfig() { HomeAssistantUrl = homeAssistantUrl, HomeAssistantToken = homeAssistantToken }, null);
        tools.AddRange(LocalFunction.FromObject(homeAssistantPlugin));
        var chatClient = ChatClient.CreateAnthropicClient(AnthropicModels.Claude4Sonnet, apiKey, null);

        var parameters = new ChatParameters()
        {
            Temperature = 0.7m,
            MaxTokens = 1000,
            ToolChoiceType = ToolChoiceType.Auto,
            AvailableLocalFunctions = tools,
        };

        List<Message> messages = [
            Message.FromText(Role.User, "[[sikk]]: Hey, wie ist das Wetter in Dielheim?"),
            Message.FromText(Role.Assistant, "Das Wetter in Dielheim ist sonnig und warm."),
            Message.FromText(Role.User, "[[krael]]: Wieviel Ladung hat mein Auto gerade?"),
        ];
        var preparedParameters = chatClient.PrepareParameters(parameters);
        var response = await chatClient.CallAsync(Wernstrom.WernstromService.CHAT_INSTRUCTION, messages, preparedParameters).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }
}

