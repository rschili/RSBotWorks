using System.Net;
using RSMatrix.Http;

namespace Stoll;

internal static class MatrixReconnectPolicy
{
    internal static bool ShouldRetry(Exception exception, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
            return false;

        return exception switch
        {
            // This client doesn't request refresh tokens. Soft logout explicitly
            // permits another password login with the same device ID.
            MatrixResponseException { ErrorCode: "M_UNKNOWN_TOKEN", SoftLogout: true } => true,
            MatrixResponseException response => response.IsTransient,
            HttpRequestException http => http.StatusCode == null || IsTransientStatus(http.StatusCode),
            OperationCanceledException => true, // request timeout, not shutdown
            _ => false
        };
    }

    private static bool IsTransientStatus(HttpStatusCode? status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int?)status is >= 500 and <= 599;

    internal static TimeSpan GetDelay(int failureCount, TimeSpan? retryAfter)
    {
        var milliseconds = Math.Min(300_000, 5_000 * Math.Pow(2, Math.Clamp(failureCount, 0, 6)));
        var backoff = TimeSpan.FromMilliseconds(milliseconds + Random.Shared.Next(0, 1000));
        return retryAfter is { } minimum && minimum > backoff ? minimum : backoff;
    }

    internal static async Task DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        // Preserve even a server delay larger than Task.Delay's supported maximum.
        while (delay > TimeSpan.Zero)
        {
            var chunk = delay > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delay;
            await Task.Delay(chunk, stoppingToken).ConfigureAwait(false);
            delay -= chunk;
        }
    }
}
