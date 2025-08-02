
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

internal class OpenAIChatClient : TypedChatClient<OpenAI.Chat.ChatClient>
{
    public OpenAIChatClient(string modelName, OpenAI.Chat.ChatClient innerClient, ILogger? logger = null)
        : base(modelName, innerClient, logger)
    {
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

        if (enableWebSearch)
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

    public static readonly ChatCompletionOptions ChatImageGenerationOptions = new()
    {
        MaxOutputTokenCount = 1000,
        StoredOutputEnabled = false,
        ResponseFormat = ChatResponseFormat.CreateTextFormat(),
    };

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

    public override async Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType)
    {
        if (!RateLimiter.Leak())
            return "Rate limit exceeded. Please try again later.";

        var imageData = BinaryData.FromBytes(imageBytes);
        var instructions = new UserChatMessage(
            [
                ChatMessageContentPart.CreateTextPart(systemPrompt),
                ChatMessageContentPart.CreateImagePart(imageData, mimeType),
            ]);

        try
        {
            var result = await ChatClient.CompleteChatAsync([instructions], ChatImageGenerationOptions).ConfigureAwait(false);
            var text = result.Value.Content[0].Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                Logger.LogWarning("OpenAI image description returned empty response.");
                return "Keine Beschreibung für das Bild generiert.";
            }
            return text;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the OpenAI image description call.");
            return $"Fehler bei der Kommunikation mit OpenAI: {ex.Message}";
        }
    }


}