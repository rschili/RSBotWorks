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
using RSBotWorks.SaneAI;

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
    
    private async Task SendGoodMorningMessage()
    {
        if (!IsRunning)
        {
            Logger.LogWarning("Attempted to send good morning message while service is not running.");
            return;
        }

        var outputChannel = await DiscordClient.GetChannelAsync(Config.MaschinenraumId).ConfigureAwait(false);
        if (outputChannel == null || outputChannel is not ITextChannel outputTextChannel)
        {
            Logger.LogError("Failed to get text channel for sending good morning message");
            return;
        }

        try
        {
            var redditPlugin = new RedditPlugin(NullLogger<RedditPlugin>.Instance, HttpClientFactory);
            var newsPosts = await redditPlugin.FetchPostsAsync("worldnews+europe+economics+germany", 2).ConfigureAwait(false);
            var generalPosts = await redditPlugin.FetchPostsAsync("futurology+science", 2).ConfigureAwait(false);
            var techPosts = await redditPlugin.FetchPostsAsync("technology+amd+hardware+pcmasterrace+selfhosted", 2).ConfigureAwait(false);

            var allPosts = newsPosts.Concat(generalPosts).Concat(techPosts).ToList();

            if (allPosts.Count == 0)
            {
                Logger.LogWarning("No reddit posts found for morning briefing.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"**Morgenbriefing — {DateTime.Now:dddd, d. MMMM yyyy}**");
            foreach (var post in allPosts)
            {
                var url = $"https://www.reddit.com{post.Permalink}";
                sb.AppendLine($"- [{post.Title}]({url}) (r/{post.SubredditName})");
            }

            await outputTextChannel.SendMessageAsync(sb.ToString(),
                flags: MessageFlags.SuppressEmbeds | MessageFlags.SuppressNotification).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while generating the morning briefing.");
        }
    }
}
