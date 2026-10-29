using Weather.Sdk;

var builder = WebApplication.CreateBuilder(args);

var apiUrl = Environment.GetEnvironmentVariable("WEATHER_API_URL") ?? "http://localhost:5080";
builder.Services.AddWeatherClient(new Uri(apiUrl));

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");

app.Run();
