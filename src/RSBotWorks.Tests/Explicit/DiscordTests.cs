using NSubstitute;
using DotNetEnv.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core.Logging;
using System.Globalization;
using RSBotWorks.Tools;

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

        var toolService = new ToolHub();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var credentials = new AIServiceCredentials(openAiKey);
        var aiService = AIServiceFactory.CreateService(AIModel.GPT41, credentials, toolService, httpClientFactory);

        var config = Wernstrom.Config.LoadFromEnvFile();
        await Assert.That(config).IsNotNull();
        var discordService = new Wernstrom.Runner(NullLogger<Wernstrom.Runner>.Instance, httpClientFactory, config, sql, aiService);

        var statusMessages = await discordService.CreateNewStatusMessages();
        await Assert.That(statusMessages).IsNotNull();
        await Assert.That(statusMessages).IsNotEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {string.Join("\n", statusMessages)}");
    }
}