using System.Globalization;
using Stoll;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using RSBotWorks.SaneAI;
using RSBotWorks.UniversalAI;
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
services.AddLogging(logBuilder => logBuilder.SetupLogging(config));
services.AddHttpClient();
using var serviceProvider = services.BuildServiceProvider();

var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

var executor = new DefaultHttpExecutor(httpClientFactory);
var aiClient = new AnthropicClient(config.ClaudeApiKey, executor);

List<LocalFunction> functions = [];
WeatherPlugin weatherPlugin = new(httpClientFactory, serviceProvider.GetRequiredService<ILogger<WeatherPlugin>>(),
    new WeahterPluginConfig() { ApiKey = config.OpenWeatherMapApiKey });
functions.AddRange(LocalFunction.FromObject(weatherPlugin));

NewsPlugin newsPlugin = new(httpClientFactory, serviceProvider.GetRequiredService<ILogger<NewsPlugin>>());
functions.AddRange(LocalFunction.FromObject(newsPlugin));

TextPlugin textPlugin = new();
functions.AddRange(LocalFunction.FromObject(textPlugin));

StollService stoll = new(serviceProvider.GetRequiredService<ILoggerFactory>(),
    config.MatrixUserId, config.MatrixPassword, httpClientFactory, aiClient, functions);

try
{
    await stoll.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
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
    public static ILoggingBuilder SetupLogging(this ILoggingBuilder builder, Config config)
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
        builder.AddFilter("System.Net.Http", LogLevel.Warning);
        builder.AddFilter(typeof(StollService).FullName, LogLevel.Information);
        builder.AddFilter($"{typeof(StollService).FullName}.Matrix", LogLevel.Warning);
        builder.SetMinimumLevel(LogLevel.Warning);
        builder.AddSeq(config.SeqUrl, config.SeqApiKey);
        return builder;
    }
}