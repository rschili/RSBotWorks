using System.Diagnostics;
using System.Text.Json;

namespace RSBotWorks.SaneAI;

public static class OpenRouterModelNames
{
    public const string Sonnet5 = "anthropic/claude-sonnet-5";
    public const string OpusLatest = "~anthropic/claude-opus-latest";
    public const string GPTChatLatest = "openai/gpt-chat-latest";
    public const string GLM5_2 = "z-ai/glm-5.2";
    public const string PresetWernstrom = "@preset/wernstrom";
    public const string PresetStoll = "@preset/stoll";
}

/// <summary>
/// OpenRouter chat completions client (OpenAI-compatible primary endpoint).
/// Composes raw HTTP requests, sends them, parses responses, and handles
/// tool call loops implicitly.
///
/// The client is stateless and thread-safe. Conversation state lives in
/// the mutable OpenRouterRequestComposer (fork it per conversation).
///
/// Errors throw <see cref="OpenRouterApiException"/> — no IsSuccess checks needed.
/// Tool calls are handled automatically when a toolExecutor is provided.
/// OpenRouter's built-in web search/fetch server tools are handled entirely
/// server-side and never surface as local tool calls.
/// </summary>
public class OpenRouterClient
{
    public const string DefaultApiUrl = "https://openrouter.ai/api/v1/chat/completions";
    public const int DefaultMaxToolRounds = 4;

    /// <summary>Header redactions for curl generation — shows $OPENROUTER_API_KEY instead of the token.</summary>
    internal static readonly Dictionary<string, string> CurlHeaderReplacements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["authorization"] = "Bearer $OPENROUTER_API_KEY"
    };

    private readonly string _apiKey;
    private readonly IHttpExecutor _httpExecutor;
    private readonly string _apiUrl;
    private readonly string? _appTitle;
    private readonly string? _appUrl;

    /// <param name="apiKey">OpenRouter API key.</param>
    /// <param name="httpExecutor">HTTP transport abstraction.</param>
    /// <param name="apiUrl">Override the endpoint URL (defaults to the OpenRouter chat completions endpoint).</param>
    /// <param name="appUrl">Optional HTTP-Referer for OpenRouter app ranking/attribution.</param>
    /// <param name="appTitle">Optional X-Title for OpenRouter app ranking/attribution.</param>
    public OpenRouterClient(string apiKey, IHttpExecutor httpExecutor,
        string? apiUrl = null, string? appUrl = null, string? appTitle = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpExecutor = httpExecutor ?? throw new ArgumentNullException(nameof(httpExecutor));
        _apiUrl = apiUrl ?? DefaultApiUrl;
        _appUrl = appUrl;
        _appTitle = appTitle;
    }

    /// <summary>
    /// Send a request with implicit tool call handling.
    ///
    /// If the model requests tool calls and a <paramref name="toolExecutor"/> is provided,
    /// tools are executed automatically and the conversation continues until the model
    /// is done (or <paramref name="maxToolRounds"/> is hit).
    ///
    /// Token usage is aggregated across all rounds.
    /// Throws <see cref="OpenRouterApiException"/> on any API error.
    /// </summary>
    /// <param name="composer">The request composer. Gets forked internally — your original is untouched.</param>
    /// <param name="toolExecutor">Optional callback to execute tool calls. Receives a ToolCall, returns the result string.</param>
    /// <param name="maxToolRounds">Max number of tool call round-trips before giving up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ChatResult> SendAsync(
        OpenRouterRequestComposer composer,
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
                throw OpenRouterApiException.FromResponse(request, response);

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
            working.AddRawAssistantMessage(result.RawContentJson!);

            var toolResults = new List<(string ToolCallId, string Result)>();
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

    private RawHttpRequest CreateRequest(string jsonBody)
    {
        var headers = new Dictionary<string, string>
        {
            ["authorization"] = $"Bearer {_apiKey}",
            ["content-type"] = "application/json"
        };
        if (_appUrl != null)
            headers["HTTP-Referer"] = _appUrl;
        if (_appTitle != null)
            headers["X-Title"] = _appTitle;

        return new RawHttpRequest
        {
            Method = "POST",
            Url = _apiUrl,
            Headers = headers,
            Body = jsonBody
        };
    }

    private static ChatResult ParseResponse(RawHttpRequest request, RawHttpResponse response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            var root = doc.RootElement;

            string? textContent = null;
            string? rawMessageJson = null;
            string? stopReason = null;
            List<ToolCall>? toolCalls = null;

            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];

                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    stopReason = fr.GetString();

                if (choice.TryGetProperty("message", out var message))
                {
                    rawMessageJson = message.GetRawText();

                    if (message.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString();
                        if (!string.IsNullOrEmpty(text))
                            textContent = text;
                    }

                    if (message.TryGetProperty("tool_calls", out var toolCallsArray)
                        && toolCallsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in toolCallsArray.EnumerateArray())
                        {
                            if (!item.TryGetProperty("function", out var func))
                                continue;
                            toolCalls ??= [];
                            toolCalls.Add(new ToolCall
                            {
                                Id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                                Name = func.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
                                ArgumentsJson = func.TryGetProperty("arguments", out var argsEl)
                                    ? (argsEl.ValueKind == JsonValueKind.String ? argsEl.GetString() ?? "{}" : argsEl.GetRawText())
                                    : "{}"
                            });
                        }
                    }
                }
            }

            // Token usage
            TokenUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageElement)
                && usageElement.ValueKind == JsonValueKind.Object)
            {
                int? cachedTokens = null;
                int? cacheWriteTokens = null;
                if (usageElement.TryGetProperty("prompt_tokens_details", out var ptd)
                    && ptd.ValueKind == JsonValueKind.Object)
                {
                    if (ptd.TryGetProperty("cached_tokens", out var ct) && ct.ValueKind == JsonValueKind.Number)
                        cachedTokens = ct.GetInt32();
                    if (ptd.TryGetProperty("cache_write_tokens", out var cw) && cw.ValueKind == JsonValueKind.Number)
                        cacheWriteTokens = cw.GetInt32();
                }

                usage = new TokenUsage
                {
                    InputTokens = usageElement.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                    OutputTokens = usageElement.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : 0,
                    CacheReadInputTokens = cachedTokens,
                    CacheCreationInputTokens = cacheWriteTokens,
                };
            }

            return new ChatResult
            {
                Request = request,
                Response = response,
                TextContent = textContent,
                Usage = usage,
                ToolCalls = toolCalls?.AsReadOnly(),
                StopReason = stopReason,
                ModelId = root.TryGetProperty("model", out var m) ? m.GetString() : null,
                RawContentJson = rawMessageJson
            };
        }
        catch (JsonException ex)
        {
            throw new OpenRouterApiException(
                response.StatusCode, null,
                $"Failed to parse OpenRouter response JSON: {ex.Message}",
                response.Body, request);
        }
    }
}
