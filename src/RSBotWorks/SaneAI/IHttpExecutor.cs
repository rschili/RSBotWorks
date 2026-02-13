namespace RSBotWorks.SaneAI;

/// <summary>
/// Minimal HTTP abstraction. One method, one request, one response.
/// Easy to mock, easy to intercept, easy to test.
/// </summary>
public interface IHttpExecutor
{
    Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Raw HTTP request - just the essentials. URL, headers, body string.
/// No magic, no hidden state.
/// </summary>
public record RawHttpRequest
{
    public required string Method { get; init; }
    public required string Url { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public string? Body { get; init; }
}

/// <summary>
/// Raw HTTP response - status code, body string, headers.
/// The body is always the raw string so you can log/inspect it before parsing.
/// </summary>
public record RawHttpResponse
{
    public required int StatusCode { get; init; }
    public required string Body { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}
