using HADotNet.Core;
using HADotNet.Core.Clients;
using Microsoft.Extensions.Logging.Abstractions;

namespace RSBotWorks.Tools;

public class HomeAssistantTool
{
    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }
    public string HomeAssistantUrl { get; private init; }
    public string HomeAssistantToken { get; private init; }

    public HomeAssistantTool(
        IHttpClientFactory httpClientFactory,
        string homeAssistantUrl,
        string homeAssistantToken,
        ILogger<HomeAssistantTool>? logger)
    {
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        HomeAssistantUrl = homeAssistantUrl ?? throw new ArgumentNullException(nameof(homeAssistantUrl));
        HomeAssistantToken = homeAssistantToken ?? throw new ArgumentNullException(nameof(homeAssistantToken));
        Logger = logger ?? NullLogger<HomeAssistantTool>.Instance;
    }

    public async Task<string> GetCupraInfoAsync()
    {
        if (!ClientFactory.IsInitialized)
        {
            Logger.LogInformation("Initializing Home Assistant client.");
            ClientFactory.Initialize(HomeAssistantUrl, HomeAssistantToken);
        }

        var statesClient = ClientFactory.GetClient<StatesClient>();

        var charge = await statesClient.GetState("sensor.cupra_born_state_of_charge");
        var charging = await statesClient.GetState("sensor.cupra_born_charging_state");
        var doorStatus = await statesClient.GetState("binary_sensor.cupra_born_door_lock_status");
        var onlineStatus = await statesClient.GetState("binary_sensor.cupra_born_car_is_online");
        var range = await statesClient.GetState("sensor.cupra_born_range_in_kilometers");

        return $"""
            Aktuell ist der Akku des Cupra Born bei {charge.State}%.
            Lade-Status: {charging.State}. Türen: {(doorStatus.State == "off" ? "verriegelt" : "entriegelt")}.
            Onlinestatus: {onlineStatus.State}. Reichweite beträgt {range.State} km.
            """;
    }
}