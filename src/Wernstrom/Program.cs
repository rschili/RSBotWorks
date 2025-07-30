using System.Globalization;
using Wernstrom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using RSBotWorks;
using Microsoft.Extensions.AI;
using Anthropic.SDK;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using RSBotWorks.Plugins;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Options;

Console.WriteLine($"Current user: {Environment.UserName}");
Console.WriteLine("Loading config...");
var config = Config.LoadFromEnvFile();

SetGermanCulture();

string dbPath = config.SqliteDbPath;
string dbDirectory = Path.GetDirectoryName(dbPath) ?? throw new InvalidOperationException("Database path is invalid or null.");
if (!Directory.Exists(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

var builder = Host.CreateApplicationBuilder();
var services = builder.Services;
builder.Logging.SetupLogging(config);
services.AddSingleton<IConfig>(config)
        .AddHttpClient(Options.DefaultName).AddHttpMessageHandler<LoggingHttpHandler>(); // comment the second part to disable logging
services.AddKernel().SetupKernel(config);
using var host = builder.Build();

/*chatService = kernel.GetRequiredService<IChatCompletionService>(); // re-get service because it may be wrapped in a proxy
await chatService.GetChatMessageContentAsync(new ChatHistory()
{
    new ChatMessageContent(AuthorRole.Developer, "My System Prompt")
},
null, kernel);*/


try
{
    var messageCache = await SqliteMessageCache.CreateAsync(config.SqliteDbPath).ConfigureAwait(false);
    await host.RunAsync();
    /*using var runner = new Runner(
        loggerFactory.CreateLogger<Runner>(),
        httpClientFactory,
        config,
        messageCache,
        openAIService);
    await runner.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);*/
}
catch (Exception ex)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Critical error during execution. The application will terminate.");
    throw;
}

static void SetGermanCulture()
{
    var cultureInfo = new CultureInfo("de-DE");
    CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
    CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
    Console.WriteLine($"Current culture: {CultureInfo.CurrentCulture.Name}");
}

public static class BuilderExtensions
{
    public static ILoggingBuilder SetupLogging(this ILoggingBuilder builder, IConfig config)
    {
        builder.ClearProviders();
        builder.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = false;
            options.TimestampFormat = "hh:mm:ss ";
            options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
        });
        builder.AddFilter("RSBotWorks.OpenAIService", LogLevel.Information);
        builder.AddFilter("RSBotWorks.Tools.WeatherToolProvider", LogLevel.Information);
        builder.AddFilter("RSBotWorks.Tools.NewsToolProvider", LogLevel.Information);
        builder.AddFilter("RSBotWorks.Tools.HomeAssistantToolProvider", LogLevel.Information);
        builder.SetMinimumLevel(LogLevel.Warning);
        builder.AddSeq(config.SeqUrl, config.SeqApiKey);
        return builder;
    }

    public static IKernelBuilder SetupKernel(this IKernelBuilder builder, IConfig config)
    {
        IChatClient client = new AnthropicClient(new APIAuthentication(config.ClaudeApiKey)).Messages.AsBuilder().UseFunctionInvocation().Build();
        var chatService = client.AsChatCompletionService();
        builder.Services.AddSingleton(chatService);
        builder.Services.AddSingleton(new HomeAssistantPluginConfig()
        {
            HomeAssistantUrl = config.HomeAssistantUrl,
            HomeAssistantToken = config.HomeAssistantToken
        });
        builder.Services.AddSingleton(new WeahterPluginConfig() { ApiKey = config.OpenWeatherMapApiKey });

        var plugins = builder.Plugins;
        plugins.AddFromType<LightsPlugin>()
               .AddFromType<HomeAssistantPlugin>()
               .AddFromType<ListPluginsPlugin>()
               .AddFromType<NewsPlugin>()
               .AddFromType<WeatherPlugin>();
        return builder;
    }
}