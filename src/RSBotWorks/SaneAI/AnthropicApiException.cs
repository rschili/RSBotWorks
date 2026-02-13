using System.Text.Json;

namespace RSBotWorks.SaneAI;

/// <summary>
/// Thrown when the Anthropic API returns a non-success HTTP status.
/// Parses the error body for verbose diagnostics. Includes the raw request
/// so you can reproduce it with CurlGenerator.
/// </summary>
public class AnthropicApiException : Exception
{
    public int StatusCode { get; }
    public string? ErrorType { get; }
    public string ErrorBody { get; }
    public RawHttpRequest Request { get; }

    public AnthropicApiException(int statusCode, string? errorType, string message,
        string errorBody, RawHttpRequest request)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
        ErrorBody = errorBody;
        Request = request;
    }

    /// <summary>
    /// Parse the Anthropic error response JSON and build a verbose exception.
    /// Expected format: { "type": "error", "error": { "type": "...", "message": "..." } }
    /// </summary>
    public static AnthropicApiException FromResponse(RawHttpRequest request, RawHttpResponse response)
    {
        string? errorType = null;
        string message = $"Anthropic API error (HTTP {response.StatusCode})";

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("type", out var t))
                    errorType = t.GetString();
                if (errorObj.TryGetProperty("message", out var m))
                    message = $"Anthropic API error (HTTP {response.StatusCode}, {errorType}): {m.GetString()}";
            }
        }
        catch (JsonException)
        {
            // Body isn't JSON — use the raw text
            message = $"Anthropic API error (HTTP {response.StatusCode}): {Truncate(response.Body, 500)}";
        }

        return new AnthropicApiException(response.StatusCode, errorType, message,
            response.Body, request);
    }

    /// <summary>Generate a curl command to reproduce this failed request.</summary>
    public string ToCurl() => CurlGenerator.Generate(Request);

    public override string ToString() =>
        $"{Message}\nCurl: {ToCurl()}\nRaw response: {Truncate(ErrorBody, 1000)}";

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
