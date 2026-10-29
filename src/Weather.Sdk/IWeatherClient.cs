using System.Net;
using Weather.Models;

namespace Weather.Sdk;

/// <summary>The contract of the Weather API. Consumers (e.g. the MCP server) depend on this, not on HTTP.</summary>
public interface IWeatherClient
{
    Task<IReadOnlyList<Location>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default);

    Task<CurrentWeather> GetCurrentAsync(string city, CancellationToken cancellationToken = default);

    Task<WeatherForecast> GetForecastAsync(string city, int days = 3, CancellationToken cancellationToken = default);
}

public sealed class WeatherApiException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
