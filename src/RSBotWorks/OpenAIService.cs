using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using RSBotWorks.Tools;
using RSMatrix.Http;

namespace RSBotWorks;

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
    
    private List<ResponseTool> _tools;

    public OpenAIService(string apiKey, ToolHub toolhub, OpenAIModel model = OpenAIModel.GPT41, ILogger<OpenAIService>? logger = null)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ToolHub = toolhub ?? throw new ArgumentNullException(nameof(toolhub));
        Client = new OpenAIResponseClient(model: ModelToString(model), apiKey: apiKey);
        _tools = GenerateOpenAITools(toolhub.Tools, toolhub.EnableWebSearch);

        // This is so silly, but the field is get-only
        foreach (var tool in _tools)
        {
            DefaultOptions.Tools.Add(tool);
            StructuredJsonArrayOptions.Tools.Add(tool);
        }
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

    private static List<ResponseTool> GenerateOpenAITools(ImmutableArray<Tool> tools, bool enableWebSearch)
    {
        var oaiTools = new List<ResponseTool>();
        
        foreach (var tool in tools)
        {
            var properties = new Dictionary<string, object>();
            var required = new List<string>();
            
            foreach (var parameter in tool.Parameters)
            {
                properties[parameter.Name] = new
                {
                    type = parameter.Type.ToLowerInvariant(),
                    description = parameter.Description
                };
                
                if (parameter.IsRequired)
                {
                    required.Add(parameter.Name);
                }
            }
            
            var schema = new
            {
                type = "object",
                properties,
                required = required.ToArray()
            };
            
            var schemaJson = properties.Count > 0 ? JsonSerializer.Serialize(schema, new JsonSerializerOptions 
            { 
                WriteIndented = false 
            }) : "{}";
            
            var responseTool = ResponseTool.CreateFunctionTool(
                functionName: tool.Name,
                functionDescription: tool.Description,
                functionParameters: BinaryData.FromString(schemaJson)
            );
            
            oaiTools.Add(responseTool);
        }
        
        if(enableWebSearch)
            oaiTools.Add(ResponseTool.CreateWebSearchTool()); // Add web search tool by default
        return oaiTools;
    }

    public static readonly ResponseCreationOptions DefaultOptions = new()
    {
        MaxOutputTokenCount = 1000,
        StoredOutputEnabled = false,
        TextOptions = new ResponseTextOptions
        {
            TextFormat = ResponseTextFormat.CreateTextFormat()
        },
        ToolChoice = ResponseToolChoice.CreateAutoChoice(),
    };

    public static readonly ResponseCreationOptions StructuredJsonArrayOptions = new()
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
        ToolChoice = ResponseToolChoice.CreateAutoChoice(),
    };

    public static readonly ResponseCreationOptions PlainTextWithNoToolsOptions = new()
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
            return await GenerateResponseInternalAsync(instructions, 1, 0, options).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the OpenAI call.");
            return $"Fehler bei der Kommunikation mit OpenAI: {ex.Message}";
        }
    }

    public async Task<string> GenerateResponseInternalAsync(List<ResponseItem> instructions, int depth = 1, int toolCalls = 0, ResponseCreationOptions? options = null)
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
            return await GenerateResponseInternalAsync(instructions, depth + 1, toolCalls).ConfigureAwait(false);
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
        using JsonDocument argumentsJson = JsonDocument.Parse(functionCall.FunctionArguments);
        var argsDict = new Dictionary<string, string>();
        foreach (var property in argumentsJson.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            var str = property.Value.GetString();
            if (string.IsNullOrEmpty(str))
                continue;

            argsDict[property.Name] = str;
        }
        var response = await ToolHub.CallAsync(functionCall.FunctionName, argsDict);
        instructions.Add(ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, response));
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
