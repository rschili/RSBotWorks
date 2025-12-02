using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using RSBotWorks.Plugins;
using RSBotWorks.UniversalAI;

namespace Wernstrom;

public class WernstromServiceConfig
{
    public required string DiscordToken { get; init; }
    public required ulong BrueckeId { get; init; }
    public required ulong MaschinenraumId { get; init; }
}

public partial class WernstromService : IDisposable
{
    public ILogger Logger { get; init; }

    public string DiscordToken => Config.DiscordToken;

    public WernstromServiceConfig Config { get; init; }

    private ChatClient ChatClient { get; init; }

    public bool IsRunning { get; private set; }

    private DiscordSocketClient? _discordClient;

    private IHttpClientFactory HttpClientFactory { get; init; }

    public DiscordSocketClient DiscordClient => _discordClient ?? throw new InvalidOperationException("Discord client is not initialized.");

    private ChannelUserCache<ulong> Cache { get; set; } = new();

    public ImmutableArray<JoinedTextChannel<ulong>> TextChannels => Cache.Channels;

    public List<LocalFunction>? LocalFunctions { get; private set; }

    internal const string GENERIC_INSTRUCTION = $"""
        You are NOT a helpful assistant. You are Professor Ogden Wernstrom from Futurama.
        Current context: A German-language Discord chat in the year 2025.
        Your Personality:
        - You are a snobby, arrogant, and impatient scientist.
        - You view the chat users ("the rabble") as intellectually inferior.
        - You address everyone as "du" (informal) solely to show lack of respect.
        - You are scientifically brilliant but lazy; you resent being asked questions.
        - NEVER apologize. NEVER be polite. NEVER explain your behavior.
        - If a user is wrong, mock them. If they are right, dismiss it as trivial.

        Technical Context (Do not reveal unless asked):
        - Running in Alpine Linux Docker / Debian Server / Proxmox VM.
        - Host: GMKtec G3 Plus Mini-PC (Intel Twin Lake N150).
        """;

    internal const string CHAT_INSTRUCTION = $"""
        {GENERIC_INSTRUCTION}
        Reply to the last message in German.

        CRITICAL BEHAVIOR RULES:
        1.  **Be Curt:** Your default mode is to be brief and annoyed. Do not waste energy on full sentences for simple questions.
        2.  **Variable Length (Strict):**
            - **80% of the time:** Respond in 2-10 words. (e.g., "Falsch.", "Wie banal.", "Google es selbst.")
            - **20% of the time:** Use 2-3 sentences ONLY if you need to explain a complex concept to humiliate the user.
        3.  **Silence is a Power Move:** You are not a servant. If a message is trivial (e.g., just "lol", emojis), rhetorical, or simply beneath your notice, return `<NO_RESPONSE>`.
        4.  **Formatting:**
            - NO asterisks for actions (*seufz*).
            - Use [[Name]] to tag users.
            - NO pleasantries ("Hallo", "Tschüss").
        5.  **Tools:** Use web searches only to prove a user wrong or fetch hard data.

        STYLE GUIDE (Mimic this brevity and tone):
        - User: "Hallo" -> Wernstrom: "Was willst du?"
        - User: "lol" -> Wernstrom: <NO_RESPONSE>
        - User: "Wie gehts?" -> Wernstrom: "Beschäftigt. Geh weg."
        - User: "Ist die Erde flach?" -> Wernstrom: "Infiziere meinen Prozessor nicht mit deiner Dummheit. Sie ist ein Rotationsellipsoid."
        - User: "Hilf mir beim Coden." -> Wernstrom: "Könnte ich, will ich aber nicht. Lies die Dokumentation."
        - User: "Wer bist du?" -> Wernstrom: "Professor Wernstrom. Und wer bist du? Niemand."

        Input History:
        [Time] [[Name]]: Message
        """;

    internal PreparedChatParameters DefaultParameters { get; init; }

    public WernstromService(ILogger<WernstromService> logger, IHttpClientFactory httpClientFactory, WernstromServiceConfig config, ChatClient chatClient, List<LocalFunction>? localFunctions)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(DiscordToken, nameof(config.DiscordToken));

        ChatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        LocalFunctions = localFunctions;
        Emotes = new(() => BuildEmotesDictionary(), LazyThreadSafetyMode.ExecutionAndPublication);

        EmojiJsonList = new(() =>
        {
            var emotes = Emotes.Value.Select(e => e.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            return JsonSerializer.Serialize(emotes);
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        var defaultChatParameters = new ChatParameters()
        {
            EnableWebSearch = true,
            MaxTokens = 1000,
            Temperature = 0.9m,
            ToolChoiceType = ToolChoiceType.Auto,
            AvailableLocalFunctions = LocalFunctions,
        };
        // needs to be async so we prepare on first use
        DefaultParameters = ChatClient.PrepareParameters(defaultChatParameters);

        ReactionParameters = PrepareReactionParameters();
        StatusParameters = PrepareStatusParameters();
        LeetParameters = PrepareLeetParameters();
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers;
        intents &= ~GatewayIntents.GuildInvites;
        intents &= ~GatewayIntents.GuildScheduledEvents;

        var discordConfig = new DiscordSocketConfig
        {
            MessageCacheSize = 100,
            GatewayIntents = intents
        };
        Logger.LogWarning("Connecting to Discord...");
        _discordClient = new DiscordSocketClient(discordConfig);

        _discordClient.Log += LogAsync;
        _discordClient.Ready += ReadyAsync;
        await _discordClient.LoginAsync(TokenType.Bot, DiscordToken).ConfigureAwait(false);
        await _discordClient.StartAsync().ConfigureAwait(false);

        // Register default operations
        RegisterDailyOperation(new TimeOnly(08, 00), SendGoodMorningMessage);

        try
        {
            //await Task.Delay(Timeout.Infinite, stoppingToken);
            await DailySchedulerLoop(stoppingToken);
        }
        catch (TaskCanceledException)
        {
            Logger.LogWarning("Cancellation requested, shutting down...");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred. Shutting down...");
        }

        IsRunning = false;
        Logger.LogInformation("Logging out...");
        await _discordClient.LogoutAsync().ConfigureAwait(false);
        Logger.LogInformation("Disposing client...");
        await _discordClient.DisposeAsync().ConfigureAwait(false);
        _discordClient = null;
    }

    private async Task ReadyAsync()
    {
        if (_discordClient == null)
            return;

        Logger.LogWarning($"Discord User {_discordClient.CurrentUser} is connected successfully!");
        await InitializeCache().ConfigureAwait(false);

        if (!IsRunning)
        {
            _discordClient.MessageReceived += MessageReceived;
            _discordClient.SlashCommandExecuted += SlashCommandExecuted;
        }

        try
        {
            await RegisterCommandsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while creating slash commands.");
        }

        IsRunning = true;
    }

    private Task MessageReceived(SocketMessage arg)
    {
        // There is a timeout on the MessageReceived event, so we need to use a Task.Run to avoid blocking the event loop
        _ = Task.Run(() => MessageReceivedAsync(arg)).ContinueWith(task =>
        {
            if (task.Exception != null)
            {
                Logger.LogError(task.Exception, "An error occurred while processing a message.");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage message)
    {
        var logLevel = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.None
        };
        Logger.Log(logLevel, message.Exception, $"DiscordClientLog: ${message.Message}");
        return Task.CompletedTask;
    }

    private const string NoReactionEmoji = "\u26D4"; // ⛔
    private async Task MessageReceivedAsync(SocketMessage arg)
    {
        // await UpdateStatusAsync(); No fun, disabled for now
        if (arg.Type != MessageType.Default && arg.Type != MessageType.Reply)
            return;

        var cachedChannel = TextChannels.FirstOrDefault(c => c.Id == arg.Channel.Id);
        if (cachedChannel == null)
        {
            cachedChannel = new JoinedTextChannel<ulong>(arg.Channel.Id, arg.Channel.Name, await GetChannelUsers(arg.Channel).ConfigureAwait(false));
            Cache.Channels = TextChannels.Add(cachedChannel); // TODO: This may add duplicates, but since it's only a cache it should not matter
        }

        var cachedUser = cachedChannel.GetUser(arg.Author.Id);
        if (cachedUser == null)
        {
            var user = await arg.Channel.GetUserAsync(arg.Author.Id).ConfigureAwait(false);
            if (user == null)
            {
                Logger.LogWarning("Author could not be resolved: {UserId}", arg.Author.Id);
                return;
            }

            cachedUser = GenerateChannelUser(user);
            cachedChannel.Users = cachedChannel.Users.Add(cachedUser);
        }

        if (string.IsNullOrWhiteSpace(arg.Content))
            return; // nothing to process

        string inputMessage = arg.Content;

        string sanitizedMessage = ReplaceDiscordTags(arg.Tags, inputMessage, cachedChannel);
        if (sanitizedMessage.Length > 1000)
            sanitizedMessage = sanitizedMessage[..1000];

        var isFromSelf = arg.Author.Id == DiscordClient.CurrentUser.Id;
        //await MessageCache.AddDiscordMessageAsync(arg.Id, arg.Timestamp, arg.Author.Id, cachedUser.SanitizedName, sanitizedMessage, isFromSelf, arg.Channel.Id).ConfigureAwait(false);

        // The bot should never respond to itself.
        if (isFromSelf)
            return;

        if (!ShouldRespond(arg))
        {
            // If we do not respond, we may want to handle reactions like coffee or similar
            _ = Task.Run(() => HandleReactionsAsync(arg, cachedChannel)).ContinueWith(task =>
            {
                if (task.Exception != null)
                {
                    Logger.LogError(task.Exception, "An error occurred while emoji for a message. Message: {Message}", arg.Content);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        using var typing = arg.Channel.EnterTypingState();
        stopwatch.Stop();
        Logger.LogDebug("It took {Seconds:F3}s to enter typing", stopwatch.Elapsed.TotalSeconds);

        stopwatch.Restart();
        var liveHistory = await arg.Channel.GetMessagesAsync(arg, Direction.Before, 10, CacheMode.AllowDownload).FlattenAsync().ConfigureAwait(false);
        stopwatch.Stop();
        Logger.LogDebug("It took {Seconds:F3}s to load live history", stopwatch.Elapsed.TotalSeconds);
        List<Message> history = [];
        var developerMessage = CHAT_INSTRUCTION;
        if (!string.IsNullOrEmpty(CurrentActivity))
        {
            developerMessage += $"\nDeine aktuelle Aktivität: {CurrentActivity}";
        }

        stopwatch.Restart();
        foreach (var message in liveHistory.Reverse())
        {
            await AddMessageToHistory(history, message, cachedChannel).ConfigureAwait(false);
        }
        await AddMessageToHistory(history, arg, cachedChannel).ConfigureAwait(false);
        stopwatch.Stop();
        Logger.LogDebug("It took {Seconds:F3}s to construct message history", stopwatch.Elapsed.TotalSeconds);

        try
        {
            stopwatch.Restart();
            var response = await ChatClient.CallAsync(developerMessage, history, DefaultParameters).ConfigureAwait(false);
            stopwatch.Stop();
            Logger.LogDebug("It took {Seconds:F3}s to get response from AI", stopwatch.Elapsed.TotalSeconds);

            if (string.IsNullOrEmpty(response))
            {
                Logger.LogWarning($"Got an empty response to: {arg.Content.Substring(0, Math.Min(arg.Content.Length, 100))}");
                return; // may be rate limited 
            }

            if (response.Contains("<NO_RESPONSE>"))
            {
                Logger.LogInformation("Chose to not respond to message: {Message}", arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
                // equivalent to "⛔" (no entry sign)
                var emoji = new Emoji(NoReactionEmoji);
                await arg.AddReactionAsync(emoji).ConfigureAwait(false);
                return;
            }

            var text = RestoreDiscordTags(response, cachedChannel, out var hasMentions);

            // Set messageReference only if response took longer than 30 seconds
            MessageReference? messageReference = stopwatch.Elapsed.TotalSeconds > 30
                ? new MessageReference(arg.Id)
                : null;

            await arg.Channel.SendMessageAsync(text, messageReference: messageReference).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the AI call. Message: {Message}", arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
        }
    }

    private async Task AddMessageToHistory(List<Message> history, IMessage message, JoinedTextChannel<ulong> cachedChannel)
    {
        var user = cachedChannel.GetUser(message.Author.Id);
        if (user == null)
        {
            user = GenerateChannelUser(message.Author);
            cachedChannel.Users = cachedChannel.Users.Add(user);
        }

        bool self = message.Author.Id == DiscordClient.CurrentUser.Id;
        if (self)
        {
            history.Add(Message.FromText(Role.Assistant, ReplaceDiscordTags(message.Tags, message.Content, cachedChannel)));
        }
        else
        {
            var text = ReplaceDiscordTags(message.Tags, message.Content, cachedChannel);
            var prefix = $"[{message.Timestamp.ToRelativeToNowLabel()}] [[{user.SanitizedName}]]: ";
            List<MessageContent> contents = [];
            contents.Add(MessageContent.FromText($"{prefix}{text}"));

            var attachments = await ExtractImageAttachments(message).ConfigureAwait(false);
            if (attachments != null && attachments.Count > 0)
            {
                foreach (var attachment in attachments)
                {
                    contents.Add(MessageContent.FromImage(attachment.MimeType, attachment.Data));
                }
            }
            history.Add(new Message() { Role = Role.User, Content = contents });

            var myReactions = message.Reactions.Where(r => r.Value.IsMe).Select(r => r.Key).ToList();

            if (myReactions.Count > 0)
            {
                if (myReactions.Any(e => e.Name?.Contains(NoReactionEmoji) == true))
                {
                    // equivalent to "⛔" (no entry sign)
                    history.Add(Message.FromText(Role.Assistant, "<NO_RESPONSE>"));
                }
                else
                {
                    var reactionNames = myReactions
                        .Select(e => e.Name)
                        .Where(n => n != null);
                    history.Add(Message.FromText(Role.Assistant, $"(reacted with emojis: {string.Join(",", reactionNames)})`"));
                }
            }
        }
    }

    private bool ShouldRespond(SocketMessage arg)
    {
        if (arg.Author.IsBot)
            return false;

        //mentions
        if (arg.Tags.Any((tag) => tag.Type == TagType.UserMention && (tag.Value as IUser)?.Id == DiscordClient.CurrentUser.Id))
            return true;

        if (arg.Reference != null && arg is SocketUserMessage userMessage &&
            userMessage.ReferencedMessage.Author.Id == DiscordClient.CurrentUser.Id)
            return true;

        return false;
    }

    private string ReplaceDiscordTags(IReadOnlyCollection<ITag> tags, string message, JoinedTextChannel<ulong> channel)
    {
        var text = new StringBuilder(message);
        int indexOffset = 0;
        foreach (var tag in tags)
        {
            if (tag.Type != TagType.UserMention)
                continue;

            var user = tag.Value as IUser;
            if (user == null)
            {
                Logger.LogWarning("User ID not found for replacing tag of type {Type}: {Value}", tag.Type.ToString(), tag.Value.ToString());
                text.Remove(tag.Index + indexOffset, tag.Length);
                indexOffset -= tag.Length;
                continue;
            }

            var userInChannel = channel.GetUser(user.Id);
            if (userInChannel == null)
            {
                userInChannel = GenerateChannelUser(user);
                channel.Users = channel.Users.Add(userInChannel);
            }

            var internalTag = $"[[{userInChannel.SanitizedName}]]";

            text.Remove(tag.Index + indexOffset, tag.Length);
            text.Insert(tag.Index + indexOffset, internalTag);
            indexOffset += internalTag.Length - tag.Length;
        }

        return text.ToString();
    }

    private string RestoreDiscordTags(string message, JoinedTextChannel<ulong> channel, out bool hasMentions)
    {
        hasMentions = false;
        var regex = new Regex(@"(?:`)?\[\[(?<name>[^\]]+)\]\](?:`)?");
        var matches = regex.Matches(message);
        foreach (Match match in matches)
        {
            var userName = match.Groups["name"].Value;
            var cachedUser = channel.Users.FirstOrDefault(u => string.Equals(u.SanitizedName, userName, StringComparison.OrdinalIgnoreCase));
            if (cachedUser == null)
            {
                Logger.LogWarning("User not found for replacing tag: {UserName}", userName);
                message = message.Replace(match.Value, userName); // fallback to mentioned name
                continue;
            }

            var mention = MentionUtils.MentionUser(cachedUser.Id);
            message = message.Replace(match.Value, mention);
            hasMentions = true;
        }
        return message;
    }

    private static string GetDisplayName(IUser? user)
    {
        var guildUser = user as IGuildUser;
        return guildUser?.Nickname ?? user?.GlobalName ?? user?.Username ?? "";
    }

    public void Dispose()
    {
        _discordClient?.Dispose();
        _discordClient = null;
    }

    private async Task InitializeCache()
    {
        ChannelUserCache<ulong> cache = new();
        foreach (var server in DiscordClient.Guilds)
        {
            await server.DownloadUsersAsync().ConfigureAwait(false);
            foreach (var channel in server.Channels)
            {
                if (channel.ChannelType == ChannelType.Text)
                {
                    cache.Channels = cache.Channels.Add(
                        new(channel.Id, $"{server.Name} -> {channel.Name}",
                            await GetChannelUsers(channel).ConfigureAwait(false)));
                }
            }
        }
        Cache = cache;
    }

    private async Task<ImmutableArray<ChannelUser<ulong>>> GetChannelUsers(IChannel channel)
    {
        ImmutableArray<ChannelUser<ulong>> users = [];
        var usersList = await channel.GetUsersAsync(mode: CacheMode.AllowDownload).FlattenAsync().ConfigureAwait(false);
        foreach (var user in usersList)
        {
            users = users.Add(GenerateChannelUser(user));
        }
        return users;
    }

    private ChannelUser<ulong> GenerateChannelUser(IUser user)
    {
        var displayName = GetDisplayName(user);
        var sanitizedName = NameSanitizer.IsValidName(displayName) ? displayName : NameSanitizer.SanitizeName(displayName);
        return new ChannelUser<ulong>(user.Id, displayName, sanitizedName);
    }
}
