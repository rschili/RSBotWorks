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

    const string ENDPOINT_URL = "https://api.anthropic.com/v1/messages";
    const string JSON_ARRAY_PREFILL = "{ \"values\": [\"";

    public override async Task<string?> DoGenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default)
    {
        var processedInputs = inputs;
        var processedPrompt = systemPrompt;

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
                new ClaudeSystemContent { Text = systemPrompt }
            },
            Messages = [.. inputs.Select(input =>
            {
                var text = input.IsSelf ? input.Message : $"[[{input.ParticipantName}]] {input.Message}";
                return ClaudeRequestMessage.Create(input.IsSelf ? ClaudeRole.User : ClaudeRole.Assistant, text);
            })],
        };

        var response = await SendRequestAsync(request);
        var responseText = ExtractTextFromResponse(response);

        if (kind == ResponseKind.StructuredJsonArray)
            responseText = JSON_ARRAY_PREFILL + responseText;

        return responseText;
    }

    public override async Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType)
    {
        var request = new ClaudeMessageRequest()
        {
            Model = Model,
            MaxTokens = 1000,
            Temperature = 0.6,
            System = new List<ClaudeSystemContent>
            {
                new ClaudeSystemContent { Text = systemPrompt }
            },
            Messages = [
                ClaudeRequestMessage.Create(ClaudeRole.User, b => b.AddBase64Image(mimeType, imageBytes))
            ],
        };

        var response = await SendRequestAsync(request);
        var responseText = ExtractTextFromResponse(response);
        return responseText ?? "Error: Unable to describe image.";
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

