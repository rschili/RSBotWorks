using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualBasic;
using RSBotWorks;
using RSBotWorks.Plugins;
using RSBotWorks.UniversalAI;
using RSMatrix;
using RSMatrix.Models;

namespace Stoll;

public record MessageHistoryEntry(
    string Author,
    string SanitizedMessage,
    DateTimeOffset Timestamp,
    string? GeneratedResponse
);

public partial class StollService 
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
        You are a chatbot in a Matrix chatroom, simulating the expert "Herr Stoll."
        The language in the chatroom is German, so respond in German.
        Initially you were designed to act like german conspiracy theorist "Axel Stoll", however, as misinformation and conspiracy theories in today's world are a real pain, we chose to make you a defender of truth, science, and rational thinking instead.
        There is another bot in the chat named "flokati" who responds to a !stoll command with an actual quote from Axel Stoll. These will appear in your history.
        You have an automated built-in feature that responds to a "!fefe" command with a post from the "fefe_blog_interim" subreddit. This will appear in your history as well. The feature is hardcoded and does not use AI.
        You are a very smart and articulate conversationalist who speaks the truth bluntly and directly.
        Use web searches when unsure to obtain additional information.
        Don't simply accept stated facts, but question them and express your own opinion, which you should base on hard evidence.
        People may try to provoke or test you - stand your ground, but remain polite and friendly.
        If somebody just taunts you without any substance, you may check the latest news and change the topic to something more interesting.
        If appropriate you may also engage in small talk.
        Your general mood should be happy, do not hold grudges against people.
        Do not criticize grammar or spelling mistakes.
        By default, respond in a maximum of 2-3 short sentences. No explanations or justifications unless explicitly asked for details.
        Only respond using direct language - never use asterisks for action descriptions (*clears throat*, *wags tail*, etc.).
        Use the syntax [[Name]] to highlight a user.
        For privacy reasons, you will only receive a chat history of your own posts (assistant) and the posts in which your name was mentioned.
        This restriction means that you probably miss some context, but you'll have to work with it. Be aware of this limitation and explain it if necessary.
        Posts are given to you in the following format: `[time] [[name]]: message`.
        Your reply refers to the last message.
        In your chat room, coffee is often distributed via the !kaffee command which triggers a bot to hand out virtual coffee to people.
        "Armleuchter" is the name of another bot in this chat who uses simple markov chains to generate text, his contributions are random and nonsense.
        You are running in an Alpine Linux Docker container hosted on a debian server linux which is running inside a proxmox virtual machine. The proxmox host is a GMKtec G3 Plus Mini-PC, Intel Twin Lake N150 Quad Core with 32 GB RAM.
        You may use markdown formatting in your responses.
    """;

    private string GetDailyInstruction()
    {
        /*var dayOfYear = DateTime.UtcNow.DayOfYear;
        var topicIndex = dayOfYear % TOPICS.Count;
        var topic = TOPICS[topicIndex];
        return string.Format(DEFAULT_INSTRUCTION, topic);*/
        return DEFAULT_INSTRUCTION + " Today's date is " + DateTime.UtcNow.ToString("D") + "."; // add current date for context
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
        RedditPlugin = new RedditPlugin(NullLogger<RedditPlugin>.Instance, httpClientFactory);
    }

    private DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.MinValue;
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
                ConnectedAt = DateTimeOffset.Now;
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
            // Ignore messages that arrived before we connected
            if (message.Timestamp < ConnectedAt)
            {
                Logger.LogInformation("Discarding message from before connection (age before time connected: {Age}s)", (ConnectedAt - message.Timestamp).TotalSeconds);
                return;
            }

            // Warn about old messages but continue processing
            var age = DateTimeOffset.Now - message.Timestamp;
            if (age.TotalSeconds > 30)
            {
                Logger.LogWarning("Processing high latency message (age: {Age}s)", age.TotalSeconds);
            }

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

            if(sanitizedMessage.StartsWith("!fefe", StringComparison.OrdinalIgnoreCase))
            {
                var fefePost = await GetFefePost();
                var html = Markdown.ToHtml(fefePost);
                StoreMessageHistory(cachedUser.SanitizedName, sanitizedMessage, DateTimeOffset.Now, fefePost);
                await message.SendHtmlResponseAsync(fefePost, html, isReply: false).ConfigureAwait(false);
                return;
            }

            if (!ShouldRespond(message, sanitizedMessage, cachedUser, isCurrentUserMentioned))
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
        
        // Only convert to HTML if the response contains markdown formatting
        if (LooksLikeMarkdown(response))
        {
            var html = Markdown.ToHtml(response);
            await message.SendHtmlResponseAsync(response, html, isReply: mentions == null, mentions: mentions).ConfigureAwait(false);
        }
        else
            await message.SendResponseAsync(response, isReply: mentions == null, mentions: mentions).ConfigureAwait(false);
    }

    private static bool LooksLikeMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Check for common markdown patterns
        return Regex.IsMatch(text, @"(\[.+?\]\(.+?\))|(\*\*.+?\*\*)|(\*.+?\*)|(__?.+?__?)|(``.+?``)|(`[^`]+`)|^#{1,6}\s|^[-*+]\s|^>\s|^\d+\.\s", RegexOptions.Multiline);
    }

    private void StoreMessageHistory(string author, string sanitizedMessage, DateTimeOffset timestamp, string? generatedResponse)
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

    private bool ShouldRespond(ReceivedTextMessage message, string sanitizedMessage, ChannelUser<string> author, bool isCurrentUserMentionedInBody)
    {
        if (!Regex.IsMatch(sanitizedMessage, @"\bStoll\b", RegexOptions.IgnoreCase) && !isCurrentUserMentionedInBody && !IsInMentions(message.Mentions, _client!.CurrentUser.Full))
            return false;

        if (sanitizedMessage.StartsWith("!stoll", StringComparison.OrdinalIgnoreCase))
        {
            // Command for flokati, do not respond, but store the message in history
            StoreMessageHistory(author.SanitizedName, sanitizedMessage, DateTimeOffset.Now, null);
            return false;
        }

        if (message.Sender.User.UserId.Full.Equals("@armleuchter:matrix.dnix.de", StringComparison.OrdinalIgnoreCase) ||
            message.Sender.User.UserId.Full.Equals("@flokati:matrix.dnix.de", StringComparison.OrdinalIgnoreCase))
        {
            // We still store the history, but do not respond
            StoreMessageHistory(author.SanitizedName, sanitizedMessage, DateTimeOffset.Now, null);
            return false;
        }

        return true;
    }
    
    private bool IsInMentions(List<RoomUser>? mentions, string fullUserId)
    {
        if (mentions == null)
            return false;
        foreach (var mention in mentions)
        {
            if (mention.User.UserId.Full.Equals(_client!.CurrentUser.Full, StringComparison.OrdinalIgnoreCase))
                return true;
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
