using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RSBotWorks.SaneAI;

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
        Liefere die Statusmeldungen als pures JSON-Array, ohne zusätzliche Erklärungen oder Formatierungen. (Beispiel: ["Status 1", "Status 2", "Status 3", "Status 4", "Status 5"])
        """;

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

        try
        {
            var composer = StatusTemplate.Fork()
                .AddUserMessage(STATUS_INSTRUCTION);

            var stopwatch = Stopwatch.StartNew();
            var result = await AiClient.SendAsync(composer).ConfigureAwait(false);
            stopwatch.Stop();

            LogAiResult("Status", result, stopwatch.Elapsed);

            if (string.IsNullOrEmpty(result.TextContent))
            {
                Logger.LogWarning("Got an empty response for status messages");
                return [];
            }

            try
            {
                using var doc = JsonDocument.Parse(result.TextContent);
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
                    Logger.LogWarning("Status message response was not a JSON array: {Text}", result.TextContent);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse status messages JSON: {Text}", result.TextContent);
            }
        }
        catch (AnthropicApiException ex)
        {
            Logger.LogError(ex, "Anthropic API error ({ErrorType}) during status message generation. Curl: {Curl}",
                ex.ErrorType, ex.ToCurl());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the AI call to generate status messages.");
        }

        return statusMessages;
    }
}
