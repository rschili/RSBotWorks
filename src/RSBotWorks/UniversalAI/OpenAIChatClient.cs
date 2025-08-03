
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace RSBotWorks.UniversalAI;

public static class OpenAIModel
{
    public const string GPT4o = "gpt-4o";
    public const string GPT41 = "gpt-4.1";
    public const string O1 = "o1";
    public const string O3Mini = "o3-mini";
}

internal class OpenAIChatParameters : CompiledChatParameters
{
    public required ChatCompletionOptions Options { get; internal set; }
}

internal class OpenAIChatClient : TypedChatClient<OpenAI.Chat.ChatClient>
{
    public OpenAIChatClient(string modelName, OpenAI.Chat.ChatClient innerClient, ILogger? logger = null)
        : base(modelName, innerClient, logger)
    {
    }

    public override async Task<string> CallAsync(string systemPrompt, IEnumerable<Message> inputs, CompiledChatParameters parameters)
    {

    }

    public override Task<CompiledChatParameters> CompileParametersAsync(ChatParameters parameters)
    {
        ChatCompletionOptions options = new()
        {
            MaxOutputTokenCount = 1000,
            StoredOutputEnabled = false,
            ResponseFormat = ChatResponseFormat.CreateTextFormat(),
            ToolChoice = parameters.ToolChoiceType == ToolChoiceType.Auto ? ChatToolChoice.CreateAutoChoice() : ChatToolChoice.CreateNoneChoice(),
        };
        if (parameters.EnableWebSearch)
        {
            options.WebSearchOptions = new();
        }

        if (parameters.AvailableLocalFunctions != null && parameters.AvailableLocalFunctions.Count > 0)
            {
                foreach (var tool in GenerateOpenAITools(parameters.AvailableLocalFunctions))
                {
                    options.Tools.Add(tool);
                }
            }
        
        return Task.FromResult<CompiledChatParameters>(new OpenAIChatParameters
        {
            Options = options,
            OriginalParameters = parameters
        });
    }

    private static List<ChatTool> GenerateOpenAITools(IEnumerable<LocalFunction> functions)
    {
        var oaiTools = new List<ChatTool>();

        foreach (var localFunction in functions)
        {
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var parameter in localFunction.Parameters)
            {
                properties[parameter.Name] = new
                {
                    type = parameter.Type.ToString().ToLowerInvariant(),
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

            var responseTool = ChatTool.CreateFunctionTool(
                functionName: localFunction.Name,
                functionDescription: localFunction.Description,
                functionParameters: BinaryData.FromString(schemaJson)
            );

            oaiTools.Add(responseTool);
        }

        return oaiTools;
    }

/*
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
    };*/

    public override async Task<string?> DoGenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default)
    {
        var instructions = new List<ResponseItem>() { ResponseItem.CreateDeveloperMessageItem(systemPrompt) };
        foreach (var input in inputs)
        {
            var participantName = input.ParticipantName;
            if (participantName == null || participantName.Length >= 100)
                throw new ArgumentException("Participant name is too long.", nameof(participantName));

            if (!NameSanitizer.IsValidName(participantName))
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

        ResponseCreationOptions options = kind switch
        {
            ResponseKind.Default => DefaultOptions,
            ResponseKind.StructuredJsonArray => StructuredJsonArrayOptions,
            ResponseKind.NoTools => PlainTextWithNoToolsOptions,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown response kind")
        };

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

    private async Task<string> GenerateResponseInternalAsync(List<ResponseItem> instructions, int depth = 1, int toolCalls = 0, ResponseCreationOptions? options = null)
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

}