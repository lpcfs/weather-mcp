namespace Weather.Models;

public enum WeatherCondition
{
    Unknown,
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Showers,
    Thunderstorm
}

public sealed record Location(
    string Name,
    string? Country,
    double Latitude,
    double Longitude,
    string? Timezone);

public sealed record CurrentWeather(
    Location Location,
    DateTimeOffset ObservedAt,
    double TemperatureCelsius,
    double ApparentTemperatureCelsius,
    int RelativeHumidityPercent,
    double WindSpeedKmh,
    int WindDirectionDegrees,
    double PrecipitationMm,
    WeatherCondition Condition);

public sealed record DailyForecast(
    DateOnly Date,
    double MinTemperatureCelsius,
    double MaxTemperatureCelsius,
    double PrecipitationMm,
    double MaxWindSpeedKmh,
    WeatherCondition Condition);

public sealed record WeatherForecast(
    Location Location,
    IReadOnlyList<DailyForecast> Days);
