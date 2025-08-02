
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace RSBotWorks.UniversalAI;

internal class AnthropicChatParameters : NativeChatParameters
{

}

internal class AnthropicChatClient : TypedChatClient<AnthropicClient>
{
    public AnthropicChatClient(string modelName, AnthropicClient innerClient, ILogger? logger = null)
        : base(modelName, innerClient, logger)
    {
    }


    public override Task<string> CallAsync(string systemPrompt, IEnumerable<Message> inputs, NativeChatParameters parameters)
    {
        throw new NotImplementedException();
    }

    public override Task<NativeChatParameters> CompileParameters(ChatParameters parameters)
    {
        var nativeParameters = new AnthropicChatParameters() { OriginalParameters = parameters };
        // TODO: convert tools
        List<Tool> tools = [];
    }

    public override async Task<string?> DoGenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default)
    {
        var processedInputs = inputs;
        var processedPrompt = systemPrompt;

        var messages = new List<Message>()
{
    new Message(RoleType.User, "Who won the world series in 2020?"),
    new Message(RoleType.Assistant, "The Los Angeles Dodgers won the World Series in 2020."),
    new Message(RoleType.User, "Where was it played?"),
};

        var parameters = new MessageParameters()
        {
            Messages = messages,
            MaxTokens = 1024,
            Model = AnthropicModels.Claude35Sonnet,
            Stream = false,
            Temperature = 1.0m,
        };
        parameters.Tools
                var firstResult = await InnerClient.Messages.GetClaudeMessageAsync(parameters);

        if (kind == ResponseKind.StructuredJsonArray)
        {
            processedPrompt += """
                
                Please respond with a JSON object that contains a 'values' property that holds an array of strings.
                For example: { "values": ["value1", "value2", "value3"] }
                """;
            processedInputs = inputs.Append(new AIMessage(true, JSON_ARRAY_PREFILL, ""));
        }
        var request = new ClaudeMessageRequest
        {
            Model = Model,
            MaxTokens = 1000,
            Temperature = 0.6,
            System = new List<ClaudeSystemContent>
            {
                new ClaudeSystemContent { Text = processedPrompt }
            },
            Messages = [.. processedInputs.Select(input =>
            {
                var text = input.IsSelf ? input.Message : $"[[{input.ParticipantName}]] {input.Message}";
                return ClaudeRequestMessage.Create(input.IsSelf ? ClaudeRole.Assistant : ClaudeRole.User, text);
            })],
        };

        var response = await SendRequestAsync(request);
        var responseText = ExtractTextFromResponse(response);

        if (kind == ResponseKind.StructuredJsonArray)
            responseText = JSON_ARRAY_PREFILL + responseText;

        return responseText;
    }

    private async Task<ClaudeResponse?> SendRequestAsync(ClaudeMessageRequest request)
    {
        using var client = CreateClientWithApiKey();
        var response = await client.PostAsJsonAsync(ENDPOINT_URL, request).ConfigureAwait(false);

        if (response.Content == null)
        {
            Logger.LogWarning("Claude response content was null. Status code: {StatusCode}", response.StatusCode);
            return null;
        }

        var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var claudeResponse = await JsonSerializer.DeserializeAsync<ClaudeResponse>(responseStream);

        if (claudeResponse == null)
        {
            Logger.LogWarning("Claude response deserialization failed. Status code: {StatusCode}", response.StatusCode);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            if (claudeResponse is ClaudeErrorResponse claudeError)
            {
                Logger.LogError("Claude AI error: {ErrorMessage}", claudeError.Error?.Message);
            }
            else
            {
                Logger.LogError("Failed to get response from Claude AI. Status code: {StatusCode}", response.StatusCode);
            }
            return null;
        }

        return claudeResponse;
    }

    private string? ExtractTextFromResponse(ClaudeResponse? response)
    {
        if (response is not ClaudeMessageResponse messageResponse)
        {
            Logger.LogWarning("Response was not a ClaudeMessageResponse");
            return null;
        }

        if (messageResponse.Content == null || !messageResponse.Content.Any())
        {
            Logger.LogWarning("Claude response content was empty");
            return null;
        }

        var responseText = HandleClaudeResponse(messageResponse);
        if (string.IsNullOrEmpty(responseText))
        {
            Logger.LogWarning("Claude response text was empty after handling");
            return null;
        }

        return responseText;
    }

    public string HandleClaudeResponse(ClaudeMessageResponse response)
    {
        if (response.StopReason == "end_turn")
        {
            return CollectAllTextContent(response);
        }

        switch (response.StopReason)
        {
            case "tool_use":
                throw new NotImplementedException("Tool use response handling is not implemented yet.");
            case "max_tokens":
                return $"{CollectAllTextContent(response)} (truncated due to max tokens limit)";
            case "pause_turn":
                throw new NotImplementedException("Pause turn response handling is not implemented yet.");
            case "refusal":
                return $"Claude refused to answer: {CollectAllTextContent(response)}";
            default:
                // Handle "end_turn" and other/unknown cases
                return response.Content.FirstOrDefault()?.Text ?? string.Empty;
        }
    }

    public string CollectAllTextContent(ClaudeMessageResponse response)
    {
        return string.Concat(response.Content.Where(c => c.Type == ClaudeContentType.Text)
            .Select(c => c.Text)
            .Where(text => !string.IsNullOrEmpty(text)));
    }
}