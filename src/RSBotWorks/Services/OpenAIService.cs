using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI.Responses;
using RSBotWorks.Tools;
using RSMatrix.Http;

namespace RSHome.Services;

public enum OpenAIModel
{
    GPT4o,
    O1,
    O3Mini,
    GPT41
}

public class OpenAIService
{
    public OpenAIResponseClient Client { get; private init; }
    public ILogger Logger { get; private init; }

    public ToolHub ToolHub { get; private init; }

    public LeakyBucketRateLimiter RateLimiter { get; private init; } = new(10, 60);

    public OpenAIService(string apiKey, ToolHub toolhub, OpenAIModel model = OpenAIModel.GPT41, ILogger<OpenAIService>? logger = null)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ToolHub = toolhub ?? throw new ArgumentNullException(nameof(toolhub));
        Client = new OpenAIResponseClient(model: ModelToString(model), apiKey: apiKey);
    }

    private static string ModelToString(OpenAIModel model)
    {
        return model switch
        {
            OpenAIModel.GPT4o => "gpt-4o",
            OpenAIModel.GPT41 => "gpt-4.1",
            OpenAIModel.O1 => "o1",
            OpenAIModel.O3Mini => "o3-mini",
            _ => throw new ArgumentOutOfRangeException(nameof(model), model, null)
        };
    }

    private const string WEATHER_TOOL_NAME = "weather_current";
    private static readonly ResponseTool weatherCurrentTool = ResponseTool.CreateFunctionTool
    (
        functionName: WEATHER_TOOL_NAME,
        functionDescription: "Get the current weather and sunrise/sunset for a given location",
        functionParameters: BinaryData.FromBytes("""
            {
                "type": "object",
                "properties": {
                    "location": {
                        "type": "string",
                        "description": "City name or ZIP code to get the current weather for. When providing a City name, an ISO 3166 country code can be appended with a comma e.g. 'Heidelberg,DE' to avoid ambiguity."
                    }
                },
                "required": [ "location" ]
            }
            """u8.ToArray())
    );

    private const string FORECAST_TOOL_NAME = "weather_forecast";
    private static readonly ResponseTool weatherForecastTool = ResponseTool.CreateFunctionTool
    (
        functionName: FORECAST_TOOL_NAME,
        functionDescription: "Get a 5 day weather forecast for a given location",
        functionParameters: BinaryData.FromBytes("""
            {
                "type": "object",
                "properties": {
                    "location": {
                        "type": "string",
                        "description": "City name or ZIP code to get the current weather for. When providing a City name, an ISO 3166 country code can be appended with a comma e.g. 'Heidelberg,DE' to avoid ambiguity."
                    }
                },
                "required": [ "location" ]
            }
            """u8.ToArray())
    );

    private const string HEISE_TOOL_NAME = "heise_headlines";
    private static readonly ResponseTool heiseHeadlinesTool = ResponseTool.CreateFunctionTool
    (
        functionName: HEISE_TOOL_NAME,
        functionDescription: "Get the latest headlines from Heise Online (Technology related news)",
        functionParameters: BinaryData.FromBytes("""
            {}
            """u8.ToArray())
    );

    private const string POSTILLON_TOOL_NAME = "postillon_headlines";
    private static readonly ResponseTool postillonHeadlinesTool = ResponseTool.CreateFunctionTool
    (
        functionName: POSTILLON_TOOL_NAME,
        functionDescription: "Get the latest headlines from 'Der Postillon', a german satire magazine",
        functionParameters: BinaryData.FromBytes("""
            {}
            """u8.ToArray())
    );

    private const string CAR_TOOL_NAME = "car_status";
    private static readonly ResponseTool carStatusTool = ResponseTool.CreateFunctionTool
(
    functionName: CAR_TOOL_NAME,
    functionDescription: "Get status of the car of the user krael aka noppel (charge, range, doors, etc.)",
    functionParameters: BinaryData.FromBytes("""
            {}
            """u8.ToArray())
);


    private static readonly ResponseCreationOptions DefaultOptions = new()
    {
        MaxOutputTokenCount = 1000,
        StoredOutputEnabled = false,
        TextOptions = new ResponseTextOptions
        {
            TextFormat = ResponseTextFormat.CreateTextFormat()
        },
        Tools = { weatherCurrentTool, weatherForecastTool, heiseHeadlinesTool, postillonHeadlinesTool, carStatusTool, ResponseTool.CreateWebSearchTool() },
        ToolChoice = ResponseToolChoice.CreateAutoChoice(),
    };

    internal static readonly ResponseCreationOptions StructuredJsonArrayOptions = new()
    {
        MaxOutputTokenCount = 1000,
        StoredOutputEnabled = false,
        TextOptions = new ResponseTextOptions
        {
            TextFormat = ResponseTextFormat.CreateJsonSchemaFormat("structured_array",
            BinaryData.FromBytes("""
            {
                "type": "object",
                "properties": {
                    "values": {
                        "type": "array",
                        "items": {
                            "type": "string"
                        }
                    }
                },
                "required": [ "values" ],
                "additionalProperties": false
            }
            """u8.ToArray()), null, true)
        },
        Tools = { weatherCurrentTool, weatherForecastTool, heiseHeadlinesTool, postillonHeadlinesTool, carStatusTool },
        ToolChoice = ResponseToolChoice.CreateAutoChoice(),
    };

    internal static readonly ResponseCreationOptions PlainTextWithNoToolsOptions = new()
    {
        MaxOutputTokenCount = 50,
        StoredOutputEnabled = false,
        TextOptions = new ResponseTextOptions
        {
            TextFormat = ResponseTextFormat.CreateTextFormat()
        }
    };

    public async Task<string?> GenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseCreationOptions? options = null)
    {
        if (!RateLimiter.Leak())
            return null;

        systemPrompt = $"""
            {systemPrompt}
            Aktuell ist [{DateTime.Now.ToLongDateString()}, {DateTime.Now.ToLongTimeString()} Uhr. 
            """;

        var instructions = new List<ResponseItem>() { ResponseItem.CreateDeveloperMessageItem(systemPrompt) };
        foreach (var input in inputs)
        {
            var participantName = input.ParticipantName;
            if (participantName == null || participantName.Length >= 100)
                throw new ArgumentException("Participant name is too long.", nameof(participantName));

            if (!IsValidName(participantName))
                throw new ArgumentException("Participant name is invalid.", nameof(participantName));

            if (input.IsSelf)
            {
                var message = ResponseItem.CreateAssistantMessageItem(input.Message);
                instructions.Add(message);
            }
            else
            {
                var message = ResponseItem.CreateUserMessageItem($"[[{input.ParticipantName}]] {input.Message}");
                instructions.Add(message);
            }
        }

        try
        {
            return await CreateResponseAsync(instructions, 1, 0, options).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the OpenAI call.");
            return $"Fehler bei der Kommunikation mit OpenAI: {ex.Message}";
        }
    }

    public async Task<string> CreateResponseAsync(List<ResponseItem> instructions, int depth = 1, int toolCalls = 0, ResponseCreationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(instructions, nameof(instructions));
        if (depth > 3)
        {
            Logger.LogWarning("OpenAI call reached maximum recursion depth of 5. Returning empty response.");
            return "Maximale Rekursionstiefe erreicht. Keine Antwort generiert.";
        }

        var result = await Client.CreateResponseAsync(instructions, options ?? DefaultOptions).ConfigureAwait(false);
        var response = result.Value;
        List<FunctionCallResponseItem> functionCalls = [.. response.OutputItems.Where(item => item is FunctionCallResponseItem).Cast<FunctionCallResponseItem>()];
        List<WebSearchCallResponseItem> webSearchCalls = [.. response.OutputItems.Where(item => item is WebSearchCallResponseItem).Cast<WebSearchCallResponseItem>()];
        if (webSearchCalls.Count > 0)
        {
            foreach (var webSearchCall in webSearchCalls)
            {
                instructions.Add(webSearchCall);
                toolCalls++;
            }
        }
        if (functionCalls.Count > 0)
        {
            Logger.LogInformation("OpenAI call requested function calls. Depth: {Depth}.", depth);
            foreach (var functionCall in functionCalls)
            {
                Logger.LogInformation("Function call requested: {FunctionName} with arguments: {Arguments}", functionCall.FunctionName, functionCall.FunctionArguments);
                instructions.Add(functionCall);
                toolCalls++;
                await HandleFunctionCall(functionCall, instructions);
            }
            return await CreateResponseAsync(instructions, depth + 1, toolCalls).ConfigureAwait(false);
        }

        string? output = response.GetOutputText();
        if (!string.IsNullOrEmpty(output))
        {
            if (toolCalls == 0)
                return output;
            return output + $"(*{toolCalls})";
        }

        output = response.Error?.Message;
        if (!string.IsNullOrEmpty(output))
        {
            Logger.LogError("OpenAI call finished with an error: {Error}. Depth: {Depth}. Total Token Count: {TokenCount}.", output, depth, response.Usage.TotalTokenCount);
            return $"Fehler bei der OpenAI-Antwort: {output}";
        }

        Logger.LogError($"OpenAI call returned no output or error. Depth: {depth}. Total Token Count: {response.Usage.TotalTokenCount}");
        return "Keine Antwort von OpenAI erhalten.";
    }

    private async Task HandleFunctionCall(FunctionCallResponseItem functionCall, List<ResponseItem> instructions)
    {
        switch (functionCall.FunctionName)
        {
            case WEATHER_TOOL_NAME:
                {
                    using JsonDocument argumentsJson = JsonDocument.Parse(functionCall.FunctionArguments);
                    bool hasLocation = argumentsJson.RootElement.TryGetProperty("location", out JsonElement location);
                    if (!hasLocation)
                        throw new ArgumentNullException(nameof(location), "The location argument is required for the weather tool.");

                    try
                    {
                        string weatherResponse = await ToolService.GetCurrentWeatherAsync(location.GetString()!).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(weatherResponse))
                        {
                            Logger.LogWarning("Weather tool call returned no response.");
                            instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, "Keine Wetterdaten gefunden."));
                        }
                        instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, weatherResponse));
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "An error occurred while calling the weather tool.");
                        instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, $"Fehler beim Abrufen der Wetterdaten. {ex.Message}"));
                    }
                    break;
                }
            case FORECAST_TOOL_NAME:
                {
                    using JsonDocument argumentsJson = JsonDocument.Parse(functionCall.FunctionArguments);
                    bool hasLocation = argumentsJson.RootElement.TryGetProperty("location", out JsonElement location);
                    if (!hasLocation)
                        throw new ArgumentNullException(nameof(location), "The location argument is required for the weather forecast tool.");

                    try
                    {
                        string forecastResponse = await ToolService.GetWeatherForecastAsync(location.GetString()!).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(forecastResponse))
                        {
                            Logger.LogWarning("Weather forecast tool call returned no response.");
                            instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, "Keine Wettervorhersage gefunden."));
                        }
                        instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, forecastResponse));
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "An error occurred while calling the weather forecast tool.");
                        instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, $"Fehler beim Abrufen der Wettervorhersage. {ex.Message}"));
                    }
                    break;
                }
            case HEISE_TOOL_NAME:
                try
                {
                    string heiseResponse = await ToolService.GetHeiseHeadlinesAsync(15).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(heiseResponse))
                    {
                        Logger.LogWarning("Heise tool call returned no response.");
                        instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, "Keine Heise Online Nachrichten gefunden."));
                    }
                    instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, heiseResponse));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "An error occurred while calling the Heise tool.");
                    instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, $"Fehler beim Abrufen der Heise Online Nachrichten. {ex.Message}"));
                }
                break;
            case POSTILLON_TOOL_NAME:
                try
                {
                    string postillonResponse = await ToolService.GetPostillonHeadlinesAsync(10).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(postillonResponse))
                    {
                        Logger.LogWarning("Postillon tool call returned no response.");
                        instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, "Keine Postillon Nachrichten gefunden."));
                    }
                    instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, postillonResponse));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "An error occurred while calling the Postillon tool.");
                    instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, $"Fehler beim Abrufen der Postillon Online Nachrichten. {ex.Message}"));
                }
                break;
            case CAR_TOOL_NAME:
                try
                {
                    string carStatusResponse = await ToolService.GetCupraInfoAsync().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(carStatusResponse))
                    {
                        Logger.LogWarning("Car status tool call returned no response.");
                        instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, "Keine Fahrzeugdaten gefunden."));
                    }
                    instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, carStatusResponse));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "An error occurred while calling the car status tool.");
                    instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, $"Fehler beim Abrufen der Fahrzeugdaten. {ex.Message}"));
                }
                break;
            default:
                Logger.LogWarning("OpenAI called an unknown tool: {ToolName}.", functionCall.FunctionName);
                instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, $"Unbekannter Toolaufruf: {functionCall.FunctionName}"));
                break;
        }
    }

    public static string SanitizeName(string participantName)
    {
        ArgumentNullException.ThrowIfNull(participantName, nameof(participantName));

        string withoutSpaces = participantName.Replace(" ", "_");
        string normalized = withoutSpaces.Normalize(NormalizationForm.FormD);
        string safeName = Regex.Replace(normalized, @"[^a-zA-Z0-9_-]+", "");
        if (safeName.Length > 100)
            safeName = safeName.Substring(0, 100);

        safeName = safeName.Trim('_');
        return safeName;
    }

    public static bool IsValidName(string name)
    {
        return !string.IsNullOrEmpty(name) && Regex.IsMatch(name, "^[a-zA-Z0-9_-]+$");
    }
}

public record AIMessage(bool IsSelf, string Message, string ParticipantName);
