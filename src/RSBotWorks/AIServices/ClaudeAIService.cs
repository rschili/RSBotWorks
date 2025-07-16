using System.Diagnostics.CodeAnalysis;
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

    public override Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType)
    {
        throw new NotImplementedException();
    }

    public override Task<string?> GenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default)
    {
        throw new NotImplementedException();
    }
}