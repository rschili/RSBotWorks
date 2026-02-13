using System.Text;

namespace RSBotWorks.SaneAI;

/// <summary>
/// Default IHttpExecutor backed by IHttpClientFactory.
/// Does exactly what it says on the tin - sends HTTP requests.
/// </summary>
public class DefaultHttpExecutor : IHttpExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DefaultHttpExecutor(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken = default)
    {
        using var client = _httpClientFactory.CreateClient();
        using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);

        foreach (var (key, value) in request.Headers)
        {
            // content-type is set on the content itself by StringContent
            if (key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                continue;
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        }

        if (request.Body != null)
        {
            httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        var headers = new Dictionary<string, string>();
        foreach (var header in response.Headers)
            headers[header.Key] = string.Join(", ", header.Value);
        if (response.Content.Headers != null)
        {
            foreach (var header in response.Content.Headers)
                headers[header.Key] = string.Join(", ", header.Value);
        }

        return new RawHttpResponse
        {
            StatusCode = (int)response.StatusCode,
            Body = body,
            Headers = headers
        };
    }
}
