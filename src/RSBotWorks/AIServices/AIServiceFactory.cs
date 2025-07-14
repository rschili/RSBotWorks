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
    public static readonly AIModel ClaudeSonnet4 = new(AIProvider.Claude, "claude-sonnet-4"); // TODO: verify model name
    public static readonly AIModel MoonshotAIKimiK2 = new(AIProvider.MoonshotAI, "kimi-k2"); // TODO: verify model name
}

public static class AIServiceFactory
{
    public static IAIService CreateService(AIModel model, string apiKey, ToolHub toolHub, ILogger? logger = null)
    {
        return model.Provider switch
        {
            AIProvider.OpenAI => new OpenAIService(apiKey, toolHub, model.ModelName, logger),
            //AIProvider.Claude => new ClaudeService(apiKey, toolHub, model, logger),
            //AIProvider.MoonshotAI => new MoonshotAIService(apiKey, toolHub, model, logger),
            _ => throw new NotImplementedException($"AI provider {model.Provider} is not implemented.")
        };
    }
}