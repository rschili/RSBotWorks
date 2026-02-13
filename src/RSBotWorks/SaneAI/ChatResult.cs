namespace RSBotWorks.SaneAI;

/// <summary>
/// The result of a chat API call. Contains both the raw HTTP request/response 
/// (for debugging, logging, curl generation) and the parsed response fields.
/// 
/// Errors throw <see cref="AnthropicApiException"/> — if you have a ChatResult,
/// the request succeeded. Tool call loops are handled implicitly by the client;
/// aggregate usage and tool call history are included.
/// </summary>
public record ChatResult
{
    /// <summary>The raw HTTP request that was sent (last round, for debugging/curl generation).</summary>
    public required RawHttpRequest Request { get; init; }

    /// <summary>The raw HTTP response (last round, for debugging/logging).</summary>
    public required RawHttpResponse Response { get; init; }

    /// <summary>Extracted text content from the response, if any.</summary>
    public string? TextContent { get; init; }

    /// <summary>Aggregated token usage across all tool call rounds.</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>Tool calls from the last response (only set if the loop hit the max rounds limit).</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>Why the model stopped: "end_turn", "tool_use", "max_tokens", etc.</summary>
    public string? StopReason { get; init; }

    /// <summary>The model that actually served the request.</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// The raw JSON string of the "content" array from the last response.
    /// Mainly used internally for tool call loops.
    /// </summary>
    public string? RawContentJson { get; init; }

    /// <summary>How many tool call rounds were executed by the client.</summary>
    public int ToolRoundsExecuted { get; init; }

    /// <summary>All tool calls that were executed across all rounds (for logging/debugging).</summary>
    public IReadOnlyList<ToolCall>? AllToolCallsExecuted { get; init; }

    public bool HasToolCalls => ToolCalls is { Count: > 0 };
}

/// <summary>Token usage from the API response. Supports aggregation across multiple rounds.</summary>
public record TokenUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int? CacheCreationInputTokens { get; init; }
    public int? CacheReadInputTokens { get; init; }

    /// <summary>Sum two usages together (for aggregating across tool call rounds).</summary>
    public TokenUsage Add(TokenUsage other) => new()
    {
        InputTokens = InputTokens + other.InputTokens,
        OutputTokens = OutputTokens + other.OutputTokens,
        CacheCreationInputTokens = CacheCreationInputTokens.HasValue || other.CacheCreationInputTokens.HasValue
            ? (CacheCreationInputTokens ?? 0) + (other.CacheCreationInputTokens ?? 0)
            : null,
        CacheReadInputTokens = CacheReadInputTokens.HasValue || other.CacheReadInputTokens.HasValue
            ? (CacheReadInputTokens ?? 0) + (other.CacheReadInputTokens ?? 0)
            : null,
    };
}

/// <summary>A tool call requested by the model.</summary>
public record ToolCall
{
    /// <summary>Unique ID for this tool call (needed when sending results back).</summary>
    public required string Id { get; init; }

    /// <summary>Name of the tool to call.</summary>
    public required string Name { get; init; }

    /// <summary>Raw JSON string of the arguments. Parse with JsonDocument when executing.</summary>
    public required string ArgumentsJson { get; init; }
}
