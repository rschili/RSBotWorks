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
using Microsoft.Extensions.Options;
using System.Reflection.Metadata.Ecma335;

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
        .AddSingleton<LoggingHttpHandler>()
        .AddHttpClient(Options.DefaultName).AddHttpMessageHandler<LoggingHttpHandler>(); // comment the second part to disable logging
services.AddKernel().SetupKernel(config);

var wernstromConfig = new WernstromServiceConfig()
{
    DiscordToken = config.DiscordToken,
};
services.AddSingleton(wernstromConfig);
services.AddHostedService<WernstromService>();
using var host = builder.Build();

try
{
    await host.RunAsync();
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

    public static IKernelBuilder SetupKernel(this IKernelBuilder builder, IConfig config)
    {
        builder.Services.AddSingleton<IChatCompletionService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(Options.DefaultName);
            IChatClient client = new AnthropicClient(new APIAuthentication(config.ClaudeApiKey), httpClient).Messages.AsBuilder().UseFunctionInvocation().Build();
            var chatService = client.AsChatCompletionService();
            return chatService;
        });
        builder.Services.AddSingleton(new HomeAssistantPluginConfig()
        {
            HomeAssistantUrl = config.HomeAssistantUrl,
            HomeAssistantToken = config.HomeAssistantToken
        });
        builder.Services.AddSingleton(new WeahterPluginConfig() { ApiKey = config.OpenWeatherMapApiKey });

        var plugins = builder.Plugins;
        plugins//.AddFromType<LightsPlugin>()
               .AddFromType<HomeAssistantPlugin>()
               //.AddFromType<ListPluginsPlugin>()
               .AddFromType<NewsPlugin>()
               .AddFromType<WeatherPlugin>();

        return builder;
    }
}