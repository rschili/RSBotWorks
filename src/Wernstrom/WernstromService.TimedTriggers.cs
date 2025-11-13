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
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks;
using RSBotWorks.Plugins;
using RSBotWorks.UniversalAI;

namespace Wernstrom;

public partial class WernstromService
{
    private readonly List<(TimeOnly CestTime, Func<Task> Operation)> _scheduledOperations = new();

    public readonly TimeZoneInfo CESTTimeZone = GetCESTTimeZone();

    private static TimeZoneInfo GetCESTTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                // Try alternative ID for Linux/macOS
                return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Local;
            }
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>
    /// Registers a daily operation to be executed at the specified CEST time.
    /// </summary>
    /// <param name="cestTime">The time in Central European Summer Time when the operation should execute</param>
    /// <param name="operation">The async operation to execute</param>
    public void RegisterDailyOperation(TimeOnly cestTime, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation, nameof(operation));
        _scheduledOperations.Add((cestTime, operation));
        Logger.LogInformation("Registered daily operation for {Time} CEST", cestTime.ToString("HH:mm"));
    }

    private async Task DailySchedulerLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var utcNow = DateTime.UtcNow;
                var cestNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, CESTTimeZone);
                var cestCurrentTime = TimeOnly.FromDateTime(cestNow);

                // Find the next scheduled operation
                DateTime nextExecutionUtc;
                (TimeOnly CestTime, Func<Task> Operation) nextOperation = default;
                
                // Look for next operation today
                var nextTodayOperation = _scheduledOperations
                    .Where(op => op.CestTime > cestCurrentTime)
                    .OrderBy(op => op.CestTime)
                    .FirstOrDefault();
                
                if (nextTodayOperation != default)
                {
                    // There's an operation later today
                    var nextCestDateTime = cestNow.Date.Add(nextTodayOperation.CestTime.ToTimeSpan());
                    nextExecutionUtc = TimeZoneInfo.ConvertTimeToUtc(nextCestDateTime, CESTTimeZone);
                    nextOperation = nextTodayOperation;
                }
                else
                {
                    // No more operations today, use the earliest operation tomorrow
                    var earliestOperation = _scheduledOperations.OrderBy(op => op.CestTime).FirstOrDefault();
                    if (earliestOperation != default)
                    {
                        var nextCestDateTime = cestNow.Date.AddDays(1).Add(earliestOperation.CestTime.ToTimeSpan());
                        nextExecutionUtc = TimeZoneInfo.ConvertTimeToUtc(nextCestDateTime, CESTTimeZone);
                        nextOperation = earliestOperation;
                    }
                    else
                    {
                        // No operations registered, wait a bit and check again
                        await Task.Delay(TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var delay = nextExecutionUtc - utcNow;
                if (delay > TimeSpan.Zero)
                {
                    var cestExecutionTime = TimeZoneInfo.ConvertTimeFromUtc(nextExecutionUtc, CESTTimeZone);
                    Logger.LogInformation("Next daily operation scheduled for {NextTime} CEST ({Delay} from now)", 
                        cestExecutionTime.ToString("yyyy-MM-dd HH:mm:ss"), 
                        delay.ToString(@"hh\:mm\:ss"));
                    
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                if (!cancellationToken.IsCancellationRequested && nextOperation.Operation != null)
                {
                    await ExecuteScheduledOperation(nextOperation.Operation).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false); // To make sure we do not call the same task multiple times
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("Daily scheduler cancelled");
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in daily scheduler loop");
                // Wait a bit before retrying to avoid tight error loops
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            } // try
        } // while
    }// method

    private async Task ExecuteScheduledOperation(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var utcNow = DateTime.UtcNow;
            var cestNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, CESTTimeZone);
            Logger.LogError(ex, "Error during scheduled operation at {Time} CEST", cestNow.ToString("HH:mm"));
        }
    }
    

    internal const string LEET_INSTRUCTION = $"""
        {GENERIC_INSTRUCTION}
        A traditional game is played in the chat every day at 13:37, in which each user sends a message.
        A bot measures the time of the messages, and whoever is closest to 13:37 wins.
        You've participated in the game, but user sikk has blacklisted you because you were too good.
        Nevertheless, generate a message to be sent now at 13:37 PM.
        Reply in a maximum of 2-3 short sentences.
        Only reply with direct language - never use asterisks to describe actions (*clears throat*, *wags tail*, etc.).
        You address all other participants informally.
        Provide a reply directly as text, without a name, timestamp, header, or other formatting.
        """;

    internal PreparedChatParameters LeetParameters { get; init; }
    private PreparedChatParameters PrepareLeetParameters()
    {
        ChatParameters parameters = new()
        {
            ToolChoiceType = ToolChoiceType.None,
            MaxTokens = 2000,
            Temperature = 0.7m,
        };
        return ChatClient.PrepareParameters(parameters);
    }

    private async Task PerformLeetTimeOperation()
    {
        if (!IsRunning)
        {
            Logger.LogWarning("Attempted to perform 1337 time operation while service is not running.");
            return;
        }
        var response = await ChatClient.CallAsync(null, [Message.FromText(Role.User, LEET_INSTRUCTION)], LeetParameters).ConfigureAwait(false);
        if (string.IsNullOrEmpty(response))
        {
            Logger.LogWarning("Got an empty response for 1337 time operation");
            return;
        }

        var channel = await DiscordClient.GetChannelAsync(Config.BrueckeId).ConfigureAwait(false);
        if (channel == null || channel is not ITextChannel textChannel)
        {
            Logger.LogError("Failed to get text channel for 1337 time operation");
            return;
        }

        // Calculate precise timing for 13:37:00 CEST
        var cestNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CESTTimeZone);
        var target1337 = cestNow.Date.AddHours(13).AddMinutes(37); // Today's 13:37:00 CEST

        var remainingTime = target1337 - cestNow;
        int preFireMS = DiscordClient.Latency; // Account for network delay
        var waitTime = remainingTime.Subtract(TimeSpan.FromMilliseconds(preFireMS));

        if (waitTime > TimeSpan.Zero)
        {
            await Task.Delay(waitTime).ConfigureAwait(false);
        }

        await textChannel.SendMessageAsync(response).ConfigureAwait(false);
    }
    
    private async Task SendGoodMorningMessage()
    {
        if (!IsRunning)
        {
            Logger.LogWarning("Attempted to send good morning message while service is not running.");
            return;
        }

        /*var bruecke = await DiscordClient.GetChannelAsync(Config.BrueckeId).ConfigureAwait(false);
        if (bruecke == null || bruecke is not ITextChannel brueckeTextChannel)
        {
            Logger.LogError("Failed to get text channel for good morning message context");
            return;
        }*/

        var outputChannel = await DiscordClient.GetChannelAsync(Config.MaschinenraumId).ConfigureAwait(false);
        if (outputChannel == null || outputChannel is not ITextChannel outputTextChannel)
        {
            Logger.LogError("Failed to get text channel for sending good morning message");
            return;
        }
    
        /*var cachedBrueckeChannel = TextChannels.FirstOrDefault(c => c.Id == brueckeTextChannel.Id);
        if (cachedBrueckeChannel == null)
        {
            cachedBrueckeChannel = new JoinedTextChannel<ulong>(brueckeTextChannel.Id, brueckeTextChannel.Name, await GetChannelUsers(brueckeTextChannel).ConfigureAwait(false));
            Cache.Channels = TextChannels.Add(cachedBrueckeChannel);
        }*/

        var cachedOutputChannel = TextChannels.FirstOrDefault(c => c.Id == outputTextChannel.Id);
        if (cachedOutputChannel == null)
        {
            cachedOutputChannel = new JoinedTextChannel<ulong>(outputTextChannel.Id, outputTextChannel.Name, await GetChannelUsers(outputTextChannel).ConfigureAwait(false));
            Cache.Channels = TextChannels.Add(cachedOutputChannel);
        }

        var redditPlugin = new RedditPlugin(NullLogger<RedditPlugin>.Instance, HttpClientFactory);
        var news = await redditPlugin.GetRedditPostsAsync("worldnews+europe+economics+germany", 5).ConfigureAwait(false);
        var general = await redditPlugin.GetRedditPostsAsync("futurology+science", 3).ConfigureAwait(false);
        var tech = await redditPlugin.GetRedditPostsAsync("technology+amd+hardware+pcmasterrace+selfhosted", 5).ConfigureAwait(false);

        List<Message> history = new();
        /*
        var liveHistory = await brueckeTextChannel.GetMessagesAsync(5, CacheMode.AllowDownload).FlattenAsync().ConfigureAwait(false);
        foreach (var message in liveHistory.Reverse())
        {
            await AddMessageToHistory(history, message, cachedBrueckeChannel).ConfigureAwait(false);
        }
        */

        var developerMessage = $"""
            {GENERIC_INSTRUCTION}
            *** It is time to generate a daily good morning message (Sent at 8 o'clock). ***
            Today is {DateTime.Now:dddd, MMMM dd, yyyy}.
            Your message should be short and concise.
            Start with an enthusiastic greeting. You will be provided with some top news from various sources.
            Include what you deem interesting for the channel users from the news, the news may have links associated which you can
            include as masked links into the news title
            Always return 2-5 news items, each formatted on a single line like this:
            - [News Title](https://linkto.news): Short comment about the news.
            Do not end the message with a signature or conclusion.
            You'll be given several news sources provided as user messages.
            """;

        history.Add(Message.FromText(Role.User, news));
        history.Add(Message.FromText(Role.User, general));
        history.Add(Message.FromText(Role.User, tech));

        var response = await ChatClient.CallAsync(developerMessage, history, LeetParameters).ConfigureAwait(false);
        if (string.IsNullOrEmpty(response))
        {
            Logger.LogWarning("Got an empty response for good morning message");
            return;
        }

        var text = RestoreDiscordTags(response, cachedOutputChannel, out var hasMentions);
        await outputTextChannel.SendMessageAsync(text, flags: MessageFlags.SuppressEmbeds | MessageFlags.SuppressNotification).ConfigureAwait(false);
    }
}
