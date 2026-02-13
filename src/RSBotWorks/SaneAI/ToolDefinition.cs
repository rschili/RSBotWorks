using System.Text.Json.Nodes;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.SaneAI;

/// <summary>
/// Defines a tool that the model can call.
/// Carries the name, description, and JSON Schema for the input.
/// </summary>
public record ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>JSON Schema object describing the input parameters.</summary>
    public required JsonNode InputSchema { get; init; }

    /// <summary>
    /// Convert from the existing LocalFunction abstraction so you can
    /// reuse your plugins without rewriting them.
    /// </summary>
    public static ToolDefinition FromLocalFunction(LocalFunction function)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in function.Parameters)
        {
            properties[param.Name] = new JsonObject
            {
                ["type"] = param.Type switch
                {
                    LocalFunctionParameterType.String => "string",
                    LocalFunctionParameterType.Boolean => "boolean",
                    LocalFunctionParameterType.Number => "number",
                    _ => "string"
                },
                ["description"] = param.Description
            };
            if (param.IsRequired)
                required.Add(JsonValue.Create(param.Name));
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };

        return new ToolDefinition
        {
            Name = function.Name,
            Description = function.Description,
            InputSchema = schema
        };
    }
}
