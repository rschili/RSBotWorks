using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using RSBotWorks.SaneAI;

namespace Wernstrom;

/// <summary>
/// Chat history selection and rendering for the LLM.
/// Rendering is designed to be prefix-stable between requests (absolute timestamps, sticky
/// window start) so prompt caching can hit. Format of a user message:
/// <code>
/// {Reaktionen auf deine Nachricht: lol ([[Lyeis]])}       &lt;- only if the previous assistant message got reactions
/// [10:14] [[Lyeis]] (antwortet [[krael]]: „Ich mag…“): Ja dann findest du…
/// {Reaktionen: lol ([[Krael]], du), fire×3}
/// </code>
/// </summary>
public partial class WernstromService
{
    /// <summary>Largest history window we ever fetch from Discord. Mostly served from the local message cache.</summary>
    private const int MaxHistoryMessages = 50;

    /// <summary>Always keep at least this many messages, even if they are old. Keeps sparse conversations coherent.</summary>
    private const int MinHistoryMessages = 4;

    /// <summary>Images are only kept for the newest N messages. Older images are dropped (they are expensive in tokens).
    /// This is position based, so it changes the prompt near its tail only — acceptable for caching.</summary>
    private const int MaxImageDepth = 6;

    /// <summary>Total character budget for history text. Lets us include many short messages or fewer long ones.</summary>
    private const int MaxHistoryChars = 6000;

    /// <summary>User messages (history and trigger) are cut to this length. Wernstrom's own messages are left intact.</summary>
    internal const int MaxMessageChars = 1000;

    /// <summary>Messages older than this are dropped once the minimum is satisfied.</summary>
    private static readonly TimeSpan MaxHistoryAge = TimeSpan.FromHours(4);

    /// <summary>The previous window start is kept (for prompt caching) while the window stays within these limits.</summary>
    private const int MaxStickyHistoryChars = MaxHistoryChars * 3 / 2;
    private static readonly TimeSpan MaxStickyHistoryAge = TimeSpan.FromHours(6);

    /// <summary>Reactor names are resolved for Wernstrom's own messages and the newest N history messages; others get counts only.</summary>
    private const int ReactorNameDepth = 3;
    private const int MaxReactionsPerMessage = 3;
    private const int MaxReactorNames = 4;
    private const int MaxReplySnippetChars = 50;
    private const int MaxReactorCacheEntries = 2000;

    internal const string SelfReactorLabel = "du";

    /// <summary>Oldest message of the last chat window per channel. Keeps the prompt prefix stable between requests.</summary>
    private readonly ConcurrentDictionary<ulong, ulong> HistoryAnchors = new();

    /// <summary>Reactor names per (message, emote, count) — avoids a REST call per reaction on every request.</summary>
    private readonly ConcurrentDictionary<(ulong MessageId, string EmoteKey, int Count), string[]> ReactorCache = new();

    private Lazy<IReadOnlyDictionary<string, string>> EmoteNames { get; init; }

    internal readonly record struct HistorySelection(IMessage Message, bool IncludeImages);

    /// <summary>An aggregated reaction on a message. <paramref name="Names"/> excludes Wernstrom himself and is null if not resolved.</summary>
    internal readonly record struct ReactionInfo(string Emote, int Count, bool IncludesMe, IReadOnlyList<string>? Names);

    /// <summary>
    /// Picks which of the fetched messages (newest-first) to include in the prompt and whether each keeps its images.
    /// Walks from newest to oldest, stopping when the budget is exhausted, but always keeps a minimum for context.
    /// If <paramref name="anchorId"/> (the previous window start) is older than that and still within the sticky limits,
    /// the window is extended back to it so the prompt prefix stays identical for caching.
    /// Returns the selection in chronological (oldest-first) order.
    /// </summary>
    internal static List<HistorySelection> SelectHistory(IReadOnlyList<IMessage> newestFirst, DateTimeOffset triggerTime, ulong? anchorId = null)
    {
        int count = 0;
        int remainingChars = MaxHistoryChars;

        for (int i = 0; i < newestFirst.Count && count < MaxHistoryMessages; i++)
        {
            var message = newestFirst[i];
            bool haveMinimum = count >= MinHistoryMessages;

            // Drop anything older than the cutoff, but only once we already have enough
            // context — a quiet channel should still show its last few messages.
            if (haveMinimum && triggerTime - message.Timestamp > MaxHistoryAge)
                break;

            int cost = Math.Min(message.Content?.Length ?? 0, MaxMessageChars);
            if (haveMinimum && cost > remainingChars)
                break;

            remainingChars -= cost;
            count++;
        }

        if (anchorId is ulong anchor)
        {
            int anchorIndex = -1;
            for (int i = 0; i < newestFirst.Count; i++)
            {
                if (newestFirst[i].Id == anchor)
                {
                    anchorIndex = i;
                    break;
                }
            }

            if (anchorIndex >= count && anchorIndex < MaxHistoryMessages
                && triggerTime - newestFirst[anchorIndex].Timestamp <= MaxStickyHistoryAge)
            {
                int totalChars = 0;
                for (int i = 0; i <= anchorIndex; i++)
                    totalChars += Math.Min(newestFirst[i].Content?.Length ?? 0, MaxMessageChars);
                if (totalChars <= MaxStickyHistoryChars)
                    count = anchorIndex + 1;
            }
        }

        var selected = new List<HistorySelection>(count);
        for (int i = 0; i < count; i++)
            selected.Add(new HistorySelection(newestFirst[i], IncludeImages: i < MaxImageDepth));

        selected.Reverse(); // chronological order for the prompt
        return selected;
    }

    /// <summary>
    /// Adds the chronological history plus the trigger message to the composer.
    /// </summary>
    /// <param name="includeReactorNames">Resolve who reacted (REST calls, cached). Counts only if false.</param>
    private async Task AddHistoryToComposer(OpenRouterRequestComposer composer, IReadOnlyList<HistorySelection> history,
        IMessage trigger, JoinedTextChannel<ulong> channel, bool includeReactorNames)
    {
        var entries = history.Append(new HistorySelection(trigger, IncludeImages: true)).ToList();
        var selfId = DiscordClient.CurrentUser.Id;
        var referenceTime = trigger.Timestamp;

        // Reactions are collected in parallel up front (they may need REST calls for reactor names).
        // The trigger is the last entry; history messages before it are counted for the name depth.
        var reactionTasks = entries.Select((entry, index) =>
        {
            bool isSelf = entry.Message.Author.Id == selfId;
            bool withNames = includeReactorNames && (isSelf || index >= history.Count - ReactorNameDepth);
            return CollectReactionsAsync(entry.Message, withNames, channel);
        }).ToArray();
        var reactions = await Task.WhenAll(reactionTasks).ConfigureAwait(false);

        var pendingSelfReactions = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            var (message, includeImages) = entries[i];
            var reactionText = FormatReactions(reactions[i]);

            if (message.Author.Id == selfId)
            {
                // Assistant turns stay pure message text so the model never learns to imitate metadata.
                // Reactions to them are reported at the start of the next user message instead.
                composer.AddAssistantMessage(ReplaceDiscordTags(message.Tags, message.Content, channel));
                if (reactionText != null)
                    pendingSelfReactions.Add($"{{Reaktionen auf deine Nachricht: {reactionText}}}");
                continue;
            }

            var text = new StringBuilder();
            foreach (var line in pendingSelfReactions)
                text.Append(line).Append('\n');
            pendingSelfReactions.Clear();

            var user = ResolveChannelUser(message.Author, channel);
            text.Append('[').Append(FormatTimestamp(message.Timestamp, referenceTime)).Append("] [[")
                .Append(user.SanitizedName).Append("]]");
            var replyContext = FormatReplyContext(message, channel);
            if (replyContext != null)
                text.Append(' ').Append(replyContext);
            text.Append(": ").Append(TruncateMessage(ReplaceDiscordTags(message.Tags, message.Content, channel)));

            var attachments = includeImages ? await ExtractImageAttachments(message).ConfigureAwait(false) : null;
            // Images were dropped (too far back) but leave a cheap marker so the
            // model still knows something visual was shared.
            if (!includeImages && MessageHasImage(message))
                text.Append(string.IsNullOrWhiteSpace(message.Content) ? "[Bild]" : " [Bild]");

            if (reactionText != null)
                text.Append("\n{Reaktionen: ").Append(reactionText).Append('}');

            if (attachments != null && attachments.Count > 0)
            {
                var blocks = new List<OpenRouterMessageBlock> { OpenRouterMessageBlock.FromText(text.ToString()) };
                foreach (var attachment in attachments)
                    blocks.Add(OpenRouterMessageBlock.FromImage(attachment.MimeType, attachment.Data));
                composer.AddUserMessage(blocks.ToArray());
            }
            else
            {
                composer.AddUserMessage(text.ToString());
            }

            // Wernstrom's 🤐 marks a deliberate non-response. It is shown as such, not as a reaction.
            if (message.Reactions.Any(r => r.Value.IsMe && IsNoResponseMarker(r.Key)))
                composer.AddAssistantMessage("<NO_RESPONSE>");
        }
    }

    private async Task<IReadOnlyList<ReactionInfo>> CollectReactionsAsync(IMessage message, bool withNames, JoinedTextChannel<ulong> channel)
    {
        if (message.Reactions.Count == 0)
            return [];

        var candidates = new List<(IEmote Emote, int Count, bool IsMe)>();
        foreach (var (emote, metadata) in message.Reactions)
        {
            int count = metadata.ReactionCount;
            bool isMe = metadata.IsMe;
            if (isMe && IsNoResponseMarker(emote))
            {
                // Already represented as <NO_RESPONSE>; keep only other people's 🤐.
                count--;
                isMe = false;
            }
            if (count > 0)
                candidates.Add((emote, count, isMe));
        }

        // OrderByDescending is stable: ties keep Discord's order.
        var top = candidates.OrderByDescending(c => c.Count).Take(MaxReactionsPerMessage).ToList();
        var tasks = top.Select(async c => new ReactionInfo(
            GetReactionName(c.Emote), c.Count, c.IsMe,
            withNames ? await GetReactorNamesAsync(message, c.Emote, c.Count, channel).ConfigureAwait(false) : null));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<string[]?> GetReactorNamesAsync(IMessage message, IEmote emote, int count, JoinedTextChannel<ulong> channel)
    {
        var key = (message.Id, EmoteKey(emote), count);
        if (ReactorCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var selfId = DiscordClient.CurrentUser.Id;
            var users = await message.GetReactionUsersAsync(emote, 100).FlattenAsync().ConfigureAwait(false);
            // Resolve names without mutating the channel cache — this runs in parallel.
            var names = users
                .Where(u => u.Id != selfId)
                .Select(u => (channel.GetUser(u.Id) ?? GenerateChannelUser(u)).SanitizedName)
                .ToArray();

            if (ReactorCache.Count >= MaxReactorCacheEntries)
                ReactorCache.Clear();
            ReactorCache[key] = names;
            return names;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not fetch reaction users for message {MessageId}, emote {Emote}", message.Id, emote.Name);
            return null; // fall back to counts
        }
    }

    /// <summary>
    /// Formats reactions compactly, e.g. <c>lol ([[Lyeis]], [[Krael]], du), fire×3 (inkl. du), cat</c>.
    /// Returns null if there are none.
    /// </summary>
    internal static string? FormatReactions(IReadOnlyList<ReactionInfo> reactions)
    {
        if (reactions.Count == 0)
            return null;

        return string.Join(", ", reactions.Select(FormatReaction));
    }

    private static string FormatReaction(ReactionInfo reaction)
    {
        if (reaction.Names != null)
        {
            var people = new List<string>();
            if (reaction.IncludesMe)
                people.Add(SelfReactorLabel); // first, so it is never truncated
            people.AddRange(reaction.Names.Select(n => $"[[{n}]]"));
            var shown = people.Take(MaxReactorNames).ToList();
            int hidden = reaction.Count - shown.Count;
            if (hidden > 0)
                shown.Add($"+{hidden}");
            if (shown.Count > 0)
                return $"{reaction.Emote} ({string.Join(", ", shown)})";
        }

        var counted = reaction.Count > 1 ? $"{reaction.Emote}×{reaction.Count}" : reaction.Emote;
        if (!reaction.IncludesMe)
            return counted;
        return reaction.Count > 1 ? $"{counted} (inkl. {SelfReactorLabel})" : $"{counted} ({SelfReactorLabel})";
    }

    private string? FormatReplyContext(IMessage message, JoinedTextChannel<ulong> channel)
    {
        if (message.Type != MessageType.Reply || message is not IUserMessage { ReferencedMessage: { } referenced })
            return null;

        var target = referenced.Author.Id == DiscordClient.CurrentUser.Id
            ? "dir"
            : $"[[{ResolveChannelUser(referenced.Author, channel).SanitizedName}]]";
        var snippet = CreateReplySnippet(ReplaceDiscordTags(referenced.Tags, referenced.Content ?? "", channel));
        return snippet.Length == 0 ? $"(antwortet {target})" : $"(antwortet {target}: „{snippet}“)";
    }

    /// <summary>Cuts a user message to <see cref="MaxMessageChars"/>, marking the cut with an ellipsis.</summary>
    internal static string TruncateMessage(string text)
    {
        if (text.Length <= MaxMessageChars)
            return text;

        int cut = MaxMessageChars;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--; // do not split an emoji
        return text[..cut] + "…";
    }

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Single-line excerpt of a replied-to message, truncated with an ellipsis.</summary>
    internal static string CreateReplySnippet(string text)
    {
        var collapsed = WhitespaceRegex.Replace(text, " ").Trim();
        if (collapsed.Length <= MaxReplySnippetChars)
            return collapsed;

        int cut = MaxReplySnippetChars;
        if (char.IsHighSurrogate(collapsed[cut - 1]))
            cut--; // do not split an emoji
        return collapsed[..cut].TrimEnd() + "…";
    }

    /// <summary>
    /// Absolute local time — unlike relative labels ("5m ago") it does not change between requests,
    /// which keeps the prompt prefix cacheable. The date is only added for messages from another day.
    /// </summary>
    internal static string FormatTimestamp(DateTimeOffset timestamp, DateTimeOffset referenceTime)
    {
        var local = TimeZoneInfo.ConvertTime(timestamp, BotTimeZone);
        var reference = TimeZoneInfo.ConvertTime(referenceTime, BotTimeZone);
        var format = local.Date == reference.Date ? "HH:mm" : "dd.MM. HH:mm";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    private static bool IsNoResponseMarker(IEmote emote) => emote.Name?.Contains(NoReactionEmoji) == true;

    private static string EmoteKey(IEmote emote) => emote is Emote custom
        ? custom.Id.ToString(CultureInfo.InvariantCulture)
        : (emote.Name ?? "").Replace("\uFE0F", "");

    /// <summary>Translates a reaction back to the name Wernstrom uses when reacting himself.</summary>
    private string GetReactionName(IEmote emote)
    {
        if (EmoteNames.Value.TryGetValue(EmoteKey(emote), out var name))
            return name;
        return emote is Emote custom ? GetEmojiDescriptiveName(custom.Name) ?? custom.Name : emote.Name ?? "?";
    }

    private IReadOnlyDictionary<string, string> BuildEmoteNamesDictionary()
    {
        var names = new Dictionary<string, string>();
        foreach (var (name, emote) in Emotes.Value)
            names.TryAdd(EmoteKey(emote), name);
        return names;
    }

    private ChannelUser<ulong> ResolveChannelUser(IUser author, JoinedTextChannel<ulong> channel)
    {
        return channel.GetOrAddUser(author.Id, () => GenerateChannelUser(author));
    }
}
