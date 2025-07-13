using HADotNet.Core;
using HADotNet.Core.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RSBotWorks.Tools;

public class HomeAssistantToolProvider : ToolProvider
{
    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }
    public string HomeAssistantUrl { get; private init; }
    public string HomeAssistantToken { get; private init; }

    private TimedCache<string> _cupraCache = new(TimeSpan.FromMinutes(10));

    public HomeAssistantToolProvider(
        IHttpClientFactory httpClientFactory,
        string homeAssistantUrl,
        string homeAssistantToken,
        ILogger<HomeAssistantToolProvider>? logger = null)
    {
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        HomeAssistantUrl = homeAssistantUrl ?? throw new ArgumentNullException(nameof(homeAssistantUrl));
        HomeAssistantToken = homeAssistantToken ?? throw new ArgumentNullException(nameof(homeAssistantToken));
        Logger = logger ?? NullLogger<HomeAssistantToolProvider>.Instance;
        ExposeTool(new Tool("car_status", "Get current status of the EV car of the user krael aka noppel (charge, range, doors, etc.)",
            null,
            async parameters => await GetCupraInfoAsync()));
    }

    public async Task<string> GetCupraInfoAsync()
    {
        if (_cupraCache.TryGet(out var cachedValue) && !string.IsNullOrEmpty(cachedValue))
        {
            Logger.LogInformation("Returning cached Cupra info.");
            return cachedValue;
        }

        if (!ClientFactory.IsInitialized)
        {
            Logger.LogInformation("Initializing Home Assistant client.");
            ClientFactory.Initialize(HomeAssistantUrl, HomeAssistantToken);
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