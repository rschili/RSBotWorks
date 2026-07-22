using System.Text.Json;

namespace RSBotWorks.SaneAI;

/// <summary>
/// Thrown when the OpenRouter API returns a non-success HTTP status.
/// Parses the error body for verbose diagnostics. Includes the raw request
/// so you can reproduce it with CurlGenerator.
///
/// OpenRouter error format: { "error": { "code": 400, "message": "...", "metadata": { ... } } }
/// </summary>
public class OpenRouterApiException : Exception
{
    public int StatusCode { get; }

    /// <summary>The error code from the OpenRouter error body (may differ from the HTTP status).</summary>
    public int? ErrorCode { get; }

    public string ErrorBody { get; }
    public RawHttpRequest Request { get; }

    public OpenRouterApiException(int statusCode, int? errorCode, string message,
        string errorBody, RawHttpRequest request)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ErrorBody = errorBody;
        Request = request;
    }

    /// <summary>
    /// Parse the OpenRouter error response JSON and build a verbose exception.
    /// Expected format: { "error": { "code": 400, "message": "..." } }
    /// </summary>
    public static OpenRouterApiException FromResponse(RawHttpRequest request, RawHttpResponse response)
    {
        int? errorCode = null;
        string message = $"OpenRouter API error (HTTP {response.StatusCode})";

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number)
                    errorCode = c.GetInt32();
                if (errorObj.TryGetProperty("message", out var m))
                    message = $"OpenRouter API error (HTTP {response.StatusCode}, code {errorCode}): {m.GetString()}";
            }
        }
        catch (JsonException)
        {
            // Body isn't JSON — use the raw text
            message = $"OpenRouter API error (HTTP {response.StatusCode}): {Truncate(response.Body, 500)}";
        }

        return new OpenRouterApiException(response.StatusCode, errorCode, message,
            response.Body, request);
    }

    /// <summary>Generate a curl command to reproduce this failed request.</summary>
    public string ToCurl() => CurlGenerator.Generate(Request, OpenRouterClient.CurlHeaderReplacements);

    public override string ToString() =>
        $"{Message}\nCurl: {ToCurl()}\nRaw response: {Truncate(ErrorBody, 1000)}";

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
