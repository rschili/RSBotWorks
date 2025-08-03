
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;

namespace RSBotWorks.UniversalAI;

internal class AnthropicChatParameters : PreparedChatParameters
{
    public List<Anthropic.SDK.Common.Tool>? Tools { get; internal set; }
}

internal class AnthropicChatClient : TypedChatClient<AnthropicClient>
{
    public AnthropicChatClient(string modelName, AnthropicClient innerClient, ILogger? logger = null)
        : base(modelName, innerClient, logger)
    {
    }


    public override async Task<string> CallAsync(string systemPrompt, IList<Message> inputs, PreparedChatParameters parameters)
    {
        if (parameters is not AnthropicChatParameters anthropicParameters)
        {
            throw new ArgumentException("Parameters must be of type AnthropicChatParameters", nameof(parameters));
        }
        try
        {
            var message = new MessageParameters()
            {
                System = [new SystemMessage(systemPrompt)],
                Messages = inputs.Select(InputToMessage).ToList(),
                MaxTokens = parameters.OriginalParameters.MaxTokens,
                Model = ModelName,
                Stream = false,
                Temperature = parameters.OriginalParameters.Temperature,
                TopK = parameters.OriginalParameters.TopK,
                TopP = parameters.OriginalParameters.TopP,
                Tools = anthropicParameters.Tools,
            };

            if (anthropicParameters.Tools != null && anthropicParameters.Tools.Any() && parameters.OriginalParameters.ToolChoiceType == ToolChoiceType.Auto)
            {
                message.ToolChoice = new ToolChoice() { Type = Anthropic.SDK.Messaging.ToolChoiceType.Auto };
            }

            StringBuilder textResponse = new StringBuilder();
            if (parameters.OriginalParameters.Prefill != null)
            {
                message.Messages.Add(new Anthropic.SDK.Messaging.Message(RoleType.Assistant, parameters.OriginalParameters.Prefill));
                textResponse.Append(parameters.OriginalParameters.Prefill);
            }

            return await LoopToCompletion(message, anthropicParameters, textResponse);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while calling Anthropic API");
            return "An error occurred while processing your request.";
        }
    }

    private async Task<string> LoopToCompletion(MessageParameters message, AnthropicChatParameters parameters, StringBuilder result)
    {
        int responses = 1;
        int toolCalls = 0;
        while (true)
        {
            if(responses > 3 || toolCalls > 5)
            {
                Logger.LogWarning("Stopping loop due to excessive responses or tool calls: {Responses}, {ToolCalls}", responses, toolCalls);
                break; // Prevent infinite loops
            }
            var response = await InnerClient.Messages.GetClaudeMessageAsync(message);
            responses++;
            message.Messages.Add(response.Message);

            foreach (var content in response.Content)
            {
                switch (content)
                {
                    case Anthropic.SDK.Messaging.TextContent textContent:
                        result.Append(textContent.Text);
                        break;
                    case Anthropic.SDK.Messaging.ImageContent imageContent:
                        // Handle image content if needed
                        break;
                }
            }

            if (response.Usage.ServerToolUse?.WebSearchRequests != null)
                toolCalls += response.Usage.ServerToolUse.WebSearchRequests.Value;

            if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                {
                    break; // No tool calls, we can exit the loop
                }

            foreach (var toolCall in response.ToolCalls)
            {
                toolCalls++;
                var nativeTool = parameters.OriginalParameters.AvailableLocalFunctions?.FirstOrDefault(f => f.Name == toolCall.Name);
                if (nativeTool == null)
                {
                    Logger.LogWarning("Tool call '{ToolName}' not found in available local functions.", toolCall.Name);
                    message.Messages.Add(new Anthropic.SDK.Messaging.Message(toolCall, $"Could not find tool with name {toolCall.Name}"));
                    continue;
                }

                // Convert JsonNode to JsonDocument for consistency
                using var jsonDoc = JsonDocument.Parse(toolCall.Arguments.ToJsonString());
                var toolResponse = await nativeTool.ExecuteAsync(jsonDoc);
                message.Messages.Add(new Anthropic.SDK.Messaging.Message(toolCall, toolResponse));
            }
        }

        if (toolCalls > 0)
            result.Append($"*{(toolCalls == 1 ? "" : toolCalls)}");

        return result.ToString();
     }

    private Anthropic.SDK.Messaging.Message InputToMessage(Message message, int arg2)
    {
        var result = new Anthropic.SDK.Messaging.Message();
        result.Role = message.Role switch
        {
            Role.User => RoleType.User,
            Role.Assistant => RoleType.Assistant,
            _ => throw new ArgumentOutOfRangeException(nameof(message.Role), "Unknown role in message")
        };

        result.Content = message.Content.Select<MessageContent, ContentBase>(content =>
        {
            if (content is TextContent textContent)
            {
                return new Anthropic.SDK.Messaging.TextContent() { Text = textContent.Text };
            }
            else if (content is ImageContent imageContent)
            {
                return new Anthropic.SDK.Messaging.ImageContent()
                {
                    Source =
                        new ImageSource() { MediaType = imageContent.MimeType, Type = SourceType.base64, Data = Convert.ToBase64String(imageContent.Data) }
                };
            }
            else
            {
                throw new ArgumentException("Unknown content type in message", nameof(message.Content));
            }
        }).ToList();
        return result;
    }

    public override PreparedChatParameters PrepareParameters(ChatParameters parameters)
    {
        var compiledParameters = new AnthropicChatParameters() { OriginalParameters = parameters };
        // TODO: convert tools
        List<Anthropic.SDK.Common.Tool> tools = [];
        // Convert to native tools
        if (parameters.AvailableLocalFunctions != null)
        {
            foreach (var function in parameters.AvailableLocalFunctions)
            {
                var inputschema = new InputSchema();
                inputschema.Type = "object";
                inputschema.Properties = function.Parameters.ToDictionary(
                    p => p.Name,
                    p => new Property
                    {
                        Type = p.Type.ToString().ToLowerInvariant(),
                        Description = p.Description
                    });
                inputschema.Required = function.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToList();
                JsonSerializerOptions jsonSerializationOptions = new()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Converters = { new JsonStringEnumConverter() },
                    ReferenceHandler = ReferenceHandler.IgnoreCycles,
                };
                string jsonString = JsonSerializer.Serialize(inputschema, jsonSerializationOptions);
                tools.Add(new Anthropic.SDK.Common.Tool(
                    new Function(function.Name, function.Description,
                        JsonNode.Parse(jsonString))
                ));
            }
        }

        if (parameters.EnableWebSearch)
        {
            tools.Add(ServerTools.GetWebSearchTool(5, userLocation: new UserLocation
                {
                    City = "Heidelberg",
                    Country = "DE",
                    Timezone = "Europe/Berlin"
                }));
        }

        if (tools.Any())
            {
                compiledParameters.Tools = tools;
            }
        
        return compiledParameters;
    }
}