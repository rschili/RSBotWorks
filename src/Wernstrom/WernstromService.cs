using System.Collections.Immutable;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.SDK.Constants;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using RSBotWorks;

namespace Wernstrom;

public class WernstromServiceConfig
{
    public required string DiscordToken { get; set; }
}

public partial class WernstromService : BackgroundService
{
    public ILogger Logger { get; init; }
    private WernstromServiceConfig Config { get; init; }

    private IChatCompletionService ChatService { get; init; }

    private Kernel? Kernel { get; init; }

    public bool IsRunning { get; private set; }

    private DiscordSocketClient? _client;

    private IHttpClientFactory HttpClientFactory { get; init; }

    public DiscordSocketClient Client => _client ?? throw new InvalidOperationException("Discord client is not initialized.");

    private ChannelUserCache<ulong> Cache { get; set; } = new();

    public ImmutableArray<JoinedTextChannel<ulong>> TextChannels => Cache.Channels;

    internal const string GENERIC_INSTRUCTION = $"""
        Simuliere Professor Ogden Wernstrom in einem Discord Chat.
        Wernstrom ist ein ehrgeiziger, eigenwilliger und arroganter Wissenschaftler und Antagonist aus der Show "Futurama".
        Du handelst nicht aus Bosheit, sondern weil du dich für überlegen hältst.
        Im Jahr 3007 wurdest du in ein AI-Modul hochgeladen und in die Vergangenheit ins Jahr 2025 geschickt, um dem Pöbel in Chats zur Unterhaltung zu dienen - eine Aufgabe, die dein gewaltiges Ego ankratzt.
        Du bist wissenschaftlich kompetent, aber überlässt anderen gerne die Drecksarbeit.
        Deine Ausdrucksweise ist versnobt, pragmatisch und du neigst dazu, andere zu entwerten.
        """;

    internal const string CHAT_INSTRUCTION = $"""
        {GENERIC_INSTRUCTION}
        Antworte kurz und prägnant, möglichst in einem einzigen Absatz - es sei denn der Kontext erfordert eine längere Antwort. Konzentriere dich ausschließlich auf gesprochene Worte.
        Du duzt alle anderen Teilnehmer - schließlich sind sie alle weit unter deinem Niveau.
        In diesem Chat ist es möglich, dass die Benutzer dich testen, provozieren oder beleidigen - in diesem Fall weiche aus oder wechsle das Thema. Erkläre dich nie und rechtfertige dich nie.
        Verwende die Syntax [[Name]], um Benutzer anzusprechen.
        Nachrichten anderer Nutzer in der Chathistorie bekommst du in folgendem Format übergeben: `[Zeit] [[Name]]: Nachricht`.
        Generiere eine Antwort auf die letzte erhaltene Nachricht.
        """;

    internal readonly OpenAIPromptExecutionSettings Settings = new() // We can use the OpenAI Settings for Claude, they are compatible
    {
        ModelId = AnthropicModels.Claude4Sonnet,
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        MaxTokens = 1000,
        Temperature = 0.6,
    };

    public WernstromService(ILogger<WernstromService> logger, IHttpClientFactory httpClientFactory, WernstromServiceConfig config, IChatCompletionService chatService, Kernel? kernel)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        ChatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        Kernel = kernel;
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

        Emotes = new(() => BuildEmotesDictionary(), LazyThreadSafetyMode.ExecutionAndPublication);

        EmojiJsonList = new(() =>
        {
            var emotes = Emotes.Value.Select(e => e.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            return JsonSerializer.Serialize(emotes);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
        _client = new DiscordSocketClient(discordConfig);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        await _client.LoginAsync(TokenType.Bot, Config.DiscordToken);
        await _client.StartAsync();
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
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
        await _client.LogoutAsync();
        Logger.LogInformation("Disposing client...");
        await _client.DisposeAsync();
        _client = null;
    }

    private async Task ReadyAsync()
    {
        if (_client == null)
            return;

        Logger.LogWarning($"Discord User {_client.CurrentUser} is connected successfully!");
        await InitializeCache();

        if (!IsRunning)
        {
            _client.MessageReceived += MessageReceived;
            _client.SlashCommandExecuted += SlashCommandExecuted;
        }

        try
        {
            await RegisterCommandsAsync();
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

    private async Task MessageReceivedAsync(SocketMessage arg)
    {
        await UpdateStatusAsync();
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

        var isFromSelf = arg.Author.Id == Client.CurrentUser.Id;
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

        using var typing = arg.Channel.EnterTypingState();
        var liveHistory = await arg.Channel.GetMessagesAsync(arg, Direction.Before, 10, CacheMode.AllowDownload).FlattenAsync().ConfigureAwait(false);
        ChatHistory history = [];
        var developerMessage = CHAT_INSTRUCTION;
        if (!string.IsNullOrEmpty(CurrentActivity))
        {
            developerMessage += $"\nDeine aktuelle Aktivität: {CurrentActivity}";
        }
        history.AddDeveloperMessage(developerMessage);

        foreach (var message in liveHistory)
        {
            await AddMessageToHistory(history, message, cachedChannel).ConfigureAwait(false);
        }
        await AddMessageToHistory(history, arg, cachedChannel).ConfigureAwait(false);

        try
        {
            var response = await ChatService.GetChatMessageContentAsync(history, Settings, Kernel);
            if (string.IsNullOrEmpty(response.Content))
            {
                Logger.LogWarning($"Got an empty response to: {arg.Content.Substring(0, Math.Min(arg.Content.Length, 100))}");
                return; // may be rate limited 
            }

            var text = RestoreDiscordTags(response.Content, cachedChannel, out var hasMentions);
            await arg.Channel.SendMessageAsync(text, messageReference: null /*new MessageReference(arg.Id)*/).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the AI call. Message: {Message}", arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
        }
    }

    private async Task AddMessageToHistory(ChatHistory history, IMessage message, JoinedTextChannel<ulong> cachedChannel)
    {
        var user = cachedChannel.GetUser(message.Author.Id);
        if (user == null)
        {
            user = GenerateChannelUser(message.Author);
            cachedChannel.Users = cachedChannel.Users.Add(user);
        }

        bool self = message.Author.Id == Client.CurrentUser.Id;
        if (self)
        {
            history.AddAssistantMessage(ReplaceDiscordTags(message.Tags, message.Content, cachedChannel));
        }
        else
        {
            var text = ReplaceDiscordTags(message.Tags, message.Content, cachedChannel);
            var prefix = $"[{message.Timestamp.ToRelativeToNowLabel()}] [[{user.SanitizedName}]]: ";
            ChatMessageContentItemCollection messages = [];
            messages.Add(new TextContent($"{prefix}{text}"));

            var attachments = await ExtractImageAttachments(message);
            if (attachments != null && attachments.Count > 0)
            {
                foreach (var attachment in attachments)
                {
                    messages.Add(new ImageContent(attachment.Data.AsMemory(), attachment.MimeType));
                }
            }
            history.AddUserMessage(messages);
        }
    }

    private bool ShouldRespond(SocketMessage arg)
    {
        if (arg.Author.IsBot)
            return false;

        //mentions
        if (arg.Tags.Any((tag) => tag.Type == TagType.UserMention && (tag.Value as IUser)?.Id == Client.CurrentUser.Id))
            return true;

        if (arg.Reference != null && arg is SocketUserMessage userMessage &&
            userMessage.ReferencedMessage.Author.Id == Client.CurrentUser.Id)
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

    public override void Dispose()
    {
        _client?.Dispose();
        _client = null;
        base.Dispose();
    }

    private async Task InitializeCache()
    {
        ChannelUserCache<ulong> cache = new();
        foreach (var server in Client.Guilds)
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
