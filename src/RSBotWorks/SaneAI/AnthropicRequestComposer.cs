using System.Text.Json;
using System.Text.Json.Nodes;

namespace RSBotWorks.SaneAI;

/// <summary>
/// Mutable request composer for the Anthropic Messages API.
/// Settings are written directly to JSON — no fixed config records that
/// break when Anthropic ships new features.
///
/// Config setters (SetModel, SetEffort, etc.) are NOT thread-safe — set them up once.
/// Fork() and message-adding methods ARE thread-safe — fork from templates across threads.
///
/// Usage:
///   var template = new AnthropicRequestComposer()
///       .SetModel("claude-opus-4-6")
///       .SetMaxTokens(16000)
///       .SetThinkingType("adaptive")
///       .SetEffort("medium")
///       .SetSystemPrompt("You are helpful")
///       .AddTools(toolDefinitions);
///
///   var conv = template.Fork().AddUserMessage("Hello!");
///   var result = await client.SendAsync(conv, toolExecutor);
/// </summary>
public sealed class AnthropicRequestComposer
{
    private readonly object _lock = new();
    private JsonObject _root;           // top-level JSON (model, max_tokens, thinking, output_config, etc.)
    private List<JsonNode> _messages;
    private List<ToolDefinition> _tools;
    private string _toolChoiceType = "auto";
    private JsonObject? _webSearchToolJson;

    public AnthropicRequestComposer()
    {
        _root = new JsonObject();
        _messages = [];
        _tools = [];
        _root["max_tokens"] = 4096;
    }

    private AnthropicRequestComposer(JsonObject root, List<JsonNode> messages,
        List<ToolDefinition> tools, string toolChoiceType, JsonObject? webSearchToolJson)
    {
        _root = root;
        _messages = messages;
        _tools = tools;
        _toolChoiceType = toolChoiceType;
        _webSearchToolJson = webSearchToolJson;
    }

    // ----------------------------------------------------------------
    //  JSON config setters — NOT thread-safe, call during setup only
    // ----------------------------------------------------------------

    public AnthropicRequestComposer SetModel(string model)
    {
        _root["model"] = model;
        return this;
    }

    public AnthropicRequestComposer SetMaxTokens(int maxTokens)
    {
        _root["max_tokens"] = maxTokens;
        return this;
    }

    public AnthropicRequestComposer SetSystemPrompt(string systemPrompt)
    {
        _root["system"] = systemPrompt;
        return this;
    }

    public AnthropicRequestComposer SetTemperature(decimal temperature)
    {
        _root["temperature"] = temperature;
        return this;
    }

    public AnthropicRequestComposer SetTopK(int topK)
    {
        _root["top_k"] = topK;
        return this;
    }

    public AnthropicRequestComposer SetTopP(decimal topP)
    {
        _root["top_p"] = topP;
        return this;
    }

    /// <summary>
    /// Set the thinking mode. "adaptive", "enabled", "disabled".
    /// For "enabled", pass budgetTokens too.
    /// </summary>
    public AnthropicRequestComposer SetThinkingType(string type, int? budgetTokens = null)
    {
        var thinking = new JsonObject { ["type"] = type };
        if (budgetTokens.HasValue)
            thinking["budget_tokens"] = budgetTokens.Value;
        _root["thinking"] = thinking;
        return this;
    }

    /// <summary>
    /// Set the output effort level: "low", "medium", "high" (default), "max".
    /// </summary>
    public AnthropicRequestComposer SetEffort(string effort)
    {
        _root["output_config"] = new JsonObject { ["effort"] = effort };
        return this;
    }

    /// <summary>
    /// Set any arbitrary top-level JSON property.
    /// The whole point of SaneAI — no waiting for SDK updates.
    /// </summary>
    public AnthropicRequestComposer Set(string key, JsonNode value)
    {
        _root[key] = value;
        return this;
    }

    /// <summary>Remove a top-level JSON property.</summary>
    public AnthropicRequestComposer Remove(string key)
    {
        _root.Remove(key);
        return this;
    }

    // ----------------------------------------------------------------
    //  Tools — NOT thread-safe, call during setup only
    // ----------------------------------------------------------------

    public AnthropicRequestComposer SetToolChoice(string toolChoice)
    {
        _toolChoiceType = toolChoice;
        return this;
    }

    public AnthropicRequestComposer AddTools(IEnumerable<ToolDefinition> tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    public AnthropicRequestComposer AddTools(params ToolDefinition[] tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    // ----------------------------------------------------------------
    //  Web search — NOT thread-safe, call during setup only
    // ----------------------------------------------------------------

    public AnthropicRequestComposer EnableWebSearch(int maxUses = 5,
        string? city = null, string? country = null, string? timezone = null)
    {
        var tool = new JsonObject
        {
            ["type"] = "web_search_20250305",
            ["name"] = "web_search",
            ["max_uses"] = maxUses
        };
        if (city != null || country != null)
        {
            var loc = new JsonObject { ["type"] = "approximate" };
            if (city != null) loc["city"] = city;
            if (country != null) loc["country"] = country;
            if (timezone != null) loc["timezone"] = timezone;
            tool["user_location"] = loc;
        }
        _webSearchToolJson = tool;
        return this;
    }

    public AnthropicRequestComposer DisableWebSearch()
    {
        _webSearchToolJson = null;
        return this;
    }

    // ----------------------------------------------------------------
    //  Messages — thread-safe (protected by _lock)
    // ----------------------------------------------------------------

    /// <summary>Add a simple text message from the user.</summary>
    public AnthropicRequestComposer AddUserMessage(string text)
    {
        var msg = new JsonObject { ["role"] = "user", ["content"] = text };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>Add a multi-block user message (text + images).</summary>
    public AnthropicRequestComposer AddUserMessage(params MessageBlock[] blocks)
    {
        var content = new JsonArray();
        foreach (var block in blocks)
            content.Add(block.ToJsonNode());
        var msg = new JsonObject { ["role"] = "user", ["content"] = content };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>Add a simple text message from the assistant.</summary>
    public AnthropicRequestComposer AddAssistantMessage(string text)
    {
        var msg = new JsonObject { ["role"] = "assistant", ["content"] = text };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>
    /// Add the raw assistant content from a previous response.
    /// Used internally by the tool call loop — you probably don't need this directly.
    /// </summary>
    public AnthropicRequestComposer AddRawAssistantContent(string rawContentJson)
    {
        var content = JsonNode.Parse(rawContentJson)
            ?? throw new ArgumentException("Invalid JSON content", nameof(rawContentJson));
        var msg = new JsonObject { ["role"] = "assistant", ["content"] = content };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>Add a single tool result.</summary>
    public AnthropicRequestComposer AddToolResult(string toolUseId, string result)
    {
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = toolUseId,
                ["content"] = result
            }
        };
        var msg = new JsonObject { ["role"] = "user", ["content"] = content };
        lock (_lock) { _messages.Add(msg); }
        return this;
    }

    /// <summary>Add multiple tool results in a single user message (required by the API).</summary>
    public AnthropicRequestComposer AddToolResults(IEnumerable<(string ToolUseId, string Result)> results)
    {
        var content = new JsonArray();
        foreach (var (toolUseId, result) in results)
        {
            content.Add(new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = toolUseId,
                ["content"] = result
            });
        }
        var msg = new JsonObject { ["role"] = "user", ["content"] = content };
        lock (_lock) { _messages.Add(msg); }
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
    public AnthropicRequestComposer Fork()
    {
        lock (_lock)
        {
            var rootClone = _root.DeepClone().AsObject();
            var messagesClone = _messages.Select(m => m.DeepClone()).ToList();
            var toolsClone = new List<ToolDefinition>(_tools);
            var webSearchClone = _webSearchToolJson?.DeepClone().AsObject();
            return new AnthropicRequestComposer(rootClone, messagesClone,
                toolsClone, _toolChoiceType, webSearchClone);
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

        // Messages
        var messages = new JsonArray();
        foreach (var msg in messagesCopy)
            messages.Add(msg);
        root["messages"] = messages;

        // Tools (user-defined + web search)
        var allTools = new JsonArray();
        foreach (var tool in _tools)
        {
            allTools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = tool.InputSchema.DeepClone()
            });
        }

        if (_webSearchToolJson != null)
            allTools.Add(_webSearchToolJson.DeepClone());

        if (allTools.Count > 0)
        {
            root["tools"] = allTools;
            root["tool_choice"] = new JsonObject { ["type"] = _toolChoiceType };
        }

        return root;
    }
}

// --- Message building blocks ---

/// <summary>Building block for multi-content messages (text + images).</summary>
public abstract record MessageBlock
{
    public abstract JsonNode ToJsonNode();

    public static MessageBlock FromText(string text) => new TextMessageBlock(text);
    public static MessageBlock FromImage(string mimeType, byte[] data) => new ImageMessageBlock(mimeType, data);
    public static MessageBlock FromImage(string mimeType, string base64Data) => new Base64ImageMessageBlock(mimeType, base64Data);
}

public record TextMessageBlock(string Text) : MessageBlock
{
    public override JsonNode ToJsonNode() => new JsonObject
    {
        ["type"] = "text",
        ["text"] = Text
    };
}

public record ImageMessageBlock(string MimeType, byte[] Data) : MessageBlock
{
    public override JsonNode ToJsonNode() => new JsonObject
    {
        ["type"] = "image",
        ["source"] = new JsonObject
        {
            ["type"] = "base64",
            ["media_type"] = MimeType,
            ["data"] = Convert.ToBase64String(Data)
        }
    };
}

public record Base64ImageMessageBlock(string MimeType, string Base64Data) : MessageBlock
{
    public override JsonNode ToJsonNode() => new JsonObject
    {
        ["type"] = "image",
        ["source"] = new JsonObject
        {
            ["type"] = "base64",
            ["media_type"] = MimeType,
            ["data"] = Base64Data
        }
    };
}
