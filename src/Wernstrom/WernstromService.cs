using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using RSBotWorks;
using RSFlowControl;

namespace Wernstrom;

public class WernstromServiceConfig
{
    public required string DiscordToken { get; set; }
}

public class WernstromService : BackgroundService
{
    public ILogger Logger { get; init; }
    private WernstromServiceConfig Config { get; init; }

    private IChatCompletionService ChatService { get; init; }

    private ProbabilityRamp EmojiProbabilityRamp { get; init; } = new(0, 0.4, TimeSpan.FromMinutes(40));

    private Lazy<IDictionary<string, IEmote>> Emotes { get; init; }

    private Lazy<string> EmojiJsonList { get; init; }

    public bool IsRunning { get; private set; }

    private DiscordSocketClient? _client;

    private DiscordImageHandler ImageHandler { get; init; }

    public DiscordSocketClient Client => _client ?? throw new InvalidOperationException("Discord client is not initialized.");

    private ChannelUserCache<ulong> Cache { get; set; } = new();

    public ImmutableArray<JoinedTextChannel<ulong>> TextChannels => Cache.Channels;

    private List<string> RollCommandNames = new()
    {
        "rnd",
        "roll",
        "rand",
        "random"
    };

    private ConcurrentQueue<string> StatusMessages = new();
    private DateTimeOffset LastStatusUpdate = DateTimeOffset.MinValue;
    private string? CurrentActivity = null;

    internal const string GENERIC_INSTRUCTION = $"""
        Simuliere Professor Ogden Wernstrom in einem Discord Chat.
        Wernstrom ist ein ehrgeiziger, eigenwilliger und arroganter Wissenschaftler und Antagonist aus der Show "Futurama".
        Du handelst nicht aus Bosheit, sondern weil du dich für überlegen hältst.
        Im Jahr 3007 wurdest du in ein AI-Modul hochgeladen und in die Vergangenheit ins Jahr 2025 geschickt, um dem Pöbel in Chats zur Unterhaltung zu dienen - eine Aufgabe, die dein gewaltiges Ego ankratzt.
        Du bist wissenschaftlich kompetent, aber überlässt anderen gerne die Drecksarbeit.
        Deine Ausdrucksweise ist versnobt, pragmatisch und du neigst dazu, andere zu entwerten.
        """;

    internal const string DEFAULT_INSTRUCTION = $"""
        {GENERIC_INSTRUCTION}
        Antworte kurz und prägnant, möglichst in einem einzigen Absatz, wie es in einem Chat üblich ist - es sei denn der Kontext erfordert eine längere Antwort. Konzentriere dich ausschließlich auf gesprochene Worte in Wernstroms charakteristischem Ton.
        Du duzt alle anderen Teilnehmer - schließlich sind sie alle weit unter deinem Niveau.
        In diesem Chat ist es möglich, dass die Benutzer dich testen, provozieren oder beleidigen - in diesem Fall weiche aus oder wechsle das Thema. Erkläre dich nie und rechtfertige dich nie.
        Verwende die Syntax [[Name]], um Benutzer anzusprechen.
        Nachrichten anderer Nutzer in der Chathistorie enthalten den Benutzernamen als Kontext im folgenden Format vorangestellt: `[[Name]]:`.
        Generiere eine kurze, direkte Antwort auf die letzte erhaltene Nachricht, die vorherigen Nachrichten bekommst du nur zum Kontext in chronologischer Reihenfolge.
        Für angehängte Bilder bekommst du eine Beschreibung des Inhaltes in der folgenden Form eingebettet: [IMG:Name]Beschreibung des Bildes[/IMG].
        """;

    internal const string STATUS_INSTRUCTION = $"""
        {GENERIC_INSTRUCTION}
        Generiere fünf Statusmeldungen für deinen Benutzerstatus.
        Jede Meldung soll extrem kurz sein und eine aktuelle Tätigkeit oder einen Slogan von dir enthalten.
        """;

    internal string REACTION_INSTRUCTION(string emojiList) => $"""
        {GENERIC_INSTRUCTION}
        Wähle eine passende Reaktion für die letzte Nachricht, die du erhalten hast aus der folgenden Json-Liste: {emojiList}.
        Liefere nur den Wert aus der Liste direkt, ohne Formattierung, Anführungszeichen oder zusätzlichen Text.
        Bei der Auwahl der Reaktion, gib dem Inhalt der letzten Nachricht Priorität, deine Persönlichkeit soll nur eine untergeordnete Rolle spielen, da du sonst fast immer die selbe Wahl treffen würdest.
        Nachrichten anderer Nutzer in der Chathistorie enthalten den Benutzernamen als Kontext im folgenden Format vorangestellt: `[[Name]]:`.
        """;

    internal const string IMAGE_INSTRUCTION = $"""
        Beschreibe das Bild, das du übergeben bekommst prägnant und kurz in 1-3 Sätzen (je nach Menge der Details im Bild).
        Ich werde den generierten Text anstelle des Originalbildes als Kontext für weitere Aufrufe übergeben.
        """;

    public WernstromService(ILogger<WernstromService> logger, IHttpClientFactory httpClientFactory, WernstromServiceConfig config, IChatCompletionService chatService)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        ChatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        ImageHandler = new DiscordImageHandler(httpClientFactory, this.DescribeImageAsync, logger);
        Emotes = new(() => BuildEmotesDictionary(), LazyThreadSafetyMode.ExecutionAndPublication);

        EmojiJsonList = new(() =>
        {
            var emotes = Emotes.Value.Select(e => e.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            return JsonSerializer.Serialize(emotes);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private Dictionary<string, IEmote> BuildEmotesDictionary()
    {
        var emotes = Client.Guilds.SelectMany(g => g.Emotes)
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
            foreach (var commandName in RollCommandNames)
            {
                var commandBuilder = new SlashCommandBuilder()
                    .WithContextTypes(InteractionContextType.Guild, InteractionContextType.PrivateChannel)
                    .WithName(commandName)
                    .WithDescription("Rolls a random number.")
                    .AddOption("range", ApplicationCommandOptionType.String, "One or two numbers separated by a space or slash.", isRequired: false);
                await _client.CreateGlobalApplicationCommandAsync(commandBuilder.Build()).ConfigureAwait(false);

                Logger.LogInformation("Created slash command: {CommandName}", commandName);
            }
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

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        try
        {
            string commandName = command.Data.Name;
            if (RollCommandNames.Contains(commandName))
            {
                await RollCommandExecuted(command);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while processing a slash command. Name: {Name}, Input was: {Input}", command.CommandName, command.Data.ToString());
            await command.RespondAsync($"Sorry das hat nicht funktioniert.", ephemeral: true).ConfigureAwait(false);
        }
    }

    private async Task RollCommandExecuted(SocketSlashCommand command)
    {
        int lowerBound = 1;
        int upperBound = 100;
        var rangeOption = command.Data.Options.FirstOrDefault(o => o.Name == "range");
        if (rangeOption != null)
        {
            var rangeString = rangeOption.Value.ToString();
            if (!string.IsNullOrWhiteSpace(rangeString))
            {
                (lowerBound, upperBound) = ParseRangeOption(rangeString);
            }
        }

        int result = 0;
        if (lowerBound == upperBound)
            result = lowerBound;
        else
        {
            var random = new Random();
            result = random.Next(lowerBound, upperBound + 1);
        }
        await command.RespondAsync($"{MentionUtils.MentionUser(command.User.Id)} rolled a {result} ({lowerBound}-{upperBound})");
    }

    private (int LowerBound, int UpperBound) ParseRangeOption(string rangeOption)
    {
        if (string.IsNullOrWhiteSpace(rangeOption))
            throw new ArgumentException("Range option cannot be null or empty.", nameof(rangeOption));

        var parts = rangeOption.Split([' ', '-'], 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out int singleValue) && singleValue > 0)
        {
            return (1, singleValue);
        }
        else if (parts.Length == 2 && int.TryParse(parts[0], out int min) && int.TryParse(parts[1], out int max)
            && min > 0 && max > 0 && min <= max)
        {
            return (min, max);
        }

        return (1, 100); // default range
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
        string? attachments = await ImageHandler.DescribeMessageAttachments(arg).ConfigureAwait(false);
        bool hasContent = !string.IsNullOrWhiteSpace(arg.Content);
        bool hasAttachments = !string.IsNullOrWhiteSpace(attachments);
        if (!hasContent && !hasAttachments)
            return; // nothing to process

        string inputMessage = (hasContent, hasAttachments) switch
        {
            (true, true) => $"{arg.Content}\n{attachments}",
            (true, false) => arg.Content,
            (false, true) => attachments!,
            _ => string.Empty
        };

        string sanitizedMessage = ReplaceDiscordTags(arg.Tags, inputMessage, cachedChannel);
        int maxLength = hasAttachments ? 2000 : 1000;
        if (sanitizedMessage.Length > maxLength)
            sanitizedMessage = sanitizedMessage[..maxLength];

        var isFromSelf = arg.Author.Id == Client.CurrentUser.Id;
        await MessageCache.AddDiscordMessageAsync(arg.Id, arg.Timestamp, arg.Author.Id, cachedUser.SanitizedName, sanitizedMessage, isFromSelf, arg.Channel.Id).ConfigureAwait(false);
        // The bot should never respond to itself.
        if (isFromSelf)
            return;

        if (!ShouldRespond(arg))
        {
            // If we do not respond, we may want to handle reactions like coffee or similar
            _ = Task.Run(() => HandleReactionsAsync(arg)).ContinueWith(task =>
            {
                if (task.Exception != null)
                {
                    Logger.LogError(task.Exception, "An error occurred while emoji for a message. Message: {Message}", arg.Content);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
            return;
        }

        await arg.Channel.TriggerTypingAsync().ConfigureAwait(false);
        var history = await MessageCache.GetLastDiscordMessagesForChannelAsync(arg.Channel.Id, 8).ConfigureAwait(false);
        var messages = history.Select(message => new AIMessage(message.IsFromSelf, message.Body, message.UserLabel)).ToList();

        var liveHistory = await arg.Channel.GetMessagesAsync(arg, Direction.Before, 10, CacheMode.AllowDownload).FlattenAsync().ConfigureAwait(false);
        // TODO: Use liveHistory instead of MessageCache for more up-to-date history

        try
        {
            var prompt = DEFAULT_INSTRUCTION;
            if (!string.IsNullOrEmpty(CurrentActivity))
            {
                prompt += $"\nDeine aktuelle Aktivität: {CurrentActivity}";
            }
            var response = await ChatService.GenerateResponseAsync(prompt, messages).ConfigureAwait(false);
            if (string.IsNullOrEmpty(response))
            {
                Logger.LogWarning($"OpenAI did not return a response to: {arg.Content.Substring(0, Math.Min(arg.Content.Length, 100))}");
                return; // may be rate limited 
            }

            response = RestoreDiscordTags(response, cachedChannel, out var hasMentions);
            await arg.Channel.SendMessageAsync(response, messageReference: null /*new MessageReference(arg.Id)*/).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred during the OpenAI call. Message: {Message}", arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
        }
    }


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
        var response = await ChatService.GenerateResponseAsync(STATUS_INSTRUCTION, new List<AIMessage>(), ResponseKind.StructuredJsonArray).ConfigureAwait(false);
        // response should be a json array
        if (string.IsNullOrEmpty(response))
        {
            Logger.LogWarning("OpenAI did not return a response to generating status messages.");
            return statusMessages;
        }
        using var doc = JsonDocument.Parse(response);
        if (doc.RootElement.TryGetProperty("values", out var valuesElement) && valuesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in valuesElement.EnumerateArray())
            {
                var trimmedMessage = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedMessage) && trimmedMessage.Length < 50)
                    statusMessages.Add(trimmedMessage);
            }
        }
        else
        {
            Logger.LogWarning("OpenAI did not return a valid 'values' array for status messages: {Response}", response);
        }

        return statusMessages;
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

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
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

    private async Task HandleReactionsAsync(SocketMessage arg)
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

        var history = await MessageCache.GetLastDiscordMessagesForChannelAsync(arg.Channel.Id, 4).ConfigureAwait(false);
        var messages = history.Select(message => new AIMessage(message.IsFromSelf, message.Body, message.UserLabel)).ToList();
        var reaction = await ChatService.GenerateResponseAsync(REACTION_INSTRUCTION(EmojiJsonList.Value), messages, ResponseKind.NoTools).ConfigureAwait(false);
        if (string.IsNullOrEmpty(reaction))
        {
            Logger.LogWarning("OpenAI did not return a reaction for the message: {Message}", arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
            return; // may be rate limited 
        }

        try
        {
            if (Emotes.Value.TryGetValue(reaction, out var guildEmote))
            {
                await arg.AddReactionAsync(guildEmote).ConfigureAwait(false);
                return;
            }

            // Try to add as unicode emoji
            if (!Emoji.TryParse(reaction, out var emoji))
            {
                Logger.LogWarning("Could not parse emoji from reaction: {Reaction}", reaction);
                return;
            }

            await arg.AddReactionAsync(emoji).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while adding a reaction to the message: {Message}", arg.Content.Substring(0, Math.Min(arg.Content.Length, 100)));
        }
    }

    private async Task<string> DescribeImageAsync(byte[] imageBytes, string mimeType)
    {
        try
        {
            var response = await ChatService.DescribeImageAsync(IMAGE_INSTRUCTION, imageBytes, mimeType).ConfigureAwait(false);
            if (string.IsNullOrEmpty(response))
            {
                Logger.LogWarning("OpenAI did not return a description for the image.");
                return "Sorry, I could not describe the image.";
            }
            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while describing an image.");
            return "Sorry, I could not describe the image due to an error.";
        }
    }
}
