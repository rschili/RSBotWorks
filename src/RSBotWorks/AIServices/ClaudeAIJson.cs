using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RSBotWorks;
using RSBotWorks.Tools;

// Base class for polymorphic deserialization
[JsonConverter(typeof(ClaudeResponseConverter))]
public abstract class ClaudeResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = default!;
}

// Custom converter
public class ClaudeResponseConverter : JsonConverter<ClaudeResponse>
{
    public override ClaudeResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement))
            throw new JsonException("Missing 'type' property");

        var type = typeElement.GetString();
        
        return type switch
        {
            "error" => root.Deserialize<ClaudeErrorResponse>(options),
            "message" => root.Deserialize<ClaudeMessageResponse>(options),
            _ => throw new JsonException($"Unknown type: {type}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ClaudeResponse value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

// Error response
public class ClaudeErrorResponse : ClaudeResponse
{

    [JsonPropertyName("error")]
    public required ClaudeErrorDetail Error { get; set; } = default!;
}

public class ClaudeErrorDetail
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = default!;

    [JsonPropertyName("message")]
    public string Message { get; set; } = default!;
}

// Message response
public class ClaudeMessageResponse : ClaudeResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("role")]
    public string Role { get; set; } = default!;

    [JsonPropertyName("content")]
    public List<ClaudeContentPart> Content { get; set; } = new();

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; set; }

    [JsonPropertyName("usage")]
    public ClaudeUsage Usage { get; set; } = default!;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClaudeContentType
{
    [JsonStringEnumMemberName("text")]
    Text,

    [JsonStringEnumMemberName("thinking")]
    Thinking,

    [JsonStringEnumMemberName("redacted_thinking")]
    RedactedThinking,

    [JsonStringEnumMemberName("tool_use")]
    ToolUse,

    [JsonStringEnumMemberName("server_tool_use")]
    ServerToolUse,

    [JsonStringEnumMemberName("web_search_tool_result")]
    WebSearchToolResult,

    [JsonStringEnumMemberName("code_execution_tool_result")]
    CodeExecutionToolResult,

    [JsonStringEnumMemberName("mcp_tool_use")]
    McpToolUse,

    [JsonStringEnumMemberName("mcp_tool_result")]
    McpToolResult,

    [JsonStringEnumMemberName("container_upload")]
    ContainerUpload
}

public class ClaudeContentPart
{
    [JsonPropertyName("type")]
    public required ClaudeContentType Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class ClaudeUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClaudeRole
{
    [JsonStringEnumMemberName("user")]
    User,

    [JsonStringEnumMemberName("assistant")]
    Assistant
}

public class ClaudeMessageRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = default!;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClaudeSystemContent>? System { get; set; }

    [JsonPropertyName("messages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClaudeRequestMessage>? Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Temperature { get; set; }

    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; set; } = null;

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClaudeTool>? Tools { get; set; } = null;
}

public class ClaudeSystemContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = default!;
}

public class ClaudeRequestMessage
{
    [JsonPropertyName("role")]
    public required ClaudeRole Role { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Content { get; set; }

    [JsonIgnore]
    public ClaudeMessageContentSetter ContentSetter => new ClaudeMessageContentSetter(this);

    public static ClaudeRequestMessage Create(ClaudeRole role, string text)
    {
        var message = new ClaudeRequestMessage { Role = role };
        message.ContentSetter.Set(text);
        return message;
    }

    public static ClaudeRequestMessage Create(ClaudeRole role, Action<ClaudeMessageContentBuilder> contentBuilder)
    {
        var message = new ClaudeRequestMessage { Role = role };
        var builder = message.ContentSetter.CreateBuilder();
        contentBuilder(builder);
        return message;
    }
}

public class ClaudeMessageContentSetter
{
    private readonly ClaudeRequestMessage _message;

    public ClaudeMessageContentSetter(ClaudeRequestMessage message)
    {
        _message = message;
    }

    public void Set(string text)
    {
        _message.Content = JsonValue.Create(text);
    }

    public ClaudeMessageContentBuilder CreateBuilder()
    {
        var array = new JsonArray();
        _message.Content = array;
        return new ClaudeMessageContentBuilder(array);
    }
}

public class ClaudeMessageContentBuilder
{
    private readonly JsonArray _array;

    public ClaudeMessageContentBuilder(JsonArray array)
    {
        _array = array;
    }

    public ClaudeMessageContentBuilder AddText(string text)
    {
        var obj = new JsonObject
        {
            ["type"] = "text",
            ["text"] = text
        };
        _array.Add(obj);
        return this;
    }

    public ClaudeMessageContentBuilder AddBase64Image(string mimeType, byte[] imageBytes)
    {
        var base64Data = Convert.ToBase64String(imageBytes);
        return AddBase64Image(mimeType, base64Data);
    }

    public ClaudeMessageContentBuilder AddBase64Image(string mimeType, string base64Data)
    {
        var obj = new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"] = "base64",
                ["media_type"] = mimeType,
                ["data"] = base64Data
            }
        };
        _array.Add(obj);
        return this;
    }
}

public class ClaudeTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    ///  JSON schema for the tool input shape that the model will produce in tool_use output content blocks.
    /// </summary>
    [JsonPropertyName("input_schema")]
    public string InputSchema { get; set; } = default!;
}