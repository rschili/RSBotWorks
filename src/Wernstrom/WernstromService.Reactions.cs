using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using RSBotWorks.SaneAI;
using RSFlowControl;

namespace Wernstrom;


public partial class WernstromService
{
    private ProbabilityRamp EmojiProbabilityRamp { get; init; } = new(0, 0.4, TimeSpan.FromMinutes(40));

    private Lazy<IDictionary<string, IEmote>> Emotes { get; set; }

    private Lazy<string> EmojiJsonList { get; set; }

    internal string REACTION_INSTRUCTION(string emojiList) => $"""
        {GENERIC_INSTRUCTION}
        Choose an appropriate reaction for the last message you received from the following JSON list: {emojiList}.
        Only provide the value directly, without formatting, quotes, or additional text.
        When choosing a reaction, prioritize the content of the last message; your personality should play a minor role, as otherwise you would almost always make the same choice.
        Messages from other users in the chat history include the username prefixed as context in the following format: `[[Name]]:`.
        """;

    private Dictionary<string, IEmote> BuildEmotesDictionary()
    {
        var emotes = DiscordClient.Guilds.SelectMany(g => g.Emotes)
            .Where(e => e.IsAvailable == true)
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

        var emotesDict = new Dictionary<string, IEmote>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in emotes)
        {
            var name = group.Key;
            var value = group.First();
            var desc = GetEmojiDescriptiveName(name);
            if (desc == null)
                continue;

            emotesDict[desc] = value;
        }

        emotesDict["coffee"] = new Emoji("☕");
        emotesDict["tea"] = new Emoji("🍵");
        emotesDict["icecream"] = new Emoji("🍦");
        emotesDict["croissant"] = new Emoji("🥐");
        emotesDict["fried-egg"] = new Emoji("🍳");
        emotesDict["whiskey"] = new Emoji("🥃");
        emotesDict["baguette"] = new Emoji("🥖");
        emotesDict["cheese"] = new Emoji("🧀");
        emotesDict["honey"] = new Emoji("🍯");
        emotesDict["milk"] = new Emoji("🥛");
        emotesDict["alarm_clock"] = new Emoji("⏰");
        emotesDict["pizza"] = new Emoji("🍕");
        emotesDict["heart"] = new Emoji("❤️");
        emotesDict["brain"] = new Emoji("🧠");
        return emotesDict;
    }

    private string? GetEmojiDescriptiveName(string name) =>
        name switch
        {
            "pepe_cry" => "pepe-cry",
            "quinkerella" => "cat",
            "avery" => "dog",
            "sidus2" => "groundhog",
            "coins" => "coins",
            "banking" => "treasure",
            "disgusted" => "disgusted",
            "zonk" => "fail",
            "gustaff" => "silly-face",
            "evil" => "evil-grin",
            "salt" => "salt",
            "louisdefunes_lol" => "lol",
            "louisdefunes_shocked" => "shocked",
            "troll" => "troll",
            "homerdrool" => "tasty",
            "facepalmpicard" => "disappointed",
            "homer" => "yay",
            "nsfw" => "nsfw",
            "wernstrom" => "wernstrom",
            "hypnotoad" => "hypnotoad",
            "zoidberg" => "zoidberg",
            "farnsworth" => "farnsworth",
            "angry_sun" => "angry-sun",
            _ => null // no description available
        };

    private static readonly ImmutableHashSet<string> CoffeeKeywords = ImmutableHashSet.Create(
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
        "spinotwachtldroha",
        "scheipi",
        "heisl",
        "gschissana",
        "christkindl");

    private async Task HandleReactionsAsync(SocketMessage arg, JoinedTextChannel<ulong> cachedChannel)
    {
        if (CoffeeKeywords.Contains(arg.Content.Trim()))
        {
            var breakfasts = new string[]
            {
                "☕",
                "🍵",
                "🍦",
                "🥐",
                "🥯",
                "🍳",
                "🥃",
                "🥖",
                "🧀",
                "🍯",
                "🥛",
                "🍕",
                "🍌",
                "🍎",
                "🍊",
                "🍋",
                "🍓",
                "🥑",
                "🥦",
                "🥕",
                "🍔",
                "🍟",
                "🌭",
                "🥨",
                "🥗",
                "🍰",
                "🧁",
                "🍩",
                "🍪",
                "🍫",
                "🍬",
                "🍭",
                "🍮",
            };
            var emoji = breakfasts[Random.Shared.Next(breakfasts.Length)];
            await arg.AddReactionAsync(new Emoji(emoji)).ConfigureAwait(false);
            return;
        }

        var shouldReact = EmojiProbabilityRamp.Check();
        if (!shouldReact)
            return;

        var liveHistory = await arg.Channel.GetMessagesAsync(arg, Direction.Before, 3, CacheMode.AllowDownload).FlattenAsync().ConfigureAwait(false);

        string systemPrompt = REACTION_INSTRUCTION(EmojiJsonList.Value);
        var composer = ReactionTemplate.Fork()
            .SetSystemPrompt(systemPrompt);

        foreach (var message in liveHistory)
        {
            await AddMessageToComposer(composer, message, cachedChannel).ConfigureAwait(false);
        }
        await AddMessageToComposer(composer, arg, cachedChannel).ConfigureAwait(false);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await AiClient.SendAsync(composer).ConfigureAwait(false);
            stopwatch.Stop();

            LogAiResult("Reaction", result, stopwatch.Elapsed);

            var reaction = result.TextContent;
            if (string.IsNullOrEmpty(reaction))
            {
                Logger.LogWarning("AI did not return a reaction for the message: {Message}", arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
                return;
            }
            if (Emotes.Value.TryGetValue(reaction, out var guildEmote))
            {
                await arg.AddReactionAsync(guildEmote).ConfigureAwait(false);
                return;
            }

            // Try to add as unicode emoji
            if (!Emoji.TryParse(reaction, out var emojiReaction))
            {
                Logger.LogWarning("Could not parse emoji from reaction: {Reaction}", reaction);
                return;
            }

            await arg.AddReactionAsync(emojiReaction).ConfigureAwait(false);
        }
        catch (AnthropicApiException ex)
        {
            Logger.LogError(ex, "Anthropic API error ({ErrorType}) during reaction. Message: {Message}",
                ex.ErrorType, arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while adding a reaction to the message: {Message}", arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
        }
    }
}
