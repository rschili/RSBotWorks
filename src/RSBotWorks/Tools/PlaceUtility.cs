using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

public class PlaceUtility
{
    public IHttpClientFactory HttpClientFactory { get; private init; }

    public PlaceUtility(IHttpClientFactory httpClientFactory)
    {
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// Attempt to supplement the city name with additional information based on the coordinates.
    /// </summary>
    /// <param name="cityName"></param>
    /// <param name="latitude"></param>
    /// <param name="longitude"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public async Task<(bool Success, string SupplementedCityName)> TrySupplement(string cityName, double latitude, double longitude)
    {
        using var httpClient = HttpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "RSBotWorks/1.0 github.com/rschili chat bot");

        var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latitude}&lon={longitude}&addressdetails=1&zoom=18";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<NominatimResponse>(responseBody);
        var address = result?.Address;
        if (address == null)
        {
            return (false, cityName);
        }



        if (address.Village == null || address.Postcode == null || !string.Equals(address.CountryCode, "DE", StringComparison.OrdinalIgnoreCase))
        {
            return (false, cityName);
        }

        // If the village is not null, we can use it as the supplemented city name
        return (true, $"{address.Postcode} {address.Village} ({address.State})");
    }
}

public class NominatimResponse
{
    [JsonPropertyName("address")]
    public required Address Address { get; set; }
}

public class Address
{
    [JsonPropertyName("village")]
    public string? Village { get; set; }

    [JsonPropertyName("country")]
    public required string Country { get; set; }

    [JsonPropertyName("country_code")]
    public required string CountryCode { get; set; }

    [JsonPropertyName("state")]
    public required string State { get; set; }

    [JsonPropertyName("postcode")]
    public string? Postcode { get; set; }
}