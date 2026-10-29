using Microsoft.Extensions.DependencyInjection;

namespace Weather.Sdk;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWeatherClient(this IServiceCollection services, Uri baseAddress)
    {
        services.AddHttpClient<IWeatherClient, WeatherClient>(c => c.BaseAddress = baseAddress);
        return services;
    }
}
