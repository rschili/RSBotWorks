using System.Collections.Concurrent;
using System.Text.Json;
using Anthropic.SDK.Constants;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RSBotWorks.UniversalAI;

namespace Wernstrom;

public partial class WernstromService
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

    internal PreparedChatParameters StatusParameters { get; private set; }

    private PreparedChatParameters PrepareStatusParameters()
    {
        ChatParameters parameters = new()
        {
            ToolChoiceType = ToolChoiceType.None,
            MaxTokens = 1000,
            Temperature = 0.6m,
            Prefill = "[\"",
        };
        return ChatClient.PrepareParameters(parameters);
    }

    private async Task UpdateStatusAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - LastStatusUpdate < TimeSpan.FromMinutes(120))
            return;

        LastStatusUpdate = now;
        if (StatusMessages.IsEmpty)
        {
            var newMessages = await CreateNewStatusMessages().ConfigureAwait(false);
            foreach (var msg in newMessages)
            {
                StatusMessages.Enqueue(msg);
            }
        }

        var activity = StatusMessages.TryDequeue(out var statusMessage) ? statusMessage : null;
        CurrentActivity = activity;
        await DiscordClient.SetCustomStatusAsync(activity).ConfigureAwait(false);
    }

    internal async Task<List<string>> CreateNewStatusMessages()
    {
        List<string> statusMessages = [];
        List<Message> history = [Message.FromText(Role.User, STATUS_INSTRUCTION)];
        string? systemPrompt = null;

        try
        {
            var response = await ChatClient.CallAsync(systemPrompt, history, StatusParameters).ConfigureAwait(false);
            if (string.IsNullOrEmpty(response))
            {
                Logger.LogWarning($"Got an empty response for status messages");
                return []; // may be rate limited 
            }

            try
            {
                using var doc = JsonDocument.Parse(response);
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
                    Logger.LogWarning("Status message response was not a JSON array: {Text}", response);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse status messages JSON: {Text}", response);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the AI call to generate status messages.");
        }

        return statusMessages;
    }
}
