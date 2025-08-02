using Anthropic.SDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RSBotWorks.UniversalAI;

public static class OpenAIModels
{
    public const string GPT4o = "gpt-4o";
    public const string GPT41 = "gpt-4.1";
    public const string O1 = "o1";
    public const string O3Mini = "o3-mini";
}

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
    
    public static ChatClient CreateAnthropicClient(string modelName, string apiKey, IHttpClientFactory? httpClientFactory = null, ILogger? logger = null)
    {
        var innerClient = new AnthropicClient(new APIAuthentication(apiKey), httpClientFactory != null ? httpClientFactory.CreateClient() : null);
        return new AnthropicChatClient(modelName, innerClient, logger);
    }

    public abstract void Dispose();
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
