using System.Text;

namespace RSBotWorks.SaneAI;

/// <summary>
/// Generates curl commands from raw HTTP requests.
/// Dead useful for reproducing failed API calls from the command line.
/// Automatically redacts sensitive headers (API keys).
/// </summary>
public static class CurlGenerator
{
    private static readonly HashSet<string> DefaultSensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "x-api-key",
        "authorization"
    };

    /// <summary>
    /// Generate a curl command from a raw HTTP request.
    /// Sensitive headers are replaced with environment variable references.
    /// </summary>
    /// <param name="request">The raw request to convert.</param>
    /// <param name="sensitiveHeaderReplacements">
    /// Optional overrides for header redaction. Key = header name, Value = replacement text.
    /// If null, defaults to replacing x-api-key with $ANTHROPIC_API_KEY and authorization with $AUTH_TOKEN.
    /// </param>
    public static string Generate(RawHttpRequest request,
        IDictionary<string, string>? sensitiveHeaderReplacements = null)
    {
        var sb = new StringBuilder();
        sb.Append($"curl {request.Url}");

        foreach (var (key, value) in request.Headers)
        {
            // Skip content-type for body requests, curl handles it
            string headerValue;
            if (sensitiveHeaderReplacements != null
                && sensitiveHeaderReplacements.TryGetValue(key, out var replacement))
            {
                headerValue = replacement;
            }
            else if (DefaultSensitiveHeaders.Contains(key))
            {
                headerValue = GetDefaultRedaction(key);
            }
            else
            {
                headerValue = value;
            }

            sb.Append($" \\\n  --header \"{key}: {headerValue}\"");
        }

        if (request.Body != null)
        {
            // Escape single quotes for shell safety
            var escapedBody = request.Body.Replace("'", "'\\''");
            sb.Append($" \\\n  --data '{escapedBody}'");
        }

        return sb.ToString();
    }

    /// <summary>Generate a curl command from a ChatResult (convenience overload).</summary>
    public static string Generate(ChatResult result,
        IDictionary<string, string>? sensitiveHeaderReplacements = null)
        => Generate(result.Request, sensitiveHeaderReplacements);

    private static string GetDefaultRedaction(string headerName) => headerName.ToLowerInvariant() switch
    {
        "x-api-key" => "$ANTHROPIC_API_KEY",
        "authorization" => "$AUTH_TOKEN",
        _ => "***REDACTED***"
    };
}
