using NSubstitute;
using DotNetEnv.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core.Logging;
using System.Globalization;
using RSBotWorks.SaneAI;
using RSBotWorks.UniversalAI;
using Wernstrom;

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
        string claudeKey = env["CLAUDE_API_KEY"];
        if (string.IsNullOrEmpty(claudeKey))
        {
            Assert.Fail("CLAUDE_API_KEY is not set in the .env file.");
            return;
        }

        var sql = await SqliteMessageCache.CreateAsync(":memory:"); ;
        await Assert.That(sql).IsNotNull();

        var services = new ServiceCollection();
        services.AddHttpClient();
        using var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        var executor = new DefaultHttpExecutor(httpClientFactory);
        var aiClient = new AnthropicClient(claudeKey, executor);

        var wernstromConfig = new WernstromServiceConfig()
        {
            DiscordToken = "",
            BrueckeId = 32434534ul,
            MaschinenraumId = 32434535ul
        };
        var discordService = new Wernstrom.WernstromService(NullLogger<Wernstrom.WernstromService>.Instance, httpClientFactory, wernstromConfig, aiClient, null);

        var statusMessages = await discordService.CreateNewStatusMessages();
        await Assert.That(statusMessages).IsNotNull();
        await Assert.That(statusMessages).IsNotEmpty();
        var logger = TestContext.Current?.GetDefaultLogger();
        if (logger != null)
            await logger.LogInformationAsync($"Response: {string.Join("\n", statusMessages)}");
    }
}