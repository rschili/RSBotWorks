using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

internal class OpenAIChatParameters : PreparedChatParameters
{
    public required ChatCompletionOptions Options { get; internal set; }
}

internal class OpenAIChatClient : TypedChatClient<OpenAI.Chat.ChatClient>
{
    public OpenAIChatClient(string modelName, OpenAI.Chat.ChatClient innerClient, ILogger? logger = null)
        : base(modelName, innerClient, logger)
    {
    }

    public override async Task<string> CallAsync(string? systemPrompt, IList<Message> inputs, PreparedChatParameters parameters)
    {
        if (parameters is not OpenAIChatParameters openAIParameters)
        {
            throw new ArgumentException("Parameters must be of type OpenAIChatParameters", nameof(parameters));
        }

        try
        {
            var messages = new List<ChatMessage>();
            if(!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new SystemChatMessage(systemPrompt));
            }

            // Convert input messages to OpenAI format
            foreach (var input in inputs)
            {
                messages.Add(InputToMessage(input));
            }

            // Add prefill if specified
            if (parameters.OriginalParameters.Prefill != null)
            {
                messages.Add(new AssistantChatMessage(parameters.OriginalParameters.Prefill));
            }

            return await LoopToCompletion(messages, openAIParameters);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error while calling OpenAI API");
            return "An error occurred while processing your request.";
        }
    }

    public override PreparedChatParameters PrepareParameters(ChatParameters parameters)
    {
        ChatCompletionOptions options = new()
        {
            MaxOutputTokenCount = 1000,
            StoredOutputEnabled = false,
            ResponseFormat = ChatResponseFormat.CreateTextFormat(),
        };

        if (parameters.EnableWebSearch)
        {
            Logger.LogError("Web Search not supported by OpenAI with chat client yet.");
            //options.WebSearchOptions = new();
        }

        if (parameters.AvailableLocalFunctions != null && parameters.AvailableLocalFunctions.Count > 0)
        {
            foreach (var tool in GenerateOpenAITools(parameters.AvailableLocalFunctions))
            {
                options.Tools.Add(tool);
            }
            options.ToolChoice = parameters.ToolChoiceType == ToolChoiceType.Auto ? ChatToolChoice.CreateAutoChoice() : ChatToolChoice.CreateNoneChoice();
        }
        
        return new OpenAIChatParameters
        {
            Options = options,
            OriginalParameters = parameters
        };
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

    private async Task<string> LoopToCompletion(List<ChatMessage> messages, OpenAIChatParameters parameters)
    {
        int responses = 1;
        int toolCalls = 0;
        var result = new StringBuilder();

        if (parameters.OriginalParameters.Prefill != null)
        {
            result.Append(parameters.OriginalParameters.Prefill);
        }

        while (true)
        {
            if (responses > 3 || toolCalls > 5)
            {
                Logger?.LogWarning("Stopping loop due to excessive responses or tool calls: {Responses}, {ToolCalls}", responses, toolCalls);
                break; // Prevent infinite loops
            }

            var completion = await InnerClient.CompleteChatAsync(messages, parameters.Options);
            responses++;
            
            var responseMessage = completion.Value.Content.FirstOrDefault();
            if (responseMessage?.Text != null)
            {
                result.Append(responseMessage.Text);
                messages.Add(new AssistantChatMessage(responseMessage.Text));
            }

            // Handle tool calls
            var toolCallsInResponse = completion.Value.ToolCalls;
            if (toolCallsInResponse == null || toolCallsInResponse.Count == 0)
            {
                break; // No tool calls, we can exit the loop
            }

            // Add assistant message with tool calls
            messages.Add(new AssistantChatMessage(toolCallsInResponse));

            foreach (var toolCall in toolCallsInResponse)
            {
                var nativeTool = parameters.OriginalParameters.AvailableLocalFunctions?.FirstOrDefault(f => f.Name == toolCall.FunctionName);
                if (nativeTool == null)
                {
                    Logger?.LogWarning("Tool call '{ToolName}' not found in available local functions.", toolCall.FunctionName);
                    messages.Add(new ToolChatMessage(toolCall.Id, $"Could not find tool with name {toolCall.FunctionName}"));
                    continue;
                }

                try
                {
                    using var argumentsDoc = JsonDocument.Parse(toolCall.FunctionArguments);
                    var toolResponse = await nativeTool.ExecuteAsync(argumentsDoc);
                    messages.Add(new ToolChatMessage(toolCall.Id, toolResponse));
                    toolCalls++;
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error executing tool call '{ToolName}'", toolCall.FunctionName);
                    messages.Add(new ToolChatMessage(toolCall.Id, $"Error executing tool: {ex.Message}"));
                }
            }
        }

        if (toolCalls > 0)
            result.Append($"*{(toolCalls == 1 ? "" : toolCalls)}");

        return result.ToString();
    }

    private ChatMessage InputToMessage(Message message)
    {
        return message.Role switch
        {
            Role.User => CreateUserMessage(message),
            Role.Assistant => new AssistantChatMessage(GetTextContent(message)),
            _ => throw new ArgumentOutOfRangeException(nameof(message.Role), "Unknown role in message")
        };
    }

    private ChatMessage CreateUserMessage(Message message)
    {
        var contentParts = new List<ChatMessageContentPart>();

        foreach (var content in message.Content)
        {
            switch (content)
            {
                case TextContent textContent:
                    contentParts.Add(ChatMessageContentPart.CreateTextPart(textContent.Text));
                    break;
                case ImageContent imageContent:
                    var imageBytes = imageContent.Data.ToArray();
                    contentParts.Add(ChatMessageContentPart.CreateImagePart(
                        BinaryData.FromBytes(imageBytes), 
                        imageContent.MimeType));
                    break;
                default:
                    throw new ArgumentException($"Unknown content type: {content.GetType().Name}", nameof(message.Content));
            }
        }

        return new UserChatMessage(contentParts);
    }

    private string GetTextContent(Message message)
    {
        var textContents = message.Content.OfType<TextContent>();
        return string.Join("", textContents.Select(tc => tc.Text));
    }
}

    // ...existing code...