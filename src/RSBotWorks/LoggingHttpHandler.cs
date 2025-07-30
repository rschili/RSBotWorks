using Microsoft.Extensions.Logging;

namespace RSBotWorks;

// Use like this:
// services.AddTransient<LoggingHandler>();
//     services.AddHttpClient().AddHttpMessageHandler<LoggingHttpHandler>();

public class LoggingHttpHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHttpHandler> _logger;

    public LoggingHttpHandler(ILogger<LoggingHttpHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Log Request URL
        _logger.LogInformation("Request: {Method} {Uri}", request.Method, request.RequestUri);

        // Log JSON request content if present
        if (request.Content != null &&
            request.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var req = await request.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Request Body:\n{Body}", req);
        }

        var response = await base.SendAsync(request, cancellationToken);

        // Log response status
        _logger.LogInformation("Response: {StatusCode}", response.StatusCode);

        // Log JSON response content if present
        if (response.Content != null &&
            response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var resp = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Response Body:\n{Body}", resp);
        }

        return response;
    }
}
