using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RSBotWorks.UniversalAI;

public abstract class ChatClient : IDisposable
{
    public string ModelName { get; init; }

    public ILogger Logger { get; init; }

    protected ChatClient(string modelName, ILogger? logger = null)
    {
        ModelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        Logger = logger ?? NullLogger<ChatClient>.Instance;
    }

    public static ChatClient CreateOpenAIClient(string modelName, string apiKey, ILogger? logger = null)
    {
        OpenAI.Chat.ChatClient innerClient = new OpenAI.Chat.ChatClient(modelName, apiKey);
        return new OpenAIChatClient(modelName, innerClient, logger);
    }

    public static ChatClient CreateOpenAIResponsesClient(string modelName, string apiKey, ILogger? logger = null)
    {
        var innerClient = new OpenAI.Responses.ResponsesClient(modelName, apiKey);
        return new OpenAIResponsesChatClient(modelName, innerClient, logger);
    }

    public static ChatClient CreateAnthropicClient(string modelName, string apiKey, IHttpClientFactory? httpClientFactory = null, ILogger? logger = null)
    {
        var innerClient = new AnthropicClient(new APIAuthentication(apiKey), httpClientFactory != null ? httpClientFactory.CreateClient() : null);
        return new AnthropicChatClient(modelName, innerClient, logger);
    }

    public abstract void Dispose();

    public abstract Task<string> CallAsync(string? systemPrompt, IList<Message> inputs, PreparedChatParameters parameters);

    public abstract PreparedChatParameters PrepareParameters(ChatParameters parameters);
}

public enum Role
{
    Assistant,
    User
}

public record Message
{
    public required Role Role { get; set; }
    public required List<MessageContent> Content { get; set; }

    public static Message FromText(Role role, string text) => new Message() { Role = role, Content = [MessageContent.FromText(text)] };
}

public record MessageContent
{
    public static MessageContent FromImage(string mimeType, byte[] data) => new ImageContent() { MimeType = mimeType, Data = data };

    public static MessageContent FromText(string text) => new TextContent() { Text = text };
}

public record ImageContent : MessageContent
{
    public required string MimeType { get; set; }
    public required byte[] Data { get; set; }
}

public record TextContent : MessageContent
{
    public required string Text { get; set; }
}


internal abstract class TypedChatClient<TNativeClient> : ChatClient
{
    public TNativeClient InnerClient { get; init; }

    internal TypedChatClient(string modelName, TNativeClient innerClient, ILogger? logger = null)
        : base(modelName, logger)
    {
        InnerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
    }

    public override void Dispose()
    {
        if (InnerClient is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

public enum ToolChoiceType
{
    Auto,
    None
}

public abstract class PreparedChatParameters
{
    public required ChatParameters OriginalParameters { get; init; }
}

public record ChatParameters
{
    public int MaxTokens { get; set; }

    public decimal? Temperature { get; set; }

    public int? TopK { get; set; }

    public decimal? TopP { get; set; }

    public ToolChoiceType ToolChoiceType { get; set; } = ToolChoiceType.Auto;

    public bool DisableParallelToolUses { get; set; } = false;

    public bool EnableWebSearch { get; set; } = false;

    public IList<LocalFunction>? AvailableLocalFunctions { get; set; }

    /// <summary>
    /// Can be used to prefill the response, this text will always be included in the response.
    /// </summary>
    public string? Prefill { get; set; }
}
