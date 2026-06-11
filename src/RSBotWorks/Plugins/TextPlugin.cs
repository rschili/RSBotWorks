using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public class TextPlugin
{
    // Keep inputs small so the tools stay cheap and cannot be turned into an attack vector.
    private const int MaxInputLength = 4000;
    private const int MaxPatternLength = 200;
    private const int MaxMatches = 100;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    [LocalFunction("count_substring")]
    [Description("Counts how often a substring or letter occurs within a piece of text. Useful for questions like 'how many E are in Erdbeere'. Use this when asked, don't skip it!")]
    public Task<string> CountSubstringAsync(
        [Description("The text to search in.")] string text,
        [Description("The substring or single letter to count occurrences of.")] string fragment,
        [Description("If true, the count ignores upper/lower case differences. Defaults to true.")] bool ignoreCase = true)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult("Error: 'text' must not be empty.");

        if (string.IsNullOrEmpty(fragment))
            return Task.FromResult("Error: 'fragment' must not be empty.");

        if (text.Length > MaxInputLength)
            return Task.FromResult($"Error: 'text' is too long ({text.Length} characters). The maximum allowed length is {MaxInputLength} characters.");

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(fragment, index, comparison)) != -1)
        {
            count++;
            index += fragment.Length; // count non-overlapping occurrences
        }

        var caseNote = ignoreCase ? "case-insensitive" : "case-sensitive";
        return Task.FromResult($"The fragment \"{fragment}\" occurs {count} time(s) in the given text ({caseNote}).");
    }

    [LocalFunction("regex_match")]
    [Description("Runs a .NET regular expression against a piece of text and reports the matches. Always returns the match count; can optionally include the matched substrings. Note: uses the non-backtracking engine, so backreferences and lookarounds are not supported.")]
    public Task<string> RegexMatchAsync(
        [Description("The text to run the regex against.")] string text,
        [Description("The .NET regular expression pattern.")] string pattern,
        [Description("If true, the matched substrings are included in the result (capped). If false, only the match count is returned. Defaults to false.")] bool includeMatches = false)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult("Error: 'text' must not be empty.");

        if (string.IsNullOrEmpty(pattern))
            return Task.FromResult("Error: 'pattern' must not be empty.");

        if (text.Length > MaxInputLength)
            return Task.FromResult($"Error: 'text' is too long ({text.Length} characters). The maximum allowed length is {MaxInputLength} characters.");

        if (pattern.Length > MaxPatternLength)
            return Task.FromResult($"Error: 'pattern' is too long ({pattern.Length} characters). The maximum allowed length is {MaxPatternLength} characters.");

        Regex regex;
        try
        {
            // NonBacktracking guarantees linear-time matching (no catastrophic backtracking),
            // the timeout is a second line of defence.
            regex = new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexParseException)
        {
            return Task.FromResult($"Error: invalid or unsupported regular expression: {ex.Message}");
        }

        try
        {
            int count = 0;
            var matches = includeMatches ? new List<string>(MaxMatches + 1) : null;

            for (var match = regex.Match(text); match.Success; match = match.NextMatch())
            {
                count++;
                if (count > MaxMatches)
                    return Task.FromResult($"Error: too many matches (more than {MaxMatches}). Please use a more specific pattern.");

                matches?.Add(match.Value);
            }

            if (matches is null)
                return Task.FromResult($"The pattern matched {count} time(s).");

            if (count == 0)
                return Task.FromResult("The pattern matched 0 time(s).");

            var sb = new StringBuilder();
            sb.Append($"The pattern matched {count} time(s):");
            foreach (var value in matches)
            {
                sb.Append(Environment.NewLine);
                sb.Append("- ");
                sb.Append(value);
            }
            return Task.FromResult(sb.ToString());
        }
        catch (RegexMatchTimeoutException)
        {
            return Task.FromResult("Error: the regular expression took too long to evaluate and was aborted.");
        }
    }
}
