using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Security.Cryptography.X509Certificates;
using RSHome.Services;
using Microsoft.AspNetCore.Builder;
using System.Text;
using System.Globalization;

Console.WriteLine($"Current user: {Environment.UserName}");
Console.WriteLine("Loading variables...");
var config = ConfigService.LoadFromEnvFile();

var cultureInfo = new CultureInfo("de-DE");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
Console.WriteLine($"Current culture: {CultureInfo.CurrentCulture.Name}");

string dbPath = config.SqliteDbPath;
string dbDirectory = Path.GetDirectoryName(dbPath) ?? throw new InvalidOperationException("Database path is invalid or null.");
if (!Directory.Exists(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}
using (StreamWriter sw = new(Path.Combine(dbDirectory, "log.txt"), append: true))
{
    sw.WriteLine($"Starting RSHome at {DateTime.UtcNow}");
}

var builder = WebApplication.CreateBuilder(args);
foreach (var s in builder.Configuration.Sources)
{ // fix a problem with linux running out of file system watchers. https://stackoverflow.com/questions/56360697/how-can-i-disable-the-defaul-aspnet-core-config-change-watcher
    if (s is FileConfigurationSource)
        ((FileConfigurationSource)s).ReloadOnChange = false;
}

builder.Logging
    .ClearProviders()
    .AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.SingleLine = false;
        options.TimestampFormat = "hh:mm:ss ";
        options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
    })
    .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning) // Filter logs from HttpClient
    .AddFilter("RSHome.Services.MatrixWorkerService", LogLevel.Warning)
    .AddFilter("RSHome.Services.OpenAIService", LogLevel.Information)
    .AddFilter("RSHome.Services.ToolService", LogLevel.Information)
    .SetMinimumLevel(LogLevel.Warning)
    .AddSeq(config.SeqUrl, config.SeqApiKey);

var sqliteService = await SqliteMessageCache.CreateAsync(config).ConfigureAwait(false);
builder.Services
    .AddSingleton<IConfigService>(config)
    .AddSingleton<SecurityService>()
    .AddSingleton<WordleService>()
    .AddHttpClient()
    .AddSingleton<ISqliteService>(sqliteService)
    .AddSingleton<IToolService, ToolService>()
    .AddSingleton<OpenAIService>()
    .AddSingleton<DiscordWorkerService>()
    .AddHostedService(p => p.GetRequiredService<DiscordWorkerService>())
    .AddSingleton<MatrixWorkerService>()
    .AddHostedService(p => p.GetRequiredService<MatrixWorkerService>())
    .AddRazorPages(options => {
        options.RootDirectory = "/Pages";
        options.Conventions.AuthorizeFolder("/", "admin");
        options.Conventions.AllowAnonymousToPage("/Index");
        options.Conventions.AllowAnonymousToPage("/Login");
        options.Conventions.AllowAnonymousToPage("/Wordle");
    });

builder.Host.ConfigureHostOptions(hostOptions =>
    {
        hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    });
builder.Services
    .AddAntiforgery()
    //.AddHealthChecks();
    //AddProblemDetails();
    .AddAuthentication()
        .AddCookie(options =>
        {
            options.LoginPath = "/Login";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = true;
        });

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(config.WebKeyStore))
    .SetApplicationName("RSHome");

builder.Services.AddHttpClient(); // for matrix

builder.Services.AddAuthorizationBuilder().AddPolicy("admin", policy => policy.RequireRole("admin"));

var x509 = X509CertificateLoader.LoadPkcs12FromFile(config.CertificatePath, config.PfxPassword);
builder.WebHost.ConfigureKestrel(options =>
{
    // options.ListenAnyIP(5000); // HTTP port
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps(x509);
    });
});

using var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
app
    .Use(async (context, next) =>
    {
        await next.Invoke();
        if(context.Response.StatusCode >= 400)
            logger.LogInformation("{Method} for {Path} from {IP} resulted in HTTP {StatusCode}",
                context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress, context.Response.StatusCode);
    });



app.MapStaticAssets();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapRazorPages();

/*app.MapGet("/list-routes", (IEnumerable<EndpointDataSource> endpointSources, EndpointDataSource etc) =>
{
    var sb = new StringBuilder();
    foreach (var source in endpointSources)
    {
        foreach (var endpoint in source.Endpoints)
        {
            sb.AppendLine($"Type: {endpoint.ToString()} Label: { endpoint.DisplayName}");
            foreach(var metadata in endpoint.Metadata)
            {
                sb.AppendLine($"       Metadata: {metadata}");
            }
        }
    }

    return sb.ToString();
}).RequireAuthorization("admin");*/

app.Run();
