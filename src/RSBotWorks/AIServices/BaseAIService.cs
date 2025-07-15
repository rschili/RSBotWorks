using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.Tools;
using RSMatrix.Http;

namespace RSBotWorks;

public enum ResponseKind
{
    Default,
    StructuredJsonArray,
    NoTools
}

public interface IAIService
{
    Task<string?> GenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default);
    Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType);
}

public abstract class BaseAIService : IAIService
{
    [SetsRequiredMembers]
    protected BaseAIService(ToolHub toolhub, ILogger? logger)
    {
        Logger = logger ?? NullLogger<BaseAIService>.Instance;
        ToolHub = toolhub ?? throw new ArgumentNullException(nameof(toolhub));
    }

    public required ILogger Logger { get; init; }

    public required ToolHub ToolHub { get; init; }

    protected LeakyBucketRateLimiter RateLimiter { get; private init; } = new(10, 60);

    public abstract Task<string?> GenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default);

    public abstract Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType);
}