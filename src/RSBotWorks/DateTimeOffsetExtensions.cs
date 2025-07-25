using System;

public static class DateTimeOffsetExtensions
{
    public static string ToRelativeToNowLabel(this DateTimeOffset timestamp)
    {
        var now = DateTimeOffset.Now;
        var ts = now - timestamp;
        return ts.ToRelativeLabel();
    }
}

public static class TimeSpanExtensions
{
    public static string ToRelativeLabel(this TimeSpan timeSpan)
    {
        var ts = timeSpan;
        if (ts < TimeSpan.Zero)
            ts = TimeSpan.Zero; // No future labels

        if (ts.TotalSeconds < 60)
            return ts.TotalSeconds <= 1 ? "now" : $"{Math.Round(ts.TotalSeconds)}s ago";
        if (ts.TotalMinutes < 60)
            return $"{Math.Round(ts.TotalMinutes)}m ago";
        if (ts.TotalHours < 24)
            return $"{Math.Round(ts.TotalHours)}h ago";

        // Anything of one or more days uses days
        var days = Math.Round(ts.TotalDays);
        return $"{days}d ago";
    }
}