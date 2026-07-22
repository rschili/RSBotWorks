using System.Text.Json;
using System.Text.Json.Nodes;

namespace RSBotWorks.SaneAI;

/// <summary>
/// Mutable request composer for the OpenRouter chat completions API
/// (OpenAI-compatible, primary endpoint). Settings are written directly to
/// JSON — no fixed config records that break when OpenRouter ships new features.
///
/// Config setters (SetModel, SetReasoningEffort, etc.) are NOT thread-safe — set them up once.
/// Fork() and message-adding methods ARE thread-safe — fork from templates across threads.
///
/// Usage:
///   var template = new OpenRouterRequestComposer()
///       .SetModel("anthropic/claude-sonnet-4")
///       .SetMaxTokens(16000)
///       .SetReasoningEffort("medium")
///       .SetSystemPrompt("You are helpful")
///       .AddTools(toolDefinitions);
///
///   var conv = template.Fork().AddUserMessage("Hello!");
///   var result = await client.SendAsync(conv, toolExecutor);
/// </summary>
public sealed class OpenRouterRequestComposer
{
    private readonly object _lock = new();
    private JsonObject _root;           // top-level JSON (model, max_tokens, temperature, reasoning, etc.)
    private List<JsonNode> _messages;
    private List<ToolDefinition> _tools;
    private string? _systemPrompt;
    private string _toolChoice = "auto";
    private JsonObject? _webSearchToolJson;
    private JsonObject? _webFetchToolJson;

    public OpenRouterRequestComposer()
    {
        _root = new JsonObject();
        _messages = [];
        _tools = [];
        _root["max_tokens"] = 4096;
    }

    private OpenRouterRequestComposer(JsonObject root, List<JsonNode> messages,
        List<ToolDefinition> tools, string? systemPrompt, string toolChoice,
        JsonObject? webSearchToolJson, JsonObject? webFetchToolJson)
    {
        _root = root;
        _messages = messages;
        _tools = tools;
        _systemPrompt = systemPrompt;
        _toolChoice = toolChoice;
        _webSearchToolJson = webSearchToolJson;
        _webFetchToolJson = webFetchToolJson;
    }

    // ----------------------------------------------------------------
    //  JSON config setters — NOT thread-safe, call during setup only
    // ----------------------------------------------------------------

    public OpenRouterRequestComposer SetModel(string model)
    {
        _root["model"] = model;
        return this;
    }

    public OpenRouterRequestComposer SetMaxTokens(int maxTokens)
    {
        _root["max_tokens"] = maxTokens;
        return this;
    }

    /// <summary>
    /// Set the system prompt. Emitted as the first message with role "system".
    /// </summary>
    public OpenRouterRequestComposer SetSystemPrompt(string systemPrompt)
    {
        _systemPrompt = systemPrompt;
        return this;
    }

    public OpenRouterRequestComposer SetTemperature(decimal temperature)
    {
        _root["temperature"] = temperature;
        return this;
    }

    public OpenRouterRequestComposer SetTopK(int topK)
    {
        _root["top_k"] = topK;
        return this;
    }

    public OpenRouterRequestComposer SetTopP(decimal topP)
    {
        _root["top_p"] = topP;
        return this;
    }

    /// <summary>
    /// Set the reasoning effort for reasoning models: "minimal", "low", "medium",
    /// "high", "xhigh", "max", or "none". Emitted as { "reasoning": { "effort": ... } }.
    /// </summary>
    public OpenRouterRequestComposer SetReasoningEffort(string effort)
    {
        _root["reasoning"] = new JsonObject { ["effort"] = effort };
        return this;
    }

    /// <summary>Remove the reasoning configuration entirely.</summary>
    public OpenRouterRequestComposer DisableReasoning()
    {
        _root.Remove("reasoning");
        return this;
    }

    /// <summary>
    /// Set any arbitrary top-level JSON property.
    /// The whole point of SaneAI — no waiting for SDK updates.
    /// </summary>
    public OpenRouterRequestComposer Set(string key, JsonNode value)
    {
        _root[key] = value;
        return this;
    }

    /// <summary>Remove a top-level JSON property.</summary>
    public OpenRouterRequestComposer Remove(string key)
    {
        _root.Remove(key);
        return this;
    }

    // ----------------------------------------------------------------
    //  Tools — NOT thread-safe, call during setup only
    // ----------------------------------------------------------------

    /// <summary>Set tool choice: "auto", "none", or "required".</summary>
    public OpenRouterRequestComposer SetToolChoice(string toolChoice)
    {
        _toolChoice = toolChoice;
        return this;
    }

    public OpenRouterRequestComposer AddTools(IEnumerable<ToolDefinition> tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    public OpenRouterRequestComposer AddTools(params ToolDefinition[] tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    // ----------------------------------------------------------------
    //  Web search / fetch — NOT thread-safe, call during setup only
    // ----------------------------------------------------------------

    /// <summary>
    /// Enable OpenRouter's generic web search server tool
    /// ({ "type": "openrouter:web_search" }). The server runs the search;
    /// no local tool executor is needed.
    /// </summary>
    public OpenRouterRequestComposer EnableWebSearch(int maxResults = 5,
        string? city = null, string? country = null, string? region = null, string? timezone = null)
    {
        var parameters = new JsonObject { ["max_results"] = maxResults };
        if (city != null || country != null || region != null || timezone != null)
        {
            var loc = new JsonObject { ["type"] = "approximate" };
            if (city != null) loc["city"] = city;
            if (country != null) loc["country"] = country;
            if (region != null) loc["region"] = region;
            if (timezone != null) loc["timezone"] = timezone;
            parameters["user_location"] = loc;
        }
        _webSearchToolJson = new JsonObject
        {
            ["type"] = "openrouter:web_search",
            ["parameters"] = parameters
        };
        return this;
    }

    public OpenRouterRequestComposer DisableWebSearch()
    {
        _webSearchToolJson = null;
        return this;
    }

    /// <summary>
    /// Enable OpenRouter's generic web fetch server tool
    /// ({ "type": "openrouter:web_fetch" }) for pulling full page/PDF content.
    /// </summary>
    public OpenRouterRequestComposer EnableWebFetch(int maxUses = 10)
    {
        _webFetchToolJson = new JsonObject
        {
            ["type"] = "openrouter:web_fetch",
            ["parameters"] = new JsonObject { ["max_uses"] = maxUses }
        };
        return this;
    }

    public OpenRouterRequestComposer DisableWebFetch()
    {
        _webFetchToolJson = null;
        return this;
    }

    // ----------------------------------------------------------------
    //  Messages — thread-safe (protected by _lock)
    // ----------------------------------------------------------------

    /// <summary>Add a simple text message from the user.</summary>
    public OpenRouterRequestComposer AddUserMessage(string text)
    {
        var msg = new JsonObject { ["role"] = "user", ["content"] = text };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>Add a multi-block user message (text + images).</summary>
    public OpenRouterRequestComposer AddUserMessage(params OpenRouterMessageBlock[] blocks)
    {
        var content = new JsonArray();
        foreach (var block in blocks)
            content.Add(block.ToJsonNode());
        var msg = new JsonObject { ["role"] = "user", ["content"] = content };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>Add a simple text message from the assistant.</summary>
    public OpenRouterRequestComposer AddAssistantMessage(string text)
    {
        var msg = new JsonObject { ["role"] = "assistant", ["content"] = text };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>
    /// Add the raw assistant message object from a previous response (including tool_calls).
    /// Used internally by the tool call loop — you probably don't need this directly.
    /// </summary>
    public OpenRouterRequestComposer AddRawAssistantMessage(string rawMessageJson)
    {
        var node = JsonNode.Parse(rawMessageJson)
            ?? throw new ArgumentException("Invalid JSON content", nameof(rawMessageJson));
        lock (_lock) { _messages.Add(node); }
        return this;
    }

    /// <summary>Add a single tool result (role "tool").</summary>
    public OpenRouterRequestComposer AddToolResult(string toolCallId, string result)
    {
        var msg = new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = toolCallId,
            ["content"] = result
        };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>
    /// Add multiple tool results. Unlike Anthropic, OpenAI-style APIs expect
    /// one message per tool result.
    /// </summary>
    public OpenRouterRequestComposer AddToolResults(IEnumerable<(string ToolCallId, string Result)> results)
    {
        var msgs = new List<JsonNode>();
        foreach (var (toolCallId, result) in results)
        {
            msgs.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = toolCallId,
                ["content"] = result
            });
        }
        lock (_lock) { _messages.AddRange(msgs); }
        return this;
    }

    // ----------------------------------------------------------------
    //  Fork — thread-safe, creates a deep copy for forking conversations
    // ----------------------------------------------------------------

    /// <summary>
    /// Create a deep copy of this composer. Use the original as a template
    /// and fork it for each conversation.
    /// Thread-safe — can be called from multiple threads simultaneously.
    /// </summary>
    public OpenRouterRequestComposer Fork()
    {
        lock (_lock)
        {
            var rootClone = _root.DeepClone().AsObject();
            var messagesClone = _messages.Select(m => m.DeepClone()).ToList();
            var toolsClone = new List<ToolDefinition>(_tools);
            var webSearchClone = _webSearchToolJson?.DeepClone().AsObject();
            var webFetchClone = _webFetchToolJson?.DeepClone().AsObject();
            return new OpenRouterRequestComposer(rootClone, messagesClone,
                toolsClone, _systemPrompt, _toolChoice, webSearchClone, webFetchClone);
        }
    }

    // ----------------------------------------------------------------
    //  Build — produces the final JSON request body
    // ----------------------------------------------------------------

    /// <summary>Build the request body as a JSON string.</summary>
    public string BuildJsonString(bool indented = false)
    {
        var root = BuildJsonObject();
        var options = new JsonSerializerOptions { WriteIndented = indented };
        return root.ToJsonString(options);
    }

    /// <summary>Build the request body as a mutable JsonObject.</summary>
    public JsonObject BuildJsonObject()
    {
        var model = _root["model"];
        if (model == null || string.IsNullOrEmpty(model.GetValue<string>()))
            throw new InvalidOperationException("Model must be set before building the request.");

        List<JsonNode> messagesCopy;
        lock (_lock)
        {
            if (_messages.Count == 0)
                throw new InvalidOperationException("At least one message is required.");
            messagesCopy = _messages.Select(m => m.DeepClone()).ToList();
        }

        // Deep clone the config root — keeps the composer reusable
        var root = _root.DeepClone().AsObject();

        // Messages — prepend the system message if set
        var messages = new JsonArray();
        if (_systemPrompt != null)
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = _systemPrompt });
        foreach (var msg in messagesCopy)
            messages.Add(msg);
        root["messages"] = messages;

        // Tools (user-defined function tools + web search + web fetch)
        var allTools = new JsonArray();
        foreach (var tool in _tools)
        {
            allTools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.InputSchema.DeepClone()
                }
            });
        }

        if (_webSearchToolJson != null)
            allTools.Add(_webSearchToolJson.DeepClone());
        if (_webFetchToolJson != null)
            allTools.Add(_webFetchToolJson.DeepClone());

        if (allTools.Count > 0)
        {
            root["tools"] = allTools;
            root["tool_choice"] = _toolChoice;
        }

        return root;
    }
}

// --- Message building blocks (OpenAI-compatible content parts) ---

/// <summary>Building block for multi-content OpenRouter messages (text + images).</summary>
public abstract record OpenRouterMessageBlock
{
    public abstract JsonNode ToJsonNode();

    public static OpenRouterMessageBlock FromText(string text) => new OpenRouterTextBlock(text);
    public static OpenRouterMessageBlock FromImageUrl(string url) => new OpenRouterImageUrlBlock(url);
    public static OpenRouterMessageBlock FromImage(string mimeType, byte[] data)
        => new OpenRouterImageUrlBlock($"data:{mimeType};base64,{Convert.ToBase64String(data)}");
    public static OpenRouterMessageBlock FromImage(string mimeType, string base64Data)
        => new OpenRouterImageUrlBlock($"data:{mimeType};base64,{base64Data}");
}

public record OpenRouterTextBlock(string Text) : OpenRouterMessageBlock
{
    public override JsonNode ToJsonNode() => new JsonObject
    {
        ["type"] = "text",
        ["text"] = Text
    };
}

public record OpenRouterImageUrlBlock(string Url) : OpenRouterMessageBlock
{
    public override JsonNode ToJsonNode() => new JsonObject
    {
        ["type"] = "image_url",
        ["image_url"] = new JsonObject { ["url"] = Url }
    };
}
