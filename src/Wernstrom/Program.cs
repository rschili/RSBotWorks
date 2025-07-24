using System.Globalization;
using Wernstrom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using Microsoft.Extensions.AI;
using Anthropic.SDK;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using RSBotWorks.Plugins;

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


IChatClient client = new AnthropicClient(new APIAuthentication(config.ClaudeApiKey)).Messages.AsBuilder().UseKernelFunctionInvocation().Build();
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
var chatService = client.AsChatCompletionService();
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
var builder = Kernel.CreateBuilder();
builder.Services.AddSingleton(chatService);
var plugins = builder.Plugins;

var lightsPlugin = new LightsPlugin();
plugins.AddFromObject(lightsPlugin);

var homeAssistantPlugin = new HomeAssistantPlugin(httpClientFactory,
    new HomeAssistantPluginConfig() { HomeAssistantUrl = config.HomeAssistantUrl, HomeAssistantToken = config.HomeAssistantToken });
plugins.AddFromObject(homeAssistantPlugin);

plugins.AddFromType<ListPluginsPlugin>();
plugins.AddFromType<NewsPlugin>();
var weatherPlugin = new WeatherPlugin(httpClientFactory, loggerFactory.CreateLogger<WeatherPlugin>(), config.OpenWeatherMapApiKey);
plugins.AddFromObject(weatherPlugin);
Kernel kernel = builder.Build();

// TODO: Enable WebSearch
ChatOptions options = new()
{
    ModelId = AnthropicModels.Claude4Sonnet,
    MaxOutputTokens = 1000,
    Temperature = 0.6f,
};

chatService = kernel.GetRequiredService<IChatCompletionService>(); // re-get service because it may be wrapped in a proxy

try
{
    var messageCache = await SqliteMessageCache.CreateAsync(config.SqliteDbPath).ConfigureAwait(false);
    using var runner = new Runner(
        loggerFactory.CreateLogger<Runner>(),
        httpClientFactory,
        config,
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