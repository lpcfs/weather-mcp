using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using Weather.Sdk;

var builder = WebApplication.CreateBuilder(args);

var apiUrl = Environment.GetEnvironmentVariable("WEATHER_API_URL") ?? "http://localhost:5080";
builder.Services.AddWeatherClient(new Uri(apiUrl));

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Entra ID protection. Fail closed: running without it needs an explicit Auth:Disabled=true (local dev only).
const string Policy = "WeatherRead";
const string RequiredScope = "Weather.Read";
const string RequiredRole = "Weather.ReadAll";
const string RequiredUserRole = "Weather.User";
var authDisabled = builder.Configuration.GetValue<bool>("Auth:Disabled");
var tenantId = builder.Configuration["AzureAd:TenantId"];
var clientId = builder.Configuration["AzureAd:ClientId"];

if (!authDisabled)
{
    if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
        throw new InvalidOperationException(
            "Set AzureAd:TenantId and AzureAd:ClientId (env AzureAd__TenantId / AzureAd__ClientId), or Auth:Disabled=true for local development.");

    var authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";

    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });

    builder.Services
        .AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(o =>
        {
            o.Authority = authority;
            o.MapInboundClaims = false;
            o.TokenValidationParameters.ValidAudiences = [clientId, $"api://{clientId}"];
            o.TokenValidationParameters.ValidIssuer = authority;
            o.TokenValidationParameters.NameClaimType = "name";
        })
        .AddMcp(o =>
        {
            // Tells MCP clients (RFC 9728) which authorization server issues tokens for this resource.
            o.Events.OnResourceMetadataRequest = ctx =>
            {
                ctx.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = $"{ctx.Request.Scheme}://{ctx.Request.Host}/mcp",
                    AuthorizationServers = { $"https://login.microsoftonline.com/{tenantId}/v2.0" },
                    ScopesSupported = [$"api://{clientId}/{RequiredScope}"],
                };
                return Task.CompletedTask;
            };
        });

    // Delegated (user) tokens need the scope in `scp` AND the user must be assigned the Weather.User role (`roles`);
    // app-only (client credentials) tokens carry the Weather.ReadAll app role. Entra also enforces assignment at sign-in.
    builder.Services.AddAuthorizationBuilder().AddPolicy(Policy, p => p
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            (ctx.User.FindFirst("scp")?.Value.Split(' ').Contains(RequiredScope) == true &&
             ctx.User.FindAll("roles").Any(r => r.Value == RequiredUserRole)) ||
            ctx.User.FindAll("roles").Any(r => r.Value == RequiredRole)));
}

var app = builder.Build();

if (!authDisabled)
{
    app.UseForwardedHeaders();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMcp("/mcp").RequireAuthorization(Policy);

    // Hands the OpenRouter key to authorized callers. Only this server's managed identity can read the Key Vault;
    // clients never touch it directly. Not mapped unless a vault is configured, and never when auth is disabled.
    if (builder.Configuration["KEYVAULT_URI"] is { Length: > 0 } vaultUri)
    {
        var secrets = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
        app.MapGet("/secrets/openrouter-key", async (CancellationToken ct) =>
        {
            try
            {
                var secret = await secrets.GetSecretAsync("OPENROUTER-API-KEY", cancellationToken: ct);
                return Results.Json(new { value = secret.Value.Value }, statusCode: 200);
            }
            catch (RequestFailedException e) when (e.Status == 404)
            {
                return Results.NotFound("Secret OPENROUTER-API-KEY has not been set in the Key Vault.");
            }
        }).RequireAuthorization(Policy);
    }
}
else
{
    app.Logger.LogWarning("Auth:Disabled=true - the MCP endpoint is UNPROTECTED.");
    app.MapMcp("/mcp");
}

app.Run();
