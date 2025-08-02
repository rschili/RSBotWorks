
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace RSBotWorks.UniversalAI;

internal class OpenAIChatClient : TypedChatClient<OpenAI.Chat.ChatClient>
{
    public OpenAIChatClient(string modelName, OpenAI.Chat.ChatClient innerClient, ILogger? logger = null)
        : base(modelName, innerClient, logger)
    {
    }
}