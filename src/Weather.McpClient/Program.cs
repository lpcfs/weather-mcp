using System.ClientModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using Spectre.Console;

var mcpUrl = Environment.GetEnvironmentVariable("WEATHER_MCP_URL") ?? "http://localhost:5000/mcp";
var model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "anthropic/claude-sonnet-4.5";
var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

if (string.IsNullOrEmpty(apiKey))
{
    Console.Error.WriteLine("Set OPENROUTER_API_KEY first.");
    return 1;
}

await using var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(mcpUrl),
        Name = "weather",
    }));

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
