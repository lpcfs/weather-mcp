using System.ClientModel;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using Spectre.Console;

// Defaults point at the deployed (Entra-protected) server; every value can be overridden by its environment variable.
// The IDs below are public identifiers, not secrets.
const string DefaultMcpUrl = "https://ca-mcp-352acnm7t25rs.wittyplant-b06b1f5f.westeurope.azurecontainerapps.io/mcp";
const string DefaultTenantId = "154bc422-f229-400a-9dac-b217f91e6a88";
const string DefaultApiClientId = "845ba887-173a-4032-b02f-fea08a82b3df";
const string DefaultClientId = "41832e64-8e8b-4cab-8da2-b1cc41a8acc9";

var mcpUrl = Environment.GetEnvironmentVariable("WEATHER_MCP_URL") ?? DefaultMcpUrl;
var model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "anthropic/claude-sonnet-4.5";
var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

// Entra auth defaults on for the deployed server and off for a local one (WEATHER_MCP_URL on localhost), so the
// unprotected local stack keeps working. Set ENTRA_API_CLIENT_ID explicitly to force it on:
//   ENTRA_CLIENT_SECRET set -> app-only (client credentials, needs the Weather.Read app role)
//   otherwise               -> interactive browser sign-in as the user (delegated Weather.Read scope)
var isLocalServer = new Uri(mcpUrl).IsLoopback;
var apiClientId = Environment.GetEnvironmentVariable("ENTRA_API_CLIENT_ID") ?? (isLocalServer ? null : DefaultApiClientId);
var httpClient = new HttpClient();
if (!string.IsNullOrEmpty(apiClientId))
{
    var tenantId = Environment.GetEnvironmentVariable("ENTRA_TENANT_ID") ?? DefaultTenantId;
    var clientId = Environment.GetEnvironmentVariable("ENTRA_CLIENT_ID") ?? DefaultClientId;
    var secret = Environment.GetEnvironmentVariable("ENTRA_CLIENT_SECRET");

    TokenCredential credential = string.IsNullOrEmpty(secret)
        ? new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            ClientId = clientId,
            RedirectUri = new Uri("http://localhost"),
        })
        : new ClientSecretCredential(tenantId, clientId, secret);
    var scope = string.IsNullOrEmpty(secret)
        ? $"api://{apiClientId}/Weather.Read"
        : $"api://{apiClientId}/.default";

    httpClient = new HttpClient(new BearerTokenHandler(credential, scope) { InnerHandler = new HttpClientHandler() });
}

// No local key? Ask the MCP server for it: it reads the Key Vault with its own managed identity and only
// answers callers that present a valid Weather.Read token.
if (string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiClientId))
{
    var keyUrl = new Uri(new Uri(mcpUrl), "/secrets/openrouter-key");
    using var response = await httpClient.GetAsync(keyUrl);
    if (response.IsSuccessStatusCode)
        apiKey = (await response.Content.ReadFromJsonAsync<KeyResponse>())?.Value;
    else
        Console.Error.WriteLine($"Could not fetch the OpenRouter key from {keyUrl}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
}

if (string.IsNullOrEmpty(apiKey))
{
    Console.Error.WriteLine("Set OPENROUTER_API_KEY, or sign in to Entra (ENTRA_*) so the key is fetched from the MCP server.");
    return 1;
}

await using var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(mcpUrl),
        Name = "weather",
    }, httpClient, ownsHttpClient: true));

var tools = await mcpClient.ListToolsAsync();
Console.WriteLine($"Connected to {mcpUrl} - tools: {string.Join(", ", tools.Select(t => t.Name))}");

using var chatClient = new ChatClientBuilder(new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") })
        .GetChatClient(model)
        .AsIChatClient())
    .UseFunctionInvocation()
    .Build();

var options = new ChatOptions { Tools = [.. tools], MaxOutputTokens = 1024 };
List<ChatMessage> history =
[
    new(ChatRole.System, "You are a friendly weather assistant. Use the weather tools to answer. " +
        "Reply in the language of the user. If a place name is ambiguous, say which one you picked. " +
        "Always format your answers in Markdown (headings, bullet lists, tables for forecasts)."),
];

Console.WriteLine("Ask about the weather (empty line or 'exit' to quit).\n");

while (true)
{
    Console.Write("you> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    history.Add(new(ChatRole.User, input));

    List<ChatResponseUpdate> updates = [];
    await AnsiConsole.Status().StartAsync("thinking...", async _ =>
    {
        await foreach (var update in chatClient.GetStreamingResponseAsync(history, options))
            updates.Add(update);
    });

    var response = updates.ToChatResponse();
    AnsiConsole.MarkupLine("[bold cyan]bot>[/]");
    MarkdownRenderer.Write(response.Text);

    history.AddMessages(response);
}

return 0;

internal sealed record KeyResponse(string Value);
