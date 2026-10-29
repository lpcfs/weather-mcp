using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weather.Models;

namespace Weather.Sdk;

internal sealed class WeatherClient(HttpClient http) : IWeatherClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<Location>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default) =>
        await GetAsync<List<Location>>($"api/locations?query={Uri.EscapeDataString(query)}", cancellationToken);

    public Task<CurrentWeather> GetCurrentAsync(string city, CancellationToken cancellationToken = default) =>
        GetAsync<CurrentWeather>($"api/weather/current?city={Uri.EscapeDataString(city)}", cancellationToken);

    public Task<WeatherForecast> GetForecastAsync(string city, int days = 3, CancellationToken cancellationToken = default) =>
        GetAsync<WeatherForecast>($"api/weather/forecast?city={Uri.EscapeDataString(city)}&days={days}", cancellationToken);

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new WeatherApiException(
                $"Weather API returned {(int)response.StatusCode}: {body}", response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
               ?? throw new WeatherApiException("Empty response from Weather API.", response.StatusCode);
    }
}
