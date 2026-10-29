using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weather.Sdk;

var builder = Host.CreateApplicationBuilder(args);

// stdout is the MCP transport, so logs go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var apiUrl = Environment.GetEnvironmentVariable("WEATHER_API_URL") ?? "http://localhost:5080";
builder.Services.AddWeatherClient(new Uri(apiUrl));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
