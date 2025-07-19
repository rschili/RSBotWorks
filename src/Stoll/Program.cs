using System.Globalization;
using Stoll;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RSBotWorks.Tools;
using RSBotWorks;

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
services.ConfigureLogging(config)
        .AddHttpClient();
using var serviceProvider = services.BuildServiceProvider();

var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

var weatherProvider = new WeatherToolProvider(
    httpClientFactory,
    loggerFactory.CreateLogger<WeatherToolProvider>(),
    config.OpenWeatherMapApiKey);

var newsProvider = new NewsToolProvider(
    httpClientFactory,
    loggerFactory.CreateLogger<NewsToolProvider>());

var homeAssistantProvider = new HomeAssistantToolProvider(
    httpClientFactory,
    config.HomeAssistantUrl,
    config.HomeAssistantToken,
    loggerFactory.CreateLogger<HomeAssistantToolProvider>());

var toolHub = new ToolHub(loggerFactory.CreateLogger<ToolHub>());
toolHub.RegisterToolProvider(weatherProvider);
toolHub.RegisterToolProvider(newsProvider);
toolHub.RegisterToolProvider(homeAssistantProvider);
toolHub.EnableWebSearch = true; // Enable web search by default

var credentials = new AIServiceCredentials(
    OpenAIKey: config.OpenAiApiKey,
    ClaudeKey: null,
    MoonshotKey: null // Moonshot AI key is optional
);

var openAIService = AIServiceFactory.CreateService(
    AIModel.GPT41,
    credentials,
    toolHub,
    httpClientFactory,
    loggerFactory.CreateLogger<OpenAIService>());

try
{
    var messageCache = await SqliteMessageCache.CreateAsync(config.SqliteDbPath).ConfigureAwait(false);
    var runner = new Runner(
        loggerFactory.CreateLogger<Runner>(),
        config,
        httpClientFactory,
        messageCache,
        openAIService);
    await runner.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
}
catch (Exception ex)
{
    loggerFactory.CreateLogger<Program>().LogCritical(ex, "Application failed to start or run");
    throw;
}


static void SetGermanCulture()
{
    var cultureInfo = new CultureInfo("de-DE");
    CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
    CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
    Console.WriteLine($"Current culture: {CultureInfo.CurrentCulture.Name}");
}

public static class ServiceExtensions
{
    public static IServiceCollection ConfigureLogging(this IServiceCollection services, Config config)
    {
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = false;
                options.TimestampFormat = "hh:mm:ss ";
                options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
            });
            loggingBuilder.AddFilter("RSBotWorks.OpenAIService", LogLevel.Information);
            loggingBuilder.AddFilter("RSBotWorks.Tools.WeatherToolProvider", LogLevel.Information);
            loggingBuilder.AddFilter("RSBotWorks.Tools.NewsToolProvider", LogLevel.Information);
            loggingBuilder.AddFilter("RSBotWorks.Tools.HomeAssistantToolProvider", LogLevel.Information);
            loggingBuilder.SetMinimumLevel(LogLevel.Warning);
            loggingBuilder.AddSeq(config.SeqUrl, config.SeqApiKey);
        });

        return services;
    }
}