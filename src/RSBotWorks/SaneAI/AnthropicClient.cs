using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RSBotWorks.SaneAI;

/// <summary>
/// Anthropic Messages API client. Composes raw HTTP requests, sends them,
/// parses responses, and handles tool call loops implicitly.
///
/// The client is stateless and thread-safe. Conversation state lives in
/// the mutable AnthropicRequestComposer (fork it per conversation).
///
/// Errors throw <see cref="AnthropicApiException"/> — no IsSuccess checks needed.
/// Tool calls are handled automatically when a toolExecutor is provided.
/// </summary>
public class AnthropicClient
{
    public const string DefaultApiUrl = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";
    public const int DefaultMaxToolRounds = 4;

    private readonly string _apiKey;
    private readonly IHttpExecutor _httpExecutor;
    private readonly string _apiUrl;

    public AnthropicClient(string apiKey, IHttpExecutor httpExecutor,
        string? apiUrl = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpExecutor = httpExecutor ?? throw new ArgumentNullException(nameof(httpExecutor));
        _apiUrl = apiUrl ?? DefaultApiUrl;
    }

    /// <summary>
    /// Send a request with implicit tool call handling.
    /// 
    /// If the model requests tool calls and a <paramref name="toolExecutor"/> is provided,
    /// tools are executed automatically and the conversation continues until the model
    /// is done (or <paramref name="maxToolRounds"/> is hit).
    /// 
    /// Token usage is aggregated across all rounds.
    /// Throws <see cref="AnthropicApiException"/> on any API error.
    /// </summary>
    /// <param name="composer">The request composer. Gets forked internally — your original is untouched.</param>
    /// <param name="toolExecutor">Optional callback to execute tool calls. Receives a ToolCall, returns the result string.</param>
    /// <param name="maxToolRounds">Max number of tool call round-trips before giving up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ChatResult> SendAsync(
        AnthropicRequestComposer composer,
        Func<ToolCall, Task<string>>? toolExecutor = null,
        int maxToolRounds = DefaultMaxToolRounds,
        CancellationToken cancellationToken = default)
    {
        // Fork so we don't mutate the caller's composer
        var working = composer.Fork();

        var aggregatedUsage = new TokenUsage();
        var allToolCalls = new List<ToolCall>();

        // round 0 = initial request, rounds 1..maxToolRounds = tool call round-trips
        for (int round = 0; round <= maxToolRounds; round++)
        {
            var jsonBody = working.BuildJsonString();
            var request = CreateRequest(jsonBody);
            var response = await _httpExecutor.SendAsync(request, cancellationToken);

            // Non-success → throw with verbose diagnostics
            if (response.StatusCode < 200 || response.StatusCode >= 300)
                throw AnthropicApiException.FromResponse(request, response);

            var result = ParseResponse(request, response);

            if (result.Usage != null)
                aggregatedUsage = aggregatedUsage.Add(result.Usage);

            // No tool calls or no executor → final response
            if (!result.HasToolCalls || toolExecutor == null)
            {
                return result with
                {
                    Usage = aggregatedUsage,
                    ToolRoundsExecuted = round,
                    AllToolCallsExecuted = allToolCalls.Count > 0 ? allToolCalls.AsReadOnly() : null
                };
            }

            // Model wants tools but we're out of rounds
            if (round == maxToolRounds)
                throw new InvalidOperationException(
                    $"Tool call loop exceeded {maxToolRounds} rounds. " +
                    $"Last tool calls: {string.Join(", ", result.ToolCalls!.Select(t => t.Name))}. " +
                    $"Total tool calls executed: {allToolCalls.Count}.");

            allToolCalls.AddRange(result.ToolCalls!);
            working.AddRawAssistantContent(result.RawContentJson!);

            var toolResults = new List<(string ToolUseId, string Result)>();
            foreach (var toolCall in result.ToolCalls!)
            {
                var toolResult = await toolExecutor(toolCall);
                toolResults.Add((toolCall.Id, toolResult));
            }
            working.AddToolResults(toolResults);
        }

        // Unreachable: loop always returns or throws
        throw new UnreachableException();
    }

    private RawHttpRequest CreateRequest(string jsonBody) => new()
    {
        Method = "POST",
        Url = _apiUrl,
        Headers = new Dictionary<string, string>
        {
            ["x-api-key"] = _apiKey,
            ["anthropic-version"] = ApiVersion,
            ["content-type"] = "application/json"
        },
        Body = jsonBody
    };

    private static ChatResult ParseResponse(RawHttpRequest request, RawHttpResponse response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            var root = doc.RootElement;

            string? textContent = null;
            string? rawContentJson = null;
            List<ToolCall>? toolCalls = null;

            if (root.TryGetProperty("content", out var contentArray))
            {
                rawContentJson = contentArray.GetRawText();

                var textParts = new List<string>();
                foreach (var item in contentArray.EnumerateArray())
                {
                    var type = item.GetProperty("type").GetString();

                    if (type == "text")
                    {
                        textParts.Add(item.GetProperty("text").GetString()!);
                    }
                    else if (type == "tool_use")
                    {
                        toolCalls ??= [];
                        toolCalls.Add(new ToolCall
                        {
                            Id = item.GetProperty("id").GetString()!,
                            Name = item.GetProperty("name").GetString()!,
                            ArgumentsJson = item.GetProperty("input").GetRawText()
                        });
                    }
                    // Other content types (thinking, web_search_results, etc.)
                    // are preserved in RawContentJson for inspection
                }

                if (textParts.Count > 0)
                    textContent = string.Join("", textParts);
            }

            // Token usage
            TokenUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageElement))
            {
                usage = new TokenUsage
                {
                    InputTokens = usageElement.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                    OutputTokens = usageElement.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
                    CacheCreationInputTokens = usageElement.TryGetProperty("cache_creation_input_tokens", out var cc) ? cc.GetInt32() : null,
                    CacheReadInputTokens = usageElement.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : null,
                };
            }

            return new ChatResult
            {
                Request = request,
                Response = response,
                TextContent = textContent,
                Usage = usage,
                ToolCalls = toolCalls?.AsReadOnly(),
                StopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null,
                ModelId = root.TryGetProperty("model", out var m) ? m.GetString() : null,
                RawContentJson = rawContentJson
            };
        }
        catch (JsonException ex)
        {
            throw new AnthropicApiException(
                response.StatusCode, "parse_error",
                $"Failed to parse Anthropic response JSON: {ex.Message}",
                response.Body, request);
        }
    }
}
