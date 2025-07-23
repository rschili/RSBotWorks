using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RSBotWorks.Tools;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.DependencyInjection;

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

    public static IAIService CreateIChatClientService(AIServiceCredentials credentials, ToolHub toolHub, ILogger? logger = null)
    {
        // See https://github.com/tghamm/Anthropic.SDK/blob/2cc55587f233958a1171c7e9e5e6c0a0af811125/Anthropic.SDK.Tests/SemanticKernelInitializationTests.cs#L19
        IChatClient client = new AnthropicClient(new APIAuthentication(credentials.ClaudeKey)).Messages.AsBuilder().UseKernelFunctionInvocation().Build();
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        var service = client.AsChatCompletionService();
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        var builder = Kernel.CreateBuilder();
        var plugins = builder.Plugins;
        foreach (var tool in toolHub.Tools)
        {
            plugins.AddFromObject(tool);
        }
        builder.Services.AddSingleton(service);
        ChatOptions options = new()
        {
            ModelId = AnthropicModels.Claude4Sonnet,
            MaxOutputTokens = 1000,
            Temperature = 0.6f,
        };
        Kernel kernel = builder.Build();
        return new ChatClientAIService(client, toolHub, logger);
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