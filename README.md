# Weather MCP monorepo

| Project | Purpose |
|---|---|
| `Weather.Models` | Shared contract types (`CurrentWeather`, `WeatherForecast`, `Location`, ...) |
| `Weather.Api` | ASP.NET minimal API; fetches data from Open-Meteo (no API key) |
| `Weather.Sdk` | `IWeatherClient` + typed HTTP implementation + `AddWeatherClient()` DI extension |
| `Weather.McpServer` | MCP server (stdio, official `ModelContextProtocol` C# SDK); tools call the API **only through `IWeatherClient`** |

## Run

```bash
dotnet run --project src/Weather.Api            # http://localhost:5080
```

MCP client config (e.g. Claude Desktop / Claude Code):

```json
{
  "mcpServers": {
    "weather": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\Users\\LucPeeters\\source\\repos\\weather-mcp\\src\\Weather.McpServer", "--no-launch-profile"],
      "env": { "WEATHER_API_URL": "http://localhost:5080" }
    }
  }
}
```

Tools: `search_locations`, `get_current_weather`, `get_weather_forecast`.
API: `GET /api/locations?query=`, `/api/weather/current?city=`, `/api/weather/forecast?city=&days=`.
