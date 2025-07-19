using Microsoft.Extensions.Logging;
using RSBotWorks.Tools;

namespace RSBotWorks;

public enum AIProvider
{
    OpenAI,
    Claude,
    MoonshotAI
}

public record AIModel(AIProvider Provider, string ModelName)
{
    public static readonly AIModel GPT4o = new(AIProvider.OpenAI, "gpt-4o");
    public static readonly AIModel GPT41 = new(AIProvider.OpenAI, "gpt-4.1");
    public static readonly AIModel O1 = new(AIProvider.OpenAI, "o1");
    public static readonly AIModel O3Mini = new(AIProvider.OpenAI, "o3-mini");
    public static readonly AIModel ClaudeSonnet4 = new(AIProvider.Claude, "claude-sonnet-4-0");
    public static readonly AIModel MoonshotAIKimiK2 = new(AIProvider.MoonshotAI, "kimi-k2-0711-preview");
}

public static class AIServiceFactory
{
    public static IAIService CreateService(AIModel model, AIServiceCredentials credentials, ToolHub toolHub, IHttpClientFactory? httpClientFactory, ILogger? logger = null)
    {
        return model.Provider switch
        {
            AIProvider.OpenAI => new OpenAIService(credentials.GetRequiredKey(AIProvider.OpenAI), toolHub, model.ModelName, logger),
            AIProvider.Claude => new ClaudeAIService(credentials.GetRequiredKey(AIProvider.Claude), toolHub, model.ModelName, httpClientFactory ?? throw new ArgumentNullException("httpClientFactory is required for ClaudeAIService"), logger),
            AIProvider.MoonshotAI => new MoonshotAIService(credentials.GetRequiredKey(AIProvider.MoonshotAI),
                credentials.GetRequiredKey(AIProvider.OpenAI), toolHub, model.ModelName, logger),
            _ => throw new NotImplementedException($"AI provider {model.Provider} is not implemented.")
        };
    }
}

public record AIServiceCredentials(
    string? OpenAIKey = null,
    string? ClaudeKey = null,
    string? MoonshotKey = null
)
{
    public string GetRequiredKey(AIProvider provider) => provider switch
    {
        AIProvider.OpenAI => OpenAIKey ?? throw new ArgumentNullException(nameof(OpenAIKey)),
        AIProvider.Claude => ClaudeKey ?? throw new ArgumentNullException(nameof(ClaudeKey)),
        AIProvider.MoonshotAI => MoonshotKey ?? throw new ArgumentNullException(nameof(MoonshotKey)),
        _ => throw new NotImplementedException($"AI provider {provider} is not implemented.")
    };
}