using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public class ForecastResponse
{
    [JsonPropertyName("list")]
    public required List<Forecast> Forecasts { get; set; }

    [JsonPropertyName("city")]
    public required City City { get; set; }
}

public class City
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("coord")]
    public required Coordinates Coord { get; set; }

    [JsonPropertyName("country")]
    public required string Country { get; set; }
}

public class Forecast
{
    [JsonPropertyName("dt")]
    public required long DateTimeUTC { get; set; }

    [JsonPropertyName("main")]
    public required MainWeather Main { get; set; }

    [JsonPropertyName("weather")]
    public required List<WeatherInfo> Weather { get; set; }
}

public class WeatherResponse
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("weather")]
    public required List<WeatherInfo> Weather { get; set; }

    [JsonPropertyName("main")]
    public required MainWeather Main { get; set; }

    [JsonPropertyName("wind")]
    public required Wind Wind { get; set; }

    [JsonPropertyName("sys")]
    public required Sys Sys { get; set; }

    [JsonPropertyName("coord")]
    public required Coordinates Coord { get; set; }
}

public class Coordinates
{
    [JsonPropertyName("lon")]
    public required double Longitude { get; set; }

    [JsonPropertyName("lat")]
    public required double Latitude { get; set; }
}

public class WeatherInfo
    {
        [JsonPropertyName("description")]
        public required string Description { get; set; }
    }

public class MainWeather
{
    [JsonPropertyName("temp")]
    public required double Temp { get; set; }

    [JsonPropertyName("feels_like")]
    public required double FeelsLike { get; set; }

    [JsonPropertyName("humidity")]
    public required int Humidity { get; set; }
}

public class Wind
{
    [JsonPropertyName("speed")]
    public required double Speed { get; set; }
}

public class Sys
{
    [JsonPropertyName("country")]
    public required string Country { get; set; }

    [JsonPropertyName("sunrise")]
    public required long Sunrise { get; set; }

    [JsonPropertyName("sunset")]
    public required long Sunset { get; set; }
}

public class WeahterPluginConfig
{
    public required string ApiKey { get; set; }
}

public class WeatherPlugin
{
    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }

    private PlaceUtility _placeUtility;

    public string ApiKey { get; private init; }

    public WeatherPlugin(IHttpClientFactory httpClientFactory, ILogger<WeatherPlugin>? logger, WeahterPluginConfig config)
    {
        ApiKey = config?.ApiKey ?? throw new ArgumentNullException(nameof(config.ApiKey), "API Key cannot be null or empty.");
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        Logger = logger ?? NullLogger<WeatherPlugin>.Instance;
        _placeUtility = new PlaceUtility(httpClientFactory);
    }

    [LocalFunction("get_current_weather")]
    [Description("Gets the current weather and sunrise/sunset for a given location")]
    public async Task<string> GetCurrentWeatherAsync(
        [Description("City name or ZIP code. When providing a City name, ISO 3166 country code can be appended e.g. 'Heidelberg,DE' to avoid ambiguity.")]
        string location)
    {
        if (string.IsNullOrWhiteSpace(location) || location.Length > 100)
            throw new ArgumentException("Location cannot be null or empty and must not exceed 100 characters.", nameof(location));

        Logger.LogInformation("Fetching current weather for location: {Location}", location);

        using var httpClient = HttpClientFactory.CreateClient();
        string url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(location)}&appid={ApiKey}&units=metric&lang=de";
        HttpResponseMessage response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync();

        try
        {
            var weather = JsonSerializer.Deserialize<WeatherResponse>(responseBody);

            if (weather == null)
                throw ThrowWeatherApiException(responseBody);

            string cityName = weather.Name;
            string description = weather.Weather?[0]?.Description ?? "";
            double temperature = weather.Main.Temp;
            double feelsLike = weather.Main.FeelsLike;
            int humidity = weather.Main.Humidity;
            double windSpeed = weather.Wind.Speed;
            string country = weather.Sys.Country;
            long sunriseUnix = weather.Sys.Sunrise;
            long sunsetUnix = weather.Sys.Sunset;

            DateTimeOffset sunrise = DateTimeOffset.FromUnixTimeSeconds(sunriseUnix).ToLocalTime();
            DateTimeOffset sunset = DateTimeOffset.FromUnixTimeSeconds(sunsetUnix).ToLocalTime();

            cityName = await SupplementCityNameIfGermanAsync(cityName, weather.Sys.Country, weather.Coord);
            return $"Aktuelles Wetter in {cityName}: {description}, {temperature}°C (gefühlt {feelsLike}°C), Luftfeuchtigkeit: {humidity}%, Wind: {windSpeed} m/s, Sonnenaufgang: {sunrise:HH:mm}, Sonnenuntergang: {sunset:HH:mm}";
        }
        catch (JsonException)
        {
            throw ThrowWeatherApiException(responseBody);
        }
    }

    private async Task<string> SupplementCityNameIfGermanAsync(string cityName, string country, Coordinates coord)
    {
        try
        {
            if (string.Equals(country, "DE", StringComparison.OrdinalIgnoreCase))
            {
                // If the city is in Germany, we can use the PlaceUtility to get more information
                var (success, supplementedCityName) = await _placeUtility.TrySupplement(cityName, coord.Latitude, coord.Longitude);
                if (success && !string.IsNullOrEmpty(supplementedCityName))
                {
                    return supplementedCityName;
                }
            }
            return cityName;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to supplement city name for {CityName}, {Country}", cityName, country);
            // Fallback: append country code to city name if there's an error
            return $"{cityName}, {country}";
        }
    }

    [LocalFunction("weather_forecast")]
    [Description("Get a 5 day weather forecast for a given location.")]
    public async Task<string> GetWeatherForecastAsync(
        [Description("City name or ZIP code. When providing a City name, ISO 3166 country code can be appended e.g. 'Heidelberg,DE' to avoid ambiguity.")]
        string location)
    {
        if (string.IsNullOrWhiteSpace(location) || location.Length > 100)
            throw new ArgumentException("Location cannot be null or empty and must not exceed 100 characters.", nameof(location));

        Logger.LogInformation("Fetching weather forecast for location: {Location}", location);

        using var httpClient = HttpClientFactory.CreateClient();
        string url = $"https://api.openweathermap.org/data/2.5/forecast?q={Uri.EscapeDataString(location)}&appid={ApiKey}&units=metric&lang=de";
        HttpResponseMessage response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync();

        try
        {
            var weather = JsonSerializer.Deserialize<ForecastResponse>(responseBody);

            if (weather == null)
                throw ThrowWeatherApiException(responseBody);

            List<string> forecastLines = new();
            int count = weather.Forecasts.Count;
            for (int i = 0; i < count; i++)
            {
                if (i == 0 || i == count - 1 || i % 3 == 0)
                {
                    var forecast = weather.Forecasts[i];
                    DateTimeOffset dateTime = DateTimeOffset.FromUnixTimeSeconds(forecast.DateTimeUTC).ToLocalTime();
                    string description = forecast.Weather?[0]?.Description ?? "";
                    double temperature = forecast.Main.Temp;

                    string dateTimeStr = dateTime.ToString("dddd d.M.yyyy HH:mm 'Uhr'");
                    forecastLines.Add($"{dateTimeStr}: {description}, {temperature}°C");
                }
            }

            var list = string.Join(Environment.NewLine, forecastLines);
            var cityName = weather.City.Name;
            var country = weather.City.Country;
            var coordinates = weather.City.Coord;
            cityName = await SupplementCityNameIfGermanAsync(cityName, country, coordinates);
            return $"5-Tage Wettervorhersage für {cityName}:\n{list}";
        }
        catch (JsonException)
        {
            throw ThrowWeatherApiException(responseBody);
        }
    }

    private Exception ThrowWeatherApiException(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string code = root.TryGetProperty("cod", out var codElement) ? codElement.ToString() : "unknown";
            string message = root.TryGetProperty("message", out var msgElement) ? msgElement.GetString() ?? "No message" : "No message";
            Logger.LogError("Weather API error: {Code} - {Message}", code, message);
            return new Exception($"Weather API error: {message}");
        }
        catch
        {
            Logger.LogError("Failed to parse error response from Weather API: {ResponseBody}", responseBody);
            return new Exception("Unknown error from Weather API");
        }
    }
}