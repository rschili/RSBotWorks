using RSHome.Services;
using NSubstitute;
using DotNetEnv.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core.Logging;
using System.Globalization;

namespace RSHome.Tests.Integration;

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

        var config = Substitute.For<IConfigService>();
        config.OpenAiApiKey.Returns(openAiKey);
        config.SqliteDbPath.Returns(":memory:"); // Use in-memory database for testing
        var sql = Substitute.For<ISqliteService>();

        var toolService = Substitute.For<IToolService>();
        var aiService = new OpenAIService(config, NullLogger<OpenAIService>.Instance, toolService);

        var discordService = new DiscordWorkerService(NullLogger<DiscordWorkerService>.Instance, config, sql, aiService);

        var statusMessages = await discordService.CreateNewStatusMessages();
        await Assert.That(statusMessages).IsNotNull();
        await Assert.That(statusMessages).IsNotEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {string.Join("\n", statusMessages)}");
    }

    [Test]
    public async Task ChanceToReact()
    {
        for (int i = 0; i < 60; i+=2)
        {
            double chance = DiscordWorkerService.CalculateChanceToReact(i);
            await TestContext.Current!.GetDefaultLogger().LogInformationAsync($"Minutes: {i}, Chance: {chance}");
        }
    }

}