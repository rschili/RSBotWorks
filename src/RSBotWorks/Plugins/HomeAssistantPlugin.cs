using System.ComponentModel;
using HADotNet.Core;
using HADotNet.Core.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public interface IHomeAssistantPluginConfig
{
    string HomeAssistantUrl { get; }
    string HomeAssistantToken { get; }
}

public class HomeAssistantPluginConfig : IHomeAssistantPluginConfig
{
    public required string HomeAssistantUrl { get; set; }
    public required string HomeAssistantToken { get; set; }
}

public class HomeAssistantPlugin
{
    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }
    public IHomeAssistantPluginConfig Config { get; private init; }

    private TimedCache<string> _cupraCache = new(TimeSpan.FromMinutes(10));

    public HomeAssistantPlugin(
        IHttpClientFactory httpClientFactory,
        HomeAssistantPluginConfig config,
        ILogger<HomeAssistantPlugin>? logger = null)
    {
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Logger = logger ?? NullLogger<HomeAssistantPlugin>.Instance;
    }

    [LocalFunction("car_status")]
    [Description("Get current status of the EV car of the user krael aka noppel (charge, range, doors, etc.)")]
    public async Task<string> GetCarStatusAsync()
    {
        if (_cupraCache.TryGet(out var cachedValue) && !string.IsNullOrEmpty(cachedValue))
        {
            Logger.LogInformation("Returning cached Cupra status.");
            return cachedValue;
        }

        if (!ClientFactory.IsInitialized)
        {
            Logger.LogInformation("Initializing Home Assistant client.");
            ClientFactory.Initialize(Config.HomeAssistantUrl, Config.HomeAssistantToken);
        }

        var statesClient = ClientFactory.GetClient<StatesClient>();

        var charge = await statesClient.GetState("sensor.cupra_born_state_of_charge");
        var charging = (await statesClient.GetState("sensor.cupra_born_charging_state")).State;
        if (string.Equals("notReadyForCharging", charging, StringComparison.OrdinalIgnoreCase))
        {
            charging = "Kein Ladekabel verbunden";
        }
        var doorStatus = await statesClient.GetState("binary_sensor.cupra_born_door_lock_status");
        var onlineStatus = await statesClient.GetState("binary_sensor.cupra_born_car_is_online");
        var range = await statesClient.GetState("sensor.cupra_born_range_in_kilometers");

        var result = $"""
            Akkustand {charge.State}%.
            Reichweite {range.State} km.
            Lade-Status: {charging}.
            Türen: {(doorStatus.State == "off" ? "verriegelt" : "entriegelt")}.
            Onlinestatus: {onlineStatus.State}. 
            """;

        _cupraCache.Set(result);
        return result;
    }
}