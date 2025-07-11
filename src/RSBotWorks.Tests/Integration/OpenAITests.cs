using RSHome.Services;
using NSubstitute;
using DotNetEnv.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core.Logging;
using System.Globalization;

namespace RSHome.Tests.Integration;

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

        var config = Substitute.For<IConfigService>();
        config.OpenAiApiKey.Returns(openAiKey);
        var toolService = Substitute.For<IToolService>();
        var aiService = new OpenAIService(config, NullLogger<OpenAIService>.Instance, toolService);

        List<AIMessage> messages = new()
        {
            new AIMessage(false, "Hey, wir geht's?", "sikk"),
            new AIMessage(true, "Hi [sikk], mir geht's gut, danke!", "Wernstrom"),
            new AIMessage(false, "Was ist dein Lieblingsessen?", "sikk"),
        };
        var response = await aiService.GenerateResponseAsync(DiscordWorkerService.DEFAULT_INSTRUCTION, messages).ConfigureAwait(false);
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

        var config = Substitute.For<IConfigService>();
        config.OpenAiApiKey.Returns(openAiKey);
        var toolService = Substitute.For<IToolService>();
        toolService.GetCurrentWeatherAsync(Arg.Any<string>())
            .Returns(callInfo => Task.FromResult($"Das Wetter in {callInfo.Arg<string>()} ist kalt und eisig."));
        var aiService = new OpenAIService(config, NullLogger<OpenAIService>.Instance, toolService);

        List<AIMessage> messages = new()
        {
            new AIMessage(false, "Hey, wie ist das Wetter in Dielheim?", "sikk"),
            new AIMessage(true, "Das Wetter in Dielheim ist sonnig und warm.", "Wernstrom"),
            new AIMessage(false, "Und in Heidelberg?", "sikk"),
        };
        var response = await aiService.GenerateResponseAsync(DiscordWorkerService.DEFAULT_INSTRUCTION, messages).ConfigureAwait(false);
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

        var config = Substitute.For<IConfigService>();
        config.OpenAiApiKey.Returns(openAiKey);
        var toolService = Substitute.For<IToolService>();
        var aiService = new OpenAIService(config, NullLogger<OpenAIService>.Instance, toolService);

        List<AIMessage> messages = new()
        {
            new AIMessage(false, "Suche bitte im Netz nach Kinofilmen, die nächste Woche erscheinen.", "sikk"),
        };
        var response = await aiService.GenerateResponseAsync(DiscordWorkerService.DEFAULT_INSTRUCTION, messages).ConfigureAwait(false);
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

        var config = Substitute.For<IConfigService>();
        config.OpenAiApiKey.Returns(openAiKey);
        var toolService = Substitute.For<IToolService>();
        toolService.GetCupraInfoAsync()
            .Returns(callInfo => Task.FromResult($"Der Cupra Born ist aktuell zu 55% geladen und hat eine Reichweite von 250 km."));
        var aiService = new OpenAIService(config, NullLogger<OpenAIService>.Instance, toolService);

        List<AIMessage> messages = new()
        {
            new AIMessage(false, "Hey, wie ist das Wetter in Dielheim?", "sikk"),
            new AIMessage(true, "Das Wetter in Dielheim ist sonnig und warm.", "Wernstrom"),
            new AIMessage(false, "Wieviel Ladung hat mein Auto gerade?", "krael"),
        };
        var response = await aiService.GenerateResponseAsync(DiscordWorkerService.DEFAULT_INSTRUCTION, messages).ConfigureAwait(false);
        await Assert.That(response).IsNotNullOrEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {response}");
    }
}

