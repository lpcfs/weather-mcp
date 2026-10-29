using System.Text.Json.Serialization;
using Weather.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpClient<OpenMeteoProvider>();
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();

var api = app.MapGroup("/api");

api.MapGet("/locations", async (string query, OpenMeteoProvider p, CancellationToken ct) =>
    Results.Ok(await p.SearchAsync(query, 10, ct)));

api.MapGet("/weather/current", async (string city, OpenMeteoProvider p, CancellationToken ct) =>
    await p.ResolveAsync(city, ct) is { } loc
        ? Results.Ok(await p.GetCurrentAsync(loc, ct))
        : Results.NotFound($"Location '{city}' not found."));

api.MapGet("/weather/forecast", async (string city, int? days, OpenMeteoProvider p, CancellationToken ct) =>
    await p.ResolveAsync(city, ct) is { } loc
        ? Results.Ok(await p.GetForecastAsync(loc, Math.Clamp(days ?? 3, 1, 16), ct))
        : Results.NotFound($"Location '{city}' not found."));

app.Run();
