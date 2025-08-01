using System.Collections.Concurrent;
using System.Text.Json;
using Anthropic.SDK.Constants;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Wernstrom;

public partial class WernstromService : BackgroundService
{
    private ConcurrentQueue<string> StatusMessages = new();
    private DateTimeOffset LastStatusUpdate = DateTimeOffset.MinValue;

    private string? CurrentActivity = null;

    internal const string STATUS_INSTRUCTION = $"""
        {GENERIC_INSTRUCTION}
        Generiere fünf Statusmeldungen für deinen Benutzerstatus.
        Jede Meldung soll extrem kurz sein und eine aktuelle Tätigkeit oder einen Slogan von dir enthalten.
        Liefere die Statusmeldungen als JSON-Array, ohne zusätzliche Erklärungen oder Formatierungen.
        """;

    internal readonly OpenAIPromptExecutionSettings StatusSettings = new() // We can use the OpenAI Settings for Claude, they are compatible
    {
        ModelId = AnthropicModels.Claude4Sonnet,
        FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
        MaxTokens = 1000,
        Temperature = 0.6,
    };

    private async Task UpdateStatusAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - LastStatusUpdate < TimeSpan.FromMinutes(120))
            return;

        LastStatusUpdate = now;
        if (StatusMessages.IsEmpty)
        {
            var newMessages = await CreateNewStatusMessages();
            foreach (var msg in newMessages)
            {
                StatusMessages.Enqueue(msg);
            }
        }

        var activity = StatusMessages.TryDequeue(out var statusMessage) ? statusMessage : null;
        CurrentActivity = activity;
        await Client.SetCustomStatusAsync(activity).ConfigureAwait(false);
    }

    internal async Task<List<string>> CreateNewStatusMessages()
    {
        List<string> statusMessages = [];
        ChatHistory history = [];
        history.AddDeveloperMessage(STATUS_INSTRUCTION);
        const string prefill = "[\"";
        history.AddAssistantMessage(prefill);

        try
        {
            var response = await ChatService.GetChatMessageContentAsync(history, StatusSettings, Kernel);
            if (string.IsNullOrEmpty(response.Content))
            {
                Logger.LogWarning($"Got an empty response for status messages");
                return []; // may be rate limited 
            }

            var text = prefill + response.Content;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var trimmedMessage = item.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedMessage) && trimmedMessage.Length < 50)
                            statusMessages.Add(trimmedMessage);
                    }
                }
                else
                {
                    Logger.LogWarning("Status message response was not a JSON array: {Text}", text);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse status messages JSON: {Text}", text);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the AI call to generate status messages.");
        }

        return statusMessages;
    }
}
