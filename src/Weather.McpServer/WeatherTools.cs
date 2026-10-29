using System.ComponentModel;
using ModelContextProtocol.Server;
using Weather.Models;
using Weather.Sdk;

namespace Weather.McpServer;

/// <summary>MCP tools; all data access goes through the SDK contract (<see cref="IWeatherClient"/>).</summary>
[McpServerToolType]
public sealed class WeatherTools(IWeatherClient weather)
{
    [McpServerTool(Name = "search_locations", ReadOnly = true), Description("Find locations by name and return their coordinates and timezone.")]
    public Task<IReadOnlyList<Location>> SearchLocations(
        [Description("City or place name, e.g. 'Brussels'")] string query, CancellationToken ct) =>
        weather.SearchLocationsAsync(query, ct);

    [McpServerTool(Name = "get_current_weather", ReadOnly = true), Description("Get the current weather conditions for a city.")]
    public Task<CurrentWeather> GetCurrentWeather(
        [Description("City name, e.g. 'Antwerp'")] string city, CancellationToken ct) =>
        weather.GetCurrentAsync(city, ct);

    [McpServerTool(Name = "get_weather_forecast", ReadOnly = true), Description("Get a daily weather forecast for a city.")]
    public Task<WeatherForecast> GetWeatherForecast(
        [Description("City name, e.g. 'Ghent'")] string city,
        [Description("Number of days, 1-16 (default 3)")] int days = 3, CancellationToken ct = default) =>
        weather.GetForecastAsync(city, days, ct);
}
