using System.Globalization;
using Wernstrom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using Microsoft.Extensions.AI;
using RSBotWorks.Plugins;
using Microsoft.Extensions.Options;
using RSBotWorks.UniversalAI;
using Anthropic.SDK.Constants;

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

var services = new ServiceCollection();
services.AddLogging(logBuilder => logBuilder.SetupLogging(config));
services.AddSingleton<IConfig>(config)
        .AddSingleton<LoggingHttpHandler>()
        .AddHttpClient(Options.DefaultName);/*.AddHttpMessageHandler<LoggingHttpHandler>();*/ // comment the second part to disable logging
using var provider = services.BuildServiceProvider();

var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

using var chatClient = ChatClient.CreateAnthropicClient(AnthropicModels.Claude4Sonnet, config.ClaudeApiKey, httpClientFactory, provider.GetRequiredService<ILogger<ChatClient>>());
//using var chatClient = ChatClient.CreateOpenAIResponsesClient(OpenAIModel.GPT5, config.OpenAiApiKey, provider.GetRequiredService<ILogger<ChatClient>>());
//using var chatClient = ChatClient.CreateOpenAIClient(OpenAIModel.GPT5Chat, config.OpenAiApiKey, provider.GetRequiredService<ILogger<ChatClient>>());

List<LocalFunction> functions = [];
HomeAssistantPlugin haPlugin = new(httpClientFactory, new HomeAssistantPluginConfig() { HomeAssistantToken = config.HomeAssistantToken, HomeAssistantUrl = config.HomeAssistantUrl },
    provider.GetRequiredService<ILogger<HomeAssistantPlugin>>());
functions.Add(LocalFunction.FromMethod(haPlugin, nameof(HomeAssistantPlugin.GetCarStatusAsync)));
functions.Add(LocalFunction.FromMethod(haPlugin, nameof(HomeAssistantPlugin.GetHealthInfoAsync)));

WeatherPlugin weatherPlugin = new(httpClientFactory, provider.GetRequiredService<ILogger<WeatherPlugin>>(),
    new WeahterPluginConfig() { ApiKey = config.OpenWeatherMapApiKey });
functions.AddRange(LocalFunction.FromObject(weatherPlugin));

NewsPlugin newsPlugin = new(httpClientFactory, provider.GetRequiredService<ILogger<NewsPlugin>>());
functions.AddRange(LocalFunction.FromObject(newsPlugin));

FortunePlugin fortunePlugin = new();
functions.AddRange(LocalFunction.FromObject(fortunePlugin));

YoutubePlugin youtubePlugin = new(provider.GetRequiredService<ILogger<YoutubePlugin>>(), config.GeminiApiKey, config.SocialKitApiKey, httpClientFactory);
functions.Add(LocalFunction.FromMethod(youtubePlugin, nameof(YoutubePlugin.SummarizeVideoAsync)));

using WernstromService wernstrom = new(provider.GetRequiredService<ILogger<WernstromService>>(),
    httpClientFactory, config.DiscordToken, chatClient, functions);

try
{
    await wernstrom.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
}
catch (Exception ex)
{
    Console.WriteLine("Critical error during execution. The application will terminate. " + ex.Message);
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
        builder.AddFilter(typeof(WeatherPlugin).FullName, LogLevel.Information);
        builder.AddFilter(typeof(NewsPlugin).FullName, LogLevel.Information);
        builder.AddFilter(typeof(HomeAssistantPlugin).FullName, LogLevel.Information);
        builder.AddFilter(typeof(LoggingHttpHandler).FullName, LogLevel.Information);
        builder.AddFilter(typeof(WernstromService).FullName, LogLevel.Debug);
        builder.SetMinimumLevel(LogLevel.Warning);
        builder.AddSeq(config.SeqUrl, config.SeqApiKey);
        return builder;
    }
}