using System;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml;
using HADotNet.Core;
using HADotNet.Core.Clients;

namespace RSHome.Services;

public interface IToolService
{
    Task<string> GetCurrentWeatherAsync(string location);
    Task<string> GetWeatherForecastAsync(string location);
    Task<string> GetHeiseHeadlinesAsync(int count = 5);
    Task<string> GetPostillonHeadlinesAsync(int count = 5);
    Task<string> GetCupraInfoAsync();
}

public class ToolService : IToolService
{
    public IConfigService Config { get; private init; }
    public ILogger Logger { get; private init; }
    public IHttpClientFactory HttpClientFactory { get; private init; }

    public ToolService(IConfigService config, ILogger<ToolService> logger, IHttpClientFactory httpClientFactory)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<string> GetCurrentWeatherAsync(string location)
    {
        if (string.IsNullOrWhiteSpace(location) || location.Length > 100)
            throw new ArgumentException("Location cannot be null or empty and must not exceed 100 characters.", nameof(location));

        Logger.LogInformation("Fetching current weather for location: {Location}", location);

        using var httpClient = HttpClientFactory.CreateClient();
        string url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(location)}&appid={Config.OpenWeatherMapApiKey}&units=metric&lang=de";
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

            return $"Aktuelles Wetter in {cityName}, {country}: {description}, {temperature}°C (gefühlt {feelsLike}°C), Luftfeuchtigkeit: {humidity}%, Wind: {windSpeed} m/s, Sonnenaufgang: {sunrise:HH:mm}, Sonnenuntergang: {sunset:HH:mm}";
        }
        catch (JsonException)
        {
            throw ThrowWeatherApiException(responseBody);
        }
    }

    public async Task<string> GetWeatherForecastAsync(string location)
    {
        if (string.IsNullOrWhiteSpace(location) || location.Length > 100)
            throw new ArgumentException("Location cannot be null or empty and must not exceed 100 characters.", nameof(location));

        Logger.LogInformation("Fetching weather forecast for location: {Location}", location);

        using var httpClient = HttpClientFactory.CreateClient();
        string url = $"https://api.openweathermap.org/data/2.5/forecast?q={Uri.EscapeDataString(location)}&appid={Config.OpenWeatherMapApiKey}&units=metric&lang=de";
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

            return string.Join(Environment.NewLine, forecastLines);
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

    public async Task<string> GetHeiseHeadlinesAsync(int count = 5)
    {
        Logger.LogInformation("Fetching Heise headlines, count: {Count}", count);
        const string feedUrl = "https://www.heise.de/rss/heise-atom.xml";

        using var httpClient = HttpClientFactory.CreateClient();
        using var stream = await httpClient.GetStreamAsync(feedUrl);

        using XmlReader reader = XmlReader.Create(stream);
        SyndicationFeed feed = SyndicationFeed.Load(reader);

        var summaries = feed.Items.Take(count).Select(item => item.Summary.Text);
        return string.Join(Environment.NewLine, summaries);
    }

    public async Task<string> GetPostillonHeadlinesAsync(int count = 5)
    {
        Logger.LogInformation("Fetching Postillon headlines, count: {Count}", count);
        const string feedUrl = "https://follow.it/der-postillon-abo/rss";

        using var httpClient = HttpClientFactory.CreateClient();
        using var stream = await httpClient.GetStreamAsync(feedUrl);
        using XmlReader reader = XmlReader.Create(stream);
        List<string> titles = new();
        while (reader.Read()) // rss feed uses version 0.91 which is not supported by SyndicationFeed.Load, so we just fetch all item/title elements manually
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "item")
            {
                // Innerhalb des <item>-Elements nach dem <title>-Element suchen
                while (reader.Read())
                {
                    // Wenn das Ende des <item>-Elements erreicht ist, Schleife verlassen
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "item")
                        break;

                    // Wenn ein <title>-Element gefunden wird, dessen Inhalt lesen
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "title")
                    {
                        titles.Add(System.Net.WebUtility.HtmlDecode(reader.ReadElementContentAsString()));
                    }
                }
            }
        }

        var summaries = titles.Where(PostillonFilter).Take(count);
        return string.Join(Environment.NewLine, summaries);
    }

    private static readonly string[] PostillonBlacklist = ["Newsticker", "des Tages", "der Woche", "Sonntagsfrage"];
    public bool PostillonFilter(string title)
    {
        foreach (var word in PostillonBlacklist)
        {
            if (title.Contains(word, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public async Task<string> GetCupraInfoAsync()
    {
        if (!ClientFactory.IsInitialized)
        {
            Logger.LogInformation("Initializing Home Assistant client.");
            ClientFactory.Initialize(Config.HomeAssistantUrl, Config.HomeAssistantToken);
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
