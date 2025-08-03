using System.ComponentModel;
using System.Text.Json.Serialization;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

// Sample plugin taken from https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/?pivots=programming-language-csharp
public class LightsPlugin
{
    private readonly List<LightModel> lights = new()
    {
        new LightModel { Id = 1, Name = "TV Light Strip", IsOn = false, Brightness = 40, ColorHex = "FFD8B1" },
        new LightModel { Id = 2, Name = "Desktop Lamp", IsOn = false, Brightness = 50, ColorHex = "FFD8B1" },
        new LightModel { Id = 3, Name = "Shelf Lights", IsOn = true, Brightness = 75, ColorHex = "FFD8B1" }
    };

    [LocalFunction("get_lights")]
    [Description("Gets a list of lights and their current state")]
    public Task<List<LightModel>> GetLightsAsync()
    {
        return Task.FromResult(lights);
    }

    [LocalFunction("get_state")]
    [Description("Gets the state of a particular light")]
    public Task<LightModel?> GetStateAsync([Description("The ID of the light")] int id)
    {
        // Get the state of the light with the specified ID
        return Task.FromResult(lights.FirstOrDefault(light => light.Id == id));
    }

    [LocalFunction("change_state")]
    [Description("Changes the state of the light")]
    public Task<LightModel?> ChangeStateAsync([Description("The ID of the light")]int id, [Description("New parameters for the light")]LightModel LightModel)
    {
        var light = lights.FirstOrDefault(light => light.Id == id);

        if (light == null)
        {
            return Task.FromResult<LightModel?>(null);
        }

        // Update the light with the new state
        light.IsOn = LightModel.IsOn;
        light.Brightness = LightModel.Brightness;
        light.ColorHex = LightModel.ColorHex;

        return Task.FromResult<LightModel?>(light);
    }
}

public class LightModel
{
   [JsonPropertyName("id")]
   public int Id { get; set; }

   [JsonPropertyName("name")]
   public required string Name { get; set; }

   [JsonPropertyName("is_on")]
   public bool? IsOn { get; set; }

   [JsonPropertyName("brightness")]
   public byte? Brightness { get; set; }

   [JsonPropertyName("colorHex")]
   public string? ColorHex { get; set; }
}