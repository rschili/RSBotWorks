using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using RSBotWorks.Tools;

public class ClaudeAIService : BaseAIService
{

    public IHttpClientFactory HttpClientFactory { get; private init; }

    public string Model { get; private init; }

    public string ApiKey { get; private init; }

    [SetsRequiredMembers]
    public ClaudeAIService(string apiKey, ToolHub toolhub, string model, IHttpClientFactory httpClientFactory, ILogger? logger = null) : base(toolhub, logger)
    {
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    private HttpClient CreateClientWithApiKey()
    {
        var client = HttpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("Content-Type", "application/json");
        client.DefaultRequestHeaders.Add("User-Agent", "RSBotWorks/1.0 github.com/rschili chat bot");
        return client;
    }

    public override async Task<string?> DoGenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default)
    {
        throw new NotImplementedException("ClaudeAIService does not implement DoGenerateResponseAsync yet.");
    }

    public override Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType)
    {
        throw new NotImplementedException();
    }

    public string HandleClaudeResponse(ClaudeMessageResponse response)
    {
        switch (response.StopReason)
        {
            case "tool_use":
                return HandleToolUse(response);
            case "max_tokens":
                return HandleTruncation(response);
            case "pause_turn":
                return HandlePause(response);
            case "refusal":
                return HandleRefusal(response);
            default:
                // Handle "end_turn" and other/unknown cases
                return response.Content.FirstOrDefault()?.Text ?? string.Empty;
        }
    }

    // Example stubs for the handler methods:
    private string HandleToolUse(ClaudeMessageResponse response)
    {
        // Implement tool use handling logic here
        return "[Tool use required]";
    }

    private string HandleTruncation(ClaudeMessageResponse response)
    {
        // Implement truncation handling logic here
        return "[Response truncated due to max tokens]";
    }

    private string HandlePause(ClaudeMessageResponse response)
    {
        // Implement pause handling logic here
        return "[Paused turn]";
    }

    private string HandleRefusal(ClaudeMessageResponse response)
    {
        // Implement refusal handling logic here
        return "[Refusal]";
    }
}

