using System.Globalization;
using System.Text.Json;
using Weather.Models;

namespace Weather.Api;

/// <summary>Maps the free Open-Meteo service (no API key) onto the Weather.Models contract.</summary>
public sealed class OpenMeteoProvider(HttpClient http)
{
    private const string Geocoding = "https://geocoding-api.open-meteo.com/v1/search";
    private const string Forecast = "https://api.open-meteo.com/v1/forecast";

    public async Task<IReadOnlyList<Location>> SearchAsync(string query, int count, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{Geocoding}?name={Uri.EscapeDataString(query)}&count={count}", ct);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return [];
        return results.EnumerateArray().Select(r => new Location(
            r.GetProperty("name").GetString()!,
            r.TryGetProperty("country", out var c) ? c.GetString() : null,
            r.GetProperty("latitude").GetDouble(),
            r.GetProperty("longitude").GetDouble(),
            r.TryGetProperty("timezone", out var tz) ? tz.GetString() : null)).ToList();
    }

    public async Task<Location?> ResolveAsync(string city, CancellationToken ct) =>
        (await SearchAsync(city, 1, ct)).FirstOrDefault();

    public async Task<CurrentWeather> GetCurrentAsync(Location l, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(
            $"{Forecast}?latitude={F(l.Latitude)}&longitude={F(l.Longitude)}&timezone=auto&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,wind_direction_10m,precipitation,weather_code", ct);
        var c = doc.RootElement.GetProperty("current");
        var offset = TimeSpan.FromSeconds(doc.RootElement.GetProperty("utc_offset_seconds").GetInt32());
        var observed = new DateTimeOffset(DateTime.Parse(c.GetProperty("time").GetString()!, CultureInfo.InvariantCulture), offset);
        return new CurrentWeather(l, observed,
            c.GetProperty("temperature_2m").GetDouble(),
            c.GetProperty("apparent_temperature").GetDouble(),
            c.GetProperty("relative_humidity_2m").GetInt32(),
            c.GetProperty("wind_speed_10m").GetDouble(),
            c.GetProperty("wind_direction_10m").GetInt32(),
            c.GetProperty("precipitation").GetDouble(),
            MapCode(c.GetProperty("weather_code").GetInt32()));
    }

    public async Task<WeatherForecast> GetForecastAsync(Location l, int days, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(
            $"{Forecast}?latitude={F(l.Latitude)}&longitude={F(l.Longitude)}&timezone=auto&forecast_days={days}&daily=temperature_2m_min,temperature_2m_max,precipitation_sum,wind_speed_10m_max,weather_code", ct);
        var d = doc.RootElement.GetProperty("daily");
        double Num(string p, int i) => d.GetProperty(p)[i].GetDouble();
        var list = d.GetProperty("time").EnumerateArray().Select((t, i) => new DailyForecast(
            DateOnly.Parse(t.GetString()!, CultureInfo.InvariantCulture),
            Num("temperature_2m_min", i), Num("temperature_2m_max", i),
            Num("precipitation_sum", i), Num("wind_speed_10m_max", i),
            MapCode((int)Num("weather_code", i)))).ToList();
        return new WeatherForecast(l, list);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private static string F(double v) => v.ToString(CultureInfo.InvariantCulture);

    // WMO weather interpretation codes
    private static WeatherCondition MapCode(int code) => code switch
    {
        0 => WeatherCondition.Clear,
        1 or 2 => WeatherCondition.PartlyCloudy,
        3 => WeatherCondition.Cloudy,
        45 or 48 => WeatherCondition.Fog,
        >= 51 and <= 57 => WeatherCondition.Drizzle,
        >= 61 and <= 67 => WeatherCondition.Rain,
        (>= 71 and <= 77) or 85 or 86 => WeatherCondition.Snow,
        >= 80 and <= 82 => WeatherCondition.Showers,
        >= 95 => WeatherCondition.Thunderstorm,
        _ => WeatherCondition.Unknown
    };
}
