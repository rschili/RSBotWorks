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
    internal OpenRouterRequestComposer ReactionTemplate { get; init; }

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

    /// <summary>Greeting → random food/drink reaction. Purely local, the message never leaves the bot.</summary>
    private async Task HandleGreetingReactionAsync(RSMatrix.Models.ReceivedTextMessage message, string sanitizedMessage)
    {
        if (!GreetingKeywords.Contains(sanitizedMessage.Trim()))
            return;

        var emoji = GreetingReactions[Random.Shared.Next(GreetingReactions.Length)];
        try
        {
            await message.SendReactionAsync(emoji).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send greeting reaction.");
        }
    }

    /// <summary>
    /// AI-chosen emoji reaction. NOT WIRED UP on purpose: reactions target messages Stoll does not answer, i.e. messages
    /// that do not address him — sending those to the LLM violates the consent model. Kept as plumbing for a future
    /// consent-compatible use (e.g. reacting instead of answering to messages that do address him).
    /// </summary>
    private async Task HandleAiReactionAsync(RSMatrix.Models.ReceivedTextMessage message, JoinedTextChannel<string> cachedChannel, string sanitizedMessage)
    {
        // Probability ramp — most messages get no reaction
        if (!EmojiProbabilityRamp.Check())
            return;

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
        catch (OpenRouterApiException ex)
        {
            Logger.LogError("[Reaction] OpenRouter API error: HTTP {StatusCode}, code {ErrorCode}, {ErrorMessage}. " +
                "ResponseBody (truncated={ResponseBodyTruncated}): {ResponseBody}",
                ex.StatusCode, ex.ErrorCode, ex.Message,
                ex.ErrorBody.Length > MaxDiagnosticResponseChars, DiagnosticResponseExcerpt(ex.ErrorBody));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Reaction] Error adding reaction to: {Message}", sanitizedMessage.Length > 100 ? sanitizedMessage[..100] : sanitizedMessage);
        }
    }
}
