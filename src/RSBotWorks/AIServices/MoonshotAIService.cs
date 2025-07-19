using System.ClientModel;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualBasic;
using OpenAI;
using OpenAI.Chat;
using RSBotWorks.Tools;
using RSMatrix.Http;

namespace RSBotWorks;

public class MoonshotAIService : BaseAIService
{
    public ChatClient Client { get; private init; }

    // ResponseClient cannot use binary images directly, but ChatClient can
    public ChatClient ChatClient { get; private init; }

    private List<ChatTool> _tools;

    [SetsRequiredMembers]
    public MoonshotAIService(string apiKey, string openAiApiKey, ToolHub toolhub, string model, ILogger? logger = null) : base(toolhub, logger)
    {
        Client = new ChatClient(model, new ApiKeyCredential(apiKey), new OpenAIClientOptions()
        {
            Endpoint = new Uri("https://api.moonshot.ai/v1"),
        });
        // used for image interpretation, moonshot AI does not support vision
        ChatClient = new ChatClient("gpt-4o", openAiApiKey);
        _tools = GenerateOpenAITools(toolhub.Tools, toolhub.EnableWebSearch);

        // This is so silly, but the field is get-only
        foreach (var tool in _tools)
        {
            DefaultOptions.Tools.Add(tool);
            StructuredJsonArrayOptions.Tools.Add(tool);
        }
    }

    private static List<ChatTool> GenerateOpenAITools(ImmutableArray<Tool> tools, bool enableWebSearch)
    {
        var oaiTools = new List<ChatTool>();

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

            var responseTool = ChatTool.CreateFunctionTool(
                functionName: tool.Name,
                functionDescription: tool.Description,
                functionParameters: BinaryData.FromString(schemaJson)
            );

            oaiTools.Add(responseTool);
        }

        //if (enableWebSearch)
        //    oaiTools.Add(ChatTool.CreateWebSearchTool()); // Add web search tool by default
        return oaiTools;
    }

    public static readonly ChatCompletionOptions DefaultOptions = new()
    {
        MaxOutputTokenCount = 1000,
        StoredOutputEnabled = false,
        ResponseFormat = ChatResponseFormat.CreateTextFormat(),
    };

    public static readonly ChatCompletionOptions StructuredJsonArrayOptions = new()
    {
        MaxOutputTokenCount = 1000,
        StoredOutputEnabled = false,
        ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("structured_array",
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
    };

    public static readonly ChatCompletionOptions PlainTextWithNoToolsOptions = new()
    {
        MaxOutputTokenCount = 50,
        StoredOutputEnabled = false,
        ResponseFormat = ChatResponseFormat.CreateTextFormat()
    };

    public static readonly ChatCompletionOptions ChatImageGenerationOptions = new()
    {
        MaxOutputTokenCount = 1000,
        StoredOutputEnabled = false,
        ResponseFormat = ChatResponseFormat.CreateTextFormat(),
    };

    public override async Task<string?> DoGenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default)
    {
        var instructions = new List<ChatMessage>() { ChatMessage.CreateSystemMessage(systemPrompt) };
        foreach (var input in inputs)
        {
            var participantName = input.ParticipantName;
            if (participantName == null || participantName.Length >= 100)
                throw new ArgumentException("Participant name is too long.", nameof(participantName));

            if (!NameSanitizer.IsValidName(participantName))
                throw new ArgumentException("Participant name is invalid.", nameof(participantName));

            if (input.IsSelf)
            {
                var message = ChatMessage.CreateAssistantMessage(input.Message);
                instructions.Add(message);
            }
            else
            {
                var message = ChatMessage.CreateUserMessage($"[[{input.ParticipantName}]] {input.Message}");
                instructions.Add(message);
            }
        }

        ChatCompletionOptions options = kind switch
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

    private async Task<string> GenerateResponseInternalAsync(List<ChatMessage> instructions, int depth = 1, int toolCalls = 0, ChatCompletionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(instructions, nameof(instructions));
        if (depth > 3)
        {
            Logger.LogWarning("OpenAI call reached maximum recursion depth of 5. Returning empty response.");
            return "Maximale Rekursionstiefe erreicht. Keine Antwort generiert.";
        }

        var result = await Client.CompleteChatAsync(instructions, options ?? DefaultOptions).ConfigureAwait(false);
        var response = result.Value;
        var functionCalls = response.ToolCalls;
        if (functionCalls.Count > 0)
        {
            // First, add the assistant message with tool calls to the conversation history.
            instructions.Add(new AssistantChatMessage(response));
            Logger.LogInformation("OpenAI call requested function calls. Depth: {Depth}.", depth);
            foreach (var functionCall in functionCalls)
            {
                Logger.LogInformation("Function call requested: {FunctionName} with arguments: {Arguments}", functionCall.FunctionName, functionCall.FunctionArguments);
                toolCalls++;
                await HandleFunctionCall(functionCall, instructions);
            }
            return await GenerateResponseInternalAsync(instructions, depth + 1, toolCalls).ConfigureAwait(false);
        }

        string? output = response.Content[0].Text;
        if (!string.IsNullOrEmpty(output))
        {
            if (toolCalls == 0)
                return output;
            return output + $"(*{toolCalls})";
        }

        output = response.Refusal;
        if (!string.IsNullOrEmpty(output))
        {
            Logger.LogError("OpenAI call finished with an error: {Error}. Depth: {Depth}. Total Token Count: {TokenCount}.", output, depth, response.Usage.TotalTokenCount);
            return $"Fehler bei der OpenAI-Antwort: {output}";
        }

        Logger.LogError($"OpenAI call returned no output or error. Depth: {depth}. Total Token Count: {response.Usage.TotalTokenCount}");
        return "Keine Antwort von OpenAI erhalten.";
    }

    private async Task HandleFunctionCall(ChatToolCall functionCall, List<ChatMessage> instructions)
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
        instructions.Add(new ToolChatMessage(functionCall.Id, response));
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