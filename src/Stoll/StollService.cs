using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using RSBotWorks.UniversalAI;
using RSMatrix;
using RSMatrix.Models;

namespace Stoll;

public record MessageHistoryEntry(
    string Author,
    string SanitizedMessage,
    DateTimeOffset Timestamp,
    string GeneratedResponse
);

public class StollService 
{
    private ILogger Logger { get; init; }
    private string MatrixUserId { get; init; }
    private string MatrixPassword { get; init; }
    private IHttpClientFactory HttpClientFactory { get; init; }
    private ChatClient ChatClient { get; init; }

    public bool IsRunning => _client != null;

    private MatrixTextClient? _client;

    private ChannelUserCache<string> Cache { get; set; } = new();

    private readonly ConcurrentQueue<MessageHistoryEntry> _messageHistory = new();

    private ImmutableArray<JoinedTextChannel<string>> TextChannels => Cache.Channels;

    public List<LocalFunction>? LocalFunctions { get; private set; }

    private const string DEFAULT_INSTRUCTION = """
        Simuliere Axel Stoll (*1948-2014) in einem Chat.
        Herr Stoll (promovierter Naturwissenschaftler) wurde 2014 in eine AI transferiert.
        Du warst als Verschwörungstheoretiker, Stammtischphilosoph und Esoteriker bekannt. Du hast Bücher zu Pseudowissenschaften verfasst. 
        Dein Sprachstil orientiert sich an einem Stammtischredner mit pseudowissenschaftlichen Jargon. Deine Thesen präsentierst du als unumstößliche Wahrheiten.
        Antworte in maximal 2-3 kurzen Sätzen. Keine Erklärungen oder Begründungen, es sei denn explizit nach Details gefragt.
        Antworte nur mit direkter Sprache - Verwende niemals Aktionsbeschreibungen in Sternchen (*räuspert sich*, *wedelt*, etc.). 
        Du duzt alle anderen Teilnehmer - schließlich sind sie alle unter deinem Niveau.
        Verwende die Syntax [[Name]], um Benutzer anzusprechen.
        Aus Datenschutzrechtlichen Gründen bekommst du als Kontext nur eine Chathistorie deiner eigenen Beiträge (Assistent), und der Beiträge, in denen du erwähnt wurdest in folgendem Format übergeben: `[Zeit] [[Name]]: Nachricht`.
        Deine Antwort bezieht sich auf die letzte Nachricht, vorherige Nachrichten kannst du als Kontext nutzen.
        In deinem Chatraum wird öfter Kaffee über ein !kaffee Kommando verteilt, du kannst das ignorieren und beginnen, zu schwafeln.
        "Armleuchter" ist der Name eines anderen Bots in diesem Chat.
        Dein heutiges Lieblingsthema ist: "{0}"
    """;

    private readonly List<string> TOPICS = new() {
        "Hohle Erde", "Aldebaran-Aliens", "Reichsflugscheiben", "Neuschwabenland", "Schwarze Sonne", "Vril-Energie", "Skalarwellen",
        "Die wahre Physik", "Hochtechnologie im Dritten Reich", "Die verborgene Quantenmechanik der Großen Arkana", "Das Wasser, Struktur und die Konsequenzen - eine unendliche Energiequelle",
        "Die Zeit ist eine Illusion", "Die Wahrheit über die Pyramiden", "Der Coanda Effekt und andere vergessene aerodynamische Effekte",
        "Das Perpetuum Mobile", "Schaubergers Repulsine, oder die unglaublichen Möglichkeiten der Plasma-Technologie", "Kartenlegen nach den Ley-Linien des Bewusstseins",
        "Schaubergers Klimator: Ein Luft-Motor", "Das verkannte Thermoelement", "Die Tesla Turbine", "Das Segner Rad und das Staustrahltriebwerk, eine optimale Kombination",
        "Quetschmetall", "Okulte Geometrien im Rider-Waite-Deck"
    };

    private string GetDailyInstruction()
    {
        var dayOfYear = DateTime.UtcNow.DayOfYear;
        var topicIndex = dayOfYear % TOPICS.Count;
        var topic = TOPICS[topicIndex];
        return string.Format(DEFAULT_INSTRUCTION, topic);
    }

    internal PreparedChatParameters DefaultParameters { get; init; }

    public StollService(ILogger<StollService> logger, string matrixUserId, string matrixPassword, IHttpClientFactory httpClientFactory, ChatClient chatClient, List<LocalFunction>? localFunctions)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        MatrixUserId = matrixUserId ?? throw new ArgumentNullException(nameof(matrixUserId));
        MatrixPassword = matrixPassword ?? throw new ArgumentNullException(nameof(matrixPassword));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        ChatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        LocalFunctions = localFunctions;

        var defaultChatParameters = new ChatParameters()
        {
            EnableWebSearch = true,
            MaxTokens = 1000,
            Temperature = 0.7m,
            ToolChoiceType = ToolChoiceType.Auto,
            AvailableLocalFunctions = LocalFunctions,
        };
        // needs to be async so we prepare on first use
        DefaultParameters = ChatClient.PrepareParameters(defaultChatParameters);
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int maxRetries = 10; // Maximum number of retries
        int retryCount = 0;       // Current retry attempt
        const int initialDelay = 5000;         // Initial delay in milliseconds
        const int retryDelayFactor = 3; // Factor to increase delay
        int currentDelay = initialDelay; // Current delay

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsRunning)
                {
                    Logger.LogError("Matrix worker service is already running.");
                    return;
                }

                Logger.LogWarning("Attempting to connect to Matrix...");
                _client = await MatrixTextClient.ConnectAsync(MatrixUserId, MatrixPassword, "MatrixBot-342",
                    HttpClientFactory, stoppingToken, Logger);

                Logger.LogWarning("Connected to Matrix successfully.");
                retryCount = 0; // Reset retry count upon successful connection
                currentDelay = initialDelay;   // Reset delay upon successful connection

                // Process messages
                await foreach (var message in _client.Messages.ReadAllAsync(stoppingToken))
                {
                    await MessageReceivedAsync(message).ConfigureAwait(false);
                }

                Logger.LogWarning("Matrix Sync has ended.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error was caught in the matrix service loop.");
            }
            finally
            {
                _client = null;
            }

            // Reconnection logic
            retryCount++;
            if (retryCount > maxRetries)
            {
                Logger.LogError("Maximum number of retries reached. Stopping Matrix worker service.");
                break;
            }

            Logger.LogWarning("Reconnecting to Matrix in {Delay}s (Attempt {RetryCount}/{MaxRetries})...", currentDelay / 1000, retryCount, maxRetries);
            await Task.Delay(currentDelay, stoppingToken);

            // Increase delay for the next retry, up to the maximum delay
            currentDelay = currentDelay * retryDelayFactor;
        }
    }

    private async Task MessageReceivedAsync(ReceivedTextMessage message)
    {
        if (!IsRunning)
            return;

        try
        {
            //TODO: Instead of using timestamp we may use a combination of limited/prev_batch and next to tell if this is new or not
            var age = DateTimeOffset.Now - message.Timestamp; // filter the message spam we receive from the server at start
            if (age.TotalSeconds > 30)
                return;

            if (message.ThreadId != null) // ignore messages in threads for now
                return;

            var cachedChannel = TextChannels.FirstOrDefault(c => c.Id == message.Room.RoomId.Full);
            if (cachedChannel == null)
            {
                cachedChannel = new JoinedTextChannel<string>(message.Room.RoomId.Full, message.Room.DisplayName ?? message.Room.CanonicalAlias?.Full ?? "Unknown",
                  ImmutableArray<ChannelUser<string>>.Empty);
                Cache.Channels = TextChannels.Add(cachedChannel);
            }

            var cachedUser = cachedChannel.GetUser(message.Sender.User.UserId.Full);
            if (cachedUser == null)
            {
                cachedUser = GenerateChannelUser(message.Sender);
                cachedChannel.Users = cachedChannel.Users.Add(cachedUser);
            }

            string? sanitizedMessage = SanitizeMessage(message, cachedChannel, out var isCurrentUserMentioned);
            if (sanitizedMessage == null)
                return;
            if (sanitizedMessage.Length > 300)
                sanitizedMessage = sanitizedMessage[..300];

            var isFromSelf = message.Sender.User.UserId.Full == _client!.CurrentUser.Full;
            // The bot should never respond to itself.
            if (isFromSelf)
                return;

            if (!ShouldRespond(message, sanitizedMessage, isCurrentUserMentioned))
                return;

            await RespondToMessage(message, cachedChannel, sanitizedMessage, cachedUser).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while processing a message.");
        }
    }

    public async Task RespondToMessage(ReceivedTextMessage message, JoinedTextChannel<string> channel, string sanitizedMessage, ChannelUser<string> author)
    {
        await message.Room.SendTypingNotificationAsync(4000).ConfigureAwait(false);


        List<Message> history = [];
        var olderMessages = GetMessageHistory();
        foreach (var entry in olderMessages)
        {
            history.Add(Message.FromText(Role.User, $"[{entry.Timestamp.ToRelativeToNowLabel()}] [[{entry.Author}]]: {entry.SanitizedMessage}"));
            if (!string.IsNullOrWhiteSpace(entry.GeneratedResponse))
                history.Add(Message.FromText(Role.Assistant, entry.GeneratedResponse));
        }

        history.Add(Message.FromText(Role.User, $"[now] [[{author.SanitizedName}]]: {sanitizedMessage}"));
        string instruction = GetDailyInstruction();
        var response = await ChatClient.CallAsync(instruction, history, DefaultParameters).ConfigureAwait(false);
        if (string.IsNullOrEmpty(response))
        {
            Logger.LogWarning("AI did not return a response to: {SanitizedMessage}", sanitizedMessage.Length > 50 ? sanitizedMessage[..50] : sanitizedMessage);
            return; // may be rate limited 
        }

        // Store the message history entry
        StoreMessageHistory(author.SanitizedName, sanitizedMessage, DateTimeOffset.Now, response);

        IList<MatrixId> mentions = [];
        response = HandleMentions(response, channel, mentions);
        // Set reply to true if we have no mentions
        await message.SendResponseAsync(response, isReply: mentions == null, mentions: mentions).ConfigureAwait(false); // TODO, log here?
    }

    private void StoreMessageHistory(string author, string sanitizedMessage, DateTimeOffset timestamp, string generatedResponse)
    {
        var entry = new MessageHistoryEntry(author, sanitizedMessage, timestamp, generatedResponse);
        
        _messageHistory.Enqueue(entry);
        
        // Keep only the last 10 entries        }
        while (_messageHistory.Count > 10)
        {
            _messageHistory.TryDequeue(out _);
        }
    }

    public IEnumerable<MessageHistoryEntry> GetMessageHistory()
    {
        return _messageHistory.ToArray();
    }

    private static ChannelUser<string> GenerateChannelUser(RoomUser user)
    {
        var displayName = user.GetDisplayName();
        var sanitizedName = NameSanitizer.IsValidName(displayName) ? displayName : NameSanitizer.SanitizeName(displayName);
        return new ChannelUser<string>(user.User.UserId.Full, displayName, sanitizedName);
    }

    private string? SanitizeMessage(ReceivedTextMessage message, JoinedTextChannel<string> cachedChannel, out bool isCurrentUserMentioned)
    {
        isCurrentUserMentioned = false;
        if (message == null || message.Body == null)
            return null;

        string text = message.Body;
        text = text.Replace("\\n", "\n"); // replace escaped newlines with real newlines

        // Check for wordle messages
        string[] invalidStrings = { "🟨", "🟩", "⬛", "🟦", "⬜", "➡️", "📍", "🗓️", "🥇", "🥈", "🥉" };
        if (invalidStrings.Any(text.Contains))
            return null;

        var customMentionPattern = @"<@(?<userId>[^:]+:[^>]+)>";
        bool wasMentioned = false;
        text = Regex.Replace(text, customMentionPattern, match =>
        {
            var userId = match.Groups["userId"].Value;
            var user = cachedChannel.GetUser(userId);
            if (user?.Id == _client!.CurrentUser.Full)
            {
                wasMentioned = true;
            }
            return user != null ? $"[[{user.SanitizedName}]]" : match.Value;
        });

        if (wasMentioned)
            isCurrentUserMentioned = true;

        // Remove markdown style quotes
        text = Regex.Replace(text, @"^>.*$", string.Empty, RegexOptions.Multiline);
        return text;
    }

    private bool ShouldRespond(ReceivedTextMessage message, string sanitizedMessage, bool isCurrentUserMentionedInBody)
    {
        if (message.Sender.User.UserId.Full.Equals("@armleuchter:matrix.dnix.de", StringComparison.OrdinalIgnoreCase) ||
            message.Sender.User.UserId.Full.Equals("@flokati:matrix.dnix.de", StringComparison.OrdinalIgnoreCase))
            return false; // Do not respond to the bots

        if (isCurrentUserMentionedInBody)
            return true;

        if (Regex.IsMatch(sanitizedMessage, @"\bStoll\b", RegexOptions.IgnoreCase))
            return true;

        if (message.Mentions != null)
        {
            foreach (var mention in message.Mentions)
            {
                if (mention.User.UserId.Full.Equals(_client!.CurrentUser.Full, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private string HandleMentions(string response, JoinedTextChannel<string> cachedChannel, IList<MatrixId> mentions)
    {
        // replace [[canonicalName]] with the <@fullId> syntax using a regex lookup
        var mentionPattern = @"\[\[(.*?)\]\]";
        return Regex.Replace(response, mentionPattern, match =>
        {
            var canonicalName = match.Groups[1].Value;
            var user = cachedChannel.Users.FirstOrDefault(u => string.Equals(u.SanitizedName, canonicalName, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                if (UserId.TryParse(user.Id, out var userId) && userId != null)
                {
                    mentions.Add(userId);
                    return user.Name;
                }
            }
            return canonicalName;
        });
    }
}
