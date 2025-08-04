using System.ComponentModel;
using HADotNet.Core;
using HADotNet.Core.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public record HealthData(
    int Steps,
    double WalkingDistance,
    int RestingHeartRate,
    int StandingHours,
    DateTimeOffset LastUpdated
);

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
    [Description("Get current status of the electric car of user krael/noppel")]
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

    [LocalFunction("health_info")]
    [Description("Get today's health info (steps, walked distance, resting heartrate, standing hours) for user krael/noppel")]
    public async Task<string> GetHealthInfoAsync()
    {
        var healthData = await GetRawHealthDataAsync();
        
        var result = $"""
            Schritte: {healthData.Steps:N0}.
            Gelaufene Distanz: {healthData.WalkingDistance:F1} km.
            Ruhepuls: {healthData.RestingHeartRate} bpm.
            Stehstunden: {healthData.StandingHours}/12.
            Letztes Update: {healthData.LastUpdated.ToRelativeToNowLabel()}.
            """;

        return result;
    }

    public async Task<HealthData> GetRawHealthDataAsync()
    {
        const string standingHoursEntity = "hae.homeassistantexport_apple_stand_hour";
        const string restingHeartRateEntity = "hae.homeassistantexport_resting_heart_rate";
        const string stepsEntity = "hae.homeassistantexport_step_count";
        const string distanceEntity = "hae.homeassistantexport_walking_running_distance";

        if (!ClientFactory.IsInitialized)
        {
            Logger.LogInformation("Initializing Home Assistant client.");
            ClientFactory.Initialize(Config.HomeAssistantUrl, Config.HomeAssistantToken);
        }

        var statesClient = ClientFactory.GetClient<StatesClient>();

        var stepsState = await statesClient.GetState(stepsEntity);
        var distanceState = await statesClient.GetState(distanceEntity);
        var restingHeartRateState = await statesClient.GetState(restingHeartRateEntity);
        var standingHoursState = await statesClient.GetState(standingHoursEntity);

        // Find the latest update time from all states
        var latestUpdate = new[] { stepsState.LastUpdated, distanceState.LastUpdated, restingHeartRateState.LastUpdated, standingHoursState.LastUpdated }
            .Max();

        return new HealthData(
            Steps: int.TryParse(stepsState.State, out var steps) ? steps : 0,
            WalkingDistance: double.TryParse(distanceState.State, out var distance) ? distance : 0.0,
            RestingHeartRate: int.TryParse(restingHeartRateState.State, out var heartRate) ? heartRate : 0,
            StandingHours: int.TryParse(standingHoursState.State, out var standingHours) ? standingHours : 0,
            LastUpdated: latestUpdate
        );
    }
}