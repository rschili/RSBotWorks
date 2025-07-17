using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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

public class ClaudeContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = default!;

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
