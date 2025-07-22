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

public interface IAIService : IDisposable
{
    Task<string?> GenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default);
    Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType);
}

public record AIMessage(bool IsSelf, string Message, string ParticipantName);


public abstract class BaseAIService : IAIService
{
    private bool disposedValue;

    [SetsRequiredMembers]
    protected BaseAIService(ToolHub toolhub, ILogger? logger)
    {
        Logger = logger ?? NullLogger<BaseAIService>.Instance;
        ToolHub = toolhub ?? throw new ArgumentNullException(nameof(toolhub));
    }

    public required ILogger Logger { get; init; }

    public required ToolHub ToolHub { get; init; }

    protected LeakyBucketRateLimiter RateLimiter { get; private init; } = new(10, 60);

    public async Task<string?> GenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default)
    {
        if (!RateLimiter.Leak())
            return null;

        systemPrompt = $"""
            {systemPrompt}
            Aktuell ist [{DateTime.Now.ToLongDateString()}, {DateTime.Now.ToLongTimeString()} Uhr. 
            """;

        return await DoGenerateResponseAsync(systemPrompt, inputs, kind);
    }

    public abstract Task<string?> DoGenerateResponseAsync(string systemPrompt, IEnumerable<AIMessage> inputs, ResponseKind kind = ResponseKind.Default);

    public abstract Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string mimeType);

    public virtual void Dispose()
    {}
}