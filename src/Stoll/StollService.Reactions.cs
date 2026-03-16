using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using RSBotWorks.SaneAI;
using RSFlowControl;

namespace Stoll;

public partial class StollService
{
    private ProbabilityRamp EmojiProbabilityRamp { get; init; } = new(0, 0.4, TimeSpan.FromMinutes(40));

    /// <summary>Reaction composer template — tiny MaxTokens, no tools, no web search.</summary>
    internal AnthropicRequestComposer ReactionTemplate { get; init; }

    private static readonly ImmutableHashSet<string> GreetingKeywords = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "moin",
        "hi",
        "morgen",
        "morgn",
        "guten morgen",
        "servus",
        "servas",
        "dere",
        "oida",
        "porst",
        "prost",
        "grias di",
        "gude",
        "heisl",
        "mahlzeit");

    private static readonly string[] GreetingReactions =
    [
        "☕", "🍵", "🍦", "🥐", "🍳", "🥃",
        "🥖", "🧀", "🍯", "🥛", "🍕", "🍌",
        "🍎", "🍊", "🍓", "🥑", "🍔", "🥨",
        "🍰", "🧁", "🍩", "🍪", "🍫", "🍬",
    ];

    // Unicode emoji the AI can pick from — keeping it curated so the AI doesn't hallucinate garbage
    private static readonly string[] AvailableReactions =
    [
        "👍", "👎", "😂", "🤣", "😅", "😎", "🤔",
        "😱", "🤯", "🫡", "🫠", "🙄", "😤", "🤡",
        "💀", "👀", "🔥", "❤️", "💯", "🧠", "⚡",
        "🚀", "🛸", "👽", "🌍", "☀️", "🌙", "⭐",
        "🎯", "🏆", "🎉", "✅", "❌", "⚠️", "🤷",
    ];

    private static readonly ImmutableHashSet<string> AvailableReactionsSet =
        ImmutableHashSet.Create(StringComparer.Ordinal, AvailableReactions);

    private string REACTION_INSTRUCTION => $"""
        Du bist Dr. Axel Stoll. Wähle eine passende Reaktion auf die letzte Nachricht.
        Wähle aus dieser Liste: {string.Join(", ", AvailableReactions)}
        Gib NUR das Emoji zurück, ohne Formatierung, Anführungszeichen oder sonstigen Text.
        Priorisiere den Inhalt der Nachricht; deine Persönlichkeit sollte nur eine untergeordnete Rolle spielen.
        Nachrichten anderer Benutzer enthalten den Benutzernamen als Präfix im Format: `[[Name]]:`.
        """;

    private async Task HandleReactionsAsync(RSMatrix.Models.ReceivedTextMessage message, JoinedTextChannel<string> cachedChannel, string sanitizedMessage)
    {
        // Greeting → random food/drink reaction
        if (GreetingKeywords.Contains(sanitizedMessage.Trim()))
        {
            var emoji = GreetingReactions[Random.Shared.Next(GreetingReactions.Length)];
            try
            {
                await message.SendReactionAsync(emoji).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to send greeting reaction.");
            }
            return;
        }

        // Probability ramp — most messages get no reaction
        if (!EmojiProbabilityRamp.Check())
            return;

        return; // TEMP: disable reactions until we have a chance to do it in a safer way.

        var systemPrompt = REACTION_INSTRUCTION;
        var composer = ReactionTemplate.Fork()
            .SetSystemPrompt(systemPrompt);

        // Add some recent message history for context
        var olderMessages = GetMessageHistory(cachedChannel.Id);
        foreach (var entry in olderMessages)
        {
            composer.AddUserMessage($"[{entry.Timestamp.ToRelativeToNowLabel()}] [[{entry.Author}]]: {entry.SanitizedMessage}");
            if (!string.IsNullOrWhiteSpace(entry.GeneratedResponse))
                composer.AddAssistantMessage(entry.GeneratedResponse);
        }

        composer.AddUserMessage($"[now] {sanitizedMessage}");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await AiClient.SendAsync(composer).ConfigureAwait(false);
            stopwatch.Stop();

            LogAiResult("Reaction", result, stopwatch.Elapsed);

            var reaction = result.TextContent?.Trim();
            if (string.IsNullOrEmpty(reaction))
            {
                Logger.LogWarning("[Reaction] AI returned empty reaction for: {Message}", sanitizedMessage.Length > 100 ? sanitizedMessage[..100] : sanitizedMessage);
                return;
            }

            // Only allow emoji from our curated list
            if (!AvailableReactionsSet.Contains(reaction))
            {
                Logger.LogWarning("[Reaction] AI returned unknown reaction '{Reaction}', ignoring.", reaction);
                return;
            }

            await message.SendReactionAsync(reaction).ConfigureAwait(false);
        }
        catch (AnthropicApiException ex)
        {
            Logger.LogError(ex, "[Reaction] Anthropic API error ({ErrorType}). Message: {Message}",
                ex.ErrorType, sanitizedMessage.Length > 100 ? sanitizedMessage[..100] : sanitizedMessage);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Reaction] Error adding reaction to: {Message}", sanitizedMessage.Length > 100 ? sanitizedMessage[..100] : sanitizedMessage);
        }
    }
}
