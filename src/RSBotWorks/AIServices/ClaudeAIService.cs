using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
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
        client.DefaultRequestHeaders.Add("User-Agent", "RSBotWorks/1.0 github.com/rschili chat bot");
        return client;
    }

    public override async Task<string?> DoGenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default)
    {
        using var client = CreateClientWithApiKey();
        var request = new ClaudeMessageRequest
        {
            Model = Model,
            MaxTokens = 1000,
            Temperature = 0.6,
            System = new List<ClaudeSystemContent>
            {
                new ClaudeSystemContent { Text = systemPrompt }
            },
            Messages = inputs.Select(input => new ClaudeRequestMessage
            {
                Role = input.IsSelf ? ClaudeRole.Assistant : ClaudeRole.User,
                Content = input.IsSelf ? input.Message : $"[[{input.ParticipantName}]] {input.Message}"
            }).ToList()
        };

        const string url = "https://api.anthropic.com/v1/messages";
        var serializedRequest = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
        Logger.LogWarning("Claude request: {Request}", serializedRequest);
        var response = await client.PostAsJsonAsync(url, request).ConfigureAwait(false);
        ClaudeResponse? claudeResponse = null;
        if (response.Content != null)
        {
            var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            claudeResponse = await JsonSerializer.DeserializeAsync<ClaudeResponse>(responseStream);
        }
        if (claudeResponse == null)
        {
            Logger.LogWarning("Claude response was null. Status code: {StatusCode}", response.StatusCode);
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            if (claudeResponse is ClaudeErrorResponse claudeError)
            {
                Logger.LogError("Claude AI error: {ErrorMessage}", claudeError.Error?.Message);
                return null;
            }

            Logger.LogError("Failed to get response from Claude AI. Status code: {StatusCode}, content was not a ClaudeErrorResponse", response.StatusCode);
            return null;
        }
        if (claudeResponse is ClaudeMessageResponse messageResponse)
        {
            if (messageResponse.Content == null || !messageResponse.Content.Any())
            {
                Logger.LogWarning("Claude response content was empty.");
                return null;
            }

            var responseText = HandleClaudeResponse(messageResponse);
            if (string.IsNullOrEmpty(responseText))
            {
                Logger.LogWarning("Claude response text was empty after handling.");
                return null;
            }

            return responseText;
        }
        return null;
    }

    public override Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType)
    {
        throw new NotImplementedException();
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

