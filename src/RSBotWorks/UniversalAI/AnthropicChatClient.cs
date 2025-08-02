
using Anthropic.SDK;
using Microsoft.Extensions.Logging;

namespace RSBotWorks.UniversalAI;

internal class AnthropicChatClient : TypedChatClient<AnthropicClient>
{
    public AnthropicChatClient(string modelName, AnthropicClient innerClient, ILogger? logger = null)
        : base(modelName, innerClient, logger)
    {
    }
}