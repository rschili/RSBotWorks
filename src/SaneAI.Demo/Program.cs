using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using DotNetEnv.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RSBotWorks.SaneAI;
using RSBotWorks.UniversalAI;

// --- Setup ---
var cultureInfo = new CultureInfo("de-DE");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
string apiKey = env.TryGetValue("CLAUDE_API_KEY", out var key) ? key : "";
if (string.IsNullOrEmpty(apiKey))
{
    Console.Error.WriteLine("CLAUDE_API_KEY is not set in a .env file. Cannot continue.");
    return 1;
}

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
        options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
    });
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddHttpClient();
using var provider = services.BuildServiceProvider();

var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
var logger = provider.GetRequiredService<ILogger<Program>>();

// --- Create the SaneAI client ---
var executor = new DefaultHttpExecutor(httpClientFactory);
var client = new AnthropicClient(apiKey, executor);

// --- Register some tools to demonstrate tool calling ---
var demoTools = LocalFunction.FromObject(new DemoTools());
var toolDefinitions = demoTools.Select(ToolDefinition.FromLocalFunction).ToArray();
var toolMap = demoTools.ToDictionary(f => f.Name);

// Tool executor callback — the client calls this automatically during the loop
async Task<string> ExecuteTool(ToolCall toolCall)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"  [tool: {toolCall.Name}({toolCall.ArgumentsJson})]");
    Console.ResetColor();

    string result;
    if (toolMap.TryGetValue(toolCall.Name, out var localFunc))
    {
        using var argsDoc = JsonDocument.Parse(toolCall.ArgumentsJson);
        result = await localFunc.ExecuteAsync(argsDoc);
    }
    else
    {
        result = $"Unknown tool: {toolCall.Name}";
    }

    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"  [result: {Truncate(result, 120)}]");
    Console.ResetColor();
    return result;
}

// --- Base composer: reusable template primed with model + system prompt + tools ---
var template = new AnthropicRequestComposer()
    .SetModel("claude-sonnet-4-20250514")
    .SetMaxTokens(2048)
    .SetTemperature(0.7m)
    .SetSystemPrompt("""
        You are a helpful assistant in a console chat demo.
        Keep responses concise (1-3 sentences unless the user asks for more).
        You have access to tools - use them when relevant.
        Respond in the same language the user writes in.
        """)
    .AddTools(toolDefinitions);

// --- Chat loop ---
Console.WriteLine("=== SaneAI Console Chat Demo ===");
Console.WriteLine("Type your message and press Enter. Commands: /quit, /curl, /json, /clear, /web, /noweb");
Console.WriteLine();

var conversation = template.Fork();
bool enableWebSearch = false;
ChatResult? lastResult = null;

while (true)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("You> ");
    Console.ResetColor();

    var input = Console.ReadLine();
    if (input == null || input.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        break;

    // --- Slash commands ---
    if (input.Equals("/curl", StringComparison.OrdinalIgnoreCase))
    {
        if (lastResult != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(CurlGenerator.Generate(lastResult));
            Console.ResetColor();
        }
        else
            Console.WriteLine("(no previous request)");
        continue;
    }
    if (input.Equals("/json", StringComparison.OrdinalIgnoreCase))
    {
        if (lastResult != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("--- Request ---");
            Console.WriteLine(FormatJson(lastResult.Request.Body));
            Console.WriteLine("--- Response ---");
            Console.WriteLine(FormatJson(lastResult.Response.Body));
            Console.ResetColor();
        }
        else
            Console.WriteLine("(no previous request)");
        continue;
    }
    if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
    {
        conversation = template.Fork();
        if (enableWebSearch)
            conversation.EnableWebSearch(maxUses: 3, city: "Heidelberg", country: "DE", timezone: "Europe/Berlin");
        lastResult = null;
        Console.WriteLine("(conversation cleared)");
        continue;
    }
    if (input.Equals("/web", StringComparison.OrdinalIgnoreCase))
    {
        enableWebSearch = true;
        conversation.EnableWebSearch(maxUses: 3, city: "Heidelberg", country: "DE", timezone: "Europe/Berlin");
        Console.WriteLine("(web search enabled)");
        continue;
    }
    if (input.Equals("/noweb", StringComparison.OrdinalIgnoreCase))
    {
        enableWebSearch = false;
        conversation.DisableWebSearch();
        Console.WriteLine("(web search disabled)");
        continue;
    }
    if (string.IsNullOrWhiteSpace(input))
        continue;

    // --- Send message ---
    conversation.AddUserMessage(input);

    try
    {
        // That's it. One call. Tool loops are implicit.
        var result = await client.SendAsync(conversation, ExecuteTool);
        lastResult = result;

        if (!string.IsNullOrEmpty(result.TextContent))
        {
            // Add the final assistant response to conversation history
            conversation.AddAssistantMessage(result.TextContent);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Bot> ");
            Console.ResetColor();
            Console.WriteLine(result.TextContent);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            var usage = result.Usage;
            if (usage != null)
            {
                var toolInfo = result.ToolRoundsExecuted > 0
                    ? $" | {result.ToolRoundsExecuted} tool round(s)"
                    : "";
                Console.WriteLine($"     [{usage.InputTokens}in/{usage.OutputTokens}out tokens{toolInfo} | {result.ModelId}]");
            }
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"(empty response, stop_reason={result.StopReason})");
            Console.ResetColor();
        }
    }
    catch (AnthropicApiException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"API Error: {ex.Message}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Curl: {ex.ToCurl()}");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Exception: {ex.Message}");
        Console.ResetColor();
    }

    Console.WriteLine();
}

Console.WriteLine("Bye!");
return 0;

// --- Helpers ---

static string FormatJson(string? json)
{
    if (string.IsNullOrEmpty(json)) return "(empty)";
    try
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
    catch
    {
        return json;
    }
}

static string Truncate(string text, int maxLength)
    => text.Length <= maxLength ? text : text[..maxLength] + "...";

// --- Demo tools ---

/// <summary>
/// Simple demo tools to show how tool calling works with SaneAI.
/// </summary>
public class DemoTools
{
    [LocalFunction("get_current_time")]
    [Description("Get the current date and time")]
    public Task<string> GetCurrentTimeAsync()
    {
        return Task.FromResult(DateTimeOffset.Now.ToString("dddd, d. MMMM yyyy, HH:mm:ss zzz"));
    }

    [LocalFunction("calculate")]
    [Description("Evaluate a simple math expression. Supports +, -, *, /")]
    public Task<string> CalculateAsync(
        [Description("First number")] double a,
        [Description("The operator: +, -, *, /")] string op,
        [Description("Second number")] double b)
    {
        double result = op switch
        {
            "+" => a + b,
            "-" => a - b,
            "*" => a * b,
            "/" => b != 0 ? a / b : double.NaN,
            _ => double.NaN
        };
        return Task.FromResult(double.IsNaN(result) ? "Error: invalid operation" : result.ToString(CultureInfo.CurrentCulture));
    }

    [LocalFunction("roll_dice")]
    [Description("Roll one or more dice with a given number of sides")]
    public Task<string> RollDiceAsync(
        [Description("Number of dice to roll")] int count = 1,
        [Description("Number of sides per die")] int sides = 6)
    {
        var rng = Random.Shared;
        var rolls = Enumerable.Range(0, Math.Clamp(count, 1, 20))
            .Select(_ => rng.Next(1, Math.Clamp(sides, 2, 100) + 1))
            .ToArray();
        return Task.FromResult($"Rolled {count}d{sides}: [{string.Join(", ", rolls)}] = {rolls.Sum()}");
    }
}
