using NSubstitute;
using DotNetEnv.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core.Logging;
using System.Globalization;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Tests;

public class DiscordTests
{
    [Test, Explicit]
    public async Task GenerateStatusMessages()
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

        var sql = await SqliteMessageCache.CreateAsync(":memory:"); ;
        await Assert.That(sql).IsNotNull();

        List<LocalFunction> tools = [];
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var chatClient = ChatClient.CreateOpenAIClient(OpenAIModel.GPT41, openAiKey);

        var config = Wernstrom.Config.LoadFromEnvFile();
        await Assert.That(config).IsNotNull();
        var discordService = new Wernstrom.WernstromService(NullLogger<Wernstrom.WernstromService>.Instance, httpClientFactory, "", chatClient, null);

        var statusMessages = await discordService.CreateNewStatusMessages();
        await Assert.That(statusMessages).IsNotNull();
        await Assert.That(statusMessages).IsNotEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {string.Join("\n", statusMessages)}");
    }
}