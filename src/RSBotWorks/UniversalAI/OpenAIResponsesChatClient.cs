using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace RSBotWorks.UniversalAI;

internal class OpenAIResponsesParameters : PreparedChatParameters
{
    public required ResponseCreationOptions Options { get; internal set; }
}

internal class OpenAIResponsesChatClient : TypedChatClient<OpenAIResponseClient>
{
    public OpenAIResponsesChatClient(string modelName, OpenAIResponseClient innerClient, ILogger? logger = null)
        : base(modelName, innerClient, logger)
    {
    }

    public override async Task<string> CallAsync(string? systemPrompt, IList<Message> inputs, PreparedChatParameters parameters)
    {
        if (parameters is not OpenAIResponsesParameters openAIParameters)
        {
            throw new ArgumentException("Parameters must be of type OpenAIChatParameters", nameof(parameters));
        }

        try
        {
            var messages = new List<ResponseItem>();
            if(!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(ResponseItem.CreateDeveloperMessageItem(systemPrompt));
            }

            // Convert input messages to OpenAI format
            foreach (var input in inputs)
            {
                messages.Add(InputToMessage(input));
            }

            // Add prefill if specified
            if (parameters.OriginalParameters.Prefill != null)
            {
                messages.Add(ResponseItem.CreateAssistantMessageItem(parameters.OriginalParameters.Prefill));
            }

            return await LoopToCompletion(messages, openAIParameters);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error while calling OpenAI API");
            throw;
        }
    }

    public override PreparedChatParameters PrepareParameters(ChatParameters parameters)
    {
        ResponseCreationOptions options = new()
        {
            MaxOutputTokenCount = 1000,
            StoredOutputEnabled = false,
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = ResponseReasoningEffortLevel.Low,
            },
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateTextFormat(),
            },
        };

        if (parameters.EnableWebSearch)
        {
            options.Tools.Add(ResponseTool.CreateWebSearchTool());
        }

        if (parameters.AvailableLocalFunctions != null && parameters.AvailableLocalFunctions.Count > 0)
        {
            foreach (var tool in GenerateOpenAITools(parameters.AvailableLocalFunctions))
            {
                options.Tools.Add(tool);
            }
            options.ToolChoice = parameters.ToolChoiceType == ToolChoiceType.Auto ? ResponseToolChoice.CreateAutoChoice() : ResponseToolChoice.CreateNoneChoice();
        }
        
        return new OpenAIResponsesParameters
        {
            Options = options,
            OriginalParameters = parameters
        };
    }

    private static List<ResponseTool> GenerateOpenAITools(IEnumerable<LocalFunction> functions)
    {
        var oaiTools = new List<ResponseTool>();

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

            var responseTool = ResponseTool.CreateFunctionTool(
                functionName: localFunction.Name,
                functionDescription: localFunction.Description,
                functionParameters: BinaryData.FromString(schemaJson)
            );

            oaiTools.Add(responseTool);
        }

        return oaiTools;
    }

    private async Task<string> LoopToCompletion(List<ResponseItem> messages, OpenAIResponsesParameters parameters)
    {
        int responses = 1;
        int toolCalls = 0;
        var result = new StringBuilder();

        /*if (parameters.OriginalParameters.Prefill != null)
        {
            result.Append(parameters.OriginalParameters.Prefill);
        }*/

        while (true)
        {
            if (responses > 3 || toolCalls > 5)
            {
                Logger?.LogWarning("Stopping loop due to excessive responses or tool calls: {Responses}, {ToolCalls}", responses, toolCalls);
                break; // Prevent infinite loops
            }

            var completion = await InnerClient.CreateResponseAsync(messages, parameters.Options);
            var response = completion.Value;
            responses++;

            List<FunctionCallResponseItem> toolCallsInResponse = [];
            foreach (ResponseItem item in response.OutputItems)
            {
                if (item is WebSearchCallResponseItem webSearchCall)
                {
                    toolCalls++;
                    messages.Add(item);
                }
                else if (item is FunctionCallResponseItem toolCall)
                {
                    toolCalls++;
                    toolCallsInResponse.Add(toolCall);
                    messages.Add(item);
                }
                else if (item is MessageResponseItem message)
                {
                    messages.Add(item);
                }
            }

            var textContents = response.GetOutputText();
            if (!string.IsNullOrEmpty(textContents))
            {
                result.Append(textContents);
            }

            // Handle tool calls
            if (toolCallsInResponse.Count == 0)
            {
                break; // No tool calls, we can exit the loop
            }

            foreach (var toolCall in toolCallsInResponse)
            {
                var nativeTool = parameters.OriginalParameters.AvailableLocalFunctions?.FirstOrDefault(f => f.Name == toolCall.FunctionName);
                if (nativeTool == null)
                {
                    Logger?.LogWarning("Tool call '{ToolName}' not found in available local functions.", toolCall.FunctionName);
                    messages.Add(ResponseItem.CreateFunctionCallOutputItem(toolCall.CallId, $"Could not find tool with name {toolCall.FunctionName}"));
                    continue;
                }

                try
                {
                    using var argumentsDoc = JsonDocument.Parse(toolCall.FunctionArguments);
                    var toolResponse = await nativeTool.ExecuteAsync(argumentsDoc);
                    messages.Add(ResponseItem.CreateFunctionCallOutputItem(toolCall.CallId, toolResponse));
                    toolCalls++;
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error executing tool call '{ToolName}'", toolCall.FunctionName);
                    messages.Add(ResponseItem.CreateFunctionCallOutputItem(toolCall.CallId, $"Error executing tool: {ex.Message}"));
                }
            }
        }

        if (toolCalls > 0)
            result.Append($"*{(toolCalls == 1 ? "" : toolCalls)}");

        return result.ToString();
    }

    private ResponseItem InputToMessage(Message message)
    {
        return message.Role switch
        {
            Role.User => CreateUserMessage(message),
            Role.Assistant => ResponseItem.CreateAssistantMessageItem(GetTextContent(message)),
            _ => throw new ArgumentOutOfRangeException(nameof(message.Role), "Unknown role in message")
        };
    }

    private ResponseItem CreateUserMessage(Message message)
    {
        var contentParts = new List<ResponseContentPart>();

        foreach (var content in message.Content)
        {
            switch (content)
            {
                case TextContent textContent:
                    contentParts.Add(ResponseContentPart.CreateInputTextPart(textContent.Text));
                    break;
                case ImageContent imageContent:
                    var imageBytes = imageContent.Data.ToArray();
                    contentParts.Add(ResponseContentPart.CreateInputImagePart(BinaryData.FromBytes(imageBytes), 
                        imageContent.MimeType));
                    break;
                default:
                    throw new ArgumentException($"Unknown content type: {content.GetType().Name}", nameof(message.Content));
            }
        }

        return ResponseItem.CreateUserMessageItem(contentParts);
    }

    private string GetTextContent(Message message)
    {
        var textContents = message.Content.OfType<TextContent>();
        return string.Join("", textContents.Select(tc => tc.Text));
    }
}

    // ...existing code...